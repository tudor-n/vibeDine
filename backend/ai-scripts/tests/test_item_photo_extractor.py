import base64
import io
import json

import pytest
from unittest.mock import AsyncMock, MagicMock

from PIL import Image, ImageDraw

from services.item_photo_extractor import (
    FakeItemPhotoExtractor,
    GeminiItemPhotoExtractor,
    ItemOnPage,
    ItemPhotoExtractionError,
    build_item_photos,
    crop_item_photo,
    resolve_item_photo_extractor,
)


# ---------------------------------------------------------------------------
# Fixtures — build images in-memory with known coloured regions.
# ---------------------------------------------------------------------------

def _png(width: int, height: int, regions: list[tuple[tuple[int, int, int, int], tuple[int, int, int]]]) -> bytes:
    """regions: list of ((left, top, right, bottom) px, (r,g,b)) rectangles on white bg."""
    img = Image.new("RGB", (width, height), (255, 255, 255))
    draw = ImageDraw.Draw(img)
    for (left, top, right, bottom), colour in regions:
        draw.rectangle([left, top, right, bottom], fill=colour)
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def _open(jpeg_bytes: bytes) -> Image.Image:
    return Image.open(io.BytesIO(jpeg_bytes))


# ---------------------------------------------------------------------------
# crop_item_photo
# ---------------------------------------------------------------------------

def test_crop_extracts_correct_region():
    # Red square occupying normalized [100,100,400,400] on a 1000x1000 page.
    page = _png(1000, 1000, [((100, 100, 399, 399), (220, 30, 30))])
    out = crop_item_photo(page, [100, 100, 400, 400])
    assert out is not None
    assert out[:3] == b"\xff\xd8\xff"  # JPEG magic
    crop = _open(out)
    assert crop.format == "JPEG"
    # ~300x300 region
    assert 250 <= crop.width <= 350 and 250 <= crop.height <= 350
    r, g, b = crop.convert("RGB").getpixel((crop.width // 2, crop.height // 2))
    assert r > 180 and g < 90 and b < 90  # predominantly red despite JPEG noise


def test_crop_downscales_to_max_1024():
    page = _png(2000, 2000, [((0, 0, 1999, 1999), (10, 120, 220))])
    out = crop_item_photo(page, [0, 0, 1000, 1000])  # full page → 2000px region
    assert out is not None
    crop = _open(out)
    assert max(crop.width, crop.height) <= 1024


def test_crop_rejects_out_of_range_box():
    page = _png(1000, 1000, [])
    assert crop_item_photo(page, [0, 0, 1500, 500]) is None


def test_crop_rejects_inverted_box():
    page = _png(1000, 1000, [])
    assert crop_item_photo(page, [400, 400, 100, 100]) is None


def test_crop_rejects_tiny_box():
    page = _png(1000, 1000, [])
    # 30px on a side < 48px minimum
    assert crop_item_photo(page, [0, 0, 30, 30]) is None


def test_crop_rejects_malformed_box():
    page = _png(1000, 1000, [])
    assert crop_item_photo(page, [0, 0, 100]) is None
    assert crop_item_photo(page, "nope") is None  # type: ignore[arg-type]


def test_crop_garbage_bytes_returns_none():
    assert crop_item_photo(b"this is not an image", [0, 0, 400, 400]) is None


def test_crop_pdf_bytes_returns_none():
    assert crop_item_photo(b"%PDF-1.4\n%fake pdf bytes", [0, 0, 400, 400]) is None


# ---------------------------------------------------------------------------
# build_item_photos — guards, best-per-index, min-confidence, IoU dedupe
# ---------------------------------------------------------------------------

def test_build_best_per_index_and_min_confidence():
    page = _png(1000, 1000, [
        ((100, 100, 399, 399), (220, 30, 30)),
        ((500, 500, 799, 799), (30, 30, 220)),
    ])
    detections = [
        {"source_index": 0, "box_2d": [100, 100, 400, 400], "confidence": 0.9},
        {"source_index": 0, "box_2d": [100, 100, 400, 400], "confidence": 0.6},  # lower, dropped
        {"source_index": 1, "box_2d": [500, 500, 800, 800], "confidence": 0.8},
        {"source_index": 2, "box_2d": [100, 100, 400, 400], "confidence": 0.3},  # < min conf
        {"source_index": 5, "box_2d": [100, 100, 400, 400], "confidence": 0.9},  # not a valid item
    ]
    photos = build_item_photos(page, page_number=3, detections=detections, valid_indices={0, 1, 2})
    by_index = {p.source_index: p for p in photos}
    assert set(by_index) == {0, 1}
    assert by_index[0].confidence == 0.9
    assert by_index[0].source_page == 3
    # base64 decodes to a JPEG
    assert base64.b64decode(by_index[0].image_base64)[:3] == b"\xff\xd8\xff"


def test_build_rejects_oversized_area():
    page = _png(1000, 1000, [])
    detections = [{"source_index": 0, "box_2d": [0, 0, 950, 950], "confidence": 0.9}]  # 90% area
    photos = build_item_photos(page, page_number=0, detections=detections, valid_indices={0})
    assert photos == []


def test_build_iou_dedupe_keeps_highest_confidence():
    page = _png(1000, 1000, [((100, 100, 399, 399), (220, 30, 30))])
    detections = [
        {"source_index": 0, "box_2d": [100, 100, 400, 400], "confidence": 0.9},
        {"source_index": 1, "box_2d": [102, 102, 402, 402], "confidence": 0.7},  # IoU > 0.9 with idx0
    ]
    photos = build_item_photos(page, page_number=0, detections=detections, valid_indices={0, 1})
    assert len(photos) == 1
    assert photos[0].source_index == 0
    assert photos[0].confidence == 0.9


def test_build_garbage_page_returns_empty():
    detections = [{"source_index": 0, "box_2d": [100, 100, 400, 400], "confidence": 0.9}]
    assert build_item_photos(b"not an image", page_number=0, detections=detections, valid_indices={0}) == []


# ---------------------------------------------------------------------------
# GeminiItemPhotoExtractor — stub client, mirrors organizer tests
# ---------------------------------------------------------------------------

def _text_response(payload: dict) -> MagicMock:
    resp = MagicMock()
    resp.text = json.dumps(payload)
    return resp


@pytest.mark.asyncio
async def test_gemini_detector_returns_photos():
    payload = {"photos": [{"source_index": 0, "box_2d": [100, 100, 400, 400], "confidence": 0.9}]}
    client = MagicMock()
    client.aio.models.generate_content = AsyncMock(return_value=_text_response(payload))
    det = GeminiItemPhotoExtractor(client=client, model_name="gemini-test")
    out = await det.detect(b"\x89PNG", 0, [ItemOnPage(source_index=0, name="Pizza")])
    assert out == payload["photos"]
    client.aio.models.generate_content.assert_awaited_once()


@pytest.mark.asyncio
async def test_gemini_detector_raises_on_provider_error():
    client = MagicMock()
    client.aio.models.generate_content = AsyncMock(side_effect=RuntimeError("boom"))
    det = GeminiItemPhotoExtractor(client=client, model_name="gemini-test")
    with pytest.raises(ItemPhotoExtractionError):
        await det.detect(b"img", 0, [ItemOnPage(0, "Pizza")])


@pytest.mark.asyncio
async def test_gemini_detector_raises_on_invalid_json():
    bad = MagicMock()
    bad.text = "not json"
    client = MagicMock()
    client.aio.models.generate_content = AsyncMock(return_value=bad)
    det = GeminiItemPhotoExtractor(client=client, model_name="gemini-test")
    with pytest.raises(ItemPhotoExtractionError):
        await det.detect(b"img", 0, [ItemOnPage(0, "Pizza")])


@pytest.mark.asyncio
async def test_gemini_detector_item_names_threaded_into_prompt():
    payload = {"photos": []}
    client = MagicMock()
    client.aio.models.generate_content = AsyncMock(return_value=_text_response(payload))
    det = GeminiItemPhotoExtractor(client=client, model_name="gemini-test")
    await det.detect(b"img", 0, [ItemOnPage(0, "Margherita"), ItemOnPage(1, "Tiramisu")])
    contents = client.aio.models.generate_content.call_args.kwargs["contents"]
    joined = "\n".join(str(c) for c in contents)
    assert "Margherita" in joined and "Tiramisu" in joined


# ---------------------------------------------------------------------------
# FakeItemPhotoExtractor + resolve_item_photo_extractor
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_fake_detector_canned_and_records_calls():
    canned = [{"source_index": 0, "box_2d": [100, 100, 400, 400], "confidence": 0.9}]
    fake = FakeItemPhotoExtractor(canned=canned)
    out = await fake.detect(b"img", 2, [ItemOnPage(0, "Pizza")])
    assert out == canned
    assert fake.calls == [(2, [ItemOnPage(0, "Pizza")])]


@pytest.mark.asyncio
async def test_fake_detector_raises():
    fake = FakeItemPhotoExtractor(raises=ItemPhotoExtractionError("boom"))
    with pytest.raises(ItemPhotoExtractionError):
        await fake.detect(b"img", 0, [])


def test_fake_detector_requires_exactly_one_of_canned_or_raises():
    with pytest.raises(ValueError):
        FakeItemPhotoExtractor()
    with pytest.raises(ValueError):
        FakeItemPhotoExtractor(canned=[], raises=ItemPhotoExtractionError("x"))


def test_resolve_none_without_api_key(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    assert resolve_item_photo_extractor() is None


def test_resolve_none_for_claude_provider(monkeypatch):
    monkeypatch.setenv("GEMINI_API_KEY", "x")
    monkeypatch.setenv("AI_VISION_PROVIDER", "claude")
    assert resolve_item_photo_extractor() is None


def test_resolve_returns_gemini_when_configured(monkeypatch):
    monkeypatch.setenv("GEMINI_API_KEY", "x")
    monkeypatch.setenv("AI_VISION_PROVIDER", "gemini")
    det = resolve_item_photo_extractor()
    assert isinstance(det, GeminiItemPhotoExtractor)

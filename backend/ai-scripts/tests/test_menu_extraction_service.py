import io

import pytest
from PIL import Image, ImageDraw

from services.item_photo_extractor import FakeItemPhotoExtractor, ItemPhotoExtractionError
from services.menu_extraction_service import MenuExtractionService
from services.vision_extractor import FakeVisionExtractor, VisionExtractionError


def _items(*names_and_prices, hint=None):
    return [
        {"name": n, "price": p, "source_index": i, "category_hint": hint}
        for i, (n, p) in enumerate(names_and_prices)
    ]


def _page_png() -> bytes:
    img = Image.new("RGB", (1000, 1000), (255, 255, 255))
    ImageDraw.Draw(img).rectangle([100, 100, 399, 399], fill=(220, 30, 30))
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


_DETECTIONS = [{"source_index": 0, "box_2d": [100, 100, 400, 400], "confidence": 0.9}]


@pytest.mark.asyncio
async def test_single_page_success():
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(
            canned_response={"items": _items(("Ciorbă", 18.0), ("Salată", 22.5), hint="APERITIVE")},
            model_name="fake-x",
        )
    )
    result = await service.extract([b"\x00"])
    assert result.model == "fake-x"
    assert result.schema_version == 2
    assert len(result.items) == 2
    assert result.items[0].source_page == 0
    assert result.items[0].category_hint == "APERITIVE"
    assert result.failed_pages == []


@pytest.mark.asyncio
async def test_multi_page_success_preserves_page_order():
    canned = {"items": _items(("Foo", 10.0))}
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(canned_response=canned, model_name="fake-y")
    )
    result = await service.extract([b"\x00", b"\x01", b"\x02"])
    assert [it.source_page for it in result.items] == [0, 1, 2]


@pytest.mark.asyncio
async def test_one_page_fails_others_succeed():
    state = {"calls": 0}

    def factory():
        state["calls"] += 1
        if state["calls"] == 2:  # second page (index 1) fails
            return FakeVisionExtractor(raises=VisionExtractionError("boom"))
        return FakeVisionExtractor(canned_response={"items": _items(("Foo", 10.0))})

    service = MenuExtractionService(extractor_factory=factory)
    result = await service.extract([b"\x00", b"\x01", b"\x02"])
    assert {fp.source_page for fp in result.failed_pages} == {1}
    assert result.failed_pages[0].reason == "vision_api_error"
    assert {it.source_page for it in result.items} == {0, 2}


@pytest.mark.asyncio
async def test_empty_items_marked_as_no_items_detected():
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(canned_response={"items": []})
    )
    result = await service.extract([b"\x00"])
    assert result.items == []
    assert len(result.failed_pages) == 1
    assert result.failed_pages[0].reason == "no_items_detected"


@pytest.mark.asyncio
async def test_invalid_provider_payload_treated_as_vision_api_error():
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(
            canned_response={"items": [{"name": "", "price": -1.0, "source_index": 0}]}
        )
    )
    result = await service.extract([b"\x00"])
    assert result.items == []
    assert result.failed_pages[0].reason == "vision_api_error"


# ---------------------------------------------------------------------------
# Item photo detection (additive second pass)
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_item_photos_attached_when_flag_on():
    fake = FakeItemPhotoExtractor(canned=_DETECTIONS)
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(
            canned_response={"items": _items(("Pizza", 28.0))}
        ),
        photo_extractor_factory=lambda: fake,
    )
    result = await service.extract([_page_png()], extract_item_photos=True)
    assert result.item_photos is not None
    assert len(result.item_photos) == 1
    assert result.item_photos[0].source_page == 0
    assert result.item_photos[0].source_index == 0
    assert fake.calls and fake.calls[0][0] == 0


@pytest.mark.asyncio
async def test_item_photo_detector_failure_never_breaks_extraction():
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(
            canned_response={"items": _items(("Pizza", 28.0))}
        ),
        photo_extractor_factory=lambda: FakeItemPhotoExtractor(
            raises=ItemPhotoExtractionError("boom")
        ),
    )
    result = await service.extract([_page_png()], extract_item_photos=True)
    assert len(result.items) == 1
    assert result.failed_pages == []
    assert result.item_photos is None


@pytest.mark.asyncio
async def test_item_photo_detector_not_called_when_flag_off():
    fake = FakeItemPhotoExtractor(canned=_DETECTIONS)
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(
            canned_response={"items": _items(("Pizza", 28.0))}
        ),
        photo_extractor_factory=lambda: fake,
    )
    result = await service.extract([_page_png()])
    assert fake.calls == []
    assert result.item_photos is None


@pytest.mark.asyncio
async def test_item_photo_detector_skipped_on_zero_item_page():
    fake = FakeItemPhotoExtractor(canned=_DETECTIONS)
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(canned_response={"items": []}),
        photo_extractor_factory=lambda: fake,
    )
    result = await service.extract([_page_png()], extract_item_photos=True)
    assert fake.calls == []
    assert result.item_photos is None

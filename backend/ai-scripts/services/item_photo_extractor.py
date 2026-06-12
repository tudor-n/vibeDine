"""Optional second vision pass that locates printed dish photographs on a menu
page and crops them out, attributing each crop to one extracted item.

This module is purely additive: when the feature is off (no extractor wired) the
existing extraction path is byte-for-byte unchanged. Every failure here is
designed to degrade gracefully — a detection error never fails an import, it
simply yields no photos for that page.
"""

from __future__ import annotations

import base64
import io
import json
import logging
import os
from dataclasses import dataclass, field
from typing import Any, List, Optional, Protocol, Sequence

from models.menu_extraction import ExtractedItemPhoto

logger = logging.getLogger(__name__)


# Pillow's HEIC support is opt-in via the pillow-heif plugin. Register it when
# available so HEIC menu uploads can still be cropped; without the wheel we
# silently skip HEIC crops (items are unaffected).
try:  # pragma: no cover - exercised only where the optional wheel is installed
    import pillow_heif  # type: ignore

    pillow_heif.register_heif_opener()
except Exception:  # pragma: no cover - missing/incompatible wheel
    pass

from PIL import Image

GEMINI_ITEM_PHOTO_DEFAULT_MODEL = "gemini-3.1-pro-preview"

# Crop / validation tuning.
_MAX_CROP_SIDE_PX = 1024
_JPEG_QUALITY = 85
_MIN_SIDE_PX = 48
_MIN_AREA_FRACTION = 0.005
_MAX_AREA_FRACTION = 0.80
_MIN_CONFIDENCE = 0.5
_IOU_DEDUPE_THRESHOLD = 0.9
_BOX_SCALE = 1000.0  # box_2d coordinates are normalised 0..1000


class ItemPhotoExtractionError(Exception):
    """Raised when the underlying detector provider fails."""


@dataclass(frozen=True)
class ItemOnPage:
    """An extracted item the detector should try to match a photo against."""

    source_index: int
    name: str


class ItemPhotoExtractor(Protocol):
    model_name: str

    async def detect(
        self,
        image_bytes: bytes,
        page_number: int,
        items: Sequence[ItemOnPage],
    ) -> list[dict[str, Any]]:
        """Return raw detections: [{source_index, box_2d, confidence}, ...]."""
        ...


@dataclass
class FakeItemPhotoExtractor:
    """In-memory stand-in for tests. Returns ``canned`` or raises ``raises`` and
    records every ``detect`` call as ``(page_number, list(items))``."""

    canned: Optional[list[dict[str, Any]]] = None
    raises: Optional[Exception] = None
    model_name: str = "fake-item-photo-model"
    calls: list[tuple[int, list[ItemOnPage]]] = field(default_factory=list)

    def __post_init__(self) -> None:
        if (self.canned is None) == (self.raises is None):
            raise ValueError("Provide exactly one of canned or raises.")

    async def detect(self, image_bytes, page_number, items):
        self.calls.append((page_number, list(items)))
        if self.raises is not None:
            raise self.raises
        assert self.canned is not None
        return self.canned


# ---------------------------------------------------------------------------
# Geometry helpers
# ---------------------------------------------------------------------------

def _valid_box(box: Any) -> Optional[tuple[float, float, float, float]]:
    """Validate a normalised [ymin, xmin, ymax, xmax] box. Returns the tuple or
    None when malformed, out of range, or inverted."""
    if not isinstance(box, (list, tuple)) or len(box) != 4:
        return None
    try:
        ymin, xmin, ymax, xmax = (float(v) for v in box)
    except (TypeError, ValueError):
        return None
    for v in (ymin, xmin, ymax, xmax):
        if v < 0 or v > _BOX_SCALE:
            return None
    if ymax <= ymin or xmax <= xmin:
        return None
    return ymin, xmin, ymax, xmax


def _area_fraction(box: tuple[float, float, float, float]) -> float:
    ymin, xmin, ymax, xmax = box
    return ((ymax - ymin) / _BOX_SCALE) * ((xmax - xmin) / _BOX_SCALE)


def _iou(a: tuple[float, float, float, float], b: tuple[float, float, float, float]) -> float:
    ay0, ax0, ay1, ax1 = a
    by0, bx0, by1, bx1 = b
    iy0, ix0 = max(ay0, by0), max(ax0, bx0)
    iy1, ix1 = min(ay1, by1), min(ax1, bx1)
    iw, ih = max(0.0, ix1 - ix0), max(0.0, iy1 - iy0)
    inter = iw * ih
    if inter <= 0:
        return 0.0
    area_a = (ay1 - ay0) * (ax1 - ax0)
    area_b = (by1 - by0) * (bx1 - bx0)
    union = area_a + area_b - inter
    return inter / union if union > 0 else 0.0


# ---------------------------------------------------------------------------
# Cropping
# ---------------------------------------------------------------------------

def crop_item_photo(image_bytes: bytes, box_2d: Any) -> Optional[bytes]:
    """Crop the normalised box out of ``image_bytes`` and return a re-encoded
    JPEG (longest side <= 1024px, q85). Returns None for any failure: unreadable
    bytes, malformed/out-of-range/inverted box, or a crop smaller than 48px."""
    box = _valid_box(box_2d)
    if box is None:
        return None
    try:
        with Image.open(io.BytesIO(image_bytes)) as img:
            img = img.convert("RGB")
            width, height = img.size
            ymin, xmin, ymax, xmax = box
            left = int(round(xmin / _BOX_SCALE * width))
            top = int(round(ymin / _BOX_SCALE * height))
            right = int(round(xmax / _BOX_SCALE * width))
            bottom = int(round(ymax / _BOX_SCALE * height))
            left = max(0, min(left, width))
            right = max(0, min(right, width))
            top = max(0, min(top, height))
            bottom = max(0, min(bottom, height))
            if (right - left) < _MIN_SIDE_PX or (bottom - top) < _MIN_SIDE_PX:
                return None
            crop = img.crop((left, top, right, bottom))
            longest = max(crop.width, crop.height)
            if longest > _MAX_CROP_SIDE_PX:
                scale = _MAX_CROP_SIDE_PX / longest
                crop = crop.resize(
                    (max(1, int(crop.width * scale)), max(1, int(crop.height * scale))),
                    Image.LANCZOS,
                )
            out = io.BytesIO()
            crop.save(out, format="JPEG", quality=_JPEG_QUALITY)
            return out.getvalue()
    except Exception:
        logger.debug("crop_item_photo failed", exc_info=True)
        return None


def build_item_photos(
    image_bytes: bytes,
    page_number: int,
    detections: Sequence[dict[str, Any]],
    valid_indices: set[int],
) -> List[ExtractedItemPhoto]:
    """Apply hallucination guards to raw detections and crop the survivors.

    Guards: box shape/bounds/inverted, area within [0.5%, 80%] of the page,
    confidence >= 0.5, source_index in ``valid_indices``, best-confidence per
    index, IoU > 0.9 dedupe across indices, and a successful >=48px crop.
    """
    # 1. Per-detection validation → (source_index, box, confidence).
    candidates: list[tuple[int, tuple[float, float, float, float], float]] = []
    for det in detections:
        if not isinstance(det, dict):
            continue
        raw_index = det.get("source_index")
        try:
            index = int(raw_index)  # type: ignore[arg-type]
        except (TypeError, ValueError):
            continue
        if index not in valid_indices:
            continue
        confidence = det.get("confidence")
        if not isinstance(confidence, (int, float)) or confidence < _MIN_CONFIDENCE:
            continue
        box = _valid_box(det.get("box_2d"))
        if box is None:
            continue
        area = _area_fraction(box)
        if area < _MIN_AREA_FRACTION or area > _MAX_AREA_FRACTION:
            continue
        candidates.append((index, box, float(confidence)))

    # 2. Best confidence per source_index.
    best_by_index: dict[int, tuple[int, tuple[float, float, float, float], float]] = {}
    for cand in candidates:
        index, _, confidence = cand
        existing = best_by_index.get(index)
        if existing is None or confidence > existing[2]:
            best_by_index[index] = cand

    # 3. IoU dedupe across indices — keep highest confidence on overlap.
    ordered = sorted(best_by_index.values(), key=lambda c: c[2], reverse=True)
    kept: list[tuple[int, tuple[float, float, float, float], float]] = []
    for cand in ordered:
        if any(_iou(cand[1], k[1]) > _IOU_DEDUPE_THRESHOLD for k in kept):
            continue
        kept.append(cand)

    # 4. Crop survivors (cheap N) and emit photos.
    photos: list[ExtractedItemPhoto] = []
    for index, box, confidence in kept:
        crop = crop_item_photo(image_bytes, [box[0], box[1], box[2], box[3]])
        if crop is None:
            continue
        photos.append(
            ExtractedItemPhoto(
                source_page=page_number,
                source_index=index,
                image_base64=base64.standard_b64encode(crop).decode("ascii"),
                confidence=confidence,
            )
        )
    return photos


# ---------------------------------------------------------------------------
# Gemini detector
# ---------------------------------------------------------------------------

from google import genai
from google.genai import types as genai_types


ITEM_PHOTO_RESPONSE_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "photos": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "source_index": {"type": "integer"},
                    "box_2d": {"type": "array", "items": {"type": "integer"}},
                    "confidence": {"type": "number"},
                },
                "required": ["source_index", "box_2d", "confidence"],
            },
        }
    },
    "required": ["photos"],
}

_DETECTION_PROMPT = (
    "You are given a photo of one printed restaurant menu page and the list of "
    "items that were extracted from it. Find the printed PHOTOGRAPHS of dishes on "
    "the page and match each to exactly one item from the list.\n\n"
    "Rules:\n"
    "- Only real photographs of food/drinks count. Ignore logos, icons, "
    "illustrations, drawings, background textures and decorative borders.\n"
    "- At most one photo per item, and at most one item per photo. If you are "
    "unsure which item a photo belongs to, omit it.\n"
    "- box_2d is [ymin, xmin, ymax, xmax] normalised to 0-1000. Make the box "
    "tight around the photograph, excluding any caption or price text.\n"
    "- confidence is 0..1; omit detections you are not reasonably sure about.\n"
    "- source_index must be one of the indices listed below.\n\n"
    "Items (source_index | name):\n"
)


def _build_detection_prompt(items: Sequence[ItemOnPage]) -> str:
    lines = [_DETECTION_PROMPT]
    for it in items:
        lines.append(f"{it.source_index} | {it.name}")
    return "\n".join(lines)


def _sniff_media_type(image_bytes: bytes) -> str:
    if image_bytes.startswith(b"\xff\xd8\xff"):
        return "image/jpeg"
    if image_bytes.startswith(b"\x89PNG\r\n"):
        return "image/png"
    if image_bytes[:4] == b"RIFF" and image_bytes[8:12] == b"WEBP":
        return "image/webp"
    if image_bytes.startswith(b"%PDF-"):
        return "application/pdf"
    return "image/jpeg"


class GeminiItemPhotoExtractor:
    def __init__(
        self,
        api_key: str | None = None,
        model_name: str | None = None,
        client: genai.Client | None = None,
    ):
        resolved = api_key or os.environ.get("GEMINI_API_KEY")
        if not resolved and client is None:
            raise RuntimeError("GEMINI_API_KEY required for GeminiItemPhotoExtractor.")
        self.model_name = (
            model_name
            or os.environ.get("GEMINI_ITEM_PHOTO_MODEL")
            or os.environ.get("GEMINI_VISION_MODEL")
            or GEMINI_ITEM_PHOTO_DEFAULT_MODEL
        )
        self._client = client or genai.Client(api_key=resolved)

    async def detect(
        self,
        image_bytes: bytes,
        page_number: int,
        items: Sequence[ItemOnPage],
    ) -> list[dict[str, Any]]:
        contents: list[Any] = [
            genai_types.Part.from_bytes(
                data=image_bytes, mime_type=_sniff_media_type(image_bytes)
            ),
            _build_detection_prompt(items),
        ]
        try:
            response = await self._client.aio.models.generate_content(
                model=self.model_name,
                contents=contents,
                config=genai_types.GenerateContentConfig(
                    response_mime_type="application/json",
                    response_schema=ITEM_PHOTO_RESPONSE_SCHEMA,
                ),
            )
        except Exception as exc:
            raise ItemPhotoExtractionError("Item-photo provider call failed") from exc

        raw_text = getattr(response, "text", None)
        if not raw_text:
            raise ItemPhotoExtractionError("Empty response from Gemini item-photo detector")
        try:
            payload = json.loads(raw_text)
        except (ValueError, TypeError) as exc:
            raise ItemPhotoExtractionError("Failed to parse Gemini item-photo JSON") from exc
        if not isinstance(payload, dict) or not isinstance(payload.get("photos"), list):
            raise ItemPhotoExtractionError("Gemini item-photo payload missing 'photos'")
        return payload["photos"]


def resolve_item_photo_extractor() -> "ItemPhotoExtractor | None":
    """Env-driven factory. Returns None (feature no-ops) when there is no
    GEMINI_API_KEY or the configured vision provider is not Gemini — the v1
    detector is Gemini-only."""
    if not os.environ.get("GEMINI_API_KEY"):
        return None
    provider = os.environ.get("AI_VISION_PROVIDER", "gemini").lower()
    if provider != "gemini":
        return None
    try:
        return GeminiItemPhotoExtractor()
    except RuntimeError:
        return None

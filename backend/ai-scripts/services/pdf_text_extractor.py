"""
PDF text extraction via pdfplumber.
Produces one PageContent per page with text, density, and image-presence info.
"""
import logging
import re
from dataclasses import dataclass
from typing import Optional

import pdfplumber

logger = logging.getLogger(__name__)

_IMAGE_DOMINANT_AREA_RATIO = 0.85


@dataclass
class PageContent:
    page_number: int      # 0-based
    text: str             # raw extracted text, normalized whitespace
    text_density: float   # len(text.strip()) / page_area_pts
    has_images: bool      # True if pdfplumber reports embedded images on the page
    width_pts: float
    height_pts: float
    embedded_image_bytes: Optional[bytes] = None


def _normalize_text(raw_text: str) -> str:
    return re.sub(r"\s+", " ", raw_text).strip()


def _coerce_float(value) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return 0.0


def _image_area_pts(image: dict) -> float:
    width = _coerce_float(image.get("width"))
    height = _coerce_float(image.get("height"))
    if width > 0 and height > 0:
        return width * height

    x0 = _coerce_float(image.get("x0"))
    x1 = _coerce_float(image.get("x1"))
    top = _coerce_float(image.get("top"))
    bottom = _coerce_float(image.get("bottom"))
    return max(0.0, x1 - x0) * max(0.0, bottom - top)


def _dominant_image(width: float, height: float, images: list[dict]) -> Optional[dict]:
    page_area = width * height
    if page_area <= 0:
        return None

    for image in images:
        if _image_area_pts(image) / page_area >= _IMAGE_DOMINANT_AREA_RATIO:
            return image
    return None


def _stream_raw_bytes(stream) -> Optional[bytes]:
    get_rawdata = getattr(stream, "get_rawdata", None)
    if not callable(get_rawdata):
        return None
    try:
        raw_data = get_rawdata()
    except Exception:
        logger.debug("Could not read raw embedded image bytes", exc_info=True)
        return None
    if isinstance(raw_data, bytes):
        return raw_data
    if isinstance(raw_data, bytearray):
        return bytes(raw_data)
    return None


def _is_provider_supported_image_bytes(image_bytes: bytes) -> bool:
    return (
        image_bytes.startswith(b"\xff\xd8\xff")
        or image_bytes.startswith(b"\x89PNG\r\n")
        or (image_bytes[:4] == b"RIFF" and image_bytes[8:12] == b"WEBP")
    )


def _embedded_image_bytes(image: dict) -> Optional[bytes]:
    image_bytes = _stream_raw_bytes(image.get("stream"))
    if image_bytes is None:
        return None
    if not _is_provider_supported_image_bytes(image_bytes):
        return None
    return image_bytes


def extract(path: str) -> list[PageContent]:
    """Open the PDF with pdfplumber, return one PageContent per page in order.
    Closes the PDF before returning. Returns [] for unreadable/empty PDFs (do not raise).
    """
    try:
        pdf = pdfplumber.open(path)
    except Exception:
        logger.warning("Could not open PDF at %r", path, exc_info=True)
        return []

    results: list[PageContent] = []
    try:
        for page_number, page in enumerate(pdf.pages):
            try:
                width = float(page.width)
                height = float(page.height)
                area = width * height
                images = page.images or []
                has_images = bool(images)
                embedded_image_bytes = None
                dominant_image = _dominant_image(width, height, images) if has_images else None
                if dominant_image is not None:
                    normalized = ""
                    embedded_image_bytes = _embedded_image_bytes(dominant_image)
                else:
                    raw_text = page.extract_text() or ""
                    normalized = _normalize_text(raw_text)
                density = len(normalized) / area if area > 0 else 0.0
                results.append(
                    PageContent(
                        page_number=page_number,
                        text=normalized,
                        text_density=density,
                        has_images=has_images,
                        width_pts=width,
                        height_pts=height,
                        embedded_image_bytes=embedded_image_bytes,
                    )
                )
            except Exception:
                logger.warning(
                    "Error processing page %d of %r", page_number, path, exc_info=True
                )
    finally:
        pdf.close()

    return results

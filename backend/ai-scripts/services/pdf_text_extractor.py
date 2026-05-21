"""
PDF text extraction via pdfplumber.
Produces one PageContent per page with text, density, and image-presence info.
"""
import logging
import re
from dataclasses import dataclass

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


def _has_image_dominant_page(width: float, height: float, images: list[dict]) -> bool:
    page_area = width * height
    if page_area <= 0:
        return False

    return any(
        _image_area_pts(image) / page_area >= _IMAGE_DOMINANT_AREA_RATIO
        for image in images
    )


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
                if has_images and _has_image_dominant_page(width, height, images):
                    normalized = ""
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
                    )
                )
            except Exception:
                logger.warning(
                    "Error processing page %d of %r", page_number, path, exc_info=True
                )
    finally:
        pdf.close()

    return results

"""
PDF text extraction via pdfplumber.
Produces one PageContent per page with text, density, and image-presence info.
"""
import logging
import re
from dataclasses import dataclass

import pdfplumber

logger = logging.getLogger(__name__)


@dataclass
class PageContent:
    page_number: int      # 0-based
    text: str             # raw extracted text, normalized whitespace
    text_density: float   # len(text.strip()) / page_area_pts
    has_images: bool      # True if pdfplumber reports embedded images on the page
    width_pts: float
    height_pts: float


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
                raw_text = page.extract_text() or ""
                normalized = re.sub(r"\s+", " ", raw_text).strip()
                area = page.width * page.height
                density = len(normalized) / area if area > 0 else 0.0
                has_images = bool(page.images)
                results.append(
                    PageContent(
                        page_number=page_number,
                        text=normalized,
                        text_density=density,
                        has_images=has_images,
                        width_pts=float(page.width),
                        height_pts=float(page.height),
                    )
                )
            except Exception:
                logger.warning(
                    "Error processing page %d of %r", page_number, path, exc_info=True
                )
    finally:
        pdf.close()

    return results

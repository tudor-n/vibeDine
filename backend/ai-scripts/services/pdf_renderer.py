"""
PDF page rasterizer using pypdfium2.
Renders a single page to JPEG bytes at the requested scale.
"""
import io

import pypdfium2


def render_page(path: str, page_index: int, scale: float = 1.5, quality: int = 85) -> bytes:
    """Render the given 0-based page to JPEG bytes using pypdfium2 at given scale.

    Raises ValueError on bad index.
    """
    pdf = pypdfium2.PdfDocument(path)
    try:
        page_count = len(pdf)

        if page_index < 0 or page_index >= page_count:
            raise ValueError(
                f"page_index {page_index} is out of range for a {page_count}-page document"
            )

        page = pdf[page_index]
        bitmap = page.render(scale=scale)
        pil_image = bitmap.to_pil()

        buf = io.BytesIO()
        pil_image.save(buf, format="JPEG", quality=quality, optimize=True)
        return buf.getvalue()
    finally:
        pdf.close()

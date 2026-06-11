"""
PDF page rasterizer using pypdfium2.
Renders a single page to JPEG bytes at the requested scale.
"""
import io
from typing import Optional

import pypdfium2


class PdfPageRenderer:
    """Reusable PDF renderer that keeps one PdfDocument open across pages."""

    def __init__(
        self,
        path: str,
        *,
        scale: float = 1.5,
        quality: int = 85,
        optimize: bool = False,
    ):
        self._path = path
        self._scale = scale
        self._quality = quality
        self._optimize = optimize
        self._pdf: Optional[pypdfium2.PdfDocument] = None

    def __enter__(self) -> "PdfPageRenderer":
        self.open()
        return self

    def __exit__(self, exc_type, exc, tb) -> None:
        self.close()

    def open(self) -> None:
        if self._pdf is None:
            self._pdf = pypdfium2.PdfDocument(self._path)

    def close(self) -> None:
        if self._pdf is not None:
            self._pdf.close()
            self._pdf = None

    def render_page(self, page_index: int) -> bytes:
        """Render the given 0-based page to JPEG bytes."""
        self.open()
        assert self._pdf is not None
        page_count = len(self._pdf)

        if page_index < 0 or page_index >= page_count:
            raise ValueError(
                f"page_index {page_index} is out of range for a {page_count}-page document"
            )

        page = self._pdf[page_index]
        bitmap = page.render(scale=self._scale)
        pil_image = bitmap.to_pil()

        buf = io.BytesIO()
        pil_image.save(
            buf,
            format="JPEG",
            quality=self._quality,
            optimize=self._optimize,
        )
        return buf.getvalue()


def render_page(
    path: str,
    page_index: int,
    scale: float = 1.5,
    quality: int = 85,
    optimize: bool = False,
) -> bytes:
    """Render the given 0-based page to JPEG bytes using pypdfium2 at given scale.

    Raises ValueError on bad index.
    """
    with PdfPageRenderer(
        path,
        scale=scale,
        quality=quality,
        optimize=optimize,
    ) as renderer:
        return renderer.render_page(page_index)

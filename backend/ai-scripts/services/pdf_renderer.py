"""
PDF page rasterizer using pypdfium2.
Renders a single page to JPEG bytes at the requested scale.
"""
import io
import math
import os
from typing import Optional

import pypdfium2

# Ceiling on the rasterized output size (width_px * height_px) of a single page.
# A page declaring an enormous MediaBox would otherwise rasterize into a giant
# bitmap ("render bomb"); we clamp the effective scale to stay within this cap
# (AI-04). Env-overridable; 0 disables the clamp.
DEFAULT_MAX_RENDER_PIXELS = int(os.getenv("PDF_MAX_RENDER_PIXELS", "40000000"))


class PdfPageRenderer:
    """Reusable PDF renderer that keeps one PdfDocument open across pages."""

    def __init__(
        self,
        path: str,
        *,
        scale: float = 1.5,
        quality: int = 85,
        optimize: bool = False,
        max_pixels: int = DEFAULT_MAX_RENDER_PIXELS,
    ):
        self._path = path
        self._scale = scale
        self._quality = quality
        self._optimize = optimize
        self._max_pixels = max_pixels
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
        scale = self._effective_scale(page)
        bitmap = page.render(scale=scale)
        pil_image = bitmap.to_pil()

        buf = io.BytesIO()
        pil_image.save(
            buf,
            format="JPEG",
            quality=self._quality,
            optimize=self._optimize,
        )
        return buf.getvalue()

    def _effective_scale(self, page) -> float:
        """Clamp the render scale so the output bitmap stays within max_pixels.

        Points are 1/72 inch; at scale s a page renders to
        (width_pts * s) x (height_pts * s) pixels. If that exceeds the cap we
        shrink s to the largest value that still fits, so an oversized MediaBox
        cannot allocate a giant bitmap.
        """
        if self._max_pixels <= 0:
            return self._scale
        try:
            width_pts, height_pts = page.get_size()
        except Exception:
            return self._scale
        if width_pts <= 0 or height_pts <= 0:
            return self._scale
        projected = (width_pts * self._scale) * (height_pts * self._scale)
        if projected <= self._max_pixels:
            return self._scale
        return math.sqrt(self._max_pixels / (width_pts * height_pts))


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

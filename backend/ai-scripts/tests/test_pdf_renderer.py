"""
Tests for services/pdf_renderer.py  (TDD — written before implementation)
"""
import pytest

from tests.fixtures.sample_menus.build_fixtures import build_all
from services.pdf_renderer import render_page

_FIXTURE_PATHS = build_all()
DIGITAL_3PAGE = _FIXTURE_PATHS["digital_3page"]


JPEG_MAGIC = b"\xff\xd8\xff"


class TestRenderPage:
    def test_first_page_returns_bytes(self):
        result = render_page(DIGITAL_3PAGE, 0)
        assert isinstance(result, bytes)
        assert len(result) > 0

    def test_output_is_valid_jpeg(self):
        result = render_page(DIGITAL_3PAGE, 0)
        assert result[:3] == JPEG_MAGIC, (
            f"Expected JPEG magic bytes {JPEG_MAGIC!r}, got {result[:3]!r}"
        )

    def test_all_three_pages_render(self):
        for idx in range(3):
            result = render_page(DIGITAL_3PAGE, idx)
            assert result[:3] == JPEG_MAGIC, f"Page {idx} did not produce valid JPEG"

    def test_invalid_page_index_raises_value_error(self):
        with pytest.raises(ValueError):
            render_page(DIGITAL_3PAGE, 999)

    def test_negative_page_index_raises_value_error(self):
        with pytest.raises(ValueError):
            render_page(DIGITAL_3PAGE, -1)

    def test_custom_scale_changes_output_size(self):
        small = render_page(DIGITAL_3PAGE, 0, scale=1.0)
        large = render_page(DIGITAL_3PAGE, 0, scale=2.0)
        # Larger scale => larger JPEG bytes (generally true for the same content)
        assert len(large) > len(small)

    def test_default_scale_and_quality(self):
        # Should not raise; uses scale=1.5, quality=85
        result = render_page(DIGITAL_3PAGE, 0)
        assert result[:3] == JPEG_MAGIC

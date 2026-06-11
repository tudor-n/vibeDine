"""
Tests for services/pdf_renderer.py  (TDD — written before implementation)
"""
import pytest

from tests.fixtures.sample_menus.build_fixtures import build_all
from services import pdf_renderer
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

    def test_page_renderer_reuses_one_open_document_for_multiple_pages(self, monkeypatch):
        opened_paths: list[str] = []
        closed_count = 0
        save_kwargs: list[dict] = []

        class _FakeImage:
            def save(self, buf, **kwargs):
                save_kwargs.append(kwargs)
                buf.write(JPEG_MAGIC + b"fake")

        class _FakeBitmap:
            def to_pil(self):
                return _FakeImage()

        class _FakePage:
            def render(self, *, scale: float):
                return _FakeBitmap()

        class _FakeDocument:
            def __init__(self, path: str):
                opened_paths.append(path)

            def __len__(self):
                return 3

            def __getitem__(self, page_index: int):
                return _FakePage()

            def close(self):
                nonlocal closed_count
                closed_count += 1

        monkeypatch.setattr(pdf_renderer.pypdfium2, "PdfDocument", _FakeDocument)

        with pdf_renderer.PdfPageRenderer("fake.pdf", scale=1.25, quality=80) as renderer:
            assert renderer.render_page(0)[:3] == JPEG_MAGIC
            assert renderer.render_page(1)[:3] == JPEG_MAGIC

        assert opened_paths == ["fake.pdf"]
        assert closed_count == 1
        assert save_kwargs == [
            {"format": "JPEG", "quality": 80, "optimize": False},
            {"format": "JPEG", "quality": 80, "optimize": False},
        ]

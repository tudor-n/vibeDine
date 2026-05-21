"""
Tests for services/pdf_text_extractor.py  (TDD — written before implementation)
"""
import os
import tempfile

import pytest

from tests.fixtures.sample_menus.build_fixtures import build_all

# Paths to fixture PDFs — built once at module level so they exist when tests run
_FIXTURE_PATHS = build_all()
DIGITAL_3PAGE = _FIXTURE_PATHS["digital_3page"]
SCANNED_3PAGE = _FIXTURE_PATHS["scanned_3page"]


# ---------------------------------------------------------------------------
# Import the module under test (will fail until we create it)
# ---------------------------------------------------------------------------
from services.pdf_text_extractor import extract, PageContent
from services import pdf_text_extractor


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

class _FakePage:
    def __init__(
        self,
        *,
        text: str,
        images: list[dict],
        width: float = 100.0,
        height: float = 100.0,
    ):
        self._text = text
        self.extract_text_calls = 0
        self.images = images
        self.width = width
        self.height = height

    def extract_text(self):
        self.extract_text_calls += 1
        return self._text


class _FakePdf:
    def __init__(self, pages: list[_FakePage]):
        self.pages = pages
        self.closed = False

    def close(self):
        self.closed = True


class _FakeImageStream:
    def __init__(self, raw_data: bytes):
        self._raw_data = raw_data

    def get_rawdata(self) -> bytes:
        return self._raw_data


class TestDigitalPdf:
    def test_returns_three_pages(self):
        pages = extract(DIGITAL_3PAGE)
        assert len(pages) == 3

    def test_pages_are_page_content(self):
        pages = extract(DIGITAL_3PAGE)
        for p in pages:
            assert isinstance(p, PageContent)

    def test_page_numbers_are_zero_based(self):
        pages = extract(DIGITAL_3PAGE)
        assert [p.page_number for p in pages] == [0, 1, 2]

    def test_text_density_positive(self):
        pages = extract(DIGITAL_3PAGE)
        for p in pages:
            assert p.text_density > 0, f"page {p.page_number} should have text density > 0"

    def test_has_images_false_for_pure_text_pages(self):
        pages = extract(DIGITAL_3PAGE)
        for p in pages:
            assert p.has_images is False, f"page {p.page_number} should not report embedded images"

    def test_text_contains_menu_items(self):
        pages = extract(DIGITAL_3PAGE)
        all_text = " ".join(p.text for p in pages)
        assert "APERITIVE" in all_text
        assert "lei" in all_text.lower() or "RON" in all_text

    def test_width_and_height_positive(self):
        pages = extract(DIGITAL_3PAGE)
        for p in pages:
            assert p.width_pts > 0
            assert p.height_pts > 0


class TestScannedPdf:
    def test_returns_three_pages(self):
        pages = extract(SCANNED_3PAGE)
        assert len(pages) == 3

    def test_image_dominant_page_skips_extract_text(self, monkeypatch):
        page = _FakePage(
            text="This text would be expensive to ask pdfplumber for",
            images=[{"width": 95.0, "height": 95.0}],
        )
        fake_pdf = _FakePdf([page])
        monkeypatch.setattr(
            pdf_text_extractor.pdfplumber,
            "open",
            lambda path: fake_pdf,
        )

        pages = extract("fake-scanned.pdf")

        assert len(pages) == 1
        assert pages[0].text == ""
        assert pages[0].text_density == 0.0
        assert pages[0].has_images is True
        assert pages[0].width_pts == 100.0
        assert pages[0].height_pts == 100.0
        assert page.extract_text_calls == 0
        assert fake_pdf.closed is True

    def test_image_dominant_page_preserves_embedded_jpeg_bytes(self, monkeypatch):
        embedded_jpeg = b"\xff\xd8\xffembedded-jpeg"
        page = _FakePage(
            text="This text would be expensive to ask pdfplumber for",
            images=[
                {
                    "width": 95.0,
                    "height": 95.0,
                    "stream": _FakeImageStream(embedded_jpeg),
                }
            ],
        )
        fake_pdf = _FakePdf([page])
        monkeypatch.setattr(
            pdf_text_extractor.pdfplumber,
            "open",
            lambda path: fake_pdf,
        )

        pages = extract("fake-scanned.pdf")

        assert len(pages) == 1
        assert pages[0].text == ""
        assert pages[0].embedded_image_bytes == embedded_jpeg
        assert page.extract_text_calls == 0
        assert fake_pdf.closed is True

    def test_small_image_page_still_extracts_text(self, monkeypatch):
        page = _FakePage(
            text="Pizza 25,00 lei\nPasta 31,00 lei",
            images=[{"width": 12.0, "height": 10.0}],
        )
        fake_pdf = _FakePdf([page])
        monkeypatch.setattr(
            pdf_text_extractor.pdfplumber,
            "open",
            lambda path: fake_pdf,
        )

        pages = extract("fake-mixed.pdf")

        assert len(pages) == 1
        assert pages[0].text == "Pizza 25,00 lei Pasta 31,00 lei"
        assert pages[0].text_density > 0
        assert pages[0].has_images is True
        assert page.extract_text_calls == 1
        assert fake_pdf.closed is True

    def test_text_is_essentially_empty(self):
        pages = extract(SCANNED_3PAGE)
        for p in pages:
            # pdfplumber may extract a tiny amount from JPEG markers; we allow < 20 chars
            assert len(p.text.strip()) < 20, (
                f"page {p.page_number} of scanned PDF should have no real text, got: {p.text!r}"
            )

    def test_has_images_true(self):
        pages = extract(SCANNED_3PAGE)
        for p in pages:
            assert p.has_images is True, f"page {p.page_number} should report embedded images"

    def test_text_density_near_zero(self):
        pages = extract(SCANNED_3PAGE)
        for p in pages:
            assert p.text_density < 0.001, (
                f"page {p.page_number} should have near-zero text density, got {p.text_density}"
            )


class TestErrorHandling:
    def test_nonexistent_path_returns_empty_list(self):
        result = extract("/totally/nonexistent/path/menu.pdf")
        assert result == []

    def test_nonexistent_path_does_not_raise(self):
        # Should silently return []
        try:
            result = extract("/totally/nonexistent/path/menu.pdf")
        except Exception as exc:
            pytest.fail(f"extract() raised {exc!r} for non-existent file; expected []")

    def test_corrupted_pdf_returns_empty_list(self):
        with tempfile.NamedTemporaryFile(suffix=".pdf", delete=False) as f:
            f.write(b"%PDF-1.4 INVALID GARBAGE BYTES \x00\x01\x02")
            f.flush()
            corrupted_path = f.name
        try:
            result = extract(corrupted_path)
            assert result == []
        finally:
            os.unlink(corrupted_path)

    def test_corrupted_pdf_does_not_raise(self):
        with tempfile.NamedTemporaryFile(suffix=".pdf", delete=False) as f:
            f.write(b"NOT A PDF AT ALL \xff\x00 GARBAGE")
            f.flush()
            garbage_path = f.name
        try:
            extract(garbage_path)
        except Exception as exc:
            pytest.fail(f"extract() raised {exc!r} for corrupted file; expected []")
        finally:
            os.unlink(garbage_path)

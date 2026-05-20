"""
Tests for services/page_classifier.py  (TDD — written before implementation)
"""
import pytest

from services.pdf_text_extractor import PageContent
from services.page_classifier import classify, PageKind


def _make_content(
    text: str,
    page_number: int = 0,
    has_images: bool = False,
    width_pts: float = 595.0,
    height_pts: float = 842.0,
) -> PageContent:
    area = width_pts * height_pts
    density = len(text.strip()) / area if area > 0 else 0.0
    return PageContent(
        page_number=page_number,
        text=text,
        text_density=density,
        has_images=has_images,
        width_pts=width_pts,
        height_pts=height_pts,
    )


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

class TestClassifyTextPage:
    def test_rich_text_with_prices_is_text(self):
        text = (
            "APERITIVE\n"
            "Bruschetă cu roșii ..... 12,50 lei\n"
            "Platou mezeluri ..... 38,00 RON\n"
            "Supă cremă de dovleac ..... 18,00 lei\n"
            "Ardei umpluți cu brânză ..... 22,90 lei\n"
            "Salată orientală ..... 15,00 lei\n"
        )
        result = classify(_make_content(text))
        assert result == "text"

    def test_exactly_three_prices_plus_words_is_text(self):
        # Exactly 3 currency matches + words >= 4 chars => "text"
        text = (
            "Preparate speciale\n"
            "Pizza Margherita 25,00 lei\n"
            "Paste carbonara 30,00 RON\n"
            "Risotto cu ciuperci 35,00 lei\n"
        )
        result = classify(_make_content(text))
        assert result == "text"

    def test_euros_count_as_currency(self):
        text = (
            "Specialitati casa\n"
            "Ciorba de vacuta 12,00 €\n"
            "Friptura de vitel 45,00 €\n"
            "Tort de ciocolata 18,00 €\n"
            "Inghetata vanilie 10,00 €\n"
        )
        result = classify(_make_content(text))
        assert result == "text"


class TestClassifyImagePage:
    def test_empty_text_is_image(self):
        result = classify(_make_content(""))
        assert result == "image"

    def test_very_short_text_is_image(self):
        # < 30 chars after strip
        result = classify(_make_content("Menu"))
        assert result == "image"

    def test_text_with_no_prices_and_no_toc_is_image(self):
        # Decorative welcome text — no prices, no cuprins keywords
        text = "Bun venit la restaurantul nostru! Vă dorim poftă bună!"
        result = classify(_make_content(text))
        assert result == "image"

    def test_fewer_than_three_prices_falls_back_to_image(self):
        # Only 2 prices — should NOT be "text" or "skip"
        text = (
            "Bine ați venit la restaurant\n"
            "Pizza 25,00 lei\n"
            "Paste 30,00 RON\n"
        )
        result = classify(_make_content(text))
        assert result == "image"


class TestClassifySkipPage:
    def test_cuprins_with_no_prices_is_skip(self):
        text = "Cuprins\n1. Aperitive\n2. Felul principal\n3. Deserturi\n4. Bauturi"
        result = classify(_make_content(text))
        assert result == "skip"

    def test_table_of_contents_with_no_prices_is_skip(self):
        text = "Table of contents\n1. Starters\n2. Main course\n3. Desserts"
        result = classify(_make_content(text))
        assert result == "skip"

    def test_meniu_cuprins_phrase_is_skip(self):
        text = "Meniu cuprins\nAperitive pag. 2\nFel principal pag. 5"
        result = classify(_make_content(text))
        assert result == "skip"

    def test_cuprins_with_prices_is_NOT_skip(self):
        # Even if "cuprins" appears, if there are prices it should not be skip
        text = (
            "Cuprins\n"
            "Aperitive: Bruschete 12,00 lei, Platou 38,00 RON, Supa 18,00 lei\n"
            "Also 25,00 lei for the soup"
        )
        result = classify(_make_content(text))
        # Should be "text" or "image" but not "skip"
        assert result != "skip"


class TestReturnType:
    def test_return_is_valid_page_kind(self):
        for text in ["", "Cuprins\n1. intro", "APERITIVE\nBruschete 12,50 lei item 38,00 RON soup 22,90 lei"]:
            result = classify(_make_content(text))
            assert result in ("text", "image", "skip")

"""
Tests for services/dedup.py  (TDD — written before implementation)
"""
import pytest

from models.menu_extraction import ExtractedMenuItem
from services.dedup import dedup_items, normalize_name


def _item(
    name: str,
    price: float,
    source_page: int = 0,
    source_index: int = 0,
    description: str | None = None,
    category_hint: str | None = None,
    confidence: float | None = None,
) -> ExtractedMenuItem:
    return ExtractedMenuItem(
        name=name,
        price=price,
        source_page=source_page,
        source_index=source_index,
        description=description,
        category_hint=category_hint,
        confidence=confidence,
    )


# ---------------------------------------------------------------------------
# normalize_name tests
# ---------------------------------------------------------------------------

class TestNormalizeName:
    def test_lowercase(self):
        assert normalize_name("Pizza MARGHERITA") == "pizza margherita"

    def test_collapses_whitespace(self):
        assert normalize_name("pizza   margherita") == "pizza margherita"

    def test_strips_leading_trailing_punctuation(self):
        assert normalize_name(".Pizza Margherita!") == "pizza margherita"

    def test_strips_hyphens(self):
        assert normalize_name("- Pizza -") == "pizza"

    def test_empty_string(self):
        assert normalize_name("") == ""

    def test_leaves_diacritics_intact(self):
        assert normalize_name("Ciorbă de Burtă") == "ciorbă de burtă"


# ---------------------------------------------------------------------------
# dedup_items tests
# ---------------------------------------------------------------------------

class TestDedupItems:
    def test_empty_input_returns_empty(self):
        assert dedup_items([]) == []

    def test_single_item_returned_unchanged(self):
        items = [_item("Pizza Margherita", 25.0)]
        result = dedup_items(items)
        assert len(result) == 1
        assert result[0].name == "Pizza Margherita"

    def test_exact_duplicate_deduped_to_one(self):
        items = [
            _item("Pizza Margherita", 25.0, source_page=0),
            _item("Pizza Margherita", 25.0, source_page=1),
        ]
        result = dedup_items(items)
        assert len(result) == 1

    def test_same_name_same_price_with_different_decimal_representation(self):
        # 25.0 and 25.00 round to same value
        items = [
            _item("Pizza Margherita", 25.0),
            _item("Pizza Margherita", 25.00),
        ]
        result = dedup_items(items)
        assert len(result) == 1

    def test_same_name_different_price_kept_distinct(self):
        items = [
            _item("Pizza Margherita", 25.0),
            _item("Pizza Margherita", 30.0),
        ]
        result = dedup_items(items)
        assert len(result) == 2

    def test_different_names_same_price_kept_distinct(self):
        items = [
            _item("Pizza Margherita", 25.0),
            _item("Pasta Carbonara", 25.0),
        ]
        result = dedup_items(items)
        assert len(result) == 2

    def test_merge_keeps_earliest_source_page(self):
        # "earliest" means the lowest page number in document order
        items = [
            _item("Pizza Margherita", 25.0, source_page=3),
            _item("Pizza Margherita", 25.0, source_page=0),
        ]
        result = dedup_items(items)
        assert result[0].source_page == 0  # page 0 appears before page 3

    def test_merge_keeps_longest_description(self):
        items = [
            _item("Pizza Margherita", 25.0, description="Short"),
            _item("Pizza Margherita", 25.0, description="Much longer description with more detail"),
        ]
        result = dedup_items(items)
        assert result[0].description == "Much longer description with more detail"

    def test_merge_takes_max_confidence(self):
        items = [
            _item("Pizza Margherita", 25.0, confidence=0.7),
            _item("Pizza Margherita", 25.0, confidence=0.95),
        ]
        result = dedup_items(items)
        assert result[0].confidence == 0.95

    def test_merge_takes_first_non_empty_category_hint(self):
        items = [
            _item("Pizza Margherita", 25.0, category_hint=None),
            _item("Pizza Margherita", 25.0, category_hint="PIZZA"),
        ]
        result = dedup_items(items)
        assert result[0].category_hint == "PIZZA"

    def test_merge_first_category_hint_wins_if_set(self):
        items = [
            _item("Pizza Margherita", 25.0, category_hint="APERITIVE"),
            _item("Pizza Margherita", 25.0, category_hint="PIZZA"),
        ]
        result = dedup_items(items)
        assert result[0].category_hint == "APERITIVE"

    def test_preserves_insertion_order(self):
        items = [
            _item("C Item", 10.0),
            _item("A Item", 20.0),
            _item("B Item", 30.0),
        ]
        result = dedup_items(items)
        assert [r.name for r in result] == ["C Item", "A Item", "B Item"]

    def test_normalizes_case_for_dedup_key(self):
        # "pizza margherita" and "Pizza Margherita" should be treated as duplicates
        items = [
            _item("pizza margherita", 25.0),
            _item("Pizza Margherita", 25.0),
        ]
        result = dedup_items(items)
        assert len(result) == 1

    def test_merge_none_description_with_real_description(self):
        items = [
            _item("Pizza Margherita", 25.0, description=None),
            _item("Pizza Margherita", 25.0, description="Some description"),
        ]
        result = dedup_items(items)
        assert result[0].description == "Some description"

    def test_merge_both_none_confidence_stays_none(self):
        items = [
            _item("Pizza Margherita", 25.0, confidence=None),
            _item("Pizza Margherita", 25.0, confidence=None),
        ]
        result = dedup_items(items)
        assert result[0].confidence is None

    def test_three_items_two_unique(self):
        items = [
            _item("Ciorbă de burtă", 24.9, source_page=0),
            _item("Grătar mixt", 45.0, source_page=0),
            _item("Ciorbă de burtă", 24.9, source_page=1),
        ]
        result = dedup_items(items)
        assert len(result) == 2
        names = [r.name for r in result]
        assert "Grătar mixt" in names

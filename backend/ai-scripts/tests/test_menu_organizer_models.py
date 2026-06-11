import pytest
from pydantic import ValidationError

from models.menu_organizer import (
    ItemRef,
    OrganizedCategory,
    OrganizedSubcategory,
    OrganizerResult,
)


def test_itemref_confidence_range():
    ItemRef(source_page=0, source_index=1, confidence=0.5)
    with pytest.raises(ValidationError):
        ItemRef(source_page=0, source_index=1, confidence=1.5)


def test_organizer_result_minimum_valid():
    r = OrganizerResult(source="ai", categories=[], uncategorized_item_refs=[], model_notes=None)
    assert r.source == "ai"


def test_organizer_result_rejects_bad_source():
    with pytest.raises(ValidationError):
        OrganizerResult(source="random", categories=[], uncategorized_item_refs=[])


def test_organized_subcategory_requires_name_and_sort_order():
    OrganizedSubcategory(name="Classic", sort_order=0, item_refs=[])
    with pytest.raises(ValidationError):
        OrganizedSubcategory(name="", sort_order=0, item_refs=[])


def test_organized_category_full_shape():
    cat = OrganizedCategory(
        name="Pizza",
        sort_order=0,
        subcategories=[OrganizedSubcategory(name="Classic", sort_order=0, item_refs=[])],
        direct_item_refs=[ItemRef(source_page=0, source_index=2, confidence=0.6)],
    )
    assert cat.name == "Pizza"
    assert len(cat.subcategories) == 1

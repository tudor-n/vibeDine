from models.menu_extraction import ExtractedMenuItem
from models.menu_organizer import (
    ItemRef, OrganizedCategory, OrganizedSubcategory, OrganizerResult,
)
from services.menu_organizer import (
    OrganizerOptions, build_hint_fallback, merge_organizer_results,
)

def _item(page, idx, hint, name="Item", price=10.0):
    return ExtractedMenuItem(name=name, price=price, source_page=page, source_index=idx,
                             description=None, category_hint=hint)

def _ref(p, i, c=0.9):
    return ItemRef(source_page=p, source_index=i, confidence=c)

def test_hint_fallback_groups_by_distinct_hints():
    items = [
        _item(0, 0, "Pizza", "Margherita"),
        _item(0, 1, "Pizza", "Quattro"),
        _item(0, 2, "Salads", "Caesar"),
    ]
    out = build_hint_fallback(items, OrganizerOptions())
    assert out.source == "hint-fallback"
    names = {c.name for c in out.categories}
    assert names == {"Pizza", "Salads"}
    assert sum(len(c.direct_item_refs) for c in out.categories) == 3
    for c in out.categories:
        assert c.subcategories == []

def test_hint_fallback_noise_hints_become_uncategorized():
    items = [_item(0, 0, "OTHERS", "X"), _item(0, 1, None, "Y")]
    out = build_hint_fallback(items, OrganizerOptions())
    assert len(out.categories) == 0
    assert len(out.uncategorized_item_refs) == 2
    assert all(r.confidence == 0.0 for r in out.uncategorized_item_refs)

def test_hint_fallback_strips_diacritics_for_blocklist_matching():
    items = [_item(0, 0, "Altele", "Z")]
    out = build_hint_fallback(items, OrganizerOptions())
    assert len(out.uncategorized_item_refs) == 1

def test_hint_fallback_short_hint_is_noise():
    items = [_item(0, 0, "X", "A")]
    out = build_hint_fallback(items, OrganizerOptions())
    assert len(out.categories) == 0
    assert len(out.uncategorized_item_refs) == 1


def _covered(res):
    keys = set()
    for c in res.categories:
        for r in c.direct_item_refs:
            keys.add((r.source_page, r.source_index))
        for s in c.subcategories:
            for r in s.item_refs:
                keys.add((r.source_page, r.source_index))
    for r in res.uncategorized_item_refs:
        keys.add((r.source_page, r.source_index))
    return keys


def test_merge_combines_categories_by_name_and_reconciles_missing():
    items = [_item(0, i, "X") for i in range(5)]
    a = OrganizerResult(source="ai", categories=[
        OrganizedCategory(name="Mains", sort_order=0, direct_item_refs=[_ref(0, 0), _ref(0, 1)]),
    ])
    b = OrganizerResult(source="ai", categories=[
        OrganizedCategory(name="Mains", sort_order=0, direct_item_refs=[_ref(0, 2)]),
        OrganizedCategory(name="Drinks", sort_order=1, direct_item_refs=[_ref(0, 3)]),
    ])
    merged = merge_organizer_results([a, b], items, OrganizerOptions())
    by_name = {c.name: c for c in merged.categories}
    assert set(by_name) == {"Mains", "Drinks"}
    assert {(r.source_page, r.source_index) for r in by_name["Mains"].direct_item_refs} == {(0, 0), (0, 1), (0, 2)}
    # Item 4 was placed by no chunk → reconciled into uncategorized (no item lost).
    assert (0, 4) in {(r.source_page, r.source_index) for r in merged.uncategorized_item_refs}
    assert _covered(merged) == {(0, i) for i in range(5)}
    assert merged.source == "ai"


def test_merge_merges_subcategories_and_dedupes_refs():
    items = [_item(0, i, "X") for i in range(3)]
    a = OrganizerResult(source="ai", categories=[
        OrganizedCategory(name="Soups", sort_order=0,
                          subcategories=[OrganizedSubcategory(name="Hot", sort_order=0, item_refs=[_ref(0, 0)])]),
    ])
    b = OrganizerResult(source="ai", categories=[
        OrganizedCategory(name="Soups", sort_order=0,
                          subcategories=[OrganizedSubcategory(name="Hot", sort_order=0, item_refs=[_ref(0, 1), _ref(0, 0)])]),
    ])
    merged = merge_organizer_results([a, b], items, OrganizerOptions())
    assert len(merged.categories) == 1
    sub = merged.categories[0].subcategories[0]
    assert {(r.source_page, r.source_index) for r in sub.item_refs} == {(0, 0), (0, 1)}  # (0,0) not duplicated
    assert {(r.source_page, r.source_index) for r in merged.uncategorized_item_refs} == {(0, 2)}
    assert _covered(merged) == {(0, 0), (0, 1), (0, 2)}

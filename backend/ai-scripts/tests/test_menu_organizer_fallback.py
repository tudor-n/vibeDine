from models.menu_extraction import ExtractedMenuItem
from services.menu_organizer import OrganizerOptions, build_hint_fallback

def _item(page, idx, hint, name="Item", price=10.0):
    return ExtractedMenuItem(name=name, price=price, source_page=page, source_index=idx,
                             description=None, category_hint=hint)

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

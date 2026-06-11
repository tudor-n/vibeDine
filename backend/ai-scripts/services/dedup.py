"""
Deduplication logic for extracted menu items.
"""
import re

from models.menu_extraction import ExtractedMenuItem


def normalize_name(name: str) -> str:
    """Lowercase, collapse whitespace, strip leading/trailing punctuation.
    Romanian diacritics are preserved.
    """
    lowered = name.lower()
    # Collapse internal whitespace
    collapsed = re.sub(r"\s+", " ", lowered).strip()
    # Strip leading/trailing punctuation (non-word, non-letter, non-digit)
    stripped = re.sub(r"^[^\wÀ-ɏ]+|[^\wÀ-ɏ]+$", "", collapsed, flags=re.UNICODE)
    return stripped


def dedup_items(items: list[ExtractedMenuItem]) -> list[ExtractedMenuItem]:
    """Dedup by (normalize_name(name), round(price, 2)).

    On collision: keep the earliest source_page, take the longest description,
    max confidence, first non-empty category_hint. Preserves first-seen order.
    """
    # key -> merged item (preserves first-seen insertion order via dict)
    seen: dict[tuple[str, float], ExtractedMenuItem] = {}

    for item in items:
        key = (normalize_name(item.name), round(item.price, 2))

        if key not in seen:
            seen[key] = item
        else:
            existing = seen[key]

            # Keep earliest source_page
            best_page = min(existing.source_page, item.source_page)

            # Take the longest description (prefer non-None over None)
            best_desc = existing.description
            new_desc = item.description
            if new_desc is not None:
                if best_desc is None or len(new_desc) > len(best_desc):
                    best_desc = new_desc

            # Max confidence (handle None)
            if existing.confidence is None and item.confidence is None:
                best_conf = None
            elif existing.confidence is None:
                best_conf = item.confidence
            elif item.confidence is None:
                best_conf = existing.confidence
            else:
                best_conf = max(existing.confidence, item.confidence)

            # First non-empty category_hint
            best_cat = existing.category_hint if existing.category_hint is not None else item.category_hint

            seen[key] = ExtractedMenuItem(
                name=existing.name,
                price=existing.price,
                source_page=best_page,
                source_index=existing.source_index,
                description=best_desc,
                category_hint=best_cat,
                confidence=best_conf,
            )

    return list(seen.values())

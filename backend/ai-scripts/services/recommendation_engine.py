from collections import defaultdict

from models.schemas import CartItem, MenuItem, Recommendation


_CATEGORY_BOOSTS: list[tuple[list[str], str, float]] = [
    (["Feluri principale", "Salate"], "Băuturi", 0.2),
    ([], "Deserturi", 0.15),  # empty trigger list = any non-empty cart
]


def _matches_hint(hint: str, text: str) -> bool:
    return hint.lower() in text.lower()


def score_recommendations(
    cart_items: list[CartItem],
    menu_items: list[MenuItem],
    order_history: list[list[str]],
    active_directives: list[dict],
    top_n: int = 5,
) -> list[Recommendation]:
    cart_ids = {item.product_id for item in cart_items}
    if not menu_items:
        return []

    candidates: dict[str, dict] = {}
    for mi in menu_items:
        if mi.product_id not in cart_ids:
            candidates[mi.product_id] = {
                "menu_item": mi,
                "score": 0.0,
                "reasons": defaultdict(float),
            }

    if not candidates:
        return []

    # Layer 1: Directive boost
    for directive in active_directives:
        rules = directive.get("rules", [])
        if isinstance(rules, list):
            directive_rules = rules
        else:
            directive_rules = []

        for rule in directive_rules:
            action = rule.get("action")
            priority = rule.get("priority", 0.8)

            if action == "recommend":
                for hint in rule.get("item_hints", []):
                    for cid, cdata in candidates.items():
                        if _matches_hint(hint, cdata["menu_item"].name):
                            cdata["score"] += priority
                            cdata["reasons"]["directive"] += priority

            elif action == "pair":
                item_hint = rule.get("item_hint", "")
                with_hint = rule.get("with_hint", "")
                cart_match = any(
                    _matches_hint(with_hint, ci.category or "")
                    or _matches_hint(with_hint, ci.product_id)
                    for ci in cart_items
                )
                if cart_match:
                    for cid, cdata in candidates.items():
                        if _matches_hint(item_hint, cdata["menu_item"].name):
                            cdata["score"] += priority
                            cdata["reasons"]["directive"] += priority

    # Layer 2: Co-occurrence boost
    if order_history:
        co_occ: dict[str, float] = defaultdict(float)
        for order in order_history:
            order_set = set(order)
            for cart_item in cart_items:
                if cart_item.product_id in order_set:
                    for pid in order_set:
                        if pid != cart_item.product_id:
                            co_occ[pid] += 1.0

        max_co = max(co_occ.values()) if co_occ else 1.0
        for pid, count in co_occ.items():
            if pid in candidates:
                boost = (count / max_co) * 0.5
                candidates[pid]["score"] += boost
                candidates[pid]["reasons"]["pairing"] += boost

    # Layer 3: Category complement
    cart_categories = {ci.category for ci in cart_items if ci.category}
    for trigger_cats, target_cat, boost_val in _CATEGORY_BOOSTS:
        should_boost = (
            not trigger_cats and len(cart_items) > 0
        ) or any(tc in cart_categories for tc in trigger_cats)

        if should_boost:
            for cid, cdata in candidates.items():
                if cdata["menu_item"].category == target_cat:
                    cdata["score"] += boost_val
                    cdata["reasons"]["category"] += boost_val

    # Sort and pick top N
    ranked = sorted(candidates.values(), key=lambda c: c["score"], reverse=True)
    results: list[Recommendation] = []
    for entry in ranked[:top_n]:
        if entry["score"] <= 0:
            break
        best_reason = max(entry["reasons"], key=entry["reasons"].get)
        results.append(Recommendation(
            product_id=entry["menu_item"].product_id,
            reason=best_reason,
            confidence=round(min(entry["score"], 1.0), 4),
        ))

    return results

import re


_RECOMMEND_PATTERN = re.compile(
    r"(recomand[ăa]|recommend|sugereaz[ăa]|suggest)\s+(.+)",
    re.IGNORECASE,
)

_PAIR_PATTERN = re.compile(
    r"(promoveaz[ăa]|promote|asociaz[ăa]|pair)\s+(.+?)\s+(cu|with)\s+(.+)",
    re.IGNORECASE,
)

_END_CONDITION = re.compile(
    r"(la finalul|la sfâr[șs]itul|at the end|end of)",
    re.IGNORECASE,
)

_SENTENCE_SPLIT = re.compile(r"[.!?]+")

_ITEM_SPLIT = re.compile(r"\s*(?:,|\s+[șs]i\s+|\s+and\s+)\s*", re.IGNORECASE)


def _clean(text: str) -> str:
    text = _END_CONDITION.sub("", text)
    text = re.sub(
        r"(oric[ăa]rei comenzi|oric[ăa]rei mese|any order|every order|comenzii|mesei)",
        "",
        text,
        flags=re.IGNORECASE,
    )
    return text.strip().strip(".,;:!? ")


def _extract_items(text: str) -> list[str]:
    cleaned = _clean(text)
    parts = _ITEM_SPLIT.split(cleaned)
    return [p.strip() for p in parts if p.strip()]


def parse_directive(raw_prompt: str) -> dict:
    sentences = [s.strip() for s in _SENTENCE_SPLIT.split(raw_prompt) if s.strip()]
    rules: list[dict] = []

    for sentence in sentences:
        pair_match = _PAIR_PATTERN.search(sentence)
        if pair_match:
            item_hints = _extract_items(pair_match.group(2))
            with_hints = _extract_items(pair_match.group(4))
            for item in item_hints:
                for target in with_hints:
                    rules.append({
                        "action": "pair",
                        "item_hint": item,
                        "with_hint": target,
                        "priority": 0.8,
                    })
            continue

        rec_match = _RECOMMEND_PATTERN.search(sentence)
        if rec_match:
            condition = "end_of_order" if _END_CONDITION.search(sentence) else "always"
            item_hints = _extract_items(rec_match.group(2))
            rules.append({
                "action": "recommend",
                "item_hints": item_hints,
                "condition": condition,
                "priority": 0.9,
            })

    return {"rules": rules}

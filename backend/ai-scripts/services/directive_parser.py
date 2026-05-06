import re


_RECOMMEND_PATTERN = re.compile(
    r"(recomand[ăa]|recommend|sugereaz[ăa]|suggest|ofer[ăa]|offer)\s+(.+)",
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

# Discount phrases like "la 30% reducere", "cu 30% off", "30% de reducere", "at 30%".
# The (?:de|of)\b form prevents "of" from being eaten out of the word "off".
_DISCOUNT_PHRASE = re.compile(
    r"\b(la|cu|at|with|de|pe)?\s*\d{1,3}\s*%(?:\s*(?:de|of)\b)?\s*(?:off|reducere|reducer[ie])?",
    re.IGNORECASE,
)

# Standalone discount keywords left over after stripping the percent phrase
_DISCOUNT_WORD = re.compile(r"\b(off|reducere|reducer[ie])\b", re.IGNORECASE)

# Minimum-order-total clauses like "peste 190 de lei", "over 190 RON", "at least 50 lei"
_THRESHOLD = re.compile(
    r"\b(peste|over|above|cel\s+pu[țt]in|at\s+least|min(?:imum)?)\s+(\d+(?:[.,]\d+)?)\s*(?:de\s+)?(?:lei|ron)?",
    re.IGNORECASE,
)


def _extract_threshold(text: str) -> float | None:
    m = _THRESHOLD.search(text)
    if not m:
        return None
    raw = m.group(2).replace(",", ".")
    try:
        return float(raw)
    except ValueError:
        return None


def _clean(text: str) -> str:
    text = _END_CONDITION.sub("", text)
    text = re.sub(
        r"(oric[ăa]rei comenzi|oric[ăa]rei mese|any order|every order|comenzii|mesei)",
        "",
        text,
        flags=re.IGNORECASE,
    )
    text = _DISCOUNT_PHRASE.sub("", text)
    text = _DISCOUNT_WORD.sub("", text)
    text = _THRESHOLD.sub("", text)
    text = re.sub(r"\s+", " ", text)  # collapse extra whitespace
    return text.strip().strip(".,;:!? ")


def _extract_items(text: str) -> list[str]:
    cleaned = _clean(text)
    parts = _ITEM_SPLIT.split(cleaned)
    return [p.strip() for p in parts if p.strip()]


def parse_directive(raw_prompt: str) -> dict:
    sentences = [s.strip() for s in _SENTENCE_SPLIT.split(raw_prompt) if s.strip()]
    rules: list[dict] = []

    for sentence in sentences:
        threshold = _extract_threshold(sentence)

        pair_match = _PAIR_PATTERN.search(sentence)
        if pair_match:
            item_hints = _extract_items(pair_match.group(2))
            with_hints = _extract_items(pair_match.group(4))
            for item in item_hints:
                for target in with_hints:
                    rule = {
                        "action": "pair",
                        "item_hint": item,
                        "with_hint": target,
                        "priority": 0.8,
                    }
                    if threshold is not None:
                        rule["min_order_amount"] = threshold
                    rules.append(rule)
            continue

        rec_match = _RECOMMEND_PATTERN.search(sentence)
        if rec_match:
            condition = "end_of_order" if _END_CONDITION.search(sentence) else "always"
            item_hints = _extract_items(rec_match.group(2))
            rule = {
                "action": "recommend",
                "item_hints": item_hints,
                "condition": condition,
                "priority": 0.9,
            }
            if threshold is not None:
                rule["min_order_amount"] = threshold
            rules.append(rule)

    return {"rules": rules}

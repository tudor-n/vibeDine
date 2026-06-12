"""
Heuristic page classifier: decides whether a PDF page should be handled
as extracted text, sent to vision, or skipped entirely.
"""
import re
from typing import TYPE_CHECKING, Literal

if TYPE_CHECKING:
    from services.pdf_text_extractor import PageContent

PageKind = Literal["text", "image", "skip"]

# Matches: 12,50 lei  /  38,00 RON  /  12.50 €
# Note: \b does not work after € (non-word char), so we use a lookahead for
# word-chars (lei/RON) OR a non-letter-digit character / end for €.
_CURRENCY_RE = re.compile(
    r"\d+[,.]\d{2}\s*(?:lei\b|RON\b|€(?![A-Za-z0-9]))",
    re.IGNORECASE,
)

# TOC/cover trigger words
_SKIP_PHRASES = {"cuprins", "meniu cuprins", "table of contents"}


def classify(content: "PageContent") -> PageKind:
    """Classify a page into 'text', 'image', or 'skip' using simple heuristics."""
    text = content.text

    # Rule 1: Very little text → treat as image (will be sent to vision)
    if len(text.strip()) < 30:
        return "image"

    lower = text.lower()
    currency_matches = _CURRENCY_RE.findall(text)

    # Rule 2: Cover / TOC sniff — only if no prices present
    if len(currency_matches) == 0:
        for phrase in _SKIP_PHRASES:
            if phrase in lower:
                return "skip"

    # Rule 3: Enough prices + at least one real word (>= 4 chars) → parseable text page
    if len(currency_matches) >= 3:
        words = re.findall(r"[A-Za-zÀ-ÿ]{4,}", text)
        if words:
            return "text"

    # Fallback — safe: send to vision
    return "image"

"""
Text-based menu extraction providers (Packet D).

Mirrors services/vision_extractor.py in shape and style but operates on
pre-extracted page text (BatchTextExtractionPage) rather than raw image bytes.
"""
import json
import os
from typing import Any, Optional, Protocol

import httpx
from anthropic import AsyncAnthropic, APIStatusError

from models.menu_extraction import BatchTextExtractionPage


# ---------------------------------------------------------------------------
# Error type
# ---------------------------------------------------------------------------

class TextExtractionError(Exception):
    """Raised when the underlying text-extraction provider fails."""


# ---------------------------------------------------------------------------
# Protocol
# ---------------------------------------------------------------------------

class TextExtractor(Protocol):
    model_name: str

    async def extract(self, pages: list[BatchTextExtractionPage]) -> dict[str, Any]:
        """Return the raw JSON dict the provider produced for a batch of pages.

        The dict shape MUST be::

            {"items": [
                {"name": str, "price": float, "source_page": int,
                 "source_index": int, "category_hint": str|null},
                ...
            ]}

        where ``source_page`` is the ``page_number`` passed in the input page
        (NOT relative to the batch position).
        """
        ...


# ---------------------------------------------------------------------------
# FakeTextExtractor — in-memory stand-in for tests
# ---------------------------------------------------------------------------

class FakeTextExtractor:
    """In-memory stand-in for tests. Either returns canned_response or raises `raises`."""

    def __init__(
        self,
        canned_response: Optional[dict[str, Any]] = None,
        raises: Optional[Exception] = None,
        model_name: str = "fake-text-model",
    ):
        if (canned_response is None) == (raises is None):
            raise ValueError("Provide exactly one of canned_response or raises.")
        self._canned = canned_response
        self._raises = raises
        self.model_name = model_name

    async def extract(self, pages: list[BatchTextExtractionPage]) -> dict[str, Any]:
        if self._raises is not None:
            raise self._raises
        assert self._canned is not None
        return self._canned


# ---------------------------------------------------------------------------
# Shared extraction prompt & JSON schema
# ---------------------------------------------------------------------------

TEXT_EXTRACTION_PROMPT_HEADER = """\
Extract every menu item with a printed price from the page TEXT below.

The text below contains one or more pages, each delimited by lines like \
"--- PAGE N ---" (where N is the 0-based page number from the original PDF).

Rules:
- Include only rows where a price is printed. Skip section headings, decorative \
captions, restaurant name/contact info, footnotes.
- For `category_hint`: use the literal section heading printed above this item \
(e.g. "APERITIVE", "Fel principal"). Leave null if the item sits outside any \
heading. Do NOT invent a heading.
- Keep Romanian diacritics (ă, â, î, ș, ț).
- Prices in RON: convert "24,90 lei" / "24.90 RON" / "24,90" → 24.90.
- If a price range or "de la X", use the lower bound.
- If an item has multiple prices (small/large), emit one row per price, append \
the qualifier to name (e.g. "Salată Caesar (mică)").
- `source_page` is the page number from the delimiter ("--- PAGE N ---").
- `source_index` is the 0-based reading order WITHIN that page (top-to-bottom).
- If you can't read an item clearly, skip it. Do not invent items.

"""

TEXT_ITEM_JSON_SCHEMA = {
    "type": "object",
    "properties": {
        "items": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "name": {"type": "string"},
                    "price": {"type": "number"},
                    "source_page": {"type": "integer", "minimum": 0},
                    "source_index": {"type": "integer", "minimum": 0},
                    "category_hint": {"type": ["string", "null"]},
                },
                "required": ["name", "price", "source_page", "source_index"],
            },
        }
    },
    "required": ["items"],
}


def _build_pages_text(pages: list[BatchTextExtractionPage]) -> str:
    """Concatenate pages with delimiter lines for the LLM prompt."""
    parts: list[str] = []
    for page in pages:
        parts.append(f"\n--- PAGE {page.page_number} ---\n{page.text}")
    return "".join(parts)


# ---------------------------------------------------------------------------
# GeminiTextExtractor
# ---------------------------------------------------------------------------

GEMINI_TEXT_DEFAULT_MODEL = "gemini-3.1-pro-preview"

GEMINI_TEXT_RESPONSE_SCHEMA = {
    "type": "object",
    "properties": {
        "items": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "name": {"type": "string"},
                    "price": {"type": "number"},
                    "source_page": {"type": "integer"},
                    "source_index": {"type": "integer"},
                    "category_hint": {"type": "string", "nullable": True},
                },
                "required": ["name", "price", "source_page", "source_index"],
            },
        }
    },
    "required": ["items"],
}


class GeminiTextExtractor:
    def __init__(
        self,
        api_key: str | None = None,
        model_name: str | None = None,
        client: Any | None = None,
    ):
        resolved_key = api_key or os.environ.get("GEMINI_API_KEY")
        if not resolved_key and client is None:
            raise RuntimeError("GEMINI_API_KEY is required to use GeminiTextExtractor.")
        self.model_name = model_name or os.environ.get("GEMINI_TEXT_MODEL", GEMINI_TEXT_DEFAULT_MODEL)
        if client is not None:
            self._client = client
        else:
            from google import genai
            self._client = genai.Client(api_key=resolved_key)

    async def extract(self, pages: list[BatchTextExtractionPage]) -> dict[str, Any]:
        from google.genai import types as genai_types

        pages_text = _build_pages_text(pages)
        prompt = TEXT_EXTRACTION_PROMPT_HEADER + pages_text

        try:
            response = await self._client.aio.models.generate_content(
                model=self.model_name,
                contents=[prompt],
                config=genai_types.GenerateContentConfig(
                    response_mime_type="application/json",
                    response_schema=GEMINI_TEXT_RESPONSE_SCHEMA,
                ),
            )
        except Exception as exc:
            raise TextExtractionError("Text provider call failed") from exc

        raw_text = getattr(response, "text", None)
        if not raw_text:
            raise TextExtractionError("Empty response from Gemini text extractor")
        try:
            payload = json.loads(raw_text)
        except (ValueError, TypeError) as exc:
            raise TextExtractionError("Failed to parse Gemini JSON response") from exc
        if not isinstance(payload, dict) or "items" not in payload:
            raise TextExtractionError("Gemini text payload missing 'items'")
        return payload  # type: ignore[return-value]


# ---------------------------------------------------------------------------
# ClaudeTextExtractor
# ---------------------------------------------------------------------------

CLAUDE_TEXT_DEFAULT_MODEL = "claude-sonnet-4-6"

TEXT_EXTRACTION_TOOL_SCHEMA = {
    "name": "emit_text_items",
    "description": "Emit the structured list of menu items extracted from the page text.",
    "input_schema": TEXT_ITEM_JSON_SCHEMA,
}

CLAUDE_TEXT_SYSTEM_PROMPT = (
    "You extract structured menu data from text of printed restaurant menus. "
    "The menus are typically in Romanian. Use the provided tool to return the items."
)


class ClaudeTextExtractor:
    def __init__(
        self,
        api_key: str | None = None,
        model_name: str | None = None,
        client: AsyncAnthropic | None = None,
    ):
        resolved_key = api_key or os.environ.get("ANTHROPIC_API_KEY")
        if not resolved_key and client is None:
            raise RuntimeError("ANTHROPIC_API_KEY is required to use ClaudeTextExtractor.")
        self.model_name = model_name or os.environ.get("ANTHROPIC_TEXT_MODEL", CLAUDE_TEXT_DEFAULT_MODEL)
        self._client = client or AsyncAnthropic(api_key=resolved_key)

    async def extract(self, pages: list[BatchTextExtractionPage]) -> dict[str, Any]:
        pages_text = _build_pages_text(pages)
        prompt = TEXT_EXTRACTION_PROMPT_HEADER + pages_text

        try:
            response = await self._client.messages.create(
                model=self.model_name,
                max_tokens=4096,
                system=CLAUDE_TEXT_SYSTEM_PROMPT,
                tools=[TEXT_EXTRACTION_TOOL_SCHEMA],
                tool_choice={"type": "tool", "name": "emit_text_items"},
                messages=[
                    {
                        "role": "user",
                        "content": [
                            {"type": "text", "text": prompt},
                        ],
                    }
                ],
                timeout=httpx.Timeout(60.0, connect=10.0),
            )
        except APIStatusError as exc:
            raise TextExtractionError(f"Anthropic API error: {exc.status_code}") from exc
        except Exception as exc:
            raise TextExtractionError("Text provider call failed") from exc

        for block in response.content:
            if block.type == "tool_use" and block.name == "emit_text_items":
                payload = block.input
                if not isinstance(payload, dict) or "items" not in payload:
                    raise TextExtractionError("Tool-use payload missing 'items'")
                return payload  # type: ignore[return-value]
        raise TextExtractionError("Model did not return a tool_use block")

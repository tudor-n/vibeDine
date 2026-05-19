import base64
import os
from typing import Any, Optional, Protocol

import httpx
from anthropic import AsyncAnthropic, APIStatusError


class VisionExtractionError(Exception):
    """Raised when the underlying vision provider fails to return a usable response."""


class VisionExtractor(Protocol):
    model_name: str

    async def extract(self, image_bytes: bytes, page_number: int) -> dict[str, Any]:
        """Return the raw JSON dict the provider produced for one image."""
        ...


class FakeVisionExtractor:
    """In-memory stand-in for tests. Either returns canned_response or raises `raises`."""

    def __init__(
        self,
        canned_response: Optional[dict[str, Any]] = None,
        raises: Optional[Exception] = None,
        model_name: str = "fake-test-model",
    ):
        if (canned_response is None) == (raises is None):
            raise ValueError("Provide exactly one of canned_response or raises.")
        self._canned = canned_response
        self._raises = raises
        self.model_name = model_name

    async def extract(self, image_bytes: bytes, page_number: int) -> dict[str, Any]:
        if self._raises is not None:
            raise self._raises
        assert self._canned is not None
        return self._canned


# ---------------------------------------------------------------------------
# Real provider — ClaudeExtractor
# ---------------------------------------------------------------------------

MENU_EXTRACTION_TOOL_SCHEMA = {
    "name": "emit_menu_items",
    "description": "Emit the structured list of menu items extracted from the photo.",
    "input_schema": {
        "type": "object",
        "properties": {
            "items": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "name": {"type": "string"},
                        "price": {"type": "number"},
                        "source_index": {"type": "integer", "minimum": 0},
                        "category_hint": {"type": ["string", "null"]},
                    },
                    "required": ["name", "price", "source_index"],
                },
            }
        },
        "required": ["items"],
    },
}

EXTRACTION_PROMPT = """Extract every item that has a clear price from this menu photo.

Rules:
- Include only rows where a price is printed. Skip section headings ("APERITIVE", "BĂUTURI", "FEL PRINCIPAL", "DESERTURI" etc.) as items, decorative captions, the restaurant's name/contact info, and footnotes.
- For `category_hint`: use the literal section heading printed above this item (e.g. "APERITIVE", "Fel principal"). Leave it null only for items that genuinely sit outside any visible heading. Do not invent a heading.
- Keep the original Romanian spelling and diacritics (ă, â, î, ș, ț).
- Prices in RON: convert "24,90 lei" / "24.90 RON" / "24,90" → 24.90.
- If a price is a range or "de la X", use the lower bound.
- If an item has multiple prices (small/large), emit one row per price, appending the qualifier to the name (e.g. "Salată Caesar (mică)").
- `source_index` is the 0-based reading order within this page (top-to-bottom, left-to-right for two-column menus).
- If you can't read an item clearly, skip it. Do not invent items.
"""

SYSTEM_PROMPT = (
    "You extract structured menu data from photos of printed restaurant menus. "
    "The menus are typically in Romanian. Use the provided tool to return the items."
)


class ClaudeExtractor:
    def __init__(
        self,
        api_key: str | None = None,
        model_name: str | None = None,
        client: AsyncAnthropic | None = None,
    ):
        resolved_key = api_key or os.environ.get("ANTHROPIC_API_KEY")
        if not resolved_key and client is None:
            raise RuntimeError("ANTHROPIC_API_KEY is required to use ClaudeExtractor.")
        self.model_name = model_name or os.environ.get("ANTHROPIC_VISION_MODEL", "claude-sonnet-4-6")
        self._client = client or AsyncAnthropic(api_key=resolved_key)

    async def extract(self, image_bytes: bytes, page_number: int) -> dict[str, Any]:
        media_type = _sniff_media_type(image_bytes)
        encoded = base64.standard_b64encode(image_bytes).decode("ascii")
        try:
            response = await self._client.messages.create(
                model=self.model_name,
                max_tokens=4096,
                system=SYSTEM_PROMPT,
                tools=[MENU_EXTRACTION_TOOL_SCHEMA],
                tool_choice={"type": "tool", "name": "emit_menu_items"},
                messages=[
                    {
                        "role": "user",
                        "content": [
                            {
                                "type": "image",
                                "source": {"type": "base64", "media_type": media_type, "data": encoded},
                            },
                            {"type": "text", "text": EXTRACTION_PROMPT},
                        ],
                    }
                ],
                timeout=httpx.Timeout(60.0, connect=10.0),
            )
        except APIStatusError as exc:
            raise VisionExtractionError(f"Anthropic API error: {exc.status_code}") from exc
        except Exception as exc:
            raise VisionExtractionError("Vision provider call failed") from exc

        for block in response.content:
            if block.type == "tool_use" and block.name == "emit_menu_items":
                payload = block.input
                if not isinstance(payload, dict) or "items" not in payload:
                    raise VisionExtractionError("Tool-use payload missing 'items'")
                return payload  # type: ignore[return-value]
        raise VisionExtractionError("Model did not return a tool_use block")


def _sniff_media_type(image_bytes: bytes) -> str:
    if image_bytes.startswith(b"\xff\xd8\xff"):
        return "image/jpeg"
    if image_bytes.startswith(b"\x89PNG\r\n"):
        return "image/png"
    if image_bytes[:4] == b"RIFF" and image_bytes[8:12] == b"WEBP":
        return "image/webp"
    return "image/jpeg"  # default — caller already validated mime upstream


# ---------------------------------------------------------------------------
# Real provider — GeminiExtractor (default)
# ---------------------------------------------------------------------------

import json

from google import genai
from google.genai import types as genai_types


GEMINI_VISION_DEFAULT_MODEL = "gemini-2.0-flash"

GEMINI_RESPONSE_SCHEMA = {
    "type": "object",
    "properties": {
        "items": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "name": {"type": "string"},
                    "price": {"type": "number"},
                    "source_index": {"type": "integer"},
                    "category_hint": {"type": "string", "nullable": True},
                },
                "required": ["name", "price", "source_index"],
            },
        }
    },
    "required": ["items"],
}

GEMINI_PROMPT = (
    "You extract structured menu data from photos of printed restaurant menus. "
    "The menus are typically in Romanian. Return only the JSON object — no markdown fences, no commentary.\n\n"
    + EXTRACTION_PROMPT
)


class GeminiExtractor:
    def __init__(
        self,
        api_key: str | None = None,
        model_name: str | None = None,
        client: genai.Client | None = None,
    ):
        resolved_key = api_key or os.environ.get("GEMINI_API_KEY")
        if not resolved_key and client is None:
            raise RuntimeError("GEMINI_API_KEY is required to use GeminiExtractor.")
        self.model_name = model_name or os.environ.get("GEMINI_VISION_MODEL", GEMINI_VISION_DEFAULT_MODEL)
        self._client = client or genai.Client(api_key=resolved_key)

    async def extract(self, image_bytes: bytes, page_number: int) -> dict[str, Any]:
        media_type = _sniff_media_type(image_bytes)
        try:
            response = await self._client.aio.models.generate_content(
                model=self.model_name,
                contents=[
                    genai_types.Part.from_bytes(data=image_bytes, mime_type=media_type),
                    GEMINI_PROMPT,
                ],
                config=genai_types.GenerateContentConfig(
                    response_mime_type="application/json",
                    response_schema=GEMINI_RESPONSE_SCHEMA,
                ),
            )
        except Exception as exc:
            raise VisionExtractionError("Vision provider call failed") from exc

        raw_text = getattr(response, "text", None)
        if not raw_text:
            raise VisionExtractionError("Empty response from Gemini")
        try:
            payload = json.loads(raw_text)
        except (ValueError, TypeError) as exc:
            raise VisionExtractionError("Failed to parse Gemini JSON response") from exc
        if not isinstance(payload, dict) or "items" not in payload:
            raise VisionExtractionError("Gemini payload missing 'items'")
        return payload

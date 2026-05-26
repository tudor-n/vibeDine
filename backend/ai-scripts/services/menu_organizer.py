from __future__ import annotations

import logging
import os
import unicodedata
from dataclasses import dataclass, field
from typing import Any, List, Protocol, Sequence

import httpx
from anthropic import AsyncAnthropic, APIStatusError
from pydantic import ValidationError

logger = logging.getLogger(__name__)

from models.menu_extraction import ExtractedMenuItem
from models.menu_organizer import (
    ItemRef, OrganizedCategory, OrganizedSubcategory, OrganizerResult,
)


class MenuOrganizerError(Exception):
    """Raised when the underlying organizer provider fails."""


@dataclass
class OrganizerOptions:
    max_items_per_subcategory: int = 12
    min_items_to_introduce_subcategory: int = 5
    noise_hint_blocklist: List[str] = field(
        default_factory=lambda: ["others", "menu", "produse", "altele"]
    )
    locale: str = "ro-RO"


def _normalise(s: str) -> str:
    nfd = unicodedata.normalize("NFD", s)
    stripped = "".join(ch for ch in nfd if unicodedata.category(ch) != "Mn")
    return stripped.strip().lower()


def _is_noise_hint(hint: str | None, opts: OrganizerOptions) -> bool:
    if hint is None:
        return True
    n = _normalise(hint)
    if len(n) < 3:
        return True
    return n in {_normalise(b) for b in opts.noise_hint_blocklist}


def build_hint_fallback(
    items: Sequence[ExtractedMenuItem],
    opts: OrganizerOptions,
) -> OrganizerResult:
    groups: dict[str, list[ExtractedMenuItem]] = {}
    uncategorized: list[ItemRef] = []
    for it in items:
        if _is_noise_hint(it.category_hint, opts):
            uncategorized.append(
                ItemRef(source_page=it.source_page, source_index=it.source_index, confidence=0.0)
            )
            continue
        key = _normalise(it.category_hint or "")
        groups.setdefault(key, []).append(it)

    categories: list[OrganizedCategory] = []
    for sort_order, (_, group_items) in enumerate(groups.items()):
        display = (group_items[0].category_hint or "").strip()
        categories.append(OrganizedCategory(
            name=display,
            sort_order=sort_order,
            subcategories=[],
            direct_item_refs=[
                ItemRef(source_page=it.source_page, source_index=it.source_index, confidence=0.5)
                for it in group_items
            ],
        ))

    return OrganizerResult(
        source="hint-fallback",
        categories=categories,
        uncategorized_item_refs=uncategorized,
        model_notes=None,
    )


class MenuOrganizer(Protocol):
    model_name: str

    async def organize(
        self,
        items: Sequence[ExtractedMenuItem],
        opts: OrganizerOptions,
    ) -> OrganizerResult: ...


class FakeMenuOrganizer:
    """Test stand-in. Returns canned result or raises."""

    def __init__(
        self,
        canned: OrganizerResult | None = None,
        raises: Exception | None = None,
        model_name: str = "fake-organizer",
    ):
        if (canned is None) == (raises is None):
            raise ValueError("Provide exactly one of canned or raises.")
        self._canned = canned
        self._raises = raises
        self.model_name = model_name

    async def organize(self, items, opts):
        if self._raises:
            raise self._raises
        return self._canned  # type: ignore[return-value]


ORGANIZER_TOOL_SCHEMA: dict[str, Any] = {
    "name": "organize_menu",
    "description": "Emit the structured category/subcategory hierarchy.",
    "input_schema": {
        "type": "object",
        "properties": {
            "source": {"type": "string", "enum": ["ai"]},
            "categories": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "name": {"type": "string"},
                        "sort_order": {"type": "integer", "minimum": 0},
                        "subcategories": {
                            "type": "array",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "name": {"type": "string"},
                                    "sort_order": {"type": "integer", "minimum": 0},
                                    "item_refs": {"type": "array", "items": {"$ref": "#/definitions/itemRef"}},
                                },
                                "required": ["name", "sort_order", "item_refs"],
                            },
                        },
                        "direct_item_refs": {"type": "array", "items": {"$ref": "#/definitions/itemRef"}},
                    },
                    "required": ["name", "sort_order", "subcategories", "direct_item_refs"],
                },
            },
            "uncategorized_item_refs": {"type": "array", "items": {"$ref": "#/definitions/itemRef"}},
            "model_notes": {"type": ["string", "null"]},
        },
        "required": ["source", "categories", "uncategorized_item_refs"],
        "definitions": {
            "itemRef": {
                "type": "object",
                "properties": {
                    "source_page": {"type": "integer", "minimum": 0},
                    "source_index": {"type": "integer", "minimum": 0},
                    "confidence": {"type": "number", "minimum": 0, "maximum": 1},
                },
                "required": ["source_page", "source_index", "confidence"],
            },
        },
    },
}

ORGANIZER_SYSTEM_PROMPT = (
    "You organize extracted restaurant menu items into a coherent "
    "Category -> Subcategory -> Item hierarchy. Use the provided tool to return the result."
)


def _build_user_prompt(items: Sequence[ExtractedMenuItem], opts: OrganizerOptions) -> str:
    lines = [
        "Organize these items into categories and (optionally) subcategories.",
        "",
        f"Output language: {opts.locale}",
        f"Subcategories: introduce only when a category has at least "
        f"{opts.min_items_to_introduce_subcategory} items AND items split into 2+ natural clusters. "
        "A single subcategory containing every item of a category is meaningless - "
        "put those items in direct_item_refs instead.",
        f"Hints to treat as absent (noise): {opts.noise_hint_blocklist}, plus any hint shorter than 3 chars.",
        "",
        "Rules:",
        "1. Trust hints first. Items sharing a clear category_hint go into that category, "
        "   using the hint verbatim (case-normalised) as the category name.",
        "2. For items with noise/absent hints, infer category from item name/description.",
        "3. Confidence: 1.0 = explicit match; 0.7-0.9 = strong; 0.4-0.6 = plausible; "
        "   below 0.4 = put in uncategorized_item_refs.",
        "",
        "Items (page,index | hint | name | price):",
    ]
    for it in items:
        lines.append(f"{it.source_page},{it.source_index} | {it.category_hint or '-'} | {it.name} | {it.price}")
    return "\n".join(lines)


class ClaudeMenuOrganizer:
    def __init__(
        self,
        api_key: str | None = None,
        model_name: str | None = None,
        client: AsyncAnthropic | None = None,
    ):
        resolved = api_key or os.environ.get("ANTHROPIC_API_KEY")
        if not resolved and client is None:
            raise RuntimeError("ANTHROPIC_API_KEY required for ClaudeMenuOrganizer.")
        self.model_name = model_name or os.environ.get("ANTHROPIC_ORGANIZER_MODEL", "claude-sonnet-4-6")
        self._client = client or AsyncAnthropic(api_key=resolved)

    async def organize(
        self,
        items: Sequence[ExtractedMenuItem],
        opts: OrganizerOptions,
    ) -> OrganizerResult:
        try:
            resp = await self._client.messages.create(
                model=self.model_name,
                max_tokens=32768,
                system=ORGANIZER_SYSTEM_PROMPT,
                tools=[ORGANIZER_TOOL_SCHEMA],
                tool_choice={"type": "tool", "name": "organize_menu"},
                messages=[{"role": "user", "content": _build_user_prompt(items, opts)}],
                timeout=httpx.Timeout(60.0, connect=10.0),
            )
        except APIStatusError as exc:
            raise MenuOrganizerError(f"Anthropic API error: {exc.status_code}") from exc
        except Exception as exc:
            raise MenuOrganizerError("Organizer provider call failed") from exc

        for block in resp.content:
            if getattr(block, "type", None) == "tool_use" and getattr(block, "name", None) == "organize_menu":
                payload = block.input
                if not isinstance(payload, dict):
                    raise MenuOrganizerError("Tool-use payload not a dict")
                try:
                    return OrganizerResult.model_validate(payload)
                except ValidationError as exc:
                    raise MenuOrganizerError("Organizer payload failed schema") from exc
        raise MenuOrganizerError("Model did not return a tool_use block")


import json as _json

from google import genai
from google.genai import types as genai_types


GEMINI_ORGANIZER_RESPONSE_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "source": {"type": "string", "enum": ["ai"]},
        "categories": {"type": "array", "items": {
            "type": "object",
            "properties": {
                "name": {"type": "string"},
                "sort_order": {"type": "integer"},
                "subcategories": {"type": "array", "items": {
                    "type": "object",
                    "properties": {
                        "name": {"type": "string"},
                        "sort_order": {"type": "integer"},
                        "item_refs": {"type": "array", "items": {
                            "type": "object",
                            "properties": {
                                "source_page": {"type": "integer"},
                                "source_index": {"type": "integer"},
                                "confidence": {"type": "number"},
                            },
                            "required": ["source_page", "source_index", "confidence"],
                        }},
                    },
                    "required": ["name", "sort_order", "item_refs"],
                }},
                "direct_item_refs": {"type": "array", "items": {
                    "type": "object",
                    "properties": {
                        "source_page": {"type": "integer"},
                        "source_index": {"type": "integer"},
                        "confidence": {"type": "number"},
                    },
                    "required": ["source_page", "source_index", "confidence"],
                }},
            },
            "required": ["name", "sort_order", "subcategories", "direct_item_refs"],
        }},
        "uncategorized_item_refs": {"type": "array", "items": {
            "type": "object",
            "properties": {
                "source_page": {"type": "integer"},
                "source_index": {"type": "integer"},
                "confidence": {"type": "number"},
            },
            "required": ["source_page", "source_index", "confidence"],
        }},
        "model_notes": {"type": "string", "nullable": True},
    },
    "required": ["source", "categories", "uncategorized_item_refs"],
}


class GeminiMenuOrganizer:
    def __init__(
        self,
        api_key: str | None = None,
        model_name: str | None = None,
        client: genai.Client | None = None,
    ):
        resolved = api_key or os.environ.get("GEMINI_API_KEY")
        if not resolved and client is None:
            raise RuntimeError("GEMINI_API_KEY required for GeminiMenuOrganizer.")
        self.model_name = model_name or os.environ.get("GEMINI_ORGANIZER_MODEL", "gemini-2.5-flash")
        self._client = client or genai.Client(api_key=resolved)

    async def organize(
        self,
        items: Sequence[ExtractedMenuItem],
        opts: OrganizerOptions,
    ) -> OrganizerResult:
        prompt = (
            "Return ONLY the JSON object - no markdown fences or commentary.\n\n"
            + _build_user_prompt(items, opts)
        )
        try:
            resp = await self._client.aio.models.generate_content(
                model=self.model_name,
                contents=[prompt],
                config=genai_types.GenerateContentConfig(
                    response_mime_type="application/json",
                    response_schema=GEMINI_ORGANIZER_RESPONSE_SCHEMA,
                    max_output_tokens=32768,
                    # Gemini 2.5 Flash uses dynamic thinking by default, which
                    # consumes the max_output_tokens budget before visible output
                    # begins. Disable thinking so the full budget is available
                    # for the structured JSON we actually need.
                    thinking_config=genai_types.ThinkingConfig(thinking_budget=0),
                ),
            )
        except Exception as exc:
            raise MenuOrganizerError("Organizer provider call failed") from exc

        raw = getattr(resp, "text", None)
        if not raw:
            raise MenuOrganizerError("Empty response from Gemini organizer")
        # Defensive: some models wrap structured output in ```json ... ``` fences
        # despite response_mime_type. Strip them before parsing.
        stripped = raw.strip()
        if stripped.startswith("```"):
            # Remove leading fence (with optional language tag) and trailing fence
            lines = stripped.split("\n")
            if lines[0].startswith("```"):
                lines = lines[1:]
            if lines and lines[-1].strip() == "```":
                lines = lines[:-1]
            stripped = "\n".join(lines)
        try:
            payload = _json.loads(stripped)
        except (ValueError, TypeError) as exc:
            # Diagnostic: log finish reason + raw size + head/tail snippet so we can
            # diagnose truncation, markdown wrapping, or other parse failures.
            finish_reason = "unknown"
            try:
                cand = getattr(resp, "candidates", None)
                if cand and len(cand) > 0:
                    fr = getattr(cand[0], "finish_reason", None)
                    finish_reason = str(fr) if fr is not None else "missing"
            except Exception:
                pass
            head = raw[:200]
            tail = raw[-200:] if len(raw) > 200 else ""
            logger.warning(
                "Gemini organizer JSON parse failure: finish_reason=%s raw_len=%d head=%r tail=%r parse_error=%s",
                finish_reason, len(raw), head, tail, exc,
            )
            raise MenuOrganizerError("Failed to parse Gemini organizer JSON") from exc
        try:
            return OrganizerResult.model_validate(payload)
        except ValidationError as exc:
            logger.warning(
                "Gemini organizer schema validation failure: parse_error=%s payload_keys=%s",
                exc, list(payload.keys()) if isinstance(payload, dict) else type(payload).__name__,
            )
            raise MenuOrganizerError("Organizer payload failed schema") from exc


def resolve_organizer() -> "MenuOrganizer | None":
    provider = os.environ.get("AI_ORGANIZER_PROVIDER", "gemini").lower()
    if provider == "none":
        return None
    try:
        if provider == "claude":
            return ClaudeMenuOrganizer()
        return GeminiMenuOrganizer()
    except RuntimeError:
        # Missing API key → silently disable AI organizer; the pipeline will
        # fall back to hint-only grouping via build_hint_fallback.
        return None

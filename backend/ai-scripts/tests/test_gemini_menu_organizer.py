import json
import pytest
from unittest.mock import AsyncMock, MagicMock

from models.menu_extraction import ExtractedMenuItem
from services.menu_organizer import GeminiMenuOrganizer, OrganizerOptions, MenuOrganizerError


def _items(n=3):
    return [
        ExtractedMenuItem(
            name=f"Item{i}", price=10 + i, source_page=0, source_index=i,
            description=None, category_hint="Pizza",
        )
        for i in range(n)
    ]


def _text_response(payload_dict):
    resp = MagicMock()
    resp.text = json.dumps(payload_dict)
    return resp


@pytest.mark.asyncio
async def test_gemini_organizer_returns_valid_result():
    payload = {
        "source": "ai",
        "categories": [{
            "name": "Pizza", "sort_order": 0, "subcategories": [],
            "direct_item_refs": [
                {"source_page": 0, "source_index": 0, "confidence": 0.9},
                {"source_page": 0, "source_index": 1, "confidence": 0.85},
                {"source_page": 0, "source_index": 2, "confidence": 0.8},
            ],
        }],
        "uncategorized_item_refs": [],
        "model_notes": None,
    }
    client = MagicMock()
    client.aio.models.generate_content = AsyncMock(return_value=_text_response(payload))
    org = GeminiMenuOrganizer(client=client, model_name="gemini-test")
    out = await org.organize(_items(3), OrganizerOptions())
    assert out.source == "ai"
    assert len(out.categories) == 1
    client.aio.models.generate_content.assert_awaited_once()


@pytest.mark.asyncio
async def test_gemini_organizer_raises_on_provider_error():
    client = MagicMock()
    client.aio.models.generate_content = AsyncMock(side_effect=RuntimeError("boom"))
    org = GeminiMenuOrganizer(client=client, model_name="gemini-test")
    with pytest.raises(MenuOrganizerError):
        await org.organize(_items(3), OrganizerOptions())


@pytest.mark.asyncio
async def test_gemini_organizer_raises_on_invalid_json():
    bad = MagicMock()
    bad.text = "this is not json"
    client = MagicMock()
    client.aio.models.generate_content = AsyncMock(return_value=bad)
    org = GeminiMenuOrganizer(client=client, model_name="gemini-test")
    with pytest.raises(MenuOrganizerError):
        await org.organize(_items(3), OrganizerOptions())


@pytest.mark.asyncio
async def test_gemini_locale_threaded_into_contents():
    payload = {"source": "ai", "categories": [], "uncategorized_item_refs": [], "model_notes": None}
    client = MagicMock()
    client.aio.models.generate_content = AsyncMock(return_value=_text_response(payload))
    org = GeminiMenuOrganizer(client=client, model_name="gemini-test")
    await org.organize(_items(2), OrganizerOptions(locale="en-US"))
    call_kwargs = client.aio.models.generate_content.call_args.kwargs
    contents = call_kwargs["contents"]
    joined = "\n".join(str(c) for c in contents)
    assert "en-US" in joined


@pytest.mark.asyncio
async def test_gemini_organizer_config_budget_and_thinking_disabled():
    """The organizer must dedicate the full output budget to visible JSON.

    Gemini 2.5 Flash uses dynamic thinking by default, which silently consumes
    max_output_tokens before any visible output, leading to truncated JSON. We
    set a generous budget and explicitly disable thinking.
    """
    payload = {"source": "ai", "categories": [], "uncategorized_item_refs": [], "model_notes": None}
    client = MagicMock()
    client.aio.models.generate_content = AsyncMock(return_value=_text_response(payload))
    org = GeminiMenuOrganizer(client=client, model_name="gemini-test")
    await org.organize(_items(2), OrganizerOptions())
    call_kwargs = client.aio.models.generate_content.call_args.kwargs
    config = call_kwargs["config"]
    # The config is a GenerateContentConfig instance with max_output_tokens set
    assert getattr(config, "max_output_tokens", None) == 32768
    # Thinking must be explicitly disabled (thinking_budget=0).
    thinking_config = getattr(config, "thinking_config", None)
    assert thinking_config is not None, "thinking_config must be set to disable thinking"
    assert getattr(thinking_config, "thinking_budget", None) == 0


@pytest.mark.asyncio
async def test_gemini_organizer_handles_markdown_fenced_json():
    payload = {"source": "ai", "categories": [], "uncategorized_item_refs": [], "model_notes": None}
    fenced = "```json\n" + json.dumps(payload) + "\n```"
    bad = MagicMock()
    bad.text = fenced
    client = MagicMock()
    client.aio.models.generate_content = AsyncMock(return_value=bad)
    org = GeminiMenuOrganizer(client=client, model_name="gemini-test")
    out = await org.organize(_items(2), OrganizerOptions())
    assert out.source == "ai"

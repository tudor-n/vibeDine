import json
import pytest
from unittest.mock import AsyncMock, MagicMock

from models.menu_extraction import ExtractedMenuItem
from services.menu_organizer import ClaudeMenuOrganizer, OrganizerOptions, MenuOrganizerError


def _items(n=3):
    return [
        ExtractedMenuItem(
            name=f"Item{i}", price=10 + i, source_page=0, source_index=i,
            description=None, category_hint="Pizza",
        )
        for i in range(n)
    ]


def _tool_use_response(payload):
    block = MagicMock()
    block.type = "tool_use"
    block.name = "organize_menu"
    block.input = payload
    resp = MagicMock()
    resp.content = [block]
    return resp


@pytest.mark.asyncio
async def test_claude_organizer_returns_valid_result():
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
    client.messages.create = AsyncMock(return_value=_tool_use_response(payload))
    org = ClaudeMenuOrganizer(client=client, model_name="claude-test")
    out = await org.organize(_items(3), OrganizerOptions())
    assert out.source == "ai"
    assert len(out.categories) == 1
    client.messages.create.assert_awaited_once()


@pytest.mark.asyncio
async def test_claude_organizer_raises_on_provider_error():
    client = MagicMock()
    client.messages.create = AsyncMock(side_effect=RuntimeError("boom"))
    org = ClaudeMenuOrganizer(client=client, model_name="claude-test")
    with pytest.raises(MenuOrganizerError):
        await org.organize(_items(3), OrganizerOptions())


@pytest.mark.asyncio
async def test_claude_organizer_raises_on_schema_invalid_payload():
    payload = {"source": "ai", "categories": "not-a-list"}
    client = MagicMock()
    client.messages.create = AsyncMock(return_value=_tool_use_response(payload))
    org = ClaudeMenuOrganizer(client=client, model_name="claude-test")
    with pytest.raises(MenuOrganizerError):
        await org.organize(_items(3), OrganizerOptions())


@pytest.mark.asyncio
async def test_locale_is_threaded_into_prompt():
    payload = {"source": "ai", "categories": [], "uncategorized_item_refs": [], "model_notes": None}
    client = MagicMock()
    client.messages.create = AsyncMock(return_value=_tool_use_response(payload))
    org = ClaudeMenuOrganizer(client=client, model_name="claude-test")
    await org.organize(_items(2), OrganizerOptions(locale="en-US"))
    call_kwargs = client.messages.create.call_args.kwargs
    full_text = json.dumps(call_kwargs, default=str)
    assert "en-US" in full_text


@pytest.mark.asyncio
async def test_claude_organizer_sets_max_tokens():
    payload = {"source": "ai", "categories": [], "uncategorized_item_refs": [], "model_notes": None}
    client = MagicMock()
    client.messages.create = AsyncMock(return_value=_tool_use_response(payload))
    org = ClaudeMenuOrganizer(client=client, model_name="claude-test")
    await org.organize(_items(2), OrganizerOptions())
    call_kwargs = client.messages.create.call_args.kwargs
    assert call_kwargs.get("max_tokens") == 32768

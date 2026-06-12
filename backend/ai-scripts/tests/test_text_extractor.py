"""
Tests for services/text_extractor.py  (Packet D — TDD: tests written FIRST).

Test structure mirrors test_menu_extraction_service.py.
"""
import os
import pytest

from models.menu_extraction import BatchTextExtractionPage
from services.text_extractor import (
    FakeTextExtractor,
    GeminiTextExtractor,
    ClaudeTextExtractor,
    TextExtractionError,
)


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _pages(*texts) -> list[BatchTextExtractionPage]:
    """Build a list of BatchTextExtractionPage, page_number == position."""
    return [BatchTextExtractionPage(page_number=i, text=t) for i, t in enumerate(texts)]


def _canned_items(*names_and_prices):
    return {
        "items": [
            {
                "name": n,
                "price": p,
                "source_page": i,
                "source_index": i,
                "category_hint": None,
            }
            for i, (n, p) in enumerate(names_and_prices)
        ]
    }


# ---------------------------------------------------------------------------
# FakeTextExtractor tests
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_fake_returns_canned_response():
    canned = _canned_items(("Ciorbă de burtă", 18.0), ("Salată Caesar", 22.5))
    extractor = FakeTextExtractor(canned_response=canned)
    result = await extractor.extract(_pages("page text"))
    assert result == canned
    assert result["items"][0]["name"] == "Ciorbă de burtă"


@pytest.mark.asyncio
async def test_fake_propagates_raises():
    extractor = FakeTextExtractor(raises=TextExtractionError("boom"))
    with pytest.raises(TextExtractionError, match="boom"):
        await extractor.extract(_pages("text"))


def test_fake_requires_exactly_one_of_canned_or_raises():
    with pytest.raises(ValueError):
        FakeTextExtractor()  # neither
    with pytest.raises(ValueError):
        FakeTextExtractor(
            canned_response=_canned_items(("A", 1.0)),
            raises=TextExtractionError("both"),
        )


def test_fake_has_model_name():
    f = FakeTextExtractor(canned_response={"items": []}, model_name="test-model-123")
    assert f.model_name == "test-model-123"


@pytest.mark.asyncio
async def test_fake_default_model_name():
    f = FakeTextExtractor(canned_response={"items": []})
    assert isinstance(f.model_name, str)
    assert len(f.model_name) > 0


# ---------------------------------------------------------------------------
# GeminiTextExtractor mock-based tests
# ---------------------------------------------------------------------------

class _FakeGeminiResponse:
    """Minimal stand-in for the object google.genai returns."""
    def __init__(self, text: str):
        self.text = text


class _FakeGeminiModels:
    def __init__(self, response_text: str):
        self._response_text = response_text
        self.last_call: dict = {}

    async def generate_content(self, *, model, contents, config):
        self.last_call = {"model": model, "contents": contents, "config": config}
        return _FakeGeminiResponse(self._response_text)


class _FakeGeminiAio:
    def __init__(self, models):
        self.models = models


class _FakeGeminiClient:
    """Fake google.genai.Client."""
    def __init__(self, response_text: str):
        self._models = _FakeGeminiModels(response_text)
        self.aio = _FakeGeminiAio(self._models)


@pytest.mark.asyncio
async def test_gemini_text_extractor_returns_items(monkeypatch):
    import json
    canned = _canned_items(("Grătar mixt", 45.0))
    fake_client = _FakeGeminiClient(json.dumps(canned))

    extractor = GeminiTextExtractor(client=fake_client, model_name="gemini-test")
    pages = _pages("text for page 0", "text for page 1")
    result = await extractor.extract(pages)

    assert result["items"][0]["name"] == "Grătar mixt"
    assert result["items"][0]["price"] == 45.0


@pytest.mark.asyncio
async def test_gemini_prompt_contains_page_delimiters(monkeypatch):
    import json
    canned = _canned_items(("Item A", 10.0))
    fake_client = _FakeGeminiClient(json.dumps(canned))
    extractor = GeminiTextExtractor(client=fake_client, model_name="gemini-test")

    pages = _pages("first page text", "second page text")
    await extractor.extract(pages)

    # The prompt sent to the model must contain the page delimiters
    call_args = fake_client.aio.models.last_call
    # contents is a list; the text prompt is the last element
    prompt_text = None
    for part in call_args["contents"]:
        if isinstance(part, str):
            prompt_text = part
            break
    assert prompt_text is not None, "No string prompt found in contents"
    assert "--- PAGE 0 ---" in prompt_text
    assert "--- PAGE 1 ---" in prompt_text
    assert "first page text" in prompt_text
    assert "second page text" in prompt_text


def test_gemini_model_name_from_env(monkeypatch):
    monkeypatch.setenv("GEMINI_TEXT_MODEL", "gemini-env-model")
    # GeminiTextExtractor reads model_name at __init__ time from os.environ
    extractor = GeminiTextExtractor(client=object(), model_name=None)
    assert extractor.model_name == "gemini-env-model"


@pytest.mark.asyncio
async def test_gemini_raises_text_extraction_error_on_client_exception():
    class _BrokenGeminiModels:
        async def generate_content(self, **kwargs):
            raise RuntimeError("network error")

    class _BrokenGeminiAio:
        models = _BrokenGeminiModels()

    class _BrokenGeminiClient:
        aio = _BrokenGeminiAio()

    extractor = GeminiTextExtractor(client=_BrokenGeminiClient(), model_name="gemini-test")
    with pytest.raises(TextExtractionError):
        await extractor.extract(_pages("some text"))


@pytest.mark.asyncio
async def test_gemini_raises_on_empty_response():
    class _EmptyResponse:
        text = ""

    class _EmptyModels:
        async def generate_content(self, **kwargs):
            return _EmptyResponse()

    class _EmptyAio:
        models = _EmptyModels()

    class _EmptyClient:
        aio = _EmptyAio()

    extractor = GeminiTextExtractor(client=_EmptyClient(), model_name="gemini-test")
    with pytest.raises(TextExtractionError, match="[Ee]mpty"):
        await extractor.extract(_pages("some text"))


# ---------------------------------------------------------------------------
# ClaudeTextExtractor mock-based tests
# ---------------------------------------------------------------------------

class _FakeToolUseBlock:
    def __init__(self, name: str, input_data: dict):
        self.type = "tool_use"
        self.name = name
        self.input = input_data


class _FakeClaudeResponse:
    def __init__(self, items_payload: dict):
        self.content = [_FakeToolUseBlock("emit_text_items", items_payload)]


class _FakeClaudeMessages:
    def __init__(self, items_payload: dict):
        self._payload = items_payload
        self.last_call: dict = {}

    async def create(self, **kwargs):
        self.last_call = kwargs
        return _FakeClaudeResponse(self._payload)


class _FakeClaudeClient:
    def __init__(self, items_payload: dict):
        self.messages = _FakeClaudeMessages(items_payload)


@pytest.mark.asyncio
async def test_claude_text_extractor_returns_items():
    canned = _canned_items(("Cotlet de porc", 38.0))
    fake_client = _FakeClaudeClient(canned)

    extractor = ClaudeTextExtractor(client=fake_client, model_name="claude-test")
    pages = _pages("text page 0")
    result = await extractor.extract(pages)

    assert result["items"][0]["name"] == "Cotlet de porc"
    assert result["items"][0]["price"] == 38.0


@pytest.mark.asyncio
async def test_claude_prompt_contains_page_delimiters():
    canned = _canned_items(("Item B", 20.0))
    fake_client = _FakeClaudeClient(canned)

    extractor = ClaudeTextExtractor(client=fake_client, model_name="claude-test")
    pages = _pages("alpha text", "beta text")
    await extractor.extract(pages)

    call_args = fake_client.messages.last_call
    # messages[0]["content"] should be a list; last element is the text prompt
    messages = call_args["messages"]
    assert len(messages) > 0
    user_message = messages[0]
    assert user_message["role"] == "user"
    content = user_message["content"]
    # Find any text part
    prompt_text = None
    if isinstance(content, str):
        prompt_text = content
    elif isinstance(content, list):
        for part in content:
            if isinstance(part, dict) and part.get("type") == "text":
                prompt_text = part["text"]
                break
    assert prompt_text is not None
    assert "--- PAGE 0 ---" in prompt_text
    assert "--- PAGE 1 ---" in prompt_text
    assert "alpha text" in prompt_text
    assert "beta text" in prompt_text


def test_claude_model_name_from_env(monkeypatch):
    monkeypatch.setenv("ANTHROPIC_TEXT_MODEL", "claude-env-model")
    # ClaudeTextExtractor reads model_name at __init__ time from os.environ
    extractor = ClaudeTextExtractor(client=object(), model_name=None)
    assert extractor.model_name == "claude-env-model"


@pytest.mark.asyncio
async def test_claude_raises_text_extraction_error_on_api_status_error():
    from anthropic import APIStatusError
    import httpx

    class _BrokenMessages:
        async def create(self, **kwargs):
            # Build a minimal valid httpx.Response with a request so that
            # APIStatusError.__init__ can read response.request without raising.
            request = httpx.Request("POST", "https://api.anthropic.com/v1/messages")
            response = httpx.Response(400, request=request)
            raise APIStatusError(
                "bad request",
                response=response,
                body={"error": "bad"},
            )

    class _BrokenClient:
        messages = _BrokenMessages()

    extractor = ClaudeTextExtractor(client=_BrokenClient(), model_name="claude-test")
    with pytest.raises(TextExtractionError, match="[Aa]nthropic"):
        await extractor.extract(_pages("text"))


@pytest.mark.asyncio
async def test_claude_raises_text_extraction_error_on_generic_exception():
    class _BrokenMessages:
        async def create(self, **kwargs):
            raise RuntimeError("connection refused")

    class _BrokenClient:
        messages = _BrokenMessages()

    extractor = ClaudeTextExtractor(client=_BrokenClient(), model_name="claude-test")
    with pytest.raises(TextExtractionError):
        await extractor.extract(_pages("text"))


@pytest.mark.asyncio
async def test_claude_raises_when_no_tool_use_block():
    class _NoToolBlock:
        type = "text"
        text = "I don't want to use a tool."

    class _ResponseNoTool:
        content = [_NoToolBlock()]

    class _Messages:
        async def create(self, **kwargs):
            return _ResponseNoTool()

    class _Client:
        messages = _Messages()

    extractor = ClaudeTextExtractor(client=_Client(), model_name="claude-test")
    with pytest.raises(TextExtractionError, match="[Tt]ool"):
        await extractor.extract(_pages("text"))


# ---------------------------------------------------------------------------
# Round-trip source_page fidelity test
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_fake_round_trip_source_page_preserved():
    """source_page in the returned dict must equal the page_number passed in."""
    canned = {
        "items": [
            {"name": "A", "price": 10.0, "source_page": 5, "source_index": 0, "category_hint": None},
            {"name": "B", "price": 20.0, "source_page": 7, "source_index": 1, "category_hint": None},
        ]
    }
    pages = [
        BatchTextExtractionPage(page_number=5, text="page five"),
        BatchTextExtractionPage(page_number=7, text="page seven"),
    ]
    extractor = FakeTextExtractor(canned_response=canned)
    result = await extractor.extract(pages)
    assert result["items"][0]["source_page"] == 5
    assert result["items"][1]["source_page"] == 7

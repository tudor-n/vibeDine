import pytest
from services.menu_organizer import resolve_organizer, ClaudeMenuOrganizer, GeminiMenuOrganizer


def test_factory_returns_gemini_default(monkeypatch):
    monkeypatch.delenv("AI_ORGANIZER_PROVIDER", raising=False)
    monkeypatch.setenv("GEMINI_API_KEY", "fake")
    org = resolve_organizer()
    assert isinstance(org, GeminiMenuOrganizer)


def test_factory_returns_claude(monkeypatch):
    monkeypatch.setenv("AI_ORGANIZER_PROVIDER", "claude")
    monkeypatch.setenv("ANTHROPIC_API_KEY", "fake")
    org = resolve_organizer()
    assert isinstance(org, ClaudeMenuOrganizer)


def test_factory_returns_none_when_disabled(monkeypatch):
    monkeypatch.setenv("AI_ORGANIZER_PROVIDER", "none")
    assert resolve_organizer() is None


def test_factory_returns_none_when_gemini_key_missing(monkeypatch):
    monkeypatch.delenv("AI_ORGANIZER_PROVIDER", raising=False)
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    assert resolve_organizer() is None


def test_factory_returns_none_when_claude_key_missing(monkeypatch):
    monkeypatch.setenv("AI_ORGANIZER_PROVIDER", "claude")
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    assert resolve_organizer() is None

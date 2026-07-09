import json
from unittest.mock import MagicMock

from models.schemas import CartItem, MenuItem, RecommendationRequest
from services import recommendation_engine


def _text_response(payload: dict) -> MagicMock:
    resp = MagicMock()
    resp.text = json.dumps(payload)
    return resp


def _sample_request() -> RecommendationRequest:
    return RecommendationRequest(
        cart_items=[CartItem(product_id="p1", name="Grătar mixt", quantity=1)],
        table_id="t1",
        menu_items=[
            MenuItem(product_id="p2", name="Cartofi prăjiți"),
            MenuItem(product_id="p3", name="Limonadă"),
        ],
        language="ro",
    )


def test_summary_is_surfaced_alongside_recommendations(monkeypatch):
    payload = {
        "summary": "Am găsit câteva completări care merg de minune cu ce ai ales.",
        "recommendations": [
            {"product_id": "p2", "reason": "Merge perfect cu grătarul.", "confidence": 0.9},
        ],
    }
    monkeypatch.setattr(
        recommendation_engine.client.models,
        "generate_content",
        MagicMock(return_value=_text_response(payload)),
    )

    result = recommendation_engine.get_recommendations(_sample_request())

    assert result["summary"] == payload["summary"]
    assert result["recommendations"] == payload["recommendations"]


def test_summary_and_recommendations_empty_on_model_exception(monkeypatch):
    monkeypatch.setattr(
        recommendation_engine.client.models,
        "generate_content",
        MagicMock(side_effect=RuntimeError("boom")),
    )

    result = recommendation_engine.get_recommendations(_sample_request())

    assert result == {"recommendations": [], "summary": ""}


def test_summary_and_recommendations_empty_on_malformed_json(monkeypatch):
    resp = MagicMock()
    resp.text = "not json"
    monkeypatch.setattr(
        recommendation_engine.client.models,
        "generate_content",
        MagicMock(return_value=resp),
    )

    result = recommendation_engine.get_recommendations(_sample_request())

    assert result == {"recommendations": [], "summary": ""}


def test_empty_menu_items_short_circuits_without_calling_model(monkeypatch):
    mock_generate = MagicMock()
    monkeypatch.setattr(recommendation_engine.client.models, "generate_content", mock_generate)

    request = RecommendationRequest(
        cart_items=[CartItem(product_id="p1", name="Grătar mixt", quantity=1)],
        table_id="t1",
        menu_items=[],
        language="ro",
    )
    result = recommendation_engine.get_recommendations(request)

    assert result == {"recommendations": [], "summary": ""}
    mock_generate.assert_not_called()


def test_recommendations_with_unknown_product_id_are_filtered_but_summary_kept(monkeypatch):
    payload = {
        "summary": "Iată câteva idei.",
        "recommendations": [
            {"product_id": "not-a-real-id", "reason": "x", "confidence": 0.5},
            {"product_id": "p2", "reason": "Merge bine.", "confidence": 0.8},
        ],
    }
    monkeypatch.setattr(
        recommendation_engine.client.models,
        "generate_content",
        MagicMock(return_value=_text_response(payload)),
    )

    result = recommendation_engine.get_recommendations(_sample_request())

    assert result["summary"] == "Iată câteva idei."
    assert [r["product_id"] for r in result["recommendations"]] == ["p2"]

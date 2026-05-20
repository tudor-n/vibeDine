"""
TDD — tests for the /menu/extract-pdf endpoint added to routers/menu_extraction.py
"""
import json
from uuid import UUID

import pytest
from fastapi.testclient import TestClient

from main import app
from models.menu_extraction import (
    ExtractedMenuItem,
    MenuExtractionResponse,
    PdfJobRequest,
)
from routers import menu_extraction as router_module


# ---------------------------------------------------------------------------
# Fixture: fresh TestClient per test
# ---------------------------------------------------------------------------

@pytest.fixture
def client():
    yield TestClient(app)
    app.dependency_overrides.clear()


# ---------------------------------------------------------------------------
# A canned response we'll inject via dependency override
# ---------------------------------------------------------------------------

CANNED_ITEM = ExtractedMenuItem(
    name="Ciorbă de test",
    price=18.50,
    source_page=0,
    source_index=0,
    category_hint="CIORBE",
)

CANNED_RESPONSE = MenuExtractionResponse(
    items=[CANNED_ITEM],
    failed_pages=[],
    model="fake-pdf-model",
    schema_version=1,
)

VALID_JOB_ID = UUID("00000000-0000-0000-0000-000000000001")
VALID_RESTAURANT_ID = UUID("00000000-0000-0000-0000-000000000002")


def _valid_body(**overrides) -> dict:
    base = {
        "job_id": str(VALID_JOB_ID),
        "restaurant_id": str(VALID_RESTAURANT_ID),
        "storage_path": "/uploads/menu.pdf",
        "progress_callback_url": "http://internal/api/internal/menu-imports/abc/progress",
        "hmac_secret": "supersecret12345",
        "provider": "gemini",
    }
    base.update(overrides)
    return base


# ---------------------------------------------------------------------------
# Test: happy path — override factory, get canned response back
# ---------------------------------------------------------------------------

def test_extract_pdf_happy_path(client):
    """POST /menu/extract-pdf with a valid body returns the canned response."""

    def fake_factory():
        def builder(req: PdfJobRequest):
            class _FakeService:
                async def run(self, request):
                    return CANNED_RESPONSE
            return _FakeService()
        return builder

    app.dependency_overrides[router_module.get_pdf_extraction_service_factory] = fake_factory

    response = client.post("/menu/extract-pdf", json=_valid_body())

    assert response.status_code == 200
    body = response.json()
    assert body["model"] == "fake-pdf-model"
    assert body["schema_version"] == 1
    assert len(body["items"]) == 1
    assert body["items"][0]["name"] == "Ciorbă de test"
    assert body["items"][0]["price"] == 18.50
    assert body["failed_pages"] == []


# ---------------------------------------------------------------------------
# Test: invalid body → 422
# ---------------------------------------------------------------------------

def test_extract_pdf_invalid_body_returns_422(client):
    """Missing required fields should yield a 422 Unprocessable Entity."""
    response = client.post("/menu/extract-pdf", json={"foo": "bar"})
    assert response.status_code == 422


def test_extract_pdf_missing_storage_path_returns_422(client):
    body = _valid_body()
    del body["storage_path"]
    response = client.post("/menu/extract-pdf", json=body)
    assert response.status_code == 422


def test_extract_pdf_empty_storage_path_returns_422(client):
    """storage_path has min_length=1, so empty string is invalid."""
    response = client.post("/menu/extract-pdf", json=_valid_body(storage_path=""))
    assert response.status_code == 422


# ---------------------------------------------------------------------------
# Test: endpoint wiring — response_model matches MenuExtractionResponse shape
# ---------------------------------------------------------------------------

def test_extract_pdf_response_matches_schema(client):
    """Verify the response is schema-conformant (items, failed_pages, model, schema_version)."""

    def fake_factory():
        def builder(req: PdfJobRequest):
            class _FakeService:
                async def run(self, request):
                    return CANNED_RESPONSE
            return _FakeService()
        return builder

    app.dependency_overrides[router_module.get_pdf_extraction_service_factory] = fake_factory

    response = client.post("/menu/extract-pdf", json=_valid_body())
    assert response.status_code == 200
    body = response.json()
    assert set(body.keys()) >= {"items", "failed_pages", "model", "schema_version"}


# ---------------------------------------------------------------------------
# Test: factory receives the actual request data
# ---------------------------------------------------------------------------

def test_extract_pdf_factory_receives_request_data(client):
    """Ensure the request body fields reach the factory/service correctly."""
    received: list[PdfJobRequest] = []

    def fake_factory():
        def builder(req: PdfJobRequest):
            received.append(req)

            class _FakeService:
                async def run(self, request):
                    return CANNED_RESPONSE
            return _FakeService()
        return builder

    app.dependency_overrides[router_module.get_pdf_extraction_service_factory] = fake_factory

    client.post("/menu/extract-pdf", json=_valid_body())

    assert len(received) == 1
    assert received[0].storage_path == "/uploads/menu.pdf"
    assert received[0].job_id == VALID_JOB_ID
    assert received[0].restaurant_id == VALID_RESTAURANT_ID

"""
TDD — tests for the /menu/extract-pdf endpoint added to routers/menu_extraction.py
"""
import json
from pathlib import Path
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
from services.pdf_menu_extraction_service import PdfReadError


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
        "storage_path": "/tmp/menu-imports/menu.pdf",
        "progress_callback_url": "http://localhost/api/internal/menu-imports/abc/progress",
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


def test_extract_pdf_rejects_storage_path_outside_configured_root(client, monkeypatch, tmp_path):
    """The router must reject paths outside MENU_IMPORT_STORAGE_ROOT before extraction."""
    allowed_root = tmp_path / "imports"
    outside_path = tmp_path / "outside" / "menu.pdf"
    monkeypatch.setenv("MENU_IMPORT_STORAGE_ROOT", str(allowed_root))

    called = False

    def fake_factory():
        def builder(req: PdfJobRequest):
            nonlocal called
            called = True

            class _FakeService:
                async def run(self, request):
                    return CANNED_RESPONSE
            return _FakeService()
        return builder

    app.dependency_overrides[router_module.get_pdf_extraction_service_factory] = fake_factory

    response = client.post(
        "/menu/extract-pdf",
        json=_valid_body(storage_path=str(outside_path)),
    )

    assert response.status_code == 400
    assert "storage_path" in response.json()["detail"]
    assert called is False


def test_extract_pdf_accepts_storage_path_under_configured_root(client, monkeypatch, tmp_path):
    """A path under MENU_IMPORT_STORAGE_ROOT should reach the service unchanged enough to run."""
    allowed_root = tmp_path / "imports"
    allowed_path = allowed_root / "menu.pdf"
    monkeypatch.setenv("MENU_IMPORT_STORAGE_ROOT", str(allowed_root))
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

    response = client.post(
        "/menu/extract-pdf",
        json=_valid_body(storage_path=str(allowed_path)),
    )

    assert response.status_code == 200
    assert len(received) == 1
    assert received[0].storage_path == str(allowed_path.resolve(strict=False))


def test_extract_pdf_rejects_callback_host_not_in_allowlist(client, monkeypatch):
    """The router must reject callbacks to hosts outside MENU_IMPORT_CALLBACK_HOSTS."""
    monkeypatch.setenv("MENU_IMPORT_CALLBACK_HOSTS", "localhost,backend-api")
    called = False

    def fake_factory():
        def builder(req: PdfJobRequest):
            nonlocal called
            called = True

            class _FakeService:
                async def run(self, request):
                    return CANNED_RESPONSE
            return _FakeService()
        return builder

    app.dependency_overrides[router_module.get_pdf_extraction_service_factory] = fake_factory

    response = client.post(
        "/menu/extract-pdf",
        json=_valid_body(progress_callback_url="http://evil.example/progress"),
    )

    assert response.status_code == 400
    assert "progress_callback_url" in response.json()["detail"]
    assert called is False


def test_extract_pdf_allows_backend_api_callback_host_by_default(client):
    """backend-api is a dev/default allowed callback host."""
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

    response = client.post(
        "/menu/extract-pdf",
        json=_valid_body(progress_callback_url="http://backend-api/api/internal/menu-imports/abc/progress"),
    )

    assert response.status_code == 200
    assert len(received) == 1


def test_extract_pdf_unreadable_pdf_returns_http_error(client, monkeypatch, tmp_path):
    """A service-level unreadable PDF must not be returned as a successful extraction."""
    allowed_root = tmp_path / "imports"
    unreadable_path = allowed_root / "missing.pdf"
    monkeypatch.setenv("MENU_IMPORT_STORAGE_ROOT", str(allowed_root))

    def fake_factory():
        def builder(req: PdfJobRequest):
            class _FakeService:
                async def run(self, request):
                    raise PdfReadError("Unreadable PDF")
            return _FakeService()
        return builder

    app.dependency_overrides[router_module.get_pdf_extraction_service_factory] = fake_factory

    response = client.post(
        "/menu/extract-pdf",
        json=_valid_body(storage_path=str(unreadable_path)),
    )

    assert response.status_code == 400
    assert "Unreadable PDF" in response.json()["detail"]


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
    assert received[0].storage_path == str(
        Path("/tmp/menu-imports/menu.pdf").resolve(strict=False)
    )
    assert received[0].job_id == VALID_JOB_ID
    assert received[0].restaurant_id == VALID_RESTAURANT_ID


def test_pdf_extraction_factory_passes_provider_concurrency(monkeypatch):
    captured = {}

    class _CapturingService:
        def __init__(self, **kwargs):
            captured.update(kwargs)

    monkeypatch.setenv("PDF_EXTRACT_CONCURRENCY", "8")
    monkeypatch.setenv("PDF_PROVIDER_MAX_CONCURRENCY", "2")
    monkeypatch.setattr(router_module, "PdfMenuExtractionService", _CapturingService)
    monkeypatch.setattr(router_module, "_build_text_extractor_factory", lambda: (lambda: object()))
    monkeypatch.setattr(router_module, "_default_extractor_factory", lambda: object())

    builder = router_module.get_pdf_extraction_service_factory()
    service = builder(PdfJobRequest(**_valid_body()))

    assert service is not None
    assert captured["concurrency"] == 8
    assert captured["provider_concurrency"] == 2


def test_pdf_extraction_factory_passes_render_tuning(monkeypatch):
    captured = {}

    class _CapturingService:
        def __init__(self, **kwargs):
            captured.update(kwargs)

    monkeypatch.setenv("PDF_RENDER_SCALE", "1.0")
    monkeypatch.setenv("PDF_RENDER_JPEG_QUALITY", "70")
    monkeypatch.setattr(router_module, "PdfMenuExtractionService", _CapturingService)
    monkeypatch.setattr(router_module, "_build_text_extractor_factory", lambda: (lambda: object()))
    monkeypatch.setattr(router_module, "_default_extractor_factory", lambda: object())

    builder = router_module.get_pdf_extraction_service_factory()
    builder(PdfJobRequest(**_valid_body()))

    assert captured["render_scale"] == 1.0
    assert captured["render_quality"] == 70


def test_pdf_extraction_factory_passes_embedded_image_byte_budget(monkeypatch):
    captured = {}

    class _CapturingService:
        def __init__(self, **kwargs):
            captured.update(kwargs)

    monkeypatch.setenv("PDF_MAX_EMBEDDED_IMAGE_BYTES", "123456")
    monkeypatch.setattr(router_module, "PdfMenuExtractionService", _CapturingService)
    monkeypatch.setattr(router_module, "_build_text_extractor_factory", lambda: (lambda: object()))
    monkeypatch.setattr(router_module, "_default_extractor_factory", lambda: object())

    builder = router_module.get_pdf_extraction_service_factory()
    builder(PdfJobRequest(**_valid_body()))

    assert captured["max_embedded_image_bytes"] == 123456


@pytest.mark.parametrize(
    ("env_name", "value", "message"),
    [
        ("PDF_EXTRACT_CONCURRENCY", "0", "PDF_EXTRACT_CONCURRENCY"),
        ("PDF_PROVIDER_MAX_CONCURRENCY", "0", "PDF_PROVIDER_MAX_CONCURRENCY"),
        ("PDF_TEXT_BATCH_SIZE", "0", "PDF_TEXT_BATCH_SIZE"),
        ("PDF_RENDER_SCALE", "0", "PDF_RENDER_SCALE"),
        ("PDF_RENDER_SCALE", "nan", "PDF_RENDER_SCALE"),
        ("PDF_RENDER_JPEG_QUALITY", "0", "PDF_RENDER_JPEG_QUALITY"),
        ("PDF_RENDER_JPEG_QUALITY", "101", "PDF_RENDER_JPEG_QUALITY"),
        ("PDF_MAX_EMBEDDED_IMAGE_BYTES", "-1", "PDF_MAX_EMBEDDED_IMAGE_BYTES"),
    ],
)
def test_pdf_extraction_factory_rejects_invalid_runtime_config(
    monkeypatch,
    env_name,
    value,
    message,
):
    monkeypatch.setenv(env_name, value)
    monkeypatch.setattr(router_module, "_build_text_extractor_factory", lambda: (lambda: object()))

    with pytest.raises(ValueError, match=message):
        router_module.get_pdf_extraction_service_factory()


@pytest.mark.parametrize(
    ("work_concurrency", "expected_provider_concurrency"),
    [
        ("3", 3),
        ("8", 4),
    ],
)
def test_pdf_extraction_factory_defaults_provider_concurrency(
    monkeypatch,
    work_concurrency,
    expected_provider_concurrency,
):
    captured = {}

    class _CapturingService:
        def __init__(self, **kwargs):
            captured.update(kwargs)

    monkeypatch.setenv("PDF_EXTRACT_CONCURRENCY", work_concurrency)
    monkeypatch.delenv("PDF_PROVIDER_MAX_CONCURRENCY", raising=False)
    monkeypatch.setattr(router_module, "PdfMenuExtractionService", _CapturingService)
    monkeypatch.setattr(router_module, "_build_text_extractor_factory", lambda: (lambda: object()))
    monkeypatch.setattr(router_module, "_default_extractor_factory", lambda: object())

    builder = router_module.get_pdf_extraction_service_factory()
    builder(PdfJobRequest(**_valid_body()))

    assert captured["provider_concurrency"] == expected_provider_concurrency

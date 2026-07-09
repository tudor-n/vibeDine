"""
TDD — tests for the /menu/extract-pdf endpoint added to routers/menu_extraction.py

The endpoint accepts multipart/form-data: the PDF travels as an uploaded file
(no shared filesystem with the caller), metadata as form fields. The router
writes the upload to a temp file under MENU_IMPORT_STORAGE_ROOT, hands that
path to the extraction service, and deletes it when done.
"""
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


@pytest.fixture(autouse=True)
def storage_root(monkeypatch, tmp_path):
    root = tmp_path / "menu-imports"
    monkeypatch.setenv("MENU_IMPORT_STORAGE_ROOT", str(root))
    yield root


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

PDF_BYTES = b"%PDF-1.4 fake pdf bytes for upload tests"


def _valid_form(**overrides) -> dict:
    base = {
        "job_id": str(VALID_JOB_ID),
        "restaurant_id": str(VALID_RESTAURANT_ID),
        "progress_callback_url": "http://localhost/api/internal/menu-imports/abc/progress",
        "hmac_secret": "supersecret12345",
        "provider": "gemini",
        "locale": "ro-RO",
    }
    base.update(overrides)
    return base


def _pdf_file(content: bytes = PDF_BYTES) -> dict:
    return {"file": ("menu.pdf", content, "application/pdf")}


# Used by the factory-configuration tests below, which construct the internal
# PdfJobRequest model directly (they never hit the HTTP endpoint).
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


def _install_canned_factory(received: list | None = None):
    """Dependency-override factory returning a service that yields CANNED_RESPONSE."""

    def fake_factory():
        def builder(req: PdfJobRequest):
            if received is not None:
                received.append(req)

            class _FakeService:
                async def run(self, request):
                    return CANNED_RESPONSE
            return _FakeService()
        return builder

    app.dependency_overrides[router_module.get_pdf_extraction_service_factory] = fake_factory


# ---------------------------------------------------------------------------
# Test: happy path — override factory, get canned response back
# ---------------------------------------------------------------------------

def test_extract_pdf_happy_path(client):
    """POST /menu/extract-pdf with a PDF upload + form fields returns the canned response."""
    _install_canned_factory()

    response = client.post("/menu/extract-pdf", data=_valid_form(), files=_pdf_file())

    assert response.status_code == 200
    body = response.json()
    assert body["model"] == "fake-pdf-model"
    assert body["schema_version"] == 1
    assert len(body["items"]) == 1
    assert body["items"][0]["name"] == "Ciorbă de test"
    assert body["items"][0]["price"] == 18.50
    assert body["failed_pages"] == []


# ---------------------------------------------------------------------------
# Test: invalid requests → 422
# ---------------------------------------------------------------------------

def test_extract_pdf_missing_file_returns_422(client):
    """The PDF upload is required."""
    _install_canned_factory()
    response = client.post("/menu/extract-pdf", data=_valid_form())
    assert response.status_code == 422


def test_extract_pdf_missing_job_id_returns_422(client):
    _install_canned_factory()
    form = _valid_form()
    del form["job_id"]
    response = client.post("/menu/extract-pdf", data=form, files=_pdf_file())
    assert response.status_code == 422


def test_extract_pdf_malformed_job_id_returns_422(client):
    _install_canned_factory()
    response = client.post(
        "/menu/extract-pdf",
        data=_valid_form(job_id="not-a-uuid"),
        files=_pdf_file(),
    )
    assert response.status_code == 422


def test_extract_pdf_short_hmac_secret_returns_422(client):
    """hmac_secret keeps the model's min_length=8 constraint at the form layer."""
    _install_canned_factory()
    response = client.post(
        "/menu/extract-pdf",
        data=_valid_form(hmac_secret="short"),
        files=_pdf_file(),
    )
    assert response.status_code == 422


# ---------------------------------------------------------------------------
# Test: callback-host allowlist (unchanged semantics, multipart transport)
# ---------------------------------------------------------------------------

def test_extract_pdf_rejects_callback_host_not_in_allowlist(client, monkeypatch):
    """The router must reject callbacks to hosts outside MENU_IMPORT_CALLBACK_HOSTS."""
    monkeypatch.setenv("MENU_IMPORT_CALLBACK_HOSTS", "localhost,backend-api")
    received: list[PdfJobRequest] = []
    _install_canned_factory(received)

    response = client.post(
        "/menu/extract-pdf",
        data=_valid_form(progress_callback_url="http://evil.example/progress"),
        files=_pdf_file(),
    )

    assert response.status_code == 400
    assert "progress_callback_url" in response.json()["detail"]
    assert received == []


def test_extract_pdf_allows_backend_api_callback_host_by_default(client):
    """backend-api is a dev/default allowed callback host."""
    received: list[PdfJobRequest] = []
    _install_canned_factory(received)

    response = client.post(
        "/menu/extract-pdf",
        data=_valid_form(
            progress_callback_url="http://backend-api/api/internal/menu-imports/abc/progress"
        ),
        files=_pdf_file(),
    )

    assert response.status_code == 200
    assert len(received) == 1


def test_extract_pdf_allows_configured_callback_host(client, monkeypatch):
    """Hosts from MENU_IMPORT_CALLBACK_HOSTS (e.g. Railway internal DNS) are accepted."""
    monkeypatch.setenv("MENU_IMPORT_CALLBACK_HOSTS", "backend.railway.internal")
    received: list[PdfJobRequest] = []
    _install_canned_factory(received)

    response = client.post(
        "/menu/extract-pdf",
        data=_valid_form(
            progress_callback_url="http://backend.railway.internal:5000/api/internal/menu-imports/abc/progress"
        ),
        files=_pdf_file(),
    )

    assert response.status_code == 200
    assert len(received) == 1


# ---------------------------------------------------------------------------
# Test: upload handling — the service gets a real local file with the bytes
# ---------------------------------------------------------------------------

def test_extract_pdf_forwards_extract_item_photos_flag(client):
    """The per-import extract_item_photos flag reaches the PdfJobRequest."""
    received: list[PdfJobRequest] = []
    _install_canned_factory(received)

    response = client.post(
        "/menu/extract-pdf",
        data=_valid_form(extract_item_photos="true"),
        files=_pdf_file(),
    )

    assert response.status_code == 200
    assert len(received) == 1
    assert received[0].extract_item_photos is True


def test_extract_pdf_defaults_extract_item_photos_to_false(client):
    received: list[PdfJobRequest] = []
    _install_canned_factory(received)

    response = client.post("/menu/extract-pdf", data=_valid_form(), files=_pdf_file())

    assert response.status_code == 200
    assert len(received) == 1
    assert received[0].extract_item_photos is False


def test_extract_pdf_factory_receives_request_data(client, storage_root):
    """Form fields reach the factory/service; storage_path is server-controlled."""
    received: list[PdfJobRequest] = []
    _install_canned_factory(received)

    response = client.post(
        "/menu/extract-pdf",
        data=_valid_form(provider="claude", locale="en-US"),
        files=_pdf_file(),
    )

    assert response.status_code == 200
    assert len(received) == 1
    req = received[0]
    assert req.job_id == VALID_JOB_ID
    assert req.restaurant_id == VALID_RESTAURANT_ID
    assert req.provider == "claude"
    assert req.locale == "en-US"
    assert req.hmac_secret == "supersecret12345"
    resolved = Path(req.storage_path).resolve(strict=False)
    assert resolved.is_relative_to(storage_root.resolve(strict=False))


def test_extract_pdf_writes_upload_for_the_service_to_read(client):
    """The service must be able to read the uploaded bytes from storage_path during run()."""
    seen_bytes: list[bytes] = []

    def fake_factory():
        def builder(req: PdfJobRequest):
            class _FakeService:
                async def run(self, request):
                    seen_bytes.append(Path(request.storage_path).read_bytes())
                    return CANNED_RESPONSE
            return _FakeService()
        return builder

    app.dependency_overrides[router_module.get_pdf_extraction_service_factory] = fake_factory

    response = client.post("/menu/extract-pdf", data=_valid_form(), files=_pdf_file())

    assert response.status_code == 200
    assert seen_bytes == [PDF_BYTES]


def test_extract_pdf_cleans_up_temp_file_after_success(client):
    received: list[PdfJobRequest] = []
    _install_canned_factory(received)

    response = client.post("/menu/extract-pdf", data=_valid_form(), files=_pdf_file())

    assert response.status_code == 200
    assert len(received) == 1
    assert not Path(received[0].storage_path).exists()


def test_extract_pdf_cleans_up_temp_file_after_failure(client):
    received: list[PdfJobRequest] = []

    def fake_factory():
        def builder(req: PdfJobRequest):
            received.append(req)

            class _FakeService:
                async def run(self, request):
                    raise PdfReadError("Unreadable PDF")
            return _FakeService()
        return builder

    app.dependency_overrides[router_module.get_pdf_extraction_service_factory] = fake_factory

    response = client.post("/menu/extract-pdf", data=_valid_form(), files=_pdf_file())

    assert response.status_code == 400
    assert "Unreadable PDF" in response.json()["detail"]
    assert len(received) == 1
    assert not Path(received[0].storage_path).exists()


def test_extract_pdf_unreadable_pdf_returns_http_error(client):
    """A service-level unreadable PDF must not be returned as a successful extraction."""

    def fake_factory():
        def builder(req: PdfJobRequest):
            class _FakeService:
                async def run(self, request):
                    raise PdfReadError("Unreadable PDF")
            return _FakeService()
        return builder

    app.dependency_overrides[router_module.get_pdf_extraction_service_factory] = fake_factory

    response = client.post("/menu/extract-pdf", data=_valid_form(), files=_pdf_file())

    assert response.status_code == 400
    assert "Unreadable PDF" in response.json()["detail"]


# ---------------------------------------------------------------------------
# Test: endpoint wiring — response_model matches MenuExtractionResponse shape
# ---------------------------------------------------------------------------

def test_extract_pdf_response_matches_schema(client):
    """Verify the response is schema-conformant (items, failed_pages, model, schema_version)."""
    _install_canned_factory()

    response = client.post("/menu/extract-pdf", data=_valid_form(), files=_pdf_file())
    assert response.status_code == 200
    body = response.json()
    assert set(body.keys()) >= {"items", "failed_pages", "model", "schema_version"}


# ---------------------------------------------------------------------------
# Factory configuration tests (no HTTP involved — direct model construction)
# ---------------------------------------------------------------------------

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


def test_pdf_extraction_factory_passes_provider_timeout(monkeypatch):
    captured = {}

    class _CapturingService:
        def __init__(self, **kwargs):
            captured.update(kwargs)

    monkeypatch.setenv("PDF_PROVIDER_TIMEOUT_SECONDS", "12.5")
    monkeypatch.setattr(router_module, "PdfMenuExtractionService", _CapturingService)
    monkeypatch.setattr(router_module, "_build_text_extractor_factory", lambda: (lambda: object()))
    monkeypatch.setattr(router_module, "_default_extractor_factory", lambda: object())

    builder = router_module.get_pdf_extraction_service_factory()
    builder(PdfJobRequest(**_valid_body()))

    assert captured["provider_timeout_seconds"] == 12.5


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


def test_pdf_extraction_factory_passes_vision_batch_size(monkeypatch):
    captured = {}

    class _CapturingService:
        def __init__(self, **kwargs):
            captured.update(kwargs)

    monkeypatch.setenv("PDF_VISION_BATCH_SIZE", "3")
    monkeypatch.setattr(router_module, "PdfMenuExtractionService", _CapturingService)
    monkeypatch.setattr(router_module, "_build_text_extractor_factory", lambda: (lambda: object()))
    monkeypatch.setattr(router_module, "_default_extractor_factory", lambda: object())

    builder = router_module.get_pdf_extraction_service_factory()
    builder(PdfJobRequest(**_valid_body()))

    assert captured["vision_batch_size"] == 3


@pytest.mark.parametrize(
    ("env_name", "value", "message"),
    [
        ("PDF_EXTRACT_CONCURRENCY", "0", "PDF_EXTRACT_CONCURRENCY"),
        ("PDF_PROVIDER_MAX_CONCURRENCY", "0", "PDF_PROVIDER_MAX_CONCURRENCY"),
        ("PDF_PROVIDER_TIMEOUT_SECONDS", "0", "PDF_PROVIDER_TIMEOUT_SECONDS"),
        ("PDF_PROVIDER_TIMEOUT_SECONDS", "nan", "PDF_PROVIDER_TIMEOUT_SECONDS"),
        ("PDF_TEXT_BATCH_SIZE", "0", "PDF_TEXT_BATCH_SIZE"),
        ("PDF_RENDER_SCALE", "0", "PDF_RENDER_SCALE"),
        ("PDF_RENDER_SCALE", "nan", "PDF_RENDER_SCALE"),
        ("PDF_RENDER_JPEG_QUALITY", "0", "PDF_RENDER_JPEG_QUALITY"),
        ("PDF_RENDER_JPEG_QUALITY", "101", "PDF_RENDER_JPEG_QUALITY"),
        ("PDF_MAX_EMBEDDED_IMAGE_BYTES", "-1", "PDF_MAX_EMBEDDED_IMAGE_BYTES"),
        ("PDF_VISION_BATCH_SIZE", "0", "PDF_VISION_BATCH_SIZE"),
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

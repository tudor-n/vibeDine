from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from main import app
from routers import menu_extraction as router_module
from services.menu_extraction_service import MenuExtractionService
from services.vision_extractor import FakeVisionExtractor, VisionExtractionError


FIXTURE = Path(__file__).parent / "fixtures" / "tiny_pixel.png"


@pytest.fixture
def client():
    return TestClient(app)


def _override(service_factory):
    app.dependency_overrides[router_module.get_extraction_service] = service_factory
    yield
    app.dependency_overrides.clear()


@pytest.fixture
def use_service():
    """Helper to inject a MenuExtractionService for the duration of a test."""
    def _set(service: MenuExtractionService):
        app.dependency_overrides[router_module.get_extraction_service] = lambda: service
    yield _set
    app.dependency_overrides.clear()


def _png_bytes() -> bytes:
    return FIXTURE.read_bytes()


def test_extract_happy_path(client, use_service):
    use_service(MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(
            canned_response={
                "items": [
                    {"name": "Ciorbă", "price": 18.0, "source_index": 0, "category_hint": "Ciorbe"},
                ]
            },
            model_name="fake-router",
        )
    ))
    response = client.post(
        "/menu/extract",
        files=[("photos", ("p0.png", _png_bytes(), "image/png"))],
    )
    assert response.status_code == 200
    body = response.json()
    assert body["model"] == "fake-router"
    assert body["schema_version"] == 1
    assert body["items"][0]["name"] == "Ciorbă"
    assert body["items"][0]["source_page"] == 0
    assert body["failed_pages"] == []


def test_extract_multi_file_preserves_page_index(client, use_service):
    use_service(MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(
            canned_response={"items": [{"name": "X", "price": 5.0, "source_index": 0}]}
        )
    ))
    response = client.post(
        "/menu/extract",
        files=[
            ("photos", ("p0.png", _png_bytes(), "image/png")),
            ("photos", ("p1.png", _png_bytes(), "image/png")),
        ],
    )
    assert response.status_code == 200
    pages = sorted(it["source_page"] for it in response.json()["items"])
    assert pages == [0, 1]


def test_extract_rejects_zero_photos(client):
    response = client.post("/menu/extract", files=[])
    assert response.status_code == 400
    assert response.json()["detail"].lower().startswith("at least one")


def test_extract_one_page_fails_others_succeed(client, use_service):
    state = {"calls": 0}

    def factory():
        state["calls"] += 1
        if state["calls"] == 1:
            return FakeVisionExtractor(raises=VisionExtractionError("boom"))
        return FakeVisionExtractor(canned_response={"items": [{"name": "OK", "price": 9.0, "source_index": 0}]})

    use_service(MenuExtractionService(extractor_factory=factory))
    response = client.post(
        "/menu/extract",
        files=[
            ("photos", ("p0.png", _png_bytes(), "image/png")),
            ("photos", ("p1.png", _png_bytes(), "image/png")),
        ],
    )
    assert response.status_code == 200
    body = response.json()
    assert body["failed_pages"] == [{"source_page": 0, "reason": "vision_api_error"}]
    assert body["items"][0]["source_page"] == 1

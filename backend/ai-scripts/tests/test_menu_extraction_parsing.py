import pytest
from pydantic import ValidationError

from models.menu_extraction import (
    ExtractedMenuItem,
    FailedPage,
    MenuExtractionResponse,
)


def test_extracted_menu_item_minimum_fields():
    item = ExtractedMenuItem(name="Ciorbă de burtă", price=24.9, source_page=0, source_index=3)
    assert item.name == "Ciorbă de burtă"
    assert item.price == 24.9
    assert item.description is None
    assert item.category_hint is None
    assert item.confidence is None


def test_extracted_menu_item_accepts_future_optional_fields():
    item = ExtractedMenuItem(
        name="Salată Caesar",
        price=18.0,
        source_page=1,
        source_index=2,
        description="Bucățele de pui pe pat de salată romana.",
        category_hint="APERITIVE",
        confidence=0.92,
    )
    assert item.description.startswith("Bucățele")
    assert item.category_hint == "APERITIVE"
    assert item.confidence == 0.92


def test_extracted_menu_item_rejects_missing_required_fields():
    with pytest.raises(ValidationError):
        ExtractedMenuItem(name="", price=1.0)  # type: ignore[call-arg]


def test_failed_page_rejects_invalid_reason():
    with pytest.raises(ValidationError):
        FailedPage(source_page=0, reason="some_other_reason")  # type: ignore[arg-type]


def test_menu_extraction_response_defaults():
    response = MenuExtractionResponse(items=[], model="claude-sonnet-test")
    assert response.failed_pages == []
    assert response.schema_version == 1


from services.vision_extractor import FakeVisionExtractor, VisionExtractionError


@pytest.mark.asyncio
async def test_fake_vision_extractor_returns_canned_items():
    fake = FakeVisionExtractor(
        canned_response={
            "items": [
                {"name": "Ciorbă de burtă", "price": 24.9, "source_index": 0, "category_hint": "Ciorbe"},
            ]
        },
        model_name="fake-test-model",
    )
    raw = await fake.extract(image_bytes=b"\x00", page_number=0)
    assert raw == {
        "items": [
            {"name": "Ciorbă de burtă", "price": 24.9, "source_index": 0, "category_hint": "Ciorbe"},
        ]
    }


@pytest.mark.asyncio
async def test_fake_vision_extractor_can_raise():
    fake = FakeVisionExtractor(raises=VisionExtractionError("simulated"))
    with pytest.raises(VisionExtractionError):
        await fake.extract(image_bytes=b"\x00", page_number=0)

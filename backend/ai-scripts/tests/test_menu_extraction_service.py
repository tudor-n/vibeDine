import pytest

from services.menu_extraction_service import MenuExtractionService
from services.vision_extractor import FakeVisionExtractor, VisionExtractionError


def _items(*names_and_prices, hint=None):
    return [
        {"name": n, "price": p, "source_index": i, "category_hint": hint}
        for i, (n, p) in enumerate(names_and_prices)
    ]


@pytest.mark.asyncio
async def test_single_page_success():
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(
            canned_response={"items": _items(("Ciorbă", 18.0), ("Salată", 22.5), hint="APERITIVE")},
            model_name="fake-x",
        )
    )
    result = await service.extract([b"\x00"])
    assert result.model == "fake-x"
    assert result.schema_version == 2
    assert len(result.items) == 2
    assert result.items[0].source_page == 0
    assert result.items[0].category_hint == "APERITIVE"
    assert result.failed_pages == []


@pytest.mark.asyncio
async def test_multi_page_success_preserves_page_order():
    canned = {"items": _items(("Foo", 10.0))}
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(canned_response=canned, model_name="fake-y")
    )
    result = await service.extract([b"\x00", b"\x01", b"\x02"])
    assert [it.source_page for it in result.items] == [0, 1, 2]


@pytest.mark.asyncio
async def test_one_page_fails_others_succeed():
    state = {"calls": 0}

    def factory():
        state["calls"] += 1
        if state["calls"] == 2:  # second page (index 1) fails
            return FakeVisionExtractor(raises=VisionExtractionError("boom"))
        return FakeVisionExtractor(canned_response={"items": _items(("Foo", 10.0))})

    service = MenuExtractionService(extractor_factory=factory)
    result = await service.extract([b"\x00", b"\x01", b"\x02"])
    assert {fp.source_page for fp in result.failed_pages} == {1}
    assert result.failed_pages[0].reason == "vision_api_error"
    assert {it.source_page for it in result.items} == {0, 2}


@pytest.mark.asyncio
async def test_empty_items_marked_as_no_items_detected():
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(canned_response={"items": []})
    )
    result = await service.extract([b"\x00"])
    assert result.items == []
    assert len(result.failed_pages) == 1
    assert result.failed_pages[0].reason == "no_items_detected"


@pytest.mark.asyncio
async def test_invalid_provider_payload_treated_as_vision_api_error():
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(
            canned_response={"items": [{"name": "", "price": -1.0, "source_index": 0}]}
        )
    )
    result = await service.extract([b"\x00"])
    assert result.items == []
    assert result.failed_pages[0].reason == "vision_api_error"

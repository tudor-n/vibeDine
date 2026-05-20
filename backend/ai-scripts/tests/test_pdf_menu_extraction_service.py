"""
TDD — tests for services/pdf_menu_extraction_service.py

Uses FakeTextExtractor / FakeVisionExtractor and real fixture PDFs.
A stub ProgressReporter records all events sent to it.
"""
import asyncio
from pathlib import Path
from typing import Any, Optional
from uuid import UUID

import pytest

from models.menu_extraction import (
    ExtractedMenuItem,
    MenuExtractionResponse,
    ProgressEvent,
)
from services.text_extractor import FakeTextExtractor, TextExtractionError
from services.vision_extractor import FakeVisionExtractor, VisionExtractionError
from services.pdf_menu_extraction_service import PdfMenuExtractionService
from services.progress_reporter import ProgressReporter


FIXTURES_DIR = Path(__file__).parent / "fixtures" / "sample_menus"
DIGITAL_3PAGE = str(FIXTURES_DIR / "digital_3page.pdf")
SCANNED_3PAGE = str(FIXTURES_DIR / "scanned_3page.pdf")
MIXED_5PAGE = str(FIXTURES_DIR / "mixed_5page.pdf")

JOB_ID = UUID("00000000-0000-0000-0000-000000000042")


# ---------------------------------------------------------------------------
# Stub ProgressReporter: records sent events instead of making HTTP calls
# ---------------------------------------------------------------------------

class StubProgressReporter:
    """Captures all sent ProgressEvents for assertion in tests."""

    def __init__(self):
        self.events: list[ProgressEvent] = []

    async def send(self, event: ProgressEvent) -> None:
        self.events.append(event)


# ---------------------------------------------------------------------------
# Helper factories
# ---------------------------------------------------------------------------

def _text_items_for_page(page_number: int) -> list[dict]:
    """Return two menu items attributed to the given page number."""
    return [
        {"name": f"Text Item A p{page_number}", "price": 10.0 + page_number,
         "source_page": page_number, "source_index": 0, "category_hint": "APERITIVE"},
        {"name": f"Text Item B p{page_number}", "price": 20.0 + page_number,
         "source_page": page_number, "source_index": 1, "category_hint": "APERITIVE"},
    ]


def _vision_items_for_page(page_number: int) -> list[dict]:
    return [
        {"name": f"Vision Item p{page_number}", "price": 30.0 + page_number,
         "source_index": 0, "category_hint": None},
    ]


def _build_service(
    *,
    text_response: Optional[dict[str, Any]] = None,
    text_raises: Optional[Exception] = None,
    vision_response: Optional[dict[str, Any]] = None,
    vision_raises: Optional[Exception] = None,
    reporter: Optional[Any] = None,
    concurrency: int = 8,
    text_batch_size: int = 8,
) -> tuple["PdfMenuExtractionService", StubProgressReporter]:
    if reporter is None:
        reporter = StubProgressReporter()

    if text_response is None and text_raises is None:
        text_response = {"items": []}

    if vision_response is None and vision_raises is None:
        vision_response = {"items": []}

    text_factory = lambda: FakeTextExtractor(
        canned_response=text_response, raises=text_raises
    ) if text_raises else FakeTextExtractor(canned_response=text_response)

    if vision_raises:
        vision_factory = lambda: FakeVisionExtractor(raises=vision_raises)
    else:
        vision_factory = lambda: FakeVisionExtractor(canned_response=vision_response)

    service = PdfMenuExtractionService(
        text_extractor_factory=text_factory,
        vision_extractor_factory=vision_factory,
        progress=reporter,
        concurrency=concurrency,
        text_batch_size=text_batch_size,
    )
    return service, reporter


# ---------------------------------------------------------------------------
# Test: digital_3page.pdf → all text pages → 1 batch text call → items
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_digital_3page_text_extraction_returns_items():
    """3 text pages should produce items via the text extractor."""
    # Provide items for each page the batch will report
    items = []
    for page_num in range(3):
        items.extend(_text_items_for_page(page_num))

    service, reporter = _build_service(text_response={"items": items})

    request = _make_request(DIGITAL_3PAGE)
    result = await service.run(request)

    assert len(result.items) > 0
    assert result.failed_pages == []
    # Items come from the text extractor model
    assert result.model != ""


@pytest.mark.asyncio
async def test_digital_3page_progress_events_structure():
    """First event has phase=parsing, last has status=complete."""
    items = []
    for p in range(3):
        items.extend(_text_items_for_page(p))

    service, reporter = _build_service(text_response={"items": items})
    request = _make_request(DIGITAL_3PAGE)
    await service.run(request)

    assert len(reporter.events) >= 2
    assert reporter.events[0].phase == "parsing"
    last = reporter.events[-1]
    assert last.status == "complete"
    assert last.phase == "finalizing"


# ---------------------------------------------------------------------------
# Test: scanned_3page.pdf → 3 image pages → 3 vision calls
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_scanned_3page_uses_vision_extractor():
    """All image pages should call the vision extractor, not text extractor."""
    vision_calls: list[int] = []

    def vision_factory():
        extractor = FakeVisionExtractor(
            canned_response={"items": [
                {"name": "Scanned Item", "price": 15.0, "source_index": 0}
            ]}
        )
        return _CountingVisionExtractor(extractor, vision_calls)

    text_calls: list = []

    def text_factory():
        return _CountingTextExtractor(
            FakeTextExtractor(canned_response={"items": []}),
            text_calls,
        )

    stub = StubProgressReporter()
    service = PdfMenuExtractionService(
        text_extractor_factory=text_factory,
        vision_extractor_factory=vision_factory,
        progress=stub,
        concurrency=8,
        text_batch_size=8,
    )

    request = _make_request(SCANNED_3PAGE)
    result = await service.run(request)

    # 3 image pages → 3 vision calls
    assert len(vision_calls) == 3
    # Text extractor should not have been called (no text pages)
    assert len(text_calls) == 0
    # Items come from vision
    assert len(result.items) > 0


# ---------------------------------------------------------------------------
# Test: mixed_5page.pdf → correct routing
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_mixed_5page_routes_correctly():
    """Pages 0,2,4 are text; 1,3 are image. Ensure both pipelines are used."""
    vision_calls: list = []
    text_calls: list = []

    def vision_factory():
        extractor = FakeVisionExtractor(
            canned_response={"items": [
                {"name": "Vision", "price": 5.0, "source_index": 0}
            ]}
        )
        return _CountingVisionExtractor(extractor, vision_calls)

    def text_factory():
        # Return items for all 3 text pages (0, 2, 4) so no reclassification occurs
        return _CountingTextExtractor(
            FakeTextExtractor(canned_response={"items": [
                {"name": "Text", "price": 8.0, "source_page": 0, "source_index": 0},
                {"name": "Text2", "price": 9.0, "source_page": 2, "source_index": 0},
                {"name": "Text3", "price": 10.0, "source_page": 4, "source_index": 0},
            ]}),
            text_calls,
        )

    stub = StubProgressReporter()
    service = PdfMenuExtractionService(
        text_extractor_factory=text_factory,
        vision_extractor_factory=vision_factory,
        progress=stub,
        concurrency=8,
        text_batch_size=8,
    )

    request = _make_request(MIXED_5PAGE)
    result = await service.run(request)

    # 2 image pages → 2 vision calls
    assert len(vision_calls) == 2
    # 3 text pages in at least 1 batch call
    assert len(text_calls) >= 1


# ---------------------------------------------------------------------------
# Test: reclassification fallback
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_reclassification_fallback_when_text_returns_empty():
    """
    If a text batch returns 0 items for a page, that page should be
    re-routed through the vision pipeline.
    """
    vision_calls: list = []

    def vision_factory():
        extractor = FakeVisionExtractor(
            canned_response={"items": [
                {"name": "Fallback Vision Item", "price": 99.0, "source_index": 0}
            ]}
        )
        return _CountingVisionExtractor(extractor, vision_calls)

    stub = StubProgressReporter()
    service = PdfMenuExtractionService(
        text_extractor_factory=lambda: FakeTextExtractor(canned_response={"items": []}),
        vision_extractor_factory=vision_factory,
        progress=stub,
        concurrency=8,
        text_batch_size=8,
    )

    request = _make_request(DIGITAL_3PAGE)
    result = await service.run(request)

    # All 3 text pages returned empty → all reclassified through vision
    assert len(vision_calls) == 3
    # Items come from vision fallback
    assert len(result.items) > 0
    assert any(it.name == "Fallback Vision Item" for it in result.items)


# ---------------------------------------------------------------------------
# Test: dedup — identical items across pages are merged to one
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_dedup_removes_duplicate_items():
    """Same item on multiple pages produces one result after dedup."""
    # Both pages return the exact same item name/price
    items = [
        {"name": "Ciorbă", "price": 18.0, "source_page": 0, "source_index": 0},
        {"name": "Ciorbă", "price": 18.0, "source_page": 1, "source_index": 0},
        {"name": "Ciorbă", "price": 18.0, "source_page": 2, "source_index": 0},
    ]

    service, reporter = _build_service(text_response={"items": items})
    request = _make_request(DIGITAL_3PAGE)
    result = await service.run(request)

    ciorba_items = [it for it in result.items if it.name == "Ciorbă"]
    assert len(ciorba_items) == 1


# ---------------------------------------------------------------------------
# Test: unreadable PDF returns empty response and sends failed event
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_unreadable_pdf_returns_empty_response():
    """If the PDF cannot be opened (extract returns []), return empty response."""
    service, reporter = _build_service(text_response={"items": []})

    request = _make_request("/nonexistent/path/fake.pdf")
    result = await service.run(request)

    assert result.items == []
    # Should have sent a failed progress event
    failed_events = [e for e in reporter.events if e.status == "failed"]
    assert len(failed_events) == 1
    assert failed_events[0].error_message is not None


# ---------------------------------------------------------------------------
# Test: retry on extraction error with eventual success
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_vision_extraction_retries_and_succeeds():
    """A work unit that fails twice then succeeds should still produce items."""
    call_count = 0

    class _RetryVisionExtractor:
        model_name = "retry-fake"

        async def extract(self, image_bytes: bytes, page_number: int) -> dict:
            nonlocal call_count
            call_count += 1
            if call_count < 3:
                raise VisionExtractionError("transient")
            return {"items": [{"name": "Retried", "price": 5.0, "source_index": 0}]}

    stub = StubProgressReporter()
    service = PdfMenuExtractionService(
        text_extractor_factory=lambda: FakeTextExtractor(canned_response={"items": []}),
        vision_extractor_factory=lambda: _RetryVisionExtractor(),
        progress=stub,
        concurrency=8,
        text_batch_size=8,
    )

    request = _make_request(SCANNED_3PAGE)
    result = await service.run(request)

    # At least one page should have succeeded with the retried item
    assert any(it.name == "Retried" for it in result.items)


# ---------------------------------------------------------------------------
# Test: all retries exhausted → pages become FailedPage
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_vision_extraction_all_retries_exhausted_marks_failed_pages():
    """If all retries fail, affected pages are marked as FailedPage and no exception escapes."""
    service, reporter = _build_service(
        vision_raises=VisionExtractionError("permanent failure"),
    )
    # scanned_3page → 3 image pages → all should fail
    request = _make_request(SCANNED_3PAGE)
    result = await service.run(request)  # must not raise

    assert result.items == []
    assert len(result.failed_pages) == 3
    assert all(fp.reason == "vision_api_error" for fp in result.failed_pages)


# ---------------------------------------------------------------------------
# Test: final completion event always sent
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_completion_event_always_sent():
    """Even when every page fails, the final status=complete event must be sent."""
    service, reporter = _build_service(
        vision_raises=VisionExtractionError("boom"),
    )
    request = _make_request(SCANNED_3PAGE)
    await service.run(request)

    last = reporter.events[-1]
    assert last.status == "complete"


# ---------------------------------------------------------------------------
# Test: text_batch_size batching
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_text_batch_size_controls_batching():
    """With batch_size=1, each text page gets its own extract() call."""
    text_calls: list = []

    def text_factory():
        return _CountingTextExtractor(
            FakeTextExtractor(canned_response={"items": [
                {"name": "Item", "price": 5.0, "source_page": 0, "source_index": 0},
            ]}),
            text_calls,
        )

    stub = StubProgressReporter()
    service = PdfMenuExtractionService(
        text_extractor_factory=text_factory,
        vision_extractor_factory=lambda: FakeVisionExtractor(canned_response={"items": []}),
        progress=stub,
        concurrency=8,
        text_batch_size=1,  # force one call per page
    )

    request = _make_request(DIGITAL_3PAGE)
    await service.run(request)

    # 3 text pages with batch_size=1 → 3 separate extract calls
    assert len(text_calls) == 3


# ---------------------------------------------------------------------------
# Helper: make a PdfJobRequest for a given path
# ---------------------------------------------------------------------------

def _make_request(storage_path: str):
    from models.menu_extraction import PdfJobRequest
    return PdfJobRequest(
        job_id=JOB_ID,
        restaurant_id=UUID("00000000-0000-0000-0000-000000000099"),
        storage_path=storage_path,
        progress_callback_url="http://localhost/api/internal/menu-imports/x/progress",
        hmac_secret="secretsecretX1",
    )


# ---------------------------------------------------------------------------
# Counting helpers: wrap fakes so we can track calls
# ---------------------------------------------------------------------------

class _CountingVisionExtractor:
    def __init__(self, inner: FakeVisionExtractor, calls: list):
        self._inner = inner
        self._calls = calls
        self.model_name = inner.model_name

    async def extract(self, image_bytes: bytes, page_number: int) -> dict:
        self._calls.append(page_number)
        return await self._inner.extract(image_bytes, page_number)


class _CountingTextExtractor:
    def __init__(self, inner: FakeTextExtractor, calls: list):
        self._inner = inner
        self._calls = calls
        self.model_name = inner.model_name

    async def extract(self, pages) -> dict:
        self._calls.append([p.page_number for p in pages])
        return await self._inner.extract(pages)

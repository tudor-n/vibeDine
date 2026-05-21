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
from services import pdf_menu_extraction_service as service_module
from services.pdf_menu_extraction_service import (
    PdfExtractionTimings,
    PdfMenuExtractionService,
    PdfReadError,
)
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


class DelayedCountingVisionExtractor:
    model_name = "delayed-counting"

    def __init__(self):
        self.active = 0
        self.peak = 0
        self.calls: list[int] = []
        self._lock = asyncio.Lock()

    async def extract(self, image_bytes: bytes, page_number: int) -> dict[str, Any]:
        async with self._lock:
            self.active += 1
            self.peak = max(self.peak, self.active)
        await asyncio.sleep(0.05)
        async with self._lock:
            self.active -= 1
        self.calls.append(page_number)
        return {"items": [{"name": f"Vision {page_number}", "price": 10.0, "source_index": 0}]}


class SharedVisionCallCounter:
    def __init__(self):
        self.active = 0
        self.peak = 0
        self.total_calls = 0
        self._lock = asyncio.Lock()

    async def start(self) -> None:
        async with self._lock:
            self.active += 1
            self.total_calls += 1
            self.peak = max(self.peak, self.active)

    async def finish(self) -> None:
        async with self._lock:
            self.active -= 1


class SharedDelayedCountingVisionExtractor:
    model_name = "shared-delayed-counting"

    def __init__(self, counter: SharedVisionCallCounter):
        self._counter = counter

    async def extract(self, image_bytes: bytes, page_number: int) -> dict[str, Any]:
        await self._counter.start()
        try:
            await asyncio.sleep(0.05)
        finally:
            await self._counter.finish()
        return {"items": [{"name": f"Vision {page_number}", "price": 10.0, "source_index": 0}]}


class DelayedCountingTextExtractor:
    model_name = "delayed-text"

    def __init__(self):
        self.active = 0
        self.peak = 0
        self._lock = asyncio.Lock()

    async def extract(self, pages):
        async with self._lock:
            self.active += 1
            self.peak = max(self.peak, self.active)
        await asyncio.sleep(0.05)
        async with self._lock:
            self.active -= 1
        return {"items": [
            {"name": f"Text {p.page_number}", "price": 11.0, "source_page": p.page_number, "source_index": 0}
            for p in pages
        ]}


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
    """First event has phase=parsing; final progress stays non-terminal for C# completion."""
    items = []
    for p in range(3):
        items.extend(_text_items_for_page(p))

    service, reporter = _build_service(text_response={"items": items})
    request = _make_request(DIGITAL_3PAGE)
    await service.run(request)

    assert len(reporter.events) >= 2
    assert reporter.events[0].phase == "parsing"
    last = reporter.events[-1]
    assert last.status == "extracting"
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


@pytest.mark.asyncio
async def test_provider_concurrency_limits_vision_calls_even_when_page_concurrency_is_higher():
    extractor = DelayedCountingVisionExtractor()
    factory_calls = 0

    def vision_factory():
        nonlocal factory_calls
        factory_calls += 1
        return extractor

    service = PdfMenuExtractionService(
        text_extractor_factory=lambda: FakeTextExtractor(canned_response={"items": []}),
        vision_extractor_factory=vision_factory,
        progress=StubProgressReporter(),
        concurrency=8,
        provider_concurrency=2,
    )

    result = await service.run(_make_request(SCANNED_3PAGE))

    assert len(result.items) == 3
    assert extractor.peak <= 2
    assert sorted(extractor.calls) == [0, 1, 2]
    assert factory_calls == 1


@pytest.mark.asyncio
async def test_optional_timings_record_scanned_pdf_parse_render_and_provider_work():
    metrics = PdfExtractionTimings()

    service = PdfMenuExtractionService(
        text_extractor_factory=lambda: FakeTextExtractor(canned_response={"items": []}),
        vision_extractor_factory=lambda: FakeVisionExtractor(
            canned_response={"items": [
                {"name": "Timed Vision", "price": 12.0, "source_index": 0}
            ]}
        ),
        progress=StubProgressReporter(),
        concurrency=3,
        provider_concurrency=2,
        metrics=metrics,
    )

    result = await service.run(_make_request(SCANNED_3PAGE))

    assert len(result.items) == 3
    assert metrics.parse_seconds > 0
    assert metrics.classify_seconds > 0
    assert metrics.render_seconds > 0
    assert metrics.rendered_pages == 3
    assert metrics.rendered_bytes > 0
    assert metrics.avg_rendered_bytes() > 0
    assert metrics.vision_provider_seconds > 0
    assert metrics.text_provider_seconds == 0
    assert metrics.total_measured_seconds() >= metrics.render_seconds


@pytest.mark.asyncio
async def test_optional_timings_record_text_provider_work_without_rendering():
    metrics = PdfExtractionTimings()
    items = []
    for page_num in range(3):
        items.extend(_text_items_for_page(page_num))

    service = PdfMenuExtractionService(
        text_extractor_factory=lambda: FakeTextExtractor(canned_response={"items": items}),
        vision_extractor_factory=lambda: FakeVisionExtractor(canned_response={"items": []}),
        progress=StubProgressReporter(),
        metrics=metrics,
    )

    result = await service.run(_make_request(DIGITAL_3PAGE))

    assert len(result.items) == 6
    assert metrics.text_provider_seconds > 0
    assert metrics.rendered_pages == 0
    assert metrics.rendered_bytes == 0
    assert metrics.avg_rendered_bytes() == 0
    assert metrics.render_seconds == 0
    assert metrics.vision_provider_seconds == 0


@pytest.mark.asyncio
async def test_provider_concurrency_is_shared_across_concurrent_pdf_jobs():
    counter = SharedVisionCallCounter()

    service_a = PdfMenuExtractionService(
        text_extractor_factory=lambda: FakeTextExtractor(canned_response={"items": []}),
        vision_extractor_factory=lambda: SharedDelayedCountingVisionExtractor(counter),
        progress=StubProgressReporter(),
        concurrency=8,
        provider_concurrency=1,
    )
    service_b = PdfMenuExtractionService(
        text_extractor_factory=lambda: FakeTextExtractor(canned_response={"items": []}),
        vision_extractor_factory=lambda: SharedDelayedCountingVisionExtractor(counter),
        progress=StubProgressReporter(),
        concurrency=8,
        provider_concurrency=1,
    )

    result_a, result_b = await asyncio.gather(
        service_a.run(_make_request(SCANNED_3PAGE)),
        service_b.run(_make_request(SCANNED_3PAGE)),
    )

    assert len(result_a.items) == 3
    assert len(result_b.items) == 3
    assert counter.total_calls == 6
    assert counter.peak == 1


@pytest.mark.asyncio
async def test_provider_concurrency_limits_text_batches():
    extractor = DelayedCountingTextExtractor()
    service = PdfMenuExtractionService(
        text_extractor_factory=lambda: extractor,
        vision_extractor_factory=lambda: FakeVisionExtractor(canned_response={"items": []}),
        progress=StubProgressReporter(),
        concurrency=3,
        text_batch_size=1,
        provider_concurrency=1,
    )

    result = await service.run(_make_request(DIGITAL_3PAGE))

    assert len(result.items) == 3
    assert extractor.peak == 1


def test_provider_concurrency_must_be_positive():
    with pytest.raises(ValueError, match="provider_concurrency"):
        PdfMenuExtractionService(
            text_extractor_factory=lambda: FakeTextExtractor(canned_response={"items": []}),
            vision_extractor_factory=lambda: FakeVisionExtractor(canned_response={"items": []}),
            progress=StubProgressReporter(),
            provider_concurrency=0,
        )


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
# Test: unreadable PDF raises and sends failed event
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_unreadable_pdf_raises_after_failed_progress_event():
    """If the PDF cannot be opened (extract returns []), raise instead of succeeding."""
    service, reporter = _build_service(text_response={"items": []})

    request = _make_request("/nonexistent/path/fake.pdf")
    with pytest.raises(PdfReadError, match="Unreadable PDF"):
        await service.run(request)

    # Should have sent a failed progress event
    failed_events = [e for e in reporter.events if e.status == "failed"]
    assert len(failed_events) == 1
    assert failed_events[0].error_message is not None


@pytest.mark.asyncio
async def test_scanned_pdf_uses_single_page_renderer_context(monkeypatch):
    """Scanned PDFs should reuse one renderer/document across vision pages."""
    renderer_paths: list[str] = []
    rendered_pages: list[int] = []
    closed_count = 0

    class _FakePageRenderer:
        def __init__(self, path: str, *, scale: float = 1.5, quality: int = 85):
            renderer_paths.append(path)
            self.path = path
            self.scale = scale
            self.quality = quality

        def __enter__(self):
            return self

        def __exit__(self, exc_type, exc, tb):
            nonlocal closed_count
            closed_count += 1

        def render_page(self, page_number: int) -> bytes:
            rendered_pages.append(page_number)
            return b"\xff\xd8\xfffake-jpeg"

    monkeypatch.setattr(
        service_module.pdf_renderer,
        "PdfPageRenderer",
        _FakePageRenderer,
        raising=False,
    )

    service, reporter = _build_service(
        vision_response={"items": [
            {"name": "Scanned Item", "price": 15.0, "source_index": 0}
        ]},
    )

    result = await service.run(_make_request(SCANNED_3PAGE))

    assert renderer_paths == [SCANNED_3PAGE]
    assert sorted(rendered_pages) == [0, 1, 2]
    assert closed_count == 1
    assert len(result.items) > 0


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
    """Even when every page fails, final progress stays non-terminal for C# completion."""
    service, reporter = _build_service(
        vision_raises=VisionExtractionError("boom"),
    )
    request = _make_request(SCANNED_3PAGE)
    await service.run(request)

    last = reporter.events[-1]
    assert last.status == "extracting"
    assert last.phase == "finalizing"


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

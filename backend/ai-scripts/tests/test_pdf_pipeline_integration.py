"""
Integration tests for the PDF menu-import pipeline.

Uses the three fixture PDFs (digital_3page, scanned_3page, mixed_5page) against the
real PdfMenuExtractionService with FakeTextExtractor / FakeVisionExtractor so no
real LLM calls are made.

Validates:
- Correct routing (text batches vs vision pages).
- Progress callbacks: at least one parsing event + one finalizing event.
- Deduplication across both routes.
- Reclassification fallback when text returns empty items for a page.
- All three fixture PDFs produce at least one item.
"""
import asyncio
from pathlib import Path
from typing import Any
from uuid import UUID

import pytest

from models.menu_extraction import ProgressEvent, PdfJobRequest
from services.pdf_menu_extraction_service import PdfMenuExtractionService
from services.text_extractor import FakeTextExtractor, TextExtractionError
from services.vision_extractor import FakeVisionExtractor, VisionExtractionError


# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

FIXTURES_DIR = Path(__file__).parent / "fixtures" / "sample_menus"
DIGITAL_3PAGE = str(FIXTURES_DIR / "digital_3page.pdf")
SCANNED_3PAGE = str(FIXTURES_DIR / "scanned_3page.pdf")
MIXED_5PAGE   = str(FIXTURES_DIR / "mixed_5page.pdf")

JOB_ID = UUID("00000000-0000-0000-0000-000000000099")


# ---------------------------------------------------------------------------
# Stub ProgressReporter — records all events
# ---------------------------------------------------------------------------

class RecordingProgressReporter:
    def __init__(self):
        self.events: list[ProgressEvent] = []

    async def send(self, event: ProgressEvent) -> None:
        self.events.append(event)


# ---------------------------------------------------------------------------
# Counting wrappers — track calls per extractor type
# ---------------------------------------------------------------------------

class CountingTextExtractor:
    def __init__(self, inner: FakeTextExtractor, call_log: list):
        self._inner = inner
        self._log   = call_log
        self.model_name = inner.model_name

    async def extract(self, pages) -> dict:
        self._log.append([p.page_number for p in pages])
        return await self._inner.extract(pages)


class CountingVisionExtractor:
    def __init__(self, inner: FakeVisionExtractor, call_log: list):
        self._inner = inner
        self._log   = call_log
        self.model_name = inner.model_name

    async def extract(self, image_bytes: bytes, page_number: int) -> dict:
        self._log.append(page_number)
        return await self._inner.extract(image_bytes, page_number)


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _request(path: str) -> PdfJobRequest:
    return PdfJobRequest(
        job_id=JOB_ID,
        restaurant_id=UUID("00000000-0000-0000-0000-000000000001"),
        storage_path=path,
        progress_callback_url="http://localhost/api/internal/menu-imports/x/progress",
        hmac_secret="integration-test-secret-X1",
    )


def _text_items_for(page_numbers: list[int]) -> list[dict]:
    items = []
    for p in page_numbers:
        items.append({"name": f"Text Item p{p}", "price": 10.0 + p,
                      "source_page": p, "source_index": 0, "category_hint": None})
    return items


def _vision_response() -> dict:
    return {"items": [{"name": "Vision Item", "price": 20.0, "source_index": 0}]}


# ---------------------------------------------------------------------------
# A.1 — digital_3page.pdf → text pipeline → items > 0, progress events correct
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_digital_3page_produces_items_and_correct_progress():
    reporter = RecordingProgressReporter()
    text_log: list = []

    def text_factory():
        return CountingTextExtractor(
            FakeTextExtractor(canned_response={"items": _text_items_for([0, 1, 2])}),
            text_log,
        )

    service = PdfMenuExtractionService(
        text_extractor_factory=text_factory,
        vision_extractor_factory=lambda: FakeVisionExtractor(canned_response={"items": []}),
        progress=reporter,
    )

    result = await service.run(_request(DIGITAL_3PAGE))

    # Items
    assert len(result.items) > 0, "Expected at least 1 item from digital PDF"
    assert result.failed_pages == []

    # Progress events: first must be parsing, last stays non-terminal for C# completion
    assert len(reporter.events) >= 2
    assert reporter.events[0].phase == "parsing", \
        f"First event phase should be 'parsing', got '{reporter.events[0].phase}'"
    last = reporter.events[-1]
    assert last.status == "extracting", \
        f"Last event status should be 'extracting', got '{last.status}'"
    assert last.phase == "finalizing", \
        f"Last event phase should be 'finalizing', got '{last.phase}'"

    # Text extractor was used (digital PDF → text pages)
    assert len(text_log) >= 1, "Expected at least 1 text batch call for digital PDF"


# ---------------------------------------------------------------------------
# A.2 — scanned_3page.pdf → vision pipeline → items > 0, text not called
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_scanned_3page_uses_only_vision_pipeline():
    reporter = RecordingProgressReporter()
    text_log: list  = []
    vision_log: list = []

    def text_factory():
        return CountingTextExtractor(
            FakeTextExtractor(canned_response={"items": []}),
            text_log,
        )

    def vision_factory():
        return CountingVisionExtractor(
            FakeVisionExtractor(canned_response={"items": [
                {"name": "Scanned Dish", "price": 25.0, "source_index": 0}
            ]}),
            vision_log,
        )

    service = PdfMenuExtractionService(
        text_extractor_factory=text_factory,
        vision_extractor_factory=vision_factory,
        progress=reporter,
    )

    result = await service.run(_request(SCANNED_3PAGE))

    assert len(result.items) > 0, "Expected items from scanned PDF via vision"
    # 3 scanned pages → 3 vision calls
    assert len(vision_log) == 3, f"Expected 3 vision calls, got {len(vision_log)}"
    # No text pages → no text batch calls
    assert len(text_log) == 0, f"Expected 0 text calls for scanned PDF, got {len(text_log)}"

    last = reporter.events[-1]
    assert last.status == "extracting"
    assert last.phase == "finalizing"


# ---------------------------------------------------------------------------
# A.3 — mixed_5page.pdf → both pipelines exercised
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_mixed_5page_exercises_both_routes():
    reporter  = RecordingProgressReporter()
    text_log:   list = []
    vision_log: list = []

    def text_factory():
        # Return items for the three text pages (0, 2, 4)
        return CountingTextExtractor(
            FakeTextExtractor(canned_response={"items": [
                {"name": "Text A", "price": 8.0, "source_page": 0, "source_index": 0},
                {"name": "Text B", "price": 9.0, "source_page": 2, "source_index": 0},
                {"name": "Text C", "price": 10.0, "source_page": 4, "source_index": 0},
            ]}),
            text_log,
        )

    def vision_factory():
        return CountingVisionExtractor(
            FakeVisionExtractor(canned_response={"items": [
                {"name": "Vision A", "price": 15.0, "source_index": 0}
            ]}),
            vision_log,
        )

    service = PdfMenuExtractionService(
        text_extractor_factory=text_factory,
        vision_extractor_factory=vision_factory,
        progress=reporter,
        text_batch_size=8,
    )

    result = await service.run(_request(MIXED_5PAGE))

    assert len(result.items) > 0
    # mixed_5page has 2 image pages (1, 3) → 2 vision calls
    assert len(vision_log) == 2, f"Expected 2 vision calls for mixed PDF, got {len(vision_log)}"
    # 3 text pages batched into at least 1 call
    assert len(text_log) >= 1, f"Expected >= 1 text calls for mixed PDF, got {len(text_log)}"

    last = reporter.events[-1]
    assert last.status == "extracting"
    assert last.phase == "finalizing"


# ---------------------------------------------------------------------------
# B — dedup_across_routes
# Text batch returns "Pizza"/25.0 on page 0; vision (reclassified page 1) also
# returns "Pizza"/25.0. Final result must contain only 1 Pizza.
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_dedup_across_routes():
    reporter = RecordingProgressReporter()

    # Text batch returns Pizza on page 0 and 1, but page 1 gets empty → reclassified
    # We test specifically: text returns pizza on page 0, nothing on page 1
    # (page 1 gets routed to vision which also returns pizza) → deduplicated to 1.
    # digital_3page has 3 text pages (0,1,2). We provide items for 0 and 2 only;
    # page 1 gets no items → reclassified through vision → vision returns pizza.
    # Text also returns pizza on page 0 → after dedup: 1 pizza.

    def text_factory():
        return FakeTextExtractor(canned_response={"items": [
            {"name": "Pizza", "price": 25.0, "source_page": 0, "source_index": 0},
            # page 1 intentionally empty → will be reclassified
            {"name": "Salată", "price": 12.0, "source_page": 2, "source_index": 0},
        ]})

    def vision_factory():
        # Called for reclassified page 1 — also returns Pizza/25.0
        return FakeVisionExtractor(canned_response={"items": [
            {"name": "Pizza", "price": 25.0, "source_index": 0}
        ]})

    service = PdfMenuExtractionService(
        text_extractor_factory=text_factory,
        vision_extractor_factory=vision_factory,
        progress=reporter,
    )

    result = await service.run(_request(DIGITAL_3PAGE))

    pizzas = [it for it in result.items if it.name == "Pizza"]
    assert len(pizzas) == 1, \
        f"Expected exactly 1 Pizza after dedup, got {len(pizzas)}: {[it.name for it in result.items]}"


# ---------------------------------------------------------------------------
# C — reclassification_fallback
# Text returns empty for page 0. Orchestrator routes page 0 through vision.
# Final items must include the vision item.
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_reclassification_fallback():
    reporter     = RecordingProgressReporter()
    vision_log: list = []

    def vision_factory():
        return CountingVisionExtractor(
            FakeVisionExtractor(canned_response={"items": [
                {"name": "Fallback Schnitzel", "price": 35.0, "source_index": 0}
            ]}),
            vision_log,
        )

    service = PdfMenuExtractionService(
        text_extractor_factory=lambda: FakeTextExtractor(canned_response={"items": []}),
        vision_extractor_factory=vision_factory,
        progress=reporter,
    )

    # digital_3page: 3 text pages, all return empty → all reclassified via vision
    result = await service.run(_request(DIGITAL_3PAGE))

    assert len(vision_log) == 3, \
        f"All 3 text pages should have been reclassified; got {len(vision_log)} vision calls"
    fallbacks = [it for it in result.items if it.name == "Fallback Schnitzel"]
    # Dedup will keep 1 since all three pages return the same item name/price
    assert len(fallbacks) >= 1, \
        "Expected at least 1 Fallback Schnitzel from vision reclassification"


# ---------------------------------------------------------------------------
# D — completion event always sent (even on all-failure)
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_completion_event_always_sent_even_on_failure():
    reporter = RecordingProgressReporter()

    service = PdfMenuExtractionService(
        text_extractor_factory=lambda: FakeTextExtractor(canned_response={"items": []}),
        vision_extractor_factory=lambda: FakeVisionExtractor(
            raises=VisionExtractionError("forced failure")
        ),
        progress=reporter,
    )

    # scanned PDF → all image pages → all fail → finalizing event still sent
    result = await service.run(_request(SCANNED_3PAGE))

    last = reporter.events[-1]
    assert last.status == "extracting", \
        f"Expected last event status='extracting' even on failure; got '{last.status}'"
    assert last.phase == "finalizing", \
        f"Expected last event phase='finalizing' even on failure; got '{last.phase}'"
    assert len(result.failed_pages) == 3


# ---------------------------------------------------------------------------
# E — progress events: at least one parsing + one finalizing
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_progress_events_include_parsing_and_finalizing():
    reporter = RecordingProgressReporter()

    service = PdfMenuExtractionService(
        text_extractor_factory=lambda: FakeTextExtractor(canned_response={"items": [
            {"name": "Item X", "price": 5.0, "source_page": 0, "source_index": 0}
        ]}),
        vision_extractor_factory=lambda: FakeVisionExtractor(canned_response={"items": []}),
        progress=reporter,
    )

    await service.run(_request(DIGITAL_3PAGE))

    phases = [e.phase for e in reporter.events]

    assert "parsing" in phases, f"Expected 'parsing' phase in events; got {phases}"
    assert "finalizing" in phases, f"Expected 'finalizing' phase in events; got {phases}"

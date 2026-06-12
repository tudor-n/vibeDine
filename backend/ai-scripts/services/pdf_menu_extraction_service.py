"""
PdfMenuExtractionService — Orchestrates PDF menu extraction.

Routes pages through the text pipeline (batched) or vision pipeline (per page)
based on PageKind, applies retry + reclassification fallback, deduplicates,
and sends throttled progress events.
"""
from __future__ import annotations

import asyncio
import logging
import math
import random
import time
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from typing import Callable, Optional

from pydantic import ValidationError

from models.menu_extraction import (
    BatchTextExtractionPage,
    ExtractedItemPhoto,
    ExtractedMenuItem,
    FailedPage,
    MenuExtractionResponse,
    PdfJobRequest,
    ProgressEvent,
)
from models.menu_organizer import OrganizerResult
from services import pdf_text_extractor, page_classifier, pdf_renderer
from services.dedup import dedup_items
from services.item_photo_extractor import (
    ItemOnPage,
    ItemPhotoExtractor,
    build_item_photos,
)
from services.menu_organizer import (
    MenuOrganizer,
    MenuOrganizerError,
    OrganizerOptions,
    build_hint_fallback,
    resolve_organizer,
)
from services.page_classifier import PageKind
from services.progress_reporter import ProgressReporter
from services.text_extractor import TextExtractionError, TextExtractor
from services.vision_extractor import (
    VisionExtractionError,
    VisionExtractionPage,
    VisionExtractor,
)


logger = logging.getLogger(__name__)

# Retry config
_MAX_RETRIES = 3
_BACKOFF_MS = (250, 750, 2250)  # per attempt delay before each retry
_JITTER_MAX_MS = 100

# Throttle: at most one progress event per 250 ms
_THROTTLE_MS = 0.250

_PROVIDER_LIMITERS_ATTR = "_pdf_menu_provider_limiters"


def _default_provider_concurrency(work_concurrency: int) -> int:
    return max(1, min(4, work_concurrency))


def _get_provider_semaphore(limit: int) -> asyncio.Semaphore:
    if limit < 1:
        raise ValueError("provider_concurrency must be >= 1")
    loop = asyncio.get_running_loop()
    per_loop = getattr(loop, _PROVIDER_LIMITERS_ATTR, None)
    if per_loop is None:
        per_loop = {}
        setattr(loop, _PROVIDER_LIMITERS_ATTR, per_loop)
    semaphore = per_loop.get(limit)
    if semaphore is None:
        semaphore = asyncio.Semaphore(limit)
        per_loop[limit] = semaphore
    return semaphore


class PdfReadError(Exception):
    """Raised when a PDF cannot be opened/read during the parsing phase."""


@dataclass
class PdfExtractionTimings:
    parse_seconds: float = 0.0
    classify_seconds: float = 0.0
    render_seconds: float = 0.0
    text_provider_seconds: float = 0.0
    vision_provider_seconds: float = 0.0
    rendered_pages: int = 0
    rendered_bytes: int = 0
    embedded_image_pages: int = 0
    embedded_image_bytes: int = 0
    skipped_embedded_image_pages: int = 0
    skipped_embedded_image_bytes: int = 0
    vision_provider_calls: int = 0

    def total_measured_seconds(self) -> float:
        return (
            self.parse_seconds
            + self.classify_seconds
            + self.render_seconds
            + self.text_provider_seconds
            + self.vision_provider_seconds
        )

    def avg_rendered_bytes(self) -> int:
        if self.rendered_pages == 0:
            return 0
        return self.rendered_bytes // self.rendered_pages


def _record_elapsed(metrics: PdfExtractionTimings | None, field_name: str, started_at: float) -> None:
    if metrics is None:
        return
    setattr(metrics, field_name, getattr(metrics, field_name) + time.perf_counter() - started_at)


def _record_vision_provider_call(metrics: PdfExtractionTimings | None) -> None:
    if metrics is None:
        return
    metrics.vision_provider_calls += 1


def _record_rendered_bytes(metrics: PdfExtractionTimings | None, image_bytes: bytes) -> None:
    if metrics is None:
        return
    metrics.rendered_pages += 1
    metrics.rendered_bytes += len(image_bytes)


def _record_embedded_image_bytes(metrics: PdfExtractionTimings | None, image_bytes: bytes) -> None:
    if metrics is None:
        return
    metrics.embedded_image_pages += 1
    metrics.embedded_image_bytes += len(image_bytes)


def _record_skipped_embedded_image_byte_count(
    metrics: PdfExtractionTimings | None,
    byte_count: int,
) -> None:
    if metrics is None:
        return
    metrics.skipped_embedded_image_pages += 1
    metrics.skipped_embedded_image_bytes += byte_count


def _provider_error_message(exc: BaseException | None, timeout_seconds: float) -> str:
    if isinstance(exc, asyncio.TimeoutError):
        return f"provider call timed out after {timeout_seconds:g}s"
    if exc is None:
        return "unknown provider error"
    return str(exc)


class PdfMenuExtractionService:
    def __init__(
        self,
        text_extractor_factory: Callable[[], TextExtractor],
        vision_extractor_factory: Callable[[], VisionExtractor],
        progress: ProgressReporter,
        *,
        concurrency: int = 8,
        provider_concurrency: int | None = None,
        text_batch_size: int = 8,
        vision_batch_size: int = 1,
        provider_timeout_seconds: float = 60.0,
        render_scale: float = 1.5,
        render_quality: int = 85,
        max_embedded_image_bytes: int = pdf_text_extractor.DEFAULT_MAX_EMBEDDED_IMAGE_BYTES,
        metrics: PdfExtractionTimings | None = None,
        organizer_factory: Callable[[], "MenuOrganizer | None"] | None = None,
        item_photo_extractor_factory: Callable[[], "ItemPhotoExtractor | None"] | None = None,
        item_photo_max_pages: int = 50,
    ):
        self._text_factory = text_extractor_factory
        self._vision_factory = vision_extractor_factory
        self._progress = progress
        if concurrency < 1:
            raise ValueError("concurrency must be >= 1")
        self._concurrency = concurrency
        self._provider_concurrency = (
            _default_provider_concurrency(concurrency)
            if provider_concurrency is None
            else provider_concurrency
        )
        if self._provider_concurrency < 1:
            raise ValueError("provider_concurrency must be >= 1")
        if text_batch_size < 1:
            raise ValueError("text_batch_size must be >= 1")
        if vision_batch_size < 1:
            raise ValueError("vision_batch_size must be >= 1")
        if not math.isfinite(provider_timeout_seconds):
            raise ValueError("provider_timeout_seconds must be finite")
        if provider_timeout_seconds <= 0:
            raise ValueError("provider_timeout_seconds must be > 0")
        if not math.isfinite(render_scale):
            raise ValueError("render_scale must be finite")
        if render_scale <= 0:
            raise ValueError("render_scale must be > 0")
        if render_quality < 1 or render_quality > 100:
            raise ValueError("render_quality must be between 1 and 100")
        if max_embedded_image_bytes < 0:
            raise ValueError("max_embedded_image_bytes must be >= 0")
        self._text_batch_size = text_batch_size
        self._vision_batch_size = vision_batch_size
        self._provider_timeout_seconds = provider_timeout_seconds
        self._render_scale = render_scale
        self._render_quality = render_quality
        self._max_embedded_image_bytes = max_embedded_image_bytes
        if item_photo_max_pages < 0:
            raise ValueError("item_photo_max_pages must be >= 0")
        self._metrics = metrics
        self._organizer_factory = organizer_factory or resolve_organizer
        # Optional dish-photo detector. None (the default) means the feature is
        # off and the pipeline behaves byte-identically to before. Resolved once
        # per run() so a single detector instance is reused for the whole job.
        self._item_photo_factory = item_photo_extractor_factory
        self._item_photo_max_pages = item_photo_max_pages
        self._photo_extractor: "ItemPhotoExtractor | None" = None

    # ------------------------------------------------------------------
    # Public entry point
    # ------------------------------------------------------------------

    async def run(self, request: PdfJobRequest) -> MenuExtractionResponse:
        job_id = request.job_id

        # Phase 1: parsing
        await self._progress.send(ProgressEvent(
            job_id=job_id,
            status="extracting",
            phase="parsing",
            processed_pages=0,
            total_pages=0,
            failed_pages=0,
        ))

        parse_started_at = time.perf_counter()
        try:
            pages = await asyncio.to_thread(
                pdf_text_extractor.extract,
                request.storage_path,
                max_embedded_image_bytes=self._max_embedded_image_bytes,
            )
        finally:
            _record_elapsed(self._metrics, "parse_seconds", parse_started_at)
        if not pages:
            await self._progress.send(ProgressEvent(
                job_id=job_id,
                status="failed",
                phase="parsing",
                processed_pages=0,
                total_pages=0,
                failed_pages=0,
                error_message="Unreadable PDF",
            ))
            raise PdfReadError("Unreadable PDF")

        total_pages = len(pages)

        # Phase 2: classifying
        await self._progress.send(ProgressEvent(
            job_id=job_id,
            status="extracting",
            phase="classifying",
            processed_pages=0,
            total_pages=total_pages,
            failed_pages=0,
        ))

        classify_started_at = time.perf_counter()
        try:
            kinds: list[PageKind] = [page_classifier.classify(p) for p in pages]
        finally:
            _record_elapsed(self._metrics, "classify_seconds", classify_started_at)

        # Phase 3: extracting — build work units
        await self._progress.send(ProgressEvent(
            job_id=job_id,
            status="extracting",
            phase="extracting",
            processed_pages=0,
            total_pages=total_pages,
            failed_pages=0,
        ))

        work_units = _build_work_units(
            pages,
            kinds,
            self._text_batch_size,
            self._vision_batch_size,
        )

        # Resolve the optional dish-photo detector once for the whole job. Only
        # consulted when the per-import flag wired a factory; otherwise None and
        # the pipeline is unchanged.
        self._photo_extractor = self._item_photo_factory() if self._item_photo_factory else None

        # Shared mutable state for progress tracking (protected by asyncio single-thread)
        state = _ExtractionState(
            total_pages=total_pages,
            last_send_time=time.monotonic(),
        )

        semaphore = asyncio.Semaphore(self._concurrency)
        provider_semaphore = _get_provider_semaphore(self._provider_concurrency)
        payload_semaphore = asyncio.Semaphore(self._provider_concurrency)
        page_renderer = _LazyPageRenderer(
            request.storage_path,
            scale=self._render_scale,
            quality=self._render_quality,
            metrics=self._metrics,
        )
        vision_extractor = _LazyVisionExtractor(self._vision_factory)
        try:
            tasks = [
                asyncio.create_task(
                    self._run_unit(
                        unit,
                        request,
                        state,
                        semaphore,
                        provider_semaphore,
                        payload_semaphore,
                        page_renderer,
                        vision_extractor,
                    )
                )
                for unit in work_units
            ]
            await asyncio.gather(*tasks)
        finally:
            await page_renderer.close()

        # Deduplicate
        deduped = dedup_items(state.items)

        # Keep only photos whose (source_page, source_index) survived dedup.
        item_photos = self._finalize_item_photos(state.item_photos, deduped)

        # Organize into a Category -> Subcategory -> Item hierarchy
        hierarchy = await self._run_organizer(deduped, request)

        # Determine model name
        model_name = state.model_name or "unknown"

        logger.info(
            "PDF extraction complete: job=%s pages=%d items=%d failed=%d reclassified=%d",
            job_id, total_pages, len(deduped), len(state.failed_pages), state.reclassified,
        )

        # Final event
        await self._progress.send(ProgressEvent(
            job_id=job_id,
            status="extracting",
            phase="finalizing",
            processed_pages=total_pages,
            total_pages=total_pages,
            failed_pages=len(state.failed_pages),
        ))

        return MenuExtractionResponse(
            items=deduped,
            failed_pages=state.failed_pages,
            model=model_name,
            schema_version=2,
            hierarchy=hierarchy,
            item_photos=item_photos or None,
        )

    # ------------------------------------------------------------------
    # Item photo detection (optional second vision pass)
    # ------------------------------------------------------------------

    async def _maybe_detect_item_photos(
        self,
        state: "_ExtractionState",
        page_number: int,
        image_bytes: Optional[bytes],
        parsed_items: list[ExtractedMenuItem],
        provider_semaphore: asyncio.Semaphore,
    ) -> None:
        """Single-attempt dish-photo detection for one item-bearing page.

        Never raises and never adds a FailedPage — detection failures must never
        fail an import. Respects the per-job page cap (the check + counter bump
        are synchronous, so they are atomic under asyncio)."""
        if self._photo_extractor is None or not parsed_items or image_bytes is None:
            return
        if state.photo_pages_done >= self._item_photo_max_pages:
            return
        state.photo_pages_done += 1
        try:
            item_refs = [
                ItemOnPage(source_index=it.source_index, name=it.name)
                for it in parsed_items
            ]
            async with provider_semaphore:
                detections = await self._await_provider_call(
                    self._photo_extractor.detect(image_bytes, page_number, item_refs)
                )
            valid_indices = {it.source_index for it in parsed_items}
            state.item_photos.extend(
                build_item_photos(image_bytes, page_number, detections, valid_indices)
            )
        except Exception:
            logger.warning(
                "item photo detection failed for page %s", page_number, exc_info=True
            )

    def _finalize_item_photos(
        self,
        photos: list[ExtractedItemPhoto],
        deduped: list[ExtractedMenuItem],
    ) -> list[ExtractedItemPhoto]:
        """Drop photos for items that did not survive deduplication and collapse
        any duplicate (page, index) refs to the first seen."""
        valid_refs = {(it.source_page, it.source_index) for it in deduped}
        seen: set[tuple[int, int]] = set()
        result: list[ExtractedItemPhoto] = []
        for photo in photos:
            ref = (photo.source_page, photo.source_index)
            if ref in valid_refs and ref not in seen:
                seen.add(ref)
                result.append(photo)
        return result

    # ------------------------------------------------------------------
    # Organizer (post-dedup hierarchy)
    # ------------------------------------------------------------------

    async def _run_organizer(
        self,
        items: list[ExtractedMenuItem],
        request: PdfJobRequest,
    ) -> OrganizerResult:
        opts = OrganizerOptions(locale=request.locale)
        organizer = self._organizer_factory()
        started = time.perf_counter()
        if organizer is None or not items:
            result = build_hint_fallback(items, opts)
        else:
            semaphore = _get_provider_semaphore(self._provider_concurrency)
            try:
                async with semaphore:
                    result = await organizer.organize(items, opts)
            except MenuOrganizerError as exc:
                logger.warning("Organizer failed, falling back to hint-fallback: %s", exc)
                result = build_hint_fallback(items, opts)
        elapsed_ms = int((time.perf_counter() - started) * 1000)
        cat_count = len(result.categories)
        sub_count = sum(len(c.subcategories) for c in result.categories)
        low_conf = sum(
            1 for c in result.categories
            for ref in (
                *[r for s in c.subcategories for r in s.item_refs],
                *c.direct_item_refs,
            )
            if ref.confidence < 0.7
        )
        logger.info(
            "Organizer outcome: source=%s items=%d categories=%d subcategories=%d low_confidence=%d elapsed_ms=%d",
            result.source, len(items), cat_count, sub_count, low_conf, elapsed_ms,
        )
        return result

    # ------------------------------------------------------------------
    # Work unit execution
    # ------------------------------------------------------------------

    async def _run_unit(
        self,
        unit: "_WorkUnit",
        request: PdfJobRequest,
        state: "_ExtractionState",
        semaphore: asyncio.Semaphore,
        provider_semaphore: asyncio.Semaphore,
        payload_semaphore: asyncio.Semaphore,
        page_renderer: "_LazyPageRenderer",
        vision_extractor: "_LazyVisionExtractor",
    ) -> None:
        async with semaphore:
            if unit.kind == "text_batch":
                await self._run_text_batch(
                    unit,
                    request,
                    state,
                    provider_semaphore,
                    payload_semaphore,
                    page_renderer,
                    vision_extractor,
                )
            elif unit.kind == "image_batch":
                await self._run_image_batch(
                    unit,
                    request,
                    state,
                    provider_semaphore,
                    payload_semaphore,
                    page_renderer,
                    vision_extractor,
                )
            else:  # skip
                state.processed_pages += len(unit.page_numbers)

        await self._throttled_progress(request, state)

    async def _run_text_batch(
        self,
        unit: "_WorkUnit",
        request: PdfJobRequest,
        state: "_ExtractionState",
        provider_semaphore: asyncio.Semaphore,
        payload_semaphore: asyncio.Semaphore,
        page_renderer: "_LazyPageRenderer",
        vision_extractor: "_LazyVisionExtractor",
    ) -> None:
        """Extract a batch of text pages, with retry and per-page reclassification fallback."""
        batch_pages = [
            BatchTextExtractionPage(page_number=p.page_number, text=p.text)
            for p in unit.page_contents
        ]

        raw: Optional[dict] = None
        last_exc: Optional[Exception] = None

        for attempt in range(_MAX_RETRIES):
            if attempt > 0:
                jitter = random.randint(0, _JITTER_MAX_MS)
                delay = (_BACKOFF_MS[attempt - 1] + jitter) / 1000.0
                await asyncio.sleep(delay)
            try:
                extractor = self._text_factory()
                async with provider_semaphore:
                    provider_started_at = time.perf_counter()
                    try:
                        raw = await self._await_provider_call(extractor.extract(batch_pages))
                    finally:
                        _record_elapsed(self._metrics, "text_provider_seconds", provider_started_at)
                state.model_name = state.model_name or extractor.model_name
                break
            except (TextExtractionError, asyncio.TimeoutError) as exc:
                last_exc = exc
                logger.warning(
                    "Text extraction attempt %d/%d failed for pages %s: %s",
                    attempt + 1,
                    _MAX_RETRIES,
                    unit.page_numbers,
                    _provider_error_message(exc, self._provider_timeout_seconds),
                )

        if raw is None:
            # All retries exhausted
            for pnum in unit.page_numbers:
                state.failed_pages.append(FailedPage(source_page=pnum, reason="vision_api_error"))
            state.processed_pages += len(unit.page_numbers)
            return

        # Parse items from raw response
        raw_items = raw.get("items") if isinstance(raw, dict) else []
        if raw_items is None:
            raw_items = []

        # Group items by source_page
        items_by_page: dict[int, list[dict]] = {pnum: [] for pnum in unit.page_numbers}
        for raw_item in raw_items:
            src_page = raw_item.get("source_page")
            if src_page in items_by_page:
                items_by_page[src_page].append(raw_item)

        # For each page in the batch: validate items or reclassify
        for page_content in unit.page_contents:
            pnum = page_content.page_number
            page_raw_items = items_by_page.get(pnum, [])

            if not page_raw_items:
                # Reclassification fallback: route through vision
                logger.info("Reclassifying page %d through vision pipeline", pnum)
                state.reclassified += 1
                await self._run_vision_for_page(
                    pnum,
                    request,
                    state,
                    provider_semaphore,
                    payload_semaphore,
                    page_renderer,
                    vision_extractor,
                    is_reclassified=True,
                )
            else:
                parsed = _parse_items(page_raw_items, pnum)
                if isinstance(parsed, FailedPage):
                    state.failed_pages.append(parsed)
                else:
                    state.items.extend(parsed)
                    # Digital PDFs can embed dish photographs on otherwise-text
                    # pages. Render the page and detect only when the feature is
                    # on, the page reports images, and the cap allows it — never
                    # pay the render cost otherwise.
                    if (
                        self._photo_extractor is not None
                        and getattr(page_content, "has_images", False)
                        and state.photo_pages_done < self._item_photo_max_pages
                    ):
                        try:
                            image_bytes = await page_renderer.render_page(pnum)
                        except Exception:
                            logger.warning(
                                "page render for item-photo detection failed for page %s",
                                pnum,
                                exc_info=True,
                            )
                            image_bytes = None
                        await self._maybe_detect_item_photos(
                            state, pnum, image_bytes, parsed, provider_semaphore
                        )

        state.processed_pages += len(unit.page_numbers)

    async def _run_image_batch(
        self,
        unit: "_WorkUnit",
        request: PdfJobRequest,
        state: "_ExtractionState",
        provider_semaphore: asyncio.Semaphore,
        payload_semaphore: asyncio.Semaphore,
        page_renderer: "_LazyPageRenderer",
        vision_extractor: "_LazyVisionExtractor",
    ) -> None:
        extractor = await vision_extractor.get()
        extract_batch = getattr(extractor, "extract_batch", None)
        if len(unit.page_contents) < 2 or not callable(extract_batch):
            for page_content in unit.page_contents:
                await self._run_single_image_page(
                    page_content,
                    request,
                    state,
                    provider_semaphore,
                    payload_semaphore,
                    page_renderer,
                    vision_extractor,
                )
            state.processed_pages += len(unit.page_numbers)
            return

        await self._run_vision_batch(
            unit,
            request,
            state,
            provider_semaphore,
            payload_semaphore,
            page_renderer,
            extractor,
            extract_batch,
        )
        state.processed_pages += len(unit.page_numbers)

    async def _run_single_image_page(
        self,
        page_content,
        request: PdfJobRequest,
        state: "_ExtractionState",
        provider_semaphore: asyncio.Semaphore,
        payload_semaphore: asyncio.Semaphore,
        page_renderer: "_LazyPageRenderer",
        vision_extractor: "_LazyVisionExtractor",
    ) -> None:
        embedded_image_bytes = self._usable_embedded_image_bytes(page_content)
        await self._run_vision_for_page(
            page_content.page_number,
            request,
            state,
            provider_semaphore,
            payload_semaphore,
            page_renderer,
            vision_extractor,
            embedded_image_bytes=embedded_image_bytes,
            is_reclassified=False,
        )

    def _usable_embedded_image_bytes(self, page_content) -> Optional[bytes]:
        skipped_embedded_image_bytes = (
            page_content.skipped_embedded_image_bytes
            if page_content is not None
            else 0
        )
        if skipped_embedded_image_bytes > 0:
            _record_skipped_embedded_image_byte_count(
                self._metrics,
                skipped_embedded_image_bytes,
            )
        embedded_image_bytes = page_content.embedded_image_bytes if page_content is not None else None
        if (
            embedded_image_bytes is not None
            and len(embedded_image_bytes) > self._max_embedded_image_bytes
        ):
            _record_skipped_embedded_image_byte_count(
                self._metrics,
                len(embedded_image_bytes),
            )
            embedded_image_bytes = None
        return embedded_image_bytes

    async def _run_vision_batch(
        self,
        unit: "_WorkUnit",
        request: PdfJobRequest,
        state: "_ExtractionState",
        provider_semaphore: asyncio.Semaphore,
        payload_semaphore: asyncio.Semaphore,
        page_renderer: "_LazyPageRenderer",
        extractor: VisionExtractor,
        extract_batch,
    ) -> None:
        last_exc: Optional[Exception] = None
        batch_pages: Optional[list[VisionExtractionPage]] = None

        for attempt in range(_MAX_RETRIES):
            if attempt > 0:
                jitter = random.randint(0, _JITTER_MAX_MS)
                delay = (_BACKOFF_MS[attempt - 1] + jitter) / 1000.0
                await asyncio.sleep(delay)
            try:
                async with payload_semaphore:
                    if batch_pages is None:
                        batch_pages = []
                        for page_content in unit.page_contents:
                            embedded_image_bytes = self._usable_embedded_image_bytes(page_content)
                            if embedded_image_bytes is not None:
                                _record_embedded_image_bytes(self._metrics, embedded_image_bytes)
                                image_bytes = embedded_image_bytes
                            else:
                                image_bytes = await page_renderer.render_page(page_content.page_number)
                            batch_pages.append(
                                VisionExtractionPage(
                                    page_number=page_content.page_number,
                                    image_bytes=image_bytes,
                                )
                            )
                    async with provider_semaphore:
                        provider_started_at = time.perf_counter()
                        try:
                            _record_vision_provider_call(self._metrics)
                            raw = await self._await_provider_call(extract_batch(batch_pages))
                        finally:
                            _record_elapsed(self._metrics, "vision_provider_seconds", provider_started_at)
                state.model_name = state.model_name or extractor.model_name

                raw_items = raw.get("items") if isinstance(raw, dict) else []
                if raw_items is None:
                    raw_items = []

                items_by_page: dict[int, list[dict]] = {pnum: [] for pnum in unit.page_numbers}
                for raw_item in raw_items:
                    src_page = raw_item.get("source_page")
                    if src_page in items_by_page:
                        items_by_page[src_page].append(raw_item)

                image_by_page = {bp.page_number: bp.image_bytes for bp in batch_pages}
                for pnum in unit.page_numbers:
                    parsed = _parse_items(items_by_page.get(pnum, []), pnum)
                    if isinstance(parsed, FailedPage):
                        state.failed_pages.append(parsed)
                    else:
                        state.items.extend(parsed)
                        await self._maybe_detect_item_photos(
                            state, pnum, image_by_page.get(pnum), parsed, provider_semaphore
                        )
                return

            except (VisionExtractionError, ValueError, asyncio.TimeoutError) as exc:
                last_exc = exc
                logger.warning(
                    "Vision batch extraction attempt %d/%d failed for pages %s: %s",
                    attempt + 1,
                    _MAX_RETRIES,
                    unit.page_numbers,
                    _provider_error_message(exc, self._provider_timeout_seconds),
                )

        logger.warning(
            "Vision batch extraction permanently failed for pages %s after %d attempts: %s",
            unit.page_numbers,
            _MAX_RETRIES,
            _provider_error_message(last_exc, self._provider_timeout_seconds),
        )
        for pnum in unit.page_numbers:
            state.failed_pages.append(FailedPage(source_page=pnum, reason="vision_api_error"))

    async def _run_vision_for_page(
        self,
        page_number: int,
        request: PdfJobRequest,
        state: "_ExtractionState",
        provider_semaphore: asyncio.Semaphore,
        payload_semaphore: asyncio.Semaphore,
        page_renderer: "_LazyPageRenderer",
        vision_extractor: "_LazyVisionExtractor",
        *,
        embedded_image_bytes: Optional[bytes] = None,
        is_reclassified: bool,
    ) -> None:
        last_exc: Optional[Exception] = None
        image_bytes: Optional[bytes] = embedded_image_bytes
        if image_bytes is not None:
            _record_embedded_image_bytes(self._metrics, image_bytes)

        for attempt in range(_MAX_RETRIES):
            if attempt > 0:
                jitter = random.randint(0, _JITTER_MAX_MS)
                delay = (_BACKOFF_MS[attempt - 1] + jitter) / 1000.0
                await asyncio.sleep(delay)
            try:
                async with payload_semaphore:
                    # pypdfium2 uses a C library that is not thread-safe; keep rendering
                    # serial and reuse one open PdfDocument for the whole job.
                    if image_bytes is None:
                        image_bytes = await page_renderer.render_page(page_number)
                    extractor = await vision_extractor.get()
                    async with provider_semaphore:
                        provider_started_at = time.perf_counter()
                        try:
                            _record_vision_provider_call(self._metrics)
                            raw = await self._await_provider_call(
                                extractor.extract(image_bytes=image_bytes, page_number=page_number)
                            )
                        finally:
                            _record_elapsed(self._metrics, "vision_provider_seconds", provider_started_at)
                state.model_name = state.model_name or extractor.model_name

                raw_items = raw.get("items") if isinstance(raw, dict) else []
                if raw_items is None:
                    raw_items = []

                # Vision extractor returns items without source_page → inject it
                page_raw_items = [
                    {**item, "source_page": page_number}
                    for item in raw_items
                ]

                parsed = _parse_items(page_raw_items, page_number)
                if isinstance(parsed, FailedPage):
                    state.failed_pages.append(parsed)
                else:
                    state.items.extend(parsed)
                    await self._maybe_detect_item_photos(
                        state, page_number, image_bytes, parsed, provider_semaphore
                    )
                return  # success

            except (VisionExtractionError, ValueError, asyncio.TimeoutError) as exc:
                last_exc = exc
                logger.warning(
                    "Vision extraction attempt %d/%d failed for page %d: %s",
                    attempt + 1,
                    _MAX_RETRIES,
                    page_number,
                    _provider_error_message(exc, self._provider_timeout_seconds),
                )

        # All retries exhausted
        logger.warning(
            "Vision extraction permanently failed for page %d after %d attempts: %s",
            page_number,
            _MAX_RETRIES,
            _provider_error_message(last_exc, self._provider_timeout_seconds),
        )
        state.failed_pages.append(FailedPage(source_page=page_number, reason="vision_api_error"))

    async def _await_provider_call(self, awaitable):
        return await asyncio.wait_for(
            awaitable,
            timeout=self._provider_timeout_seconds,
        )

    # ------------------------------------------------------------------
    # Throttled progress reporting
    # ------------------------------------------------------------------

    async def _throttled_progress(
        self,
        request: PdfJobRequest,
        state: "_ExtractionState",
    ) -> None:
        now = time.monotonic()
        if now - state.last_send_time >= _THROTTLE_MS:
            state.last_send_time = now
            await self._progress.send(ProgressEvent(
                job_id=request.job_id,
                status="extracting",
                phase="extracting",
                processed_pages=state.processed_pages,
                total_pages=state.total_pages,
                failed_pages=len(state.failed_pages),
            ))


# ---------------------------------------------------------------------------
# Work unit definition + builder
# ---------------------------------------------------------------------------

class _WorkUnit:
    __slots__ = ("kind", "page_numbers", "page_contents")

    def __init__(self, kind: str, page_numbers: list[int], page_contents=None):
        self.kind = kind  # "text_batch" | "image_batch" | "skip"
        self.page_numbers = page_numbers
        self.page_contents = page_contents or []  # list[PageContent] for page work


class _LazyPageRenderer:
    """Open the PDF renderer only if a page actually needs the vision path."""

    def __init__(
        self,
        path: str,
        *,
        scale: float,
        quality: int,
        metrics: PdfExtractionTimings | None,
    ):
        self._path = path
        self._scale = scale
        self._quality = quality
        self._metrics = metrics
        self._renderer = None
        self._executor = ThreadPoolExecutor(max_workers=1, thread_name_prefix="pdf-page-renderer")
        self._closed = False

    async def render_page(self, page_number: int) -> bytes:
        if self._closed:
            raise RuntimeError("PDF page renderer is closed")
        loop = asyncio.get_running_loop()
        return await loop.run_in_executor(self._executor, self._render_page_sync, page_number)

    def _render_page_sync(self, page_number: int) -> bytes:
        render_started_at = time.perf_counter()
        try:
            if self._renderer is None:
                self._renderer = pdf_renderer.PdfPageRenderer(
                    self._path,
                    scale=self._scale,
                    quality=self._quality,
                )
                self._renderer.__enter__()
            image_bytes = self._renderer.render_page(page_number)
            _record_rendered_bytes(self._metrics, image_bytes)
            return image_bytes
        finally:
            _record_elapsed(self._metrics, "render_seconds", render_started_at)

    async def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        try:
            if self._renderer is not None:
                loop = asyncio.get_running_loop()
                await loop.run_in_executor(self._executor, self._close_sync)
        finally:
            self._executor.shutdown(wait=True)

    def _close_sync(self) -> None:
        if self._renderer is not None:
            self._renderer.__exit__(None, None, None)
            self._renderer = None


class _LazyVisionExtractor:
    """Create one vision extractor per PDF job, only if vision is needed."""

    def __init__(self, factory: Callable[[], VisionExtractor]):
        self._factory = factory
        self._extractor: Optional[VisionExtractor] = None
        self._lock = asyncio.Lock()

    async def get(self) -> VisionExtractor:
        if self._extractor is not None:
            return self._extractor
        async with self._lock:
            if self._extractor is None:
                self._extractor = self._factory()
            return self._extractor


def _build_work_units(
    pages,
    kinds: list[PageKind],
    text_batch_size: int,
    vision_batch_size: int,
) -> list[_WorkUnit]:
    """Group consecutive text and image pages into bounded provider batches."""
    units: list[_WorkUnit] = []
    text_buffer: list = []
    image_buffer: list = []

    def _flush_text_buffer():
        if not text_buffer:
            return
        for i in range(0, len(text_buffer), text_batch_size):
            batch = text_buffer[i: i + text_batch_size]
            units.append(_WorkUnit(
                kind="text_batch",
                page_numbers=[p.page_number for p in batch],
                page_contents=batch,
            ))
        text_buffer.clear()

    def _flush_image_buffer():
        if not image_buffer:
            return
        for i in range(0, len(image_buffer), vision_batch_size):
            batch = image_buffer[i: i + vision_batch_size]
            units.append(_WorkUnit(
                kind="image_batch",
                page_numbers=[p.page_number for p in batch],
                page_contents=batch,
            ))
        image_buffer.clear()

    for page_content, kind in zip(pages, kinds):
        if kind == "text":
            _flush_image_buffer()
            text_buffer.append(page_content)
        else:
            _flush_text_buffer()
            if kind == "image":
                image_buffer.append(page_content)
                if len(image_buffer) >= vision_batch_size:
                    _flush_image_buffer()
            else:  # skip
                _flush_image_buffer()
                units.append(_WorkUnit(
                    kind="skip",
                    page_numbers=[page_content.page_number],
                ))

    _flush_text_buffer()
    _flush_image_buffer()
    return units


# ---------------------------------------------------------------------------
# Shared mutable extraction state
# ---------------------------------------------------------------------------

class _ExtractionState:
    __slots__ = (
        "total_pages", "processed_pages", "items",
        "failed_pages", "model_name", "reclassified", "last_send_time",
        "item_photos", "photo_pages_done",
    )

    def __init__(self, total_pages: int, last_send_time: float):
        self.total_pages = total_pages
        self.processed_pages = 0
        self.items: list[ExtractedMenuItem] = []
        self.failed_pages: list[FailedPage] = []
        self.model_name: str = ""
        self.reclassified: int = 0
        self.last_send_time = last_send_time
        self.item_photos: list[ExtractedItemPhoto] = []
        self.photo_pages_done: int = 0


# ---------------------------------------------------------------------------
# Item parsing helper
# ---------------------------------------------------------------------------

def _parse_items(raw_items: list[dict], page_number: int):
    """Parse a list of raw item dicts into ExtractedMenuItem objects.

    Returns list[ExtractedMenuItem] on success, or FailedPage on validation error.
    """
    parsed: list[ExtractedMenuItem] = []
    try:
        for raw_item in raw_items:
            parsed.append(
                ExtractedMenuItem(
                    name=raw_item["name"],
                    price=float(raw_item["price"]),
                    source_page=raw_item.get("source_page", page_number),
                    source_index=int(raw_item["source_index"]),
                    category_hint=raw_item.get("category_hint"),
                )
            )
    except (KeyError, TypeError, ValueError, ValidationError):
        logger.warning("Invalid item payload for page %d", page_number, exc_info=True)
        return FailedPage(source_page=page_number, reason="vision_api_error")
    return parsed

import asyncio
import logging
from typing import Callable, List

from pydantic import ValidationError

from models.menu_extraction import (
    ExtractedItemPhoto,
    ExtractedMenuItem,
    FailedPage,
    MenuExtractionResponse,
)
from services.item_photo_extractor import (
    ItemOnPage,
    ItemPhotoExtractor,
    build_item_photos,
    resolve_item_photo_extractor,
)
from services.menu_organizer import (
    MenuOrganizer,
    MenuOrganizerError,
    OrganizerOptions,
    build_hint_fallback,
    resolve_organizer,
)
from services.vision_extractor import VisionExtractionError, VisionExtractor


logger = logging.getLogger(__name__)

# Default ceiling on how many photo pages call the vision provider at once, so a
# single multi-photo request cannot burst into unbounded concurrent paid calls
# (AI-03). Mirrors the PDF pipeline's provider-concurrency default.
DEFAULT_PHOTO_CONCURRENCY = 4


class MenuExtractionService:
    """Coordinates per-page vision-LLM calls and merges the results into one response."""

    def __init__(
        self,
        extractor_factory: Callable[[], VisionExtractor],
        organizer_factory: Callable[[], "MenuOrganizer | None"] | None = None,
        photo_extractor_factory: Callable[[], "ItemPhotoExtractor | None"] | None = None,
        concurrency: int = DEFAULT_PHOTO_CONCURRENCY,
    ):
        # Factory rather than instance so each page can be retried in isolation
        # and so tests can produce different fakes per page.
        self._extractor_factory = extractor_factory
        if concurrency < 1:
            raise ValueError("concurrency must be >= 1")
        self._concurrency = concurrency
        # Default to resolve_organizer (env-driven) so production stays wired
        # without explicit injection; tests pass their own factory.
        self._organizer_factory = organizer_factory or resolve_organizer
        # Optional dish-photo detector. Only consulted when the per-import flag
        # is on; env-driven default mirrors the organizer wiring.
        self._photo_extractor_factory = photo_extractor_factory or resolve_item_photo_extractor

    async def extract(
        self,
        image_bytes_list: List[bytes],
        locale: str = "ro-RO",
        extract_item_photos: bool = False,
    ) -> MenuExtractionResponse:
        # Resolve the detector once per request — only when the feature is on, so
        # that with the flag off the factory is never even consulted.
        photo_extractor = self._photo_extractor_factory() if extract_item_photos else None

        # Bound the fan-out so N uploaded photos never trigger N simultaneous
        # paid vision calls (matches the PDF pipeline's provider semaphore).
        semaphore = asyncio.Semaphore(self._concurrency)

        async def _bounded_extract_one(idx: int, image_bytes: bytes):
            async with semaphore:
                return await self._extract_one(idx, image_bytes, photo_extractor)

        tasks = [
            _bounded_extract_one(idx, b)
            for idx, b in enumerate(image_bytes_list)
        ]
        per_page = await asyncio.gather(*tasks)

        items: list[ExtractedMenuItem] = []
        failed: list[FailedPage] = []
        photos: list[ExtractedItemPhoto] = []
        model_name = ""
        for page_items, page_failure, page_model, page_photos in per_page:
            items.extend(page_items)
            photos.extend(page_photos)
            if page_failure is not None:
                failed.append(page_failure)
            if not model_name and page_model:
                model_name = page_model

        opts = OrganizerOptions(locale=locale)
        organizer = self._organizer_factory()
        if organizer is None or not items:
            hierarchy = build_hint_fallback(items, opts)
        else:
            try:
                hierarchy = await organizer.organize(items, opts)
            except MenuOrganizerError:
                logger.warning(
                    "Photo organizer failed, falling back to hints",
                    exc_info=True,
                )
                hierarchy = build_hint_fallback(items, opts)

        return MenuExtractionResponse(
            items=items,
            failed_pages=failed,
            model=model_name or "unknown",
            schema_version=2,
            hierarchy=hierarchy,
            item_photos=photos or None,
        )

    async def _extract_one(
        self,
        page_number: int,
        image_bytes: bytes,
        photo_extractor: "ItemPhotoExtractor | None",
    ) -> tuple[list[ExtractedMenuItem], FailedPage | None, str, list[ExtractedItemPhoto]]:
        extractor = self._extractor_factory()
        try:
            raw = await extractor.extract(image_bytes=image_bytes, page_number=page_number)
        except VisionExtractionError:
            logger.warning("vision extractor failed for page %s", page_number, exc_info=True)
            return [], FailedPage(source_page=page_number, reason="vision_api_error"), extractor.model_name, []

        raw_items = raw.get("items") if isinstance(raw, dict) else None
        if not raw_items:
            return [], FailedPage(source_page=page_number, reason="no_items_detected"), extractor.model_name, []

        parsed: list[ExtractedMenuItem] = []
        try:
            for raw_item in raw_items:
                parsed.append(
                    ExtractedMenuItem(
                        name=raw_item["name"],
                        price=float(raw_item["price"]),
                        source_page=page_number,
                        source_index=int(raw_item["source_index"]),
                        category_hint=raw_item.get("category_hint"),
                    )
                )
        except (KeyError, TypeError, ValueError, ValidationError):
            logger.warning("invalid item payload for page %s", page_number, exc_info=True)
            return [], FailedPage(source_page=page_number, reason="vision_api_error"), extractor.model_name, []

        photos = await self._detect_item_photos(photo_extractor, image_bytes, page_number, parsed)
        return parsed, None, extractor.model_name, photos

    async def _detect_item_photos(
        self,
        photo_extractor: "ItemPhotoExtractor | None",
        image_bytes: bytes,
        page_number: int,
        items: list[ExtractedMenuItem],
    ) -> list[ExtractedItemPhoto]:
        """Optional dish-photo detection for one page. Always returns a list and
        never raises — a detection failure must never fail an import."""
        if photo_extractor is None or not items:
            return []
        try:
            item_refs = [ItemOnPage(source_index=it.source_index, name=it.name) for it in items]
            detections = await photo_extractor.detect(image_bytes, page_number, item_refs)
            valid_indices = {it.source_index for it in items}
            return build_item_photos(image_bytes, page_number, detections, valid_indices)
        except Exception:
            logger.warning("item photo detection failed for page %s", page_number, exc_info=True)
            return []

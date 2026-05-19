import asyncio
import logging
from typing import Callable, List

from pydantic import ValidationError

from models.menu_extraction import (
    ExtractedMenuItem,
    FailedPage,
    MenuExtractionResponse,
)
from services.vision_extractor import VisionExtractionError, VisionExtractor


logger = logging.getLogger(__name__)


class MenuExtractionService:
    """Coordinates per-page vision-LLM calls and merges the results into one response."""

    def __init__(self, extractor_factory: Callable[[], VisionExtractor]):
        # Factory rather than instance so each page can be retried in isolation
        # and so tests can produce different fakes per page.
        self._extractor_factory = extractor_factory

    async def extract(self, image_bytes_list: List[bytes]) -> MenuExtractionResponse:
        tasks = [self._extract_one(idx, b) for idx, b in enumerate(image_bytes_list)]
        per_page = await asyncio.gather(*tasks)

        items: list[ExtractedMenuItem] = []
        failed: list[FailedPage] = []
        model_name = ""
        for page_items, page_failure, page_model in per_page:
            items.extend(page_items)
            if page_failure is not None:
                failed.append(page_failure)
            if not model_name and page_model:
                model_name = page_model

        return MenuExtractionResponse(
            items=items,
            failed_pages=failed,
            model=model_name or "unknown",
            schema_version=1,
        )

    async def _extract_one(
        self, page_number: int, image_bytes: bytes
    ) -> tuple[list[ExtractedMenuItem], FailedPage | None, str]:
        extractor = self._extractor_factory()
        try:
            raw = await extractor.extract(image_bytes=image_bytes, page_number=page_number)
        except VisionExtractionError:
            logger.warning("vision extractor failed for page %s", page_number, exc_info=True)
            return [], FailedPage(source_page=page_number, reason="vision_api_error"), extractor.model_name

        raw_items = raw.get("items") if isinstance(raw, dict) else None
        if not raw_items:
            return [], FailedPage(source_page=page_number, reason="no_items_detected"), extractor.model_name

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
            return [], FailedPage(source_page=page_number, reason="vision_api_error"), extractor.model_name

        return parsed, None, extractor.model_name

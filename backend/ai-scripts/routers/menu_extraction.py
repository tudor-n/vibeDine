import os
from typing import List

from fastapi import APIRouter, Depends, File, HTTPException, UploadFile

from models.menu_extraction import MenuExtractionResponse
from services.menu_extraction_service import MenuExtractionService
from services.vision_extractor import ClaudeExtractor, GeminiExtractor, VisionExtractor


router = APIRouter(prefix="/menu", tags=["menu-extraction"])


def _default_extractor_factory() -> VisionExtractor:
    provider = os.environ.get("AI_VISION_PROVIDER", "gemini").lower()
    if provider == "gemini":
        return GeminiExtractor()
    if provider == "claude":
        return ClaudeExtractor()
    raise RuntimeError(f"Unsupported AI_VISION_PROVIDER: {provider}")


def get_extraction_service() -> MenuExtractionService:
    """FastAPI dependency. Overridden in tests via app.dependency_overrides."""
    return MenuExtractionService(extractor_factory=_default_extractor_factory)


@router.post("/extract", response_model=MenuExtractionResponse)
async def extract_menu(
    photos: List[UploadFile] = File(default=[]),
    service: MenuExtractionService = Depends(get_extraction_service),
) -> MenuExtractionResponse:
    if not photos:
        raise HTTPException(status_code=400, detail="At least one photo is required.")
    image_bytes_list = [await p.read() for p in photos]
    return await service.extract(image_bytes_list)

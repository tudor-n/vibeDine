import os
from typing import Callable, List

import httpx
from fastapi import APIRouter, Depends, File, HTTPException, UploadFile

from models.menu_extraction import MenuExtractionResponse, PdfJobRequest
from services.menu_extraction_service import MenuExtractionService
from services.pdf_menu_extraction_service import PdfMenuExtractionService
from services.progress_reporter import ProgressReporter
from services.text_extractor import ClaudeTextExtractor, GeminiTextExtractor, TextExtractor
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


# ---------------------------------------------------------------------------
# PDF extraction endpoint (Packet F)
# ---------------------------------------------------------------------------

def _build_text_extractor_factory() -> Callable[[], TextExtractor]:
    """Create a text extractor factory based on environment variables."""
    provider = os.environ.get(
        "AI_TEXT_PROVIDER",
        os.environ.get("AI_VISION_PROVIDER", "gemini"),
    ).lower()

    def factory() -> TextExtractor:
        if provider == "gemini":
            return GeminiTextExtractor()
        if provider == "claude":
            return ClaudeTextExtractor()
        raise RuntimeError(f"Unsupported text provider: {provider}")

    return factory


def get_pdf_extraction_service_factory() -> Callable[[PdfJobRequest], PdfMenuExtractionService]:
    """FastAPI dependency that returns a per-request builder for PdfMenuExtractionService.

    Overridden in tests via app.dependency_overrides.
    """
    concurrency = int(os.environ.get("PDF_EXTRACT_CONCURRENCY", "8"))
    text_batch_size = int(os.environ.get("PDF_TEXT_BATCH_SIZE", "8"))
    render_scale = float(os.environ.get("PDF_RENDER_SCALE", "1.5"))
    render_quality = int(os.environ.get("PDF_RENDER_JPEG_QUALITY", "85"))

    text_extractor_factory = _build_text_extractor_factory()

    def builder(req: PdfJobRequest) -> PdfMenuExtractionService:
        reporter = ProgressReporter(
            callback_url=req.progress_callback_url,
            hmac_secret=req.hmac_secret,
        )
        return PdfMenuExtractionService(
            text_extractor_factory=text_extractor_factory,
            vision_extractor_factory=_default_extractor_factory,
            progress=reporter,
            concurrency=concurrency,
            text_batch_size=text_batch_size,
            render_scale=render_scale,
            render_quality=render_quality,
        )

    return builder


@router.post("/extract-pdf", response_model=MenuExtractionResponse)
async def extract_pdf(
    request: PdfJobRequest,
    factory: Callable[[PdfJobRequest], PdfMenuExtractionService] = Depends(
        get_pdf_extraction_service_factory
    ),
) -> MenuExtractionResponse:
    service = factory(request)
    return await service.run(request)

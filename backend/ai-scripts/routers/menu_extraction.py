from __future__ import annotations

import math
import os
from pathlib import Path
from typing import Callable, List
from urllib.parse import urlparse

import httpx
from fastapi import APIRouter, Depends, File, HTTPException, UploadFile

from models.menu_extraction import MenuExtractionResponse, PdfJobRequest
from services.menu_extraction_service import MenuExtractionService
from services.pdf_menu_extraction_service import PdfMenuExtractionService, PdfReadError
from services.progress_reporter import ProgressReporter
from services.text_extractor import ClaudeTextExtractor, GeminiTextExtractor, TextExtractor
from services.vision_extractor import ClaudeExtractor, GeminiExtractor, VisionExtractor


router = APIRouter(prefix="/menu", tags=["menu-extraction"])

_DEFAULT_STORAGE_ROOT = "/tmp/menu-imports"
_DEFAULT_CALLBACK_HOSTS = {
    "localhost",
    "127.0.0.1",
    "::1",
    "backend-api",
}


def _read_int_env(name: str, default: int, *, minimum: int, maximum: int | None = None) -> int:
    raw = os.environ.get(name)
    try:
        value = int(raw) if raw is not None else default
    except ValueError as exc:
        raise ValueError(f"{name} must be an integer.") from exc

    if value < minimum:
        raise ValueError(f"{name} must be >= {minimum}.")
    if maximum is not None and value > maximum:
        raise ValueError(f"{name} must be <= {maximum}.")
    return value


def _read_float_env(name: str, default: float, *, greater_than: float) -> float:
    raw = os.environ.get(name)
    try:
        value = float(raw) if raw is not None else default
    except ValueError as exc:
        raise ValueError(f"{name} must be a number.") from exc

    if not math.isfinite(value):
        raise ValueError(f"{name} must be finite.")
    if value <= greater_than:
        raise ValueError(f"{name} must be > {greater_than:g}.")
    return value


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
    concurrency = _read_int_env("PDF_EXTRACT_CONCURRENCY", 8, minimum=1)
    provider_concurrency = _read_int_env(
        "PDF_PROVIDER_MAX_CONCURRENCY",
        min(4, concurrency),
        minimum=1,
    )
    text_batch_size = _read_int_env("PDF_TEXT_BATCH_SIZE", 8, minimum=1)
    render_scale = _read_float_env("PDF_RENDER_SCALE", 1.5, greater_than=0.0)
    render_quality = _read_int_env("PDF_RENDER_JPEG_QUALITY", 85, minimum=1, maximum=100)

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
            provider_concurrency=provider_concurrency,
            text_batch_size=text_batch_size,
            render_scale=render_scale,
            render_quality=render_quality,
        )

    return builder


def _validate_storage_path(storage_path: str) -> str:
    root = Path(
        os.environ.get("MENU_IMPORT_STORAGE_ROOT", _DEFAULT_STORAGE_ROOT)
    ).expanduser().resolve(strict=False)
    candidate = Path(storage_path).expanduser()
    if not candidate.is_absolute():
        candidate = root / candidate
    resolved = candidate.resolve(strict=False)

    if not _is_relative_to(resolved, root):
        raise HTTPException(
            status_code=400,
            detail="storage_path must be under MENU_IMPORT_STORAGE_ROOT",
        )
    return str(resolved)


def _validate_progress_callback_url(callback_url: str) -> None:
    parsed = urlparse(callback_url)
    host = parsed.hostname.lower() if parsed.hostname else ""
    if parsed.scheme not in {"http", "https"} or not host:
        raise HTTPException(
            status_code=400,
            detail="progress_callback_url must be an absolute HTTP(S) URL",
        )

    allowed_hosts = _allowed_callback_hosts()
    if host not in allowed_hosts:
        raise HTTPException(
            status_code=400,
            detail="progress_callback_url host is not allowed",
        )


def _allowed_callback_hosts() -> set[str]:
    raw = os.environ.get("MENU_IMPORT_CALLBACK_HOSTS", "")
    if not raw.strip():
        return set(_DEFAULT_CALLBACK_HOSTS)
    return {
        host
        for entry in raw.split(",")
        if (host := _normalize_host(entry))
    }


def _normalize_host(entry: str) -> str:
    value = entry.strip().lower()
    if not value:
        return ""
    if "://" in value:
        return (urlparse(value).hostname or "").lower()
    if value.startswith("[") and "]" in value:
        return value[1:value.index("]")]
    if value.count(":") == 1:
        return value.split(":", 1)[0]
    return value


def _is_relative_to(path: Path, root: Path) -> bool:
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


@router.post("/extract-pdf", response_model=MenuExtractionResponse)
async def extract_pdf(
    request: PdfJobRequest,
    factory: Callable[[PdfJobRequest], PdfMenuExtractionService] = Depends(
        get_pdf_extraction_service_factory
    ),
) -> MenuExtractionResponse:
    request = request.model_copy(
        update={"storage_path": _validate_storage_path(request.storage_path)}
    )
    _validate_progress_callback_url(request.progress_callback_url)
    service = factory(request)
    try:
        return await service.run(request)
    except PdfReadError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc

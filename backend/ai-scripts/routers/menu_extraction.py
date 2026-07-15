from __future__ import annotations

import math
import os
from pathlib import Path
from typing import Callable, List
from urllib.parse import urlparse
from uuid import UUID, uuid4

import httpx
from fastapi import APIRouter, Depends, File, Form, HTTPException, UploadFile

from models.menu_extraction import MenuExtractionResponse, PdfJobRequest
from services.item_photo_extractor import resolve_item_photo_extractor
from services.menu_extraction_service import MenuExtractionService
from services.menu_organizer import resolve_organizer
from services.pdf_menu_extraction_service import (
    DEFAULT_MAX_PDF_PAGES,
    PdfMenuExtractionService,
    PdfReadError,
    PdfTooManyPagesError,
)
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
    return MenuExtractionService(
        extractor_factory=_default_extractor_factory,
        organizer_factory=resolve_organizer,
        photo_extractor_factory=resolve_item_photo_extractor,
        concurrency=_read_int_env("MENU_EXTRACT_CONCURRENCY", 4, minimum=1),
    )


@router.post("/extract", response_model=MenuExtractionResponse)
async def extract_menu(
    photos: List[UploadFile] = File(default=[]),
    locale: str = Form("ro-RO"),
    extract_item_photos: bool = Form(False),
    service: MenuExtractionService = Depends(get_extraction_service),
) -> MenuExtractionResponse:
    if not photos:
        raise HTTPException(status_code=400, detail="At least one photo is required.")

    # Cap the batch size and total bytes before reading everything into memory,
    # so one request cannot OOM the service or burst into unbounded paid calls.
    max_photos = _read_int_env("MENU_EXTRACT_MAX_PHOTOS", 20, minimum=1)
    if len(photos) > max_photos:
        raise HTTPException(
            status_code=413,
            detail=f"Too many photos: {len(photos)} (maximum {max_photos}).",
        )
    max_total_bytes = _read_int_env(
        "MENU_EXTRACT_MAX_TOTAL_BYTES", 25_000_000, minimum=1
    )

    image_bytes_list: list[bytes] = []
    total_bytes = 0
    for p in photos:
        data = await p.read()
        total_bytes += len(data)
        if total_bytes > max_total_bytes:
            raise HTTPException(
                status_code=413,
                detail=(
                    f"Uploaded photos exceed the maximum total size of "
                    f"{max_total_bytes} bytes."
                ),
            )
        image_bytes_list.append(data)

    return await service.extract(
        image_bytes_list, locale=locale, extract_item_photos=extract_item_photos
    )


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
    vision_batch_size = _read_int_env("PDF_VISION_BATCH_SIZE", 1, minimum=1)
    provider_timeout_seconds = _read_float_env(
        "PDF_PROVIDER_TIMEOUT_SECONDS",
        60.0,
        greater_than=0.0,
    )
    render_scale = _read_float_env("PDF_RENDER_SCALE", 1.5, greater_than=0.0)
    render_quality = _read_int_env("PDF_RENDER_JPEG_QUALITY", 85, minimum=1, maximum=100)
    max_embedded_image_bytes = _read_int_env(
        "PDF_MAX_EMBEDDED_IMAGE_BYTES",
        4_000_000,
        minimum=0,
    )
    item_photo_max_pages = _read_int_env("ITEM_PHOTO_MAX_PAGES", 50, minimum=0)
    organizer_chunk_size = _read_int_env("PDF_ORGANIZER_CHUNK_SIZE", 40, minimum=1)
    max_pages = _read_int_env("MAX_PDF_PAGES", DEFAULT_MAX_PDF_PAGES, minimum=1)

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
            vision_batch_size=vision_batch_size,
            provider_timeout_seconds=provider_timeout_seconds,
            render_scale=render_scale,
            render_quality=render_quality,
            max_embedded_image_bytes=max_embedded_image_bytes,
            organizer_factory=resolve_organizer,
            organizer_chunk_size=organizer_chunk_size,
            # Only wire the detector when this import opted in, so the default
            # path is byte-identical to before.
            item_photo_extractor_factory=(
                resolve_item_photo_extractor if req.extract_item_photos else None
            ),
            item_photo_max_pages=item_photo_max_pages,
            max_pages=max_pages,
        )

    return builder


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


@router.post("/extract-pdf", response_model=MenuExtractionResponse)
async def extract_pdf(
    file: UploadFile = File(...),
    job_id: UUID = Form(...),
    restaurant_id: UUID = Form(...),
    progress_callback_url: str = Form(..., min_length=1),
    hmac_secret: str = Form(..., min_length=8),
    provider: str = Form("gemini"),
    locale: str = Form("ro-RO"),
    extract_item_photos: bool = Form(False),
    factory: Callable[[PdfJobRequest], PdfMenuExtractionService] = Depends(
        get_pdf_extraction_service_factory
    ),
) -> MenuExtractionResponse:
    """The PDF arrives as a multipart upload — the caller shares no filesystem
    with this service. It is spooled to a temp file under
    MENU_IMPORT_STORAGE_ROOT for the extraction pipeline and removed after."""
    _validate_progress_callback_url(progress_callback_url)

    storage_root = Path(
        os.environ.get("MENU_IMPORT_STORAGE_ROOT", _DEFAULT_STORAGE_ROOT)
    ).expanduser()
    storage_root.mkdir(parents=True, exist_ok=True)
    storage_path = storage_root / f"{job_id}-{uuid4().hex}.pdf"

    max_upload_bytes = _read_int_env(
        "MENU_IMPORT_MAX_PDF_BYTES", 104_857_600, minimum=1
    )

    try:
        # Stream to disk in bounded chunks so an oversized upload cannot fill the
        # volume — abort (and clean up in `finally`) the moment the cap is passed.
        written = 0
        with storage_path.open("wb") as out:
            while True:
                chunk = file.file.read(1024 * 1024)
                if not chunk:
                    break
                written += len(chunk)
                if written > max_upload_bytes:
                    raise HTTPException(
                        status_code=413,
                        detail=(
                            f"Uploaded PDF exceeds the maximum size of "
                            f"{max_upload_bytes} bytes."
                        ),
                    )
                out.write(chunk)

        request = PdfJobRequest(
            job_id=job_id,
            restaurant_id=restaurant_id,
            storage_path=str(storage_path),
            progress_callback_url=progress_callback_url,
            hmac_secret=hmac_secret,
            provider=provider,
            locale=locale,
            extract_item_photos=extract_item_photos,
        )
        service = factory(request)
        try:
            return await service.run(request)
        except PdfTooManyPagesError as exc:
            raise HTTPException(status_code=413, detail=str(exc)) from exc
        except PdfReadError as exc:
            raise HTTPException(status_code=400, detail=str(exc)) from exc
    finally:
        storage_path.unlink(missing_ok=True)

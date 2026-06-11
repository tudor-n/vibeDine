from typing import List, Literal, Optional
from uuid import UUID

from pydantic import BaseModel, Field

from models.menu_organizer import OrganizerResult


class ExtractedMenuItem(BaseModel):
    name: str = Field(min_length=1, max_length=120)
    price: float = Field(gt=0, le=99_999_999.99)
    source_page: int = Field(ge=0)
    source_index: int = Field(ge=0)
    description: Optional[str] = Field(default=None, max_length=1000)
    category_hint: Optional[str] = None
    confidence: Optional[float] = Field(default=None, ge=0.0, le=1.0)


class FailedPage(BaseModel):
    source_page: int = Field(ge=0)
    reason: Literal["unreadable", "no_items_detected", "vision_api_error"]


class MenuExtractionResponse(BaseModel):
    items: List[ExtractedMenuItem]
    failed_pages: List[FailedPage] = []
    model: str
    schema_version: int = 2
    hierarchy: Optional[OrganizerResult] = None


# ---------------------------------------------------------------------------
# Packet D additions
# ---------------------------------------------------------------------------

PdfPageKind = Literal["text", "image", "skip"]


class BatchTextExtractionPage(BaseModel):
    page_number: int = Field(ge=0)
    text: str = Field(max_length=200_000)


class BatchTextExtractionInput(BaseModel):
    pages: List[BatchTextExtractionPage]


class PdfJobRequest(BaseModel):
    job_id: UUID
    restaurant_id: UUID
    storage_path: str = Field(min_length=1)
    progress_callback_url: str = Field(min_length=1)
    hmac_secret: str = Field(min_length=8)
    provider: str = "gemini"
    locale: str = "ro-RO"


class ProgressEvent(BaseModel):
    job_id: UUID
    status: Literal["pending", "extracting", "complete", "failed", "cancelled"]
    phase: Literal["parsing", "classifying", "extracting", "finalizing"]
    processed_pages: int = Field(ge=0)
    total_pages: int = Field(ge=0)
    failed_pages: int = Field(ge=0)
    eta_seconds: Optional[float] = None
    error_message: Optional[str] = None

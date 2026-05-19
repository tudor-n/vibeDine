from typing import List, Literal, Optional

from pydantic import BaseModel, Field


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
    schema_version: int = 1

from typing import List, Literal, Optional

from pydantic import BaseModel, Field


class ItemRef(BaseModel):
    source_page: int = Field(ge=0)
    source_index: int = Field(ge=0)
    confidence: float = Field(ge=0.0, le=1.0)


class OrganizedSubcategory(BaseModel):
    name: str = Field(min_length=1, max_length=80)
    sort_order: int = Field(ge=0)
    item_refs: List[ItemRef] = []


class OrganizedCategory(BaseModel):
    name: str = Field(min_length=1, max_length=80)
    sort_order: int = Field(ge=0)
    subcategories: List[OrganizedSubcategory] = []
    direct_item_refs: List[ItemRef] = []


class OrganizerResult(BaseModel):
    source: Literal["ai", "hint-fallback"]
    categories: List[OrganizedCategory] = []
    uncategorized_item_refs: List[ItemRef] = []
    model_notes: Optional[str] = None

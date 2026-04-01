from pydantic import BaseModel
from typing import Optional


class CartItem(BaseModel):
    product_id: str
    quantity: int
    category: Optional[str] = None
    tags: Optional[list[str]] = []


class MenuItem(BaseModel):
    product_id: str
    name: str
    category: str
    tags: Optional[list[str]] = []


class RecommendationRequest(BaseModel):
    cart_items: list[CartItem]
    table_id: str
    order_history: Optional[list[list[str]]] = []
    menu_items: Optional[list[MenuItem]] = []
    active_directives: Optional[list[dict]] = []


class Recommendation(BaseModel):
    product_id: str
    reason: str
    confidence: float


class RecommendationResponse(BaseModel):
    recommendations: list[Recommendation]


class SimilarityRequest(BaseModel):
    order_history: list[list[str]]


class SimilarityRule(BaseModel):
    item_a_id: str
    item_b_id: str
    score: float


class SimilarityResponse(BaseModel):
    rules: list[SimilarityRule]


class DirectiveRequest(BaseModel):
    raw_prompt: str


class DirectiveResponse(BaseModel):
    parsed_rules: dict

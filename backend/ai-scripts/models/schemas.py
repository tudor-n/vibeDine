from pydantic import BaseModel
from typing import List, Optional


class CartItem(BaseModel):
    product_id: str
    quantity: int
    name: Optional[str] = None


class MenuItem(BaseModel):
    product_id: str
    name: str


class RecommendationRequest(BaseModel):
    cart_items: List[CartItem]
    table_id: str
    menu_items: Optional[List[MenuItem]] = []
    order_history: Optional[List[str]] = []
    language: Optional[str] = "ro"


class Recommendation(BaseModel):
    product_id: str
    reason: str
    confidence: float


class RecommendationResponse(BaseModel):
    recommendations: List[Recommendation]
    summary: str = ""


class AutoGenerateRequest(BaseModel):
    order_history: List[List[str]]


class SimilarityRule(BaseModel):
    product_a: str
    product_b: str
    score: float = 0.0


class AutoGenerateResponse(BaseModel):
    rules: List[SimilarityRule] = []


class RecommendationMenuItem(BaseModel):
    product_id: str
    name: str
    category: Optional[str] = None


class ParseRecommendationDirectiveRequest(BaseModel):
    raw_prompt: str
    menu_items: List[RecommendationMenuItem] = []


class RecommendationDirectiveRule(BaseModel):
    trigger_product_ids: List[str] = []
    recommend_product_ids: List[str] = []
    reason: str = ""
    confidence: float = 0.0


class ParseRecommendationDirectiveResponse(BaseModel):
    rules: List[RecommendationDirectiveRule] = []
    unresolved: List[str] = []
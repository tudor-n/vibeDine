from pydantic import BaseModel
from typing import List, Optional


class CartItem(BaseModel):
    product_id: str
    quantity: int


class MenuItem(BaseModel):
    product_id: str
    name: str


class RecommendationRequest(BaseModel):
    cart_items: List[CartItem]
    table_id: str
    menu_items: Optional[List[MenuItem]] = []
    order_history: Optional[List[str]] = []


class Recommendation(BaseModel):
    product_id: str
    reason: str
    confidence: float


class RecommendationResponse(BaseModel):
    recommendations: List[Recommendation]


class AutoGenerateRequest(BaseModel):
    order_history: List[List[str]]


class ParseDirectiveRequest(BaseModel):
    raw_prompt: str


class RecommendationMenuItem(BaseModel):
    product_id: str
    name: str
    category: Optional[str] = None


class ParseRecommendationDirectiveRequest(BaseModel):
    raw_prompt: str
    menu_items: List[RecommendationMenuItem] = []
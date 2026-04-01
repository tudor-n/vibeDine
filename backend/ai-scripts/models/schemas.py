from pydantic import BaseModel
from typing import List, Optional

class CartItem(BaseModel):
    product_id: str
    quantity: int

class RecommendationRequest(BaseModel):
    cart_items: List[CartItem]
    table_id: str
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
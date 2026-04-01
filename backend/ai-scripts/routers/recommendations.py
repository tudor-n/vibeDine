from fastapi import APIRouter

from models.schemas import RecommendationRequest, RecommendationResponse
from services.recommendation_engine import score_recommendations

router = APIRouter()


@router.post("/recommend", response_model=RecommendationResponse)
def recommend(request: RecommendationRequest):
    results = score_recommendations(
        cart_items=request.cart_items,
        menu_items=request.menu_items,
        order_history=request.order_history,
        active_directives=request.active_directives,
    )
    return RecommendationResponse(recommendations=results)

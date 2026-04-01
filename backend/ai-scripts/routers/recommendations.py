from fastapi import APIRouter
from models.schemas import RecommendationRequest, RecommendationResponse
from services.recommendation_engine import get_recommendations

router = APIRouter()

@router.post("/recommend", response_model=RecommendationResponse)
def recommend(request: RecommendationRequest):
    return get_recommendations(request)
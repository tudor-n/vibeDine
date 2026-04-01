from fastapi import APIRouter
from models.schemas import AutoGenerateRequest
from services.similarity_engine import generate_similarity_rules

router = APIRouter()

@router.post("/similarity/auto-generate")
def auto_generate(request: AutoGenerateRequest):
    return generate_similarity_rules(request)
from fastapi import APIRouter

from models.schemas import (
    SimilarityRequest,
    SimilarityResponse,
    SimilarityRule,
    DirectiveRequest,
    DirectiveResponse,
)
from services.similarity_engine import generate_similarity_rules
from services.directive_parser import parse_directive

router = APIRouter()


@router.post("/similarity/auto-generate", response_model=SimilarityResponse)
def auto_generate(request: SimilarityRequest):
    rules = generate_similarity_rules(request.order_history)
    return SimilarityResponse(
        rules=[SimilarityRule(**r) for r in rules]
    )


@router.post("/directives/parse", response_model=DirectiveResponse)
def parse(request: DirectiveRequest):
    parsed = parse_directive(request.raw_prompt)
    return DirectiveResponse(parsed_rules=parsed)

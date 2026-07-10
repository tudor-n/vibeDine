from google import genai
from google.genai import types
import logging
import os
import json
from fastapi import HTTPException
from models.schemas import AutoGenerateRequest

logger = logging.getLogger(__name__)

client = genai.Client(api_key=os.getenv("GEMINI_API_KEY", ""))

GEMINI_MODEL = "gemini-3.1-flash-lite"

GEMINI_SIMILARITY_RESPONSE_SCHEMA = {
    "type": "object",
    "properties": {
        "rules": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "product_a": {"type": "string"},
                    "product_b": {"type": "string"},
                    "score": {"type": "number"},
                },
                "required": ["product_a", "product_b", "score"],
            },
        }
    },
    "required": ["rules"],
}


def generate_similarity_rules(request: AutoGenerateRequest) -> dict:
    if not request.order_history:
        return {"rules": []}

    prompt = f"""You are a restaurant analytics engine.

Order history (each sub-list is one order containing product UUIDs):
{json.dumps(request.order_history)}

Identify pairs of product IDs that are frequently ordered together.
Respond ONLY with JSON matching this schema, no other text:
{{
    "rules": [
        {{
            "product_a": "uuid",
            "product_b": "uuid",
            "score": 0.9
        }}
    ]
}}

Only include product_id values that appear in the order history above."""

    try:
        response = client.models.generate_content(
            model=GEMINI_MODEL,
            contents=prompt,
            config=types.GenerateContentConfig(
                response_mime_type="application/json",
                response_schema=GEMINI_SIMILARITY_RESPONSE_SCHEMA,
            ),
        )
    except Exception as exc:
        # Genuine provider outage — surface it as 5xx instead of a fake empty 200.
        logger.error("Similarity provider call failed", exc_info=True)
        raise HTTPException(
            status_code=502, detail="similarity_provider_unavailable"
        ) from exc

    try:
        result = json.loads(response.text)
        if not isinstance(result, dict):
            raise ValueError("similarity response was not a JSON object")
    except (json.JSONDecodeError, TypeError, ValueError):
        logger.warning(
            "Similarity response was not valid JSON; returning no rules",
            exc_info=True,
        )
        return {"rules": []}

    # Post-filter: drop any rule referencing a product id not present in the
    # request's order history (guards against hallucinated / injected ids).
    valid_ids = {pid for order in request.order_history for pid in order}
    rules = result.get("rules") or []
    filtered = [
        r
        for r in rules
        if isinstance(r, dict)
        and r.get("product_a") in valid_ids
        and r.get("product_b") in valid_ids
    ]
    return {"rules": filtered}

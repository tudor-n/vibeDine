from google import genai
from google.genai import types
import logging
import os
import json
from fastapi import HTTPException
from models.schemas import RecommendationRequest

logger = logging.getLogger(__name__)

client = genai.Client(api_key=os.getenv("GEMINI_API_KEY", ""))

GEMINI_MODEL = "gemini-3.1-flash-lite"


def get_recommendations(request: RecommendationRequest) -> dict:
    if not request.menu_items:
        return {"recommendations": [], "summary": ""}

    valid_ids = {item.product_id for item in request.menu_items}
    menu_context = [{"product_id": item.product_id, "name": item.name} for item in request.menu_items]
    cart_context = [
        {"product_id": item.product_id, "name": item.name, "quantity": item.quantity}
        for item in request.cart_items
    ]

    lang = (request.language or "ro").lower()
    language_name = "English" if lang.startswith("en") else "Romanian"

    prompt = f"""You are a restaurant upsell recommendation engine.

Customer cart (the dishes/drinks already chosen): {json.dumps(cart_context, ensure_ascii=False)}

Available products to recommend (use ONLY these exact product_id values):
{json.dumps(menu_context, ensure_ascii=False)}

Recommend 2 to 3 products from the available list that genuinely pair well with the
items already in the cart.

For each recommendation, write "reason": one short, appetizing, customer-facing
sentence (max ~12 words) that explains WHY this specific product goes well with what
is already in the cart. Refer to the real items by what they are. Do NOT output the
literal word "pairing" or any placeholder text. Write every "reason" in {language_name}.

Also write "summary": one warm, concise customer-facing sentence (in {language_name})
introducing the picks as a whole — e.g. for Romanian something like "Am găsit câteva
completări care merg de minune cu ce ai ales."

Respond ONLY with JSON matching this schema, no other text:
{{
    "summary": "<one warm {language_name} sentence introducing the picks>",
    "recommendations": [
        {{
            "product_id": "exact-uuid-from-available-list",
            "reason": "<short {language_name} sentence on why it pairs with the cart>",
            "confidence": 0.85
        }}
    ]
}}

Every product_id in your response MUST appear verbatim in the available products list."""

    try:
        response = client.models.generate_content(
            model=GEMINI_MODEL,
            contents=prompt,
            config=types.GenerateContentConfig(response_mime_type="application/json")
        )
    except Exception as exc:
        # Genuine provider outage — surface it as 5xx so the .NET caller can
        # retry/alert rather than recording a fake "no recommendations" success.
        logger.error("Recommendation provider call failed", exc_info=True)
        raise HTTPException(
            status_code=502, detail="recommendation_provider_unavailable"
        ) from exc

    try:
        result = json.loads(response.text)
        if not isinstance(result, dict):
            raise ValueError("recommendation response was not a JSON object")
    except (json.JSONDecodeError, TypeError, ValueError):
        # The provider responded but with unparseable content — degrade to an
        # empty result (documented graceful path) rather than 5xx.
        logger.warning(
            "Recommendation response was not valid JSON; returning no recommendations",
            exc_info=True,
        )
        return {"recommendations": [], "summary": ""}

    recs = result.get("recommendations", [])
    validated = [r for r in recs if r.get("product_id") in valid_ids]
    return {"recommendations": validated, "summary": result.get("summary", "")}
from google import genai
from google.genai import types
import os
import json
from models.schemas import RecommendationRequest

client = genai.Client(api_key=os.getenv("GEMINI_API_KEY", ""))

GEMINI_MODEL = "gemini-3.1-flash-lite"


def get_recommendations(request: RecommendationRequest) -> dict:
    if not request.menu_items:
        return {"recommendations": []}

    valid_ids = {item.product_id for item in request.menu_items}
    menu_context = [{"product_id": item.product_id, "name": item.name} for item in request.menu_items]
    cart_context = [{"product_id": item.product_id, "quantity": item.quantity} for item in request.cart_items]

    prompt = f"""You are a restaurant upsell recommendation engine.

Customer cart: {json.dumps(cart_context)}

Available products to recommend (use ONLY these exact product_id values):
{json.dumps(menu_context)}

Recommend 2 to 3 products from the available list that pair well with the cart.
Respond ONLY with JSON matching this schema, no other text:
{{
    "recommendations": [
        {{
            "product_id": "exact-uuid-from-available-list",
            "reason": "pairing",
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
        result = json.loads(response.text)
        recs = result.get("recommendations", [])
        validated = [r for r in recs if r.get("product_id") in valid_ids]
        return {"recommendations": validated}
    except Exception:
        return {"recommendations": []}
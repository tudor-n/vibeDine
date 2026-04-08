from google import genai
from google.genai import types
import os
import json
from models.schemas import AutoGenerateRequest

client = genai.Client(api_key=os.getenv("GEMINI_API_KEY", ""))

GEMINI_MODEL = "gemini-3-flash-preview"


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
            config=types.GenerateContentConfig(response_mime_type="application/json")
        )
        return json.loads(response.text)
    except Exception:
        return {"rules": []}
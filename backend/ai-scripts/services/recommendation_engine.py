from google import genai
from google.genai import types
import os
import json
from models.schemas import RecommendationRequest

client = genai.Client(api_key=os.getenv("GEMINI_API_KEY", ""))

def get_recommendations(request: RecommendationRequest) -> dict:
    prompt = f"""
    Given the following cart items: {request.cart_items}
    And the following order history: {request.order_history}
    Generate 2 product recommendations.
    Respond strictly in JSON format matching this schema:
    {{
        "recommendations": [
            {{
                "product_id": "uuid-string",
                "reason": "pairing | upsell | flash_deal",
                "confidence": 0.85
            }}
        ]
    }}
    Only use UUIDs from the provided context or generate valid UUID format strings.
    """
    
    response = client.models.generate_content(
        model='gemini-3-flash-preview',
        contents=prompt,
        config=types.GenerateContentConfig(response_mime_type="application/json")
    )
    
    try:
        return json.loads(response.text)
    except:
        return {"recommendations": []}
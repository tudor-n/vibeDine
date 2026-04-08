from google import genai
from google.genai import types
import os
import json
from models.schemas import AutoGenerateRequest

client = genai.Client(api_key=os.getenv("GEMINI_API_KEY", ""))

def generate_similarity_rules(request: AutoGenerateRequest) -> dict:
    prompt = f"""
    Given the following order history where each sub-list is an order containing product IDs:
    {request.order_history}
    Identify items that are frequently bought together and generate similarity rules.
    Respond strictly in JSON format matching this schema:
    {{
        "rules": [
            {{
                "product_a": "uuid",
                "product_b": "uuid",
                "score": 0.9,
                "description": "Frequently bought together"
            }}
        ]
    }}
    """
    
    response = client.models.generate_content(
        model='gemini-3-flash-preview',
        contents=prompt,
        config=types.GenerateContentConfig(response_mime_type="application/json")
    )
    
    try:
        return json.loads(response.text)
    except:
        return {"rules": []}
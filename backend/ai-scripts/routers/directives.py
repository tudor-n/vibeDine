from fastapi import APIRouter
from models.schemas import ParseDirectiveRequest, ParseRecommendationDirectiveRequest
from google import genai
from google.genai import types
import os
import json

router = APIRouter()
client = genai.Client(api_key=os.getenv("GEMINI_API_KEY", ""))

GEMINI_MODEL = "gemini-3.1-flash-lite"


@router.post("/directives/parse")
def parse_directive(request: ParseDirectiveRequest):
    prompt = f"""Parse the following restaurant AI directive into structured rules:
"{request.raw_prompt}"

Respond ONLY with JSON matching this schema, no other text:
{{
    "parsed_rules": {{
        "keywords": ["list", "of", "keywords"],
        "action": "boost_category | recommend_specific",
        "target": "target string"
    }}
}}"""

    try:
        response = client.models.generate_content(
            model=GEMINI_MODEL,
            contents=prompt,
            config=types.GenerateContentConfig(response_mime_type="application/json")
        )
        return json.loads(response.text)
    except Exception:
        return {"parsed_rules": {}}


@router.post("/recommendation-directives/parse")
def parse_recommendation_directive(request: ParseRecommendationDirectiveRequest):
    menu_payload = [item.model_dump() for item in request.menu_items]
    menu_json = json.dumps(menu_payload, ensure_ascii=False)

    prompt = f"""You are parsing a restaurant recommendation rule written by the admin.

Admin rule (Romanian or English):
"{request.raw_prompt}"

Available menu items (resolve admin's item names to these exact product_ids):
{menu_json}

Extract zero or more recommendation rules. Each rule says:
"when the customer's cart contains any of {{trigger_product_ids}},
 recommend {{recommend_product_ids}} with this {{reason}}".

Match item names semantically (handle Romanian diacritics, plurals, abbreviations,
and synonyms — e.g. 'limonada cu menta' should match 'Limonadă cu mentă' or
'Mint Lemonade'). If a product name in the rule cannot be confidently matched
to any menu item, list it under 'unresolved' instead of guessing.

Respond ONLY with this JSON, no other text:
{{
  "rules": [
    {{
      "trigger_product_ids": ["<uuid>"],
      "recommend_product_ids": ["<uuid>"],
      "reason": "<short customer-facing explanation in same language as rule>",
      "confidence": 0.0
    }}
  ],
  "unresolved": ["<original name>"]
}}"""

    try:
        response = client.models.generate_content(
            model=GEMINI_MODEL,
            contents=prompt,
            config=types.GenerateContentConfig(response_mime_type="application/json"),
        )
        return json.loads(response.text)
    except Exception:
        return {"rules": [], "unresolved": []}
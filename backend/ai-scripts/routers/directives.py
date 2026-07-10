from fastapi import APIRouter, HTTPException
from models.schemas import (
    ParseRecommendationDirectiveRequest,
    ParseRecommendationDirectiveResponse,
)
from google import genai
from google.genai import types
import logging
import os
import json

logger = logging.getLogger(__name__)

router = APIRouter()
client = genai.Client(api_key=os.getenv("GEMINI_API_KEY", ""))

GEMINI_MODEL = "gemini-3.1-flash-lite"

GEMINI_DIRECTIVE_RESPONSE_SCHEMA = {
    "type": "object",
    "properties": {
        "rules": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "trigger_product_ids": {"type": "array", "items": {"type": "string"}},
                    "recommend_product_ids": {"type": "array", "items": {"type": "string"}},
                    "reason": {"type": "string"},
                    "confidence": {"type": "number"},
                },
                "required": [
                    "trigger_product_ids",
                    "recommend_product_ids",
                    "reason",
                    "confidence",
                ],
            },
        },
        "unresolved": {"type": "array", "items": {"type": "string"}},
    },
    "required": ["rules", "unresolved"],
}


@router.post(
    "/recommendation-directives/parse",
    response_model=ParseRecommendationDirectiveResponse,
)
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
            config=types.GenerateContentConfig(
                response_mime_type="application/json",
                response_schema=GEMINI_DIRECTIVE_RESPONSE_SCHEMA,
            ),
        )
    except Exception as exc:
        # Genuine provider outage — surface as 5xx instead of a fake empty 200.
        logger.error("Directive parse provider call failed", exc_info=True)
        raise HTTPException(
            status_code=502, detail="directive_provider_unavailable"
        ) from exc

    try:
        result = json.loads(response.text)
        if not isinstance(result, dict):
            raise ValueError("directive response was not a JSON object")
    except (json.JSONDecodeError, TypeError, ValueError):
        logger.warning(
            "Directive response was not valid JSON; returning no rules",
            exc_info=True,
        )
        return {"rules": [], "unresolved": []}

    # Post-filter every returned product id against the request's known menu ids
    # so hallucinated / injected ids never reach the caller. A rule that loses
    # all triggers or all recommendations is dropped as meaningless.
    valid_ids = {item.product_id for item in request.menu_items}
    filtered_rules = []
    for rule in result.get("rules") or []:
        if not isinstance(rule, dict):
            continue
        triggers = [pid for pid in (rule.get("trigger_product_ids") or []) if pid in valid_ids]
        recommends = [pid for pid in (rule.get("recommend_product_ids") or []) if pid in valid_ids]
        if not triggers or not recommends:
            continue
        filtered_rules.append(
            {
                "trigger_product_ids": triggers,
                "recommend_product_ids": recommends,
                "reason": rule.get("reason", ""),
                "confidence": rule.get("confidence", 0.0),
            }
        )

    return {"rules": filtered_rules, "unresolved": result.get("unresolved") or []}

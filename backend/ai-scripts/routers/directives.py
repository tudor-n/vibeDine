from fastapi import APIRouter
from models.schemas import ParseDirectiveRequest
from google import genai
from google.genai import types
import os
import json

router = APIRouter()
client = genai.Client(api_key=os.getenv("GEMINI_API_KEY", ""))

GEMINI_MODEL = "gemini-2.0-flash"


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
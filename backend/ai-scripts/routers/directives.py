from fastapi import APIRouter
from models.schemas import ParseDirectiveRequest
from google import genai
from google.genai import types
import os
import json

router = APIRouter()
client = genai.Client(api_key=os.getenv("GEMINI_API_KEY", ""))

@router.post("/directives/parse")
def parse_directive(request: ParseDirectiveRequest):
    prompt = f"""
    Parse the following restaurant AI directive into structured rules:
    "{request.raw_prompt}"
    Respond strictly in JSON format matching this schema:
    {{
        "parsed_rules": {{
            "keywords": ["list", "of", "keywords"],
            "action": "boost_category | recommend_specific",
            "target": "target string"
        }}
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
        return {"parsed_rules": {}}
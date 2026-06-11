# Segment 14 — AI service: bootstrap & routers — review findings

Scope review against the [Segment 14 checklist](CODE_REVIEW_ROADMAP.md#segment-14--ai-service--bootstrap--routers) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). The surface is six Python files plus the trust-boundary wiring around them: [main.py](backend/ai-scripts/main.py), [routers/recommendations.py](backend/ai-scripts/routers/recommendations.py), [routers/similarity.py](backend/ai-scripts/routers/similarity.py), [routers/directives.py](backend/ai-scripts/routers/directives.py), [routers/menu_extraction.py](backend/ai-scripts/routers/menu_extraction.py), [models/schemas.py](backend/ai-scripts/models/schemas.py), [models/menu_extraction.py](backend/ai-scripts/models/menu_extraction.py); plus [requirements.txt](backend/ai-scripts/requirements.txt), [Dockerfile](backend/ai-scripts/Dockerfile), the published ports in [docker-compose.yml:57-64](docker-compose.yml#L57-L64), and the C# call sites in [RecommendationService.cs:43-96](backend/RestaurantApi/Services/RecommendationService.cs#L43-L96), [AdminController.cs:67-90, 142-163, 233-247](backend/RestaurantApi/Controllers/AdminController.cs#L67-L247), [MenuExtractionClient.cs:33-38](backend/RestaurantApi/Services/MenuExtractionClient.cs#L33-L38).

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**8 findings, three of them HIGH.** The Python service is small (16 lines of [main.py](backend/ai-scripts/main.py), four routers averaging ~30 lines each) and the right things are present in skeleton form — Pydantic request models on `/recommend` and `/similarity/auto-generate`, a `response_model` on `/menu/extract`, file-upload through `UploadFile`, and an `AI_VISION_PROVIDER` env switch. The problems are concentrated in two clusters.

**Cluster 1 — `directives.py` bypasses the contract.** Both directive-parsing endpoints ([routers/directives.py:14, 39](backend/ai-scripts/routers/directives.py#L14-L39)) are missing `response_model`, return raw `dict` from `json.loads(response.text)` ([line 34, 80](backend/ai-scripts/routers/directives.py#L34-L80)), wrap the entire Gemini call in `except Exception: return {"parsed_rules": {}}` ([line 35-36, 81-82](backend/ai-scripts/routers/directives.py#L35-L82)), and use a hardcoded model string (`"gemini-2.0-flash"`, [line 11](backend/ai-scripts/routers/directives.py#L11)) instead of `GEMINI_VISION_MODEL` from `.env.example`. This is the supply side of Segment 10's [F5](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f5--medium--ai-client-failure-mapping-is-inconsistent-across-call-sites): the C# `AdminController.CreateDirective` persists a directive with `parsedRules = null` because Python is *successfully* returning `{"parsed_rules": {}}` on every Gemini failure. The 200 OK papers over every failure mode (missing API key, rate limit, malformed JSON, transient network error) and the admin's directive is silently lost. See [F1](#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-).

**Cluster 2 — no trust boundary, no inbound limits.** The service binds to `0.0.0.0:8000` ([Dockerfile:7](backend/ai-scripts/Dockerfile#L7)) and `docker-compose.yml` publishes that port to the host as `8000:8000` ([line 60](docker-compose.yml#L60)). There is no auth middleware, no shared-secret header, no IP allowlist, and no `python-multipart` upload caps. `/menu/extract` accepts `List[UploadFile] = File(default=[])` ([routers/menu_extraction.py:30](backend/ai-scripts/routers/menu_extraction.py#L30)) with no per-file size, total-size, count, or MIME-type check — anyone with network access to the host can POST a few hundred megabytes of binary into the service, where it goes straight to `await p.read()` ([line 35](backend/ai-scripts/routers/menu_extraction.py#L35)) and then to a paid LLM. See [F2](#f2--high--menuextract-has-no-file-size-count-or-mime-type-limits-in-the-python-service) and [F3](#f3--high--no-trust-boundary-the-service-binds-00008000-is-published-to-the-host-and-has-no-auth-on-llm-spending-endpoints).

The rest are operational hygiene: [F4](#f4--medium--requirementstxt-is-effectively-unpinned-supply-chain-and-reproducibility-risk) — `requirements.txt` has unpinned versions for fastapi, uvicorn, pydantic, google-genai, etc.; [F5](#f5--medium--mainpy-has-no-global-exception-handler-no-error-envelope-no-request_id-propagation) — `main.py` has no exception handlers, no consistent error envelope, no request-ID middleware (the roadmap explicitly asks for the last item); [F6](#f6--medium--api-contract-aijson-describes-an-api-that-does-not-exist) — [shared/contracts/api-contract-ai.json](shared/contracts/api-contract-ai.json) describes a `GET /sessions/:session_id/recommendations`-shaped API that bears no resemblance to what the Python service actually exposes (this is Segment 24's home but visible from here); [F7](#f7--low--unsupported-ai_vision_provider-raises-runtimeerror-which-becomes-a-default-500-with-a-traceback) — the unsupported-provider path raises `RuntimeError` instead of `HTTPException`; [F8](#f8--low--genaiclientapi_key-default-makes-a-missing-gemini_api_key-invisible) — `genai.Client(api_key=os.getenv("GEMINI_API_KEY", ""))` at module-import with no fail-fast makes a missing key indistinguishable from a working one once [F1](#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-) eats the exception.

What does check out: the absence of CORS middleware is correct ([main.py:1-16](backend/ai-scripts/main.py#L1-L16)) given the "server-to-server only" intent (the roadmap item passes outright). `/health` is liveness-only and never calls the LLM ([main.py:14-16](backend/ai-scripts/main.py#L14-L16)) — that one's clean. The Pydantic models in [models/schemas.py](backend/ai-scripts/models/schemas.py) and [models/menu_extraction.py](backend/ai-scripts/models/menu_extraction.py) are well-shaped: [ExtractedMenuItem](backend/ai-scripts/models/menu_extraction.py#L6-L13) has proper `Field` constraints (name 1-120 chars, price `gt=0 le=99_999_999.99`, confidence `[0, 1]`, etc.); [FailedPage](backend/ai-scripts/models/menu_extraction.py#L16-L18) uses a `Literal` for `reason` so unknown values are rejected. `python-multipart>=0.0.9` is pinned correctly per the roadmap's CVE-aware item ([requirements.txt:9](backend/ai-scripts/requirements.txt#L9)). The `/recommend` and `/similarity/auto-generate` routers do declare typed request and response models — only `directives.py` skips them. None of the request schemas accept a `restaurantId`: confirmed across `RecommendationRequest`, `AutoGenerateRequest`, `ParseDirectiveRequest`, `ParseRecommendationDirectiveRequest`, and the multipart `/menu/extract` body — the AI service is correctly stateless w.r.t. tenants.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| CORS: the AI service is only called by the backend API (server-to-server). Confirm CORS is restrictive or absent | ✓ Pass | No `CORSMiddleware` is added in [main.py](backend/ai-scripts/main.py) — the FastAPI default rejects browser cross-origin preflights, which is the intended state. |
| `/health` is liveness only — does not call the LLM provider | ✓ Pass | [main.py:14-16](backend/ai-scripts/main.py#L14-L16) — returns `{"status": "ok"}` synchronously, no network calls. |
| All endpoints accept and return validated Pydantic models (no `dict` blobs) | ❌ Fail — see [F1](#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-) | `/recommend` ✓ ([recommendations.py:7-9](backend/ai-scripts/routers/recommendations.py#L7-L9)) — `response_model=RecommendationResponse`. `/similarity/auto-generate` — request validated, no response model ([similarity.py:7-9](backend/ai-scripts/routers/similarity.py#L7-L9)). `/menu/extract` ✓ ([menu_extraction.py:28-36](backend/ai-scripts/routers/menu_extraction.py#L28-L36)). `/directives/parse` and `/recommendation-directives/parse` ❌ — no `response_model`, return raw `dict` from `json.loads`. |
| No endpoint accepts a `restaurantId` and exposes data — confirm the service is stateless w.r.t. tenants | ✓ Pass | Every Pydantic request model checked: `RecommendationRequest`, `AutoGenerateRequest`, `ParseDirectiveRequest`, `ParseRecommendationDirectiveRequest` — none reference `restaurant_id`. The C# call sites confirm: [RecommendationService.cs:43-57](backend/RestaurantApi/Services/RecommendationService.cs#L43-L57), [AdminController.cs:68](backend/RestaurantApi/Controllers/AdminController.cs#L68), [AdminController.cs:233](backend/RestaurantApi/Controllers/AdminController.cs#L233) — no caller sends a tenant ID. |
| Error responses use a consistent envelope and don't leak provider stack traces | ❌ Fail — see [F1](#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-), [F5](#f5--medium--mainpy-has-no-global-exception-handler-no-error-envelope-no-request_id-propagation), [F7](#f7--low--unsupported-ai_vision_provider-raises-runtimeerror-which-becomes-a-default-500-with-a-traceback) | No `app.exception_handler` is registered in [main.py](backend/ai-scripts/main.py). `directives.py` returns `{"parsed_rules": {}}` on *any* failure (200 OK). `menu_extraction.py` lets `RuntimeError` propagate to FastAPI's default 500 handler which serializes the traceback. |
| File upload (`/menu/extract`) limits: per-file size, total request size, file count, allowed MIME types | ❌ Fail — see [F2](#f2--high--menuextract-has-no-file-size-count-or-mime-type-limits-in-the-python-service) | `photos: List[UploadFile] = File(default=[])` ([menu_extraction.py:30](backend/ai-scripts/routers/menu_extraction.py#L30)) — no `max_size`, no `max_length`, no `content_type` allowlist. The router checks only `if not photos` ([line 33-34](backend/ai-scripts/routers/menu_extraction.py#L33-L34)). |
| Timeouts configured on the LLM client side so endpoints don't hang past the caller's deadline | ⚠ Partial — see [F4](#f4--medium--requirementstxt-is-effectively-unpinned-supply-chain-and-reproducibility-risk) (vision_extractor outside scope) | The actual provider timeouts live in [services/vision_extractor.py](backend/ai-scripts/services/vision_extractor.py) and [services/directive_parser.py](backend/ai-scripts/services/directive_parser.py) — segment 15/16. From this segment's window, the routers don't set their own `asyncio.wait_for` deadline; if the SDK defaults are wrong, every call hangs to the C# client's 10 s or 6 min timeout. |
| `python-multipart` version pinned ≥ 0.0.9 | ✓ Pass | [requirements.txt:9](backend/ai-scripts/requirements.txt#L9) — `python-multipart>=0.0.9` exactly satisfies the roadmap's CVE-aware ask. |
| Logging includes `request_id` propagated from the API | ❌ Fail — see [F5](#f5--medium--mainpy-has-no-global-exception-handler-no-error-envelope-no-request_id-propagation) | No `X-Request-ID` middleware, no `contextvars` plumbing, no structured logger anywhere in [main.py](backend/ai-scripts/main.py) or any router. `print(...)` and FastAPI's default uvicorn access log are the only logging surface. |
| How does the AI service know the request came from the trusted backend and not from the public internet? (network-only isolation, or a shared header secret?) (roadmap `↳`) | ❌ Fail — see [F3](#f3--high--no-trust-boundary-the-service-binds-00008000-is-published-to-the-host-and-has-no-auth-on-llm-spending-endpoints) | Neither. No shared secret header, no IP filter, no auth dependency. The compose file publishes `8000:8000` ([docker-compose.yml:60](docker-compose.yml#L60)) so anyone on the Docker host's network reaches it. |

---

## F1 — HIGH — `directives.py` swallows every failure into an empty 200 OK; no `response_model`, no error signal 🔥

[routers/directives.py:14-36](backend/ai-scripts/routers/directives.py#L14-L36):

```python
@router.post("/directives/parse")
def parse_directive(request: ParseDirectiveRequest):
    prompt = f"""Parse the following restaurant AI directive into structured rules:
"{request.raw_prompt}"
...
"""

    try:
        response = client.models.generate_content(
            model=GEMINI_MODEL,
            contents=prompt,
            config=types.GenerateContentConfig(response_mime_type="application/json")
        )
        return json.loads(response.text)
    except Exception:
        return {"parsed_rules": {}}
```

And the twin at [line 39-82](backend/ai-scripts/routers/directives.py#L39-L82) is the same shape — `except Exception: return {"rules": [], "unresolved": []}`.

Three compounding problems:

- **No `response_model`.** Both endpoints return `dict` from `json.loads(response.text)` straight to the wire. Gemini's `response_mime_type="application/json"` is best-effort: when the model occasionally returns a string-quoted-JSON or a JSON object that doesn't match the schema in the prompt, `json.loads` succeeds and the malformed result is forwarded to the C# caller. The roadmap names this exactly: *"All endpoints accept and return validated Pydantic models (no `dict` blobs)."*
- **`except Exception` is too wide.** It catches every flavour: missing `GEMINI_API_KEY` (`AuthenticationError`), rate-limit (`ResourceExhausted`), network failures (`OSError`), malformed JSON (`JSONDecodeError`), schema drift (an unguarded `KeyError` further upstream if there were any). The exception is swallowed silently — no logging, no traceback to `stderr`, no metric increment. The handler then returns a 200 OK with an empty result.
- **The C# caller can't tell apart "no rules" from "AI broken".** [AdminController.cs:67-90](backend/RestaurantApi/Controllers/AdminController.cs#L67-L90) inspects the response and persists a `parsedRules = null` directive when the AI returns `{"parsed_rules": {}}` — see [Segment 10 F5](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f5--medium--ai-client-failure-mapping-is-inconsistent-across-call-sites). The admin sees a "success" toast in the SPA and their directive is now dead weight.

The fix shape (out of scope): catch a narrow set, return a `502 Bad Gateway` (or a typed envelope like `{"error": "ai_unavailable", "detail": ...}`) with a logging line so the C# side can map it back to a clear 503. And declare `response_model=ParseDirectiveResponse` / `response_model=ParseRecommendationDirectiveResponse` so Pydantic validates the LLM output before it leaves the Python process. The Pydantic models already exist for the *request* side ([schemas.py:36-48](backend/ai-scripts/models/schemas.py#L36-L48)); the response-side twins are missing.

A related secondary issue in the same file: the model is hardcoded to `"gemini-2.0-flash"` ([line 11](backend/ai-scripts/routers/directives.py#L11)), bypassing the `GEMINI_VISION_MODEL` env override that `.env.example` advertises ([.env.example:10](backend/ai-scripts/.env.example#L10)). A team that wants to switch directive parsing to a more capable model has to edit code, not config.

---

## F2 — HIGH — `/menu/extract` has no file-size, count, or MIME-type limits in the Python service

[routers/menu_extraction.py:28-36](backend/ai-scripts/routers/menu_extraction.py#L28-L36):

```python
@router.post("/extract", response_model=MenuExtractionResponse)
async def extract_menu(
    photos: List[UploadFile] = File(default=[]),
    service: MenuExtractionService = Depends(get_extraction_service),
) -> MenuExtractionResponse:
    if not photos:
        raise HTTPException(status_code=400, detail="At least one photo is required.")
    image_bytes_list = [await p.read() for p in photos]
    return await service.extract(image_bytes_list)
```

The roadmap names four limits:

| Limit | Configured? |
|---|---|
| Per-file size | ❌ — `UploadFile` reads the whole file with `await p.read()` regardless of size. |
| Total request size | ❌ — uvicorn defaults to `limit_max_requests=None` and no `--limit-request-body`; the FastAPI app has no `Request` body cap. |
| File count | ❌ — `List[UploadFile]` accepts any number; 100 files is the same as 10. |
| Allowed MIME types | ❌ — no check on `p.content_type`. A `.exe`, a `.zip`, an arbitrary bytestream all reach `service.extract`. |

The C# side does enforce these in `MenuScanController` ([MenuScanController.cs:14-19](backend/RestaurantApi/Controllers/MenuScanController.cs#L14-L19)): `MaxFiles = 10`, `MaxFileBytes = 5L * 1024 * 1024` (5 MB), `AllowedContentTypes = { "image/jpeg", "image/png", "image/webp", "image/heic", "application/pdf" }`. That's a useful first line of defence — but the Python service is exposed *directly* on the host (see [F3](#f3--high--no-trust-boundary-the-service-binds-00008000-is-published-to-the-host-and-has-no-auth-on-llm-spending-endpoints)), so the C# guard is bypassable by skipping the C# layer entirely.

Pragmatic concerns:

- A naive client uploading a 200 MB phone-camera HEIC blob immediately blows process RSS by 200 MB (`await p.read()` reads into memory in one shot).
- A coordinated attacker can send a few thousand small files in a single multipart request, each forwarded to the LLM provider as a separate page; per-request LLM cost is then unbounded.
- A non-image file with `Content-Type: image/png` is forwarded to the vision provider, which will return a structured error that the service may or may not handle in segment 15 territory. Either way, the LLM call has already been made.

Fix shape (out of scope): a tiny dependency that wraps `UploadFile` with a `chunked` read up to a max, a per-router `Annotated[List[UploadFile], Depends(validate_uploads)]`, and a `BaseHTTPMiddleware` enforcing total-request-body cap. None of those exist today.

---

## F3 — HIGH — No trust boundary: the service binds `0.0.0.0:8000`, is published to the host, and has no auth on LLM-spending endpoints

[backend/ai-scripts/Dockerfile:7](backend/ai-scripts/Dockerfile#L7):

```dockerfile
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
```

[docker-compose.yml:57-64](docker-compose.yml#L57-L64):

```yaml
ai-service:
  build: ./backend/ai-scripts
  ports:
    - "8000:8000"
  volumes:
    - ./backend/ai-scripts:/app
  env_file:
    - ./backend/ai-scripts/.env
```

Combined effect:

- The container listens on every interface inside the container.
- The compose file publishes `8000` to the host network. On a developer laptop that's `localhost:8000`. On a shared dev/staging VM that's `<host-public-ip>:8000`.
- There is no authentication on any router (`grep` for `Depends`, `Security`, `APIKeyHeader`, `HTTPBearer`, `OAuth2` across [backend/ai-scripts/](backend/ai-scripts/) returns the single `Depends(get_extraction_service)` call which is a DI dependency, not auth).
- No CORS is configured — irrelevant for server-to-server, but worth noting that a same-origin browser script can reach the endpoint from `localhost`.

The roadmap's `↳` follow-up names the two acceptable answers: *"network-only isolation, or a shared header secret?"* The current state is neither.

The cost surface is the part that matters more than data exposure (the routers don't return tenant-shaped data, per the checklist):

- `/directives/parse` and `/recommendation-directives/parse` each call `client.models.generate_content` on Gemini — paid token spend per request, unbounded by [F2](#f2--high--menuextract-has-no-file-size-count-or-mime-type-limits-in-the-python-service).
- `/menu/extract` is the most expensive endpoint — multi-image vision is the priciest mode.
- `/recommend` and `/similarity/auto-generate` route to their respective engines which also call LLM providers (segment 16's window).

A single anonymous caller that discovers the host port can drain the project's LLM budget. The mitigation does not have to be elaborate: a single `X-AI-Service-Secret` header check at the FastAPI app level, with the C# `AiService` HttpClient wired to send it (one line in `Program.cs` next to the `BaseAddress`). The compose file would need `--internal` network for `ai-service` or to drop the `ports:` block (the C# container reaches `http://ai-service:8000` via service DNS regardless).

Cross-segment: Segment 01's checklist already touches port exposure for Mailpit. The ai-service port is the same shape of finding — but more consequential because the listener directly spends money on every request.

---

## F4 — MEDIUM — `requirements.txt` is effectively unpinned (supply-chain and reproducibility risk)

[requirements.txt](backend/ai-scripts/requirements.txt):

```
fastapi
uvicorn
pydantic
google-genai
python-dotenv
pytest
pytest-asyncio>=0.23
anthropic>=0.40.0
python-multipart>=0.0.9
```

Of nine entries:

- Three have lower bounds (`pytest-asyncio>=0.23`, `anthropic>=0.40.0`, `python-multipart>=0.0.9`).
- None have upper bounds.
- Six have no version constraint at all (`fastapi`, `uvicorn`, `pydantic`, `google-genai`, `python-dotenv`, `pytest`).

Three practical consequences:

- **Different developers get different builds.** A team member who set up the project in May gets Pydantic v2.7; one onboarding in November gets v2.10 or v3-alpha. The Dockerfile uses `pip install --no-cache-dir -r requirements.txt` with no lockfile, so even the CI image differs from a local one.
- **Pydantic v2 → v3 break is on the horizon.** When that lands without an upper bound, every model file ([schemas.py](backend/ai-scripts/models/schemas.py), [menu_extraction.py](backend/ai-scripts/models/menu_extraction.py)) breaks on first import.
- **FastAPI's breaking changes between minor versions land unannounced.** A `uvicorn` major (1.x → 2.x is plausible in a year) shifts default loop / lifespan handling.

The fix is a `pip-compile`-generated `requirements.lock` (or `uv pip compile`) or, equivalently, manually pinning each line to the current resolved version. Roadmap Segment 25 (`pip-audit`) won't catch this — `pip-audit` validates the installed set against vulnerabilities, not the spec file against reproducibility.

---

## F5 — MEDIUM — `main.py` has no global exception handler, no error envelope, no `request_id` propagation

[main.py:1-16](backend/ai-scripts/main.py#L1-L16):

```python
from fastapi import FastAPI
from routers import recommendations, similarity, directives, menu_extraction
from dotenv import load_dotenv

load_dotenv()

app = FastAPI(title="Restaurant AI Service")

app.include_router(recommendations.router)
app.include_router(similarity.router)
app.include_router(directives.router)
app.include_router(menu_extraction.router)

@app.get("/health")
def health():
    return {"status": "ok"}
```

Missing:

- **No `@app.exception_handler(Exception)`** — any unhandled exception (e.g. `RuntimeError` from [F7](#f7--low--unsupported-ai_vision_provider-raises-runtimeerror-which-becomes-a-default-500-with-a-traceback)) becomes a default `500 {"detail": "Internal Server Error"}`, but if `app.debug` were ever set, the response body becomes the full traceback (provider stack traces are exactly what the roadmap says not to leak).
- **No consistent error envelope.** Today: `HTTPException(400, "...")` produces `{"detail": "..."}`; an uncaught exception produces `{"detail": "Internal Server Error"}`; `directives.py` produces `{"parsed_rules": {}}` (no error indicator at all). Three shapes for three failure modes.
- **No `X-Request-ID` middleware.** The roadmap names this explicitly: *"Logging includes `request_id` propagated from the API."* The C# side already has no correlation-ID middleware either (Segment 10 [F8](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f8--low--logging-does-not-include-restaurantid-or-correlationid-via-beginscope)), so there's nothing to propagate today — but the natural home for the propagation handler on the Python side is here.
- **No `logging` configuration.** Uvicorn's default access log writes to stdout; application logs use bare `print` calls (where they exist). A structlog / loguru / stdlib `logging.basicConfig` would let downstream collectors parse fields.

These are all single-block additions in `main.py`. The cumulative effect of *not* having them is that when something goes wrong, the only signal is "the C# side logged a `502 Bad Gateway`" with no trace back to the offending Python request.

---

## F6 — MEDIUM — `api-contract-ai.json` describes an API that does not exist

[shared/contracts/api-contract-ai.json](shared/contracts/api-contract-ai.json) — 354 lines describing an `/api/v1`-prefixed surface with paths like `GET /sessions/:session_id/recommendations`, `POST /sessions/:session_id/flash-deal/:deal_id/claim`, `GET /admin/restaurants/:restaurant_id/ai-config/today`, and seven model shapes including `RecommendationContext`, `Recommendation` (with `id`, `type`, `menu_item`, `discount`, `reason_type`, `source`), `FlashDeal`, `AdminAIConfig`, `SimilarityRule`.

Actual Python surface (from [main.py](backend/ai-scripts/main.py) router inclusion plus router prefixes):

```
POST /recommend
POST /similarity/auto-generate
POST /directives/parse
POST /recommendation-directives/parse
POST /menu/extract
GET  /health
```

Zero path overlap. The Python `Recommendation` model is `{product_id, reason, confidence}` ([schemas.py:22-25](backend/ai-scripts/models/schemas.py#L22-L25)) — three fields, no `type`, no `menu_item`, no `discount`. The contract describes ML-pipeline detail (`recommendation_pipeline.step_1` ... `step_6`, [api-contract-ai.json:326-333](shared/contracts/api-contract-ai.json#L326-L333)) that is largely fiction relative to the actual `recommendation_engine.py` implementation (segment 16's window).

This is Segment 24's canonical home, but it's visible from Segment 14 because someone arriving at this segment to "verify the AI service contract against the schemas" will find the contract uses none of the schemas in [models/schemas.py](backend/ai-scripts/models/schemas.py). The drift is total. Two follow-up options:

1. Delete `api-contract-ai.json` and call it superseded.
2. Regenerate it from the FastAPI OpenAPI auto-doc (`app.openapi()` produces JSON that matches the actual routes) and treat that as the source of truth.

Either way, the current file misleads any reader who consults it for ground truth.

---

## F7 — LOW — Unsupported `AI_VISION_PROVIDER` raises `RuntimeError`, which becomes a default 500 with a traceback

[routers/menu_extraction.py:14-20](backend/ai-scripts/routers/menu_extraction.py#L14-L20):

```python
def _default_extractor_factory() -> VisionExtractor:
    provider = os.environ.get("AI_VISION_PROVIDER", "gemini").lower()
    if provider == "gemini":
        return GeminiExtractor()
    if provider == "claude":
        return ClaudeExtractor()
    raise RuntimeError(f"Unsupported AI_VISION_PROVIDER: {provider}")
```

`RuntimeError` is not an `HTTPException`, so FastAPI returns its default `500 {"detail": "Internal Server Error"}` — and emits the traceback to stderr by default. If `--debug` is ever turned on, that 500 body becomes the traceback (provider-shaped stack traces).

The fix is mechanical: `raise HTTPException(status_code=500, detail="Unsupported AI_VISION_PROVIDER")` — or better, fail at app startup so the error surfaces at deploy time, not at first request: a small `validate_config()` called from a FastAPI `lifespan` handler would do it.

Related: the factory runs per-request because `get_extraction_service()` calls `_default_extractor_factory()` each invocation ([line 25](backend/ai-scripts/routers/menu_extraction.py#L25)). For Gemini and Claude SDKs this is cheap (the clients are designed to be reused), but it means a stale env var change is picked up on the *next* request rather than at startup — a small surprise.

---

## F8 — LOW — `genai.Client(api_key="")` default makes a missing `GEMINI_API_KEY` invisible

[routers/directives.py:9](backend/ai-scripts/routers/directives.py#L9):

```python
client = genai.Client(api_key=os.getenv("GEMINI_API_KEY", ""))
```

`os.getenv(key, "")` returns the empty string if the env var is unset. `genai.Client(api_key="")` is then constructed without raising. The first call to `client.models.generate_content(...)` then returns an authentication error — which is immediately swallowed by [F1](#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-)'s `except Exception: return {"parsed_rules": {}}`.

Compound failure mode: deploy the service without `GEMINI_API_KEY` in the env file, and *every* call to `/directives/parse` returns a 200 with an empty result. The admin sees their directive saved with `parsedRules = null` ([Segment 10 F5](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f5--medium--ai-client-failure-mapping-is-inconsistent-across-call-sites)) and has no way to discover the root cause is a missing env var.

The right shape is fail-fast at module import: `key = os.environ["GEMINI_API_KEY"]` (raises `KeyError` at startup if missing), or a `validate_env_vars()` in the FastAPI lifespan that checks `GEMINI_API_KEY` and `AI_VISION_PROVIDER`-conditional `ANTHROPIC_API_KEY` and raises if either is missing.

The same pattern exists in the vision extractors (segment 15's window), so this is the directives-router instance of a broader theme.

---

## What's solid

| Item | Why it's good |
|---|---|
| No CORS middleware ([main.py](backend/ai-scripts/main.py)) | Correct for server-to-server. The roadmap's "CORS is restrictive or absent" passes outright. |
| `/health` is liveness-only ([main.py:14-16](backend/ai-scripts/main.py#L14-L16)) | Doesn't call the LLM, so health checks don't spend money. |
| `models/menu_extraction.py` constraints ([menu_extraction.py:6-18](backend/ai-scripts/models/menu_extraction.py#L6-L18)) | `Field(min_length=1, max_length=120)` on name, `gt=0 le=99_999_999.99` on price, `[0, 1]` on confidence, `Literal[...]` on `FailedPage.reason`. Pydantic validates these on every response — schema drift is caught before it leaves the service. |
| `python-multipart>=0.0.9` pin ([requirements.txt:9](backend/ai-scripts/requirements.txt#L9)) | Exactly satisfies the roadmap's CVE-aware item; the only pin that's correctly tight. |
| No `restaurant_id` in any request schema | All four request models confirmed; the service is correctly stateless w.r.t. tenants. |
| Dependency-injection pattern for the extractor ([menu_extraction.py:14-25](backend/ai-scripts/routers/menu_extraction.py#L14-L25)) | `Depends(get_extraction_service)` allows test override (`app.dependency_overrides[...]`) cleanly. The roadmap's segment-15 checklist relies on this exact shape — and it's correct here. |
| Pydantic v2 `Literal` types | `FailedPage.reason: Literal["unreadable", "no_items_detected", "vision_api_error"]` is the right shape for a closed enum that crosses an LLM boundary. |

---

## Cross-segment follow-ups raised here

- **[Segment 01](CODE_REVIEW_ROADMAP.md#segment-01--compose--service-topology):** [F3](#f3--high--no-trust-boundary-the-service-binds-00008000-is-published-to-the-host-and-has-no-auth-on-llm-spending-endpoints) — the same pattern as Mailpit's exposed SMTP port (Segment 11 [F7](CODE_REVIEW_SEGMENT_11_EMAIL.md#f7--low--mailpits-smtp-port-1025-is-published-to-the-host-not-only-the-ui-port-8025)) but with paid LLM endpoints. The compose-level fix is the right home.
- **[Segment 03](CODE_REVIEW_ROADMAP.md#segment-03--api-startup-pipeline-programcs):** if a shared-secret header replaces the trust-boundary gap, the C# `Program.cs` `AddHttpClient("AiService", ...)` registration ([Program.cs:88-107](backend/RestaurantApi/Program.cs#L88-L107)) needs to attach it to every request.
- **[Segment 10](CODE_REVIEW_ROADMAP.md#segment-10--background--domain-services):** [F1](#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-) is the supply side of Segment 10's [F5](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f5--medium--ai-client-failure-mapping-is-inconsistent-across-call-sites). The C# side persists a `parsedRules = null` directive because Python returns `200 OK {"parsed_rules": {}}` on failure — the consumer can't behave better than the producer.
- **[Segment 15](CODE_REVIEW_ROADMAP.md#segment-15--ai-service--vision-extraction):** the file-upload limit story ([F2](#f2--high--menuextract-has-no-file-size-count-or-mime-type-limits-in-the-python-service)) is a router-level concern but the magic-bytes / page-split / parallel-extraction concerns the roadmap names for segment 15 land in `services/menu_extraction_service.py` and `services/vision_extractor.py`.
- **[Segment 16](CODE_REVIEW_ROADMAP.md#segment-16--ai-service--recommendationsimilaritydirectives):** the missing `response_model` on `/similarity/auto-generate` and on both directive endpoints belongs partly here and partly in segment 16 (where the engine that produces the unvalidated dicts lives).
- **[Segment 17](CODE_REVIEW_ROADMAP.md#segment-17--ai-service-tests):** segment 17's "Recommendation/similarity/directive engines have unit tests" gap is exactly the kind of test that would have caught [F1](#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-) — a single test that mocks Gemini to raise and asserts the response status was not 200.
- **[Segment 24](CODE_REVIEW_ROADMAP.md#segment-24--shared-contracts-shared):** [F6](#f6--medium--api-contract-aijson-describes-an-api-that-does-not-exist) is segment 24's canonical home; surfacing it here so the contract-vs-implementation diff is on record.
- **[Segment 25](CODE_REVIEW_ROADMAP.md#segment-25--end-to-end-security-review):** the OWASP "Vulnerable and outdated components" item asks `pip-audit` to run; [F4](#f4--medium--requirementstxt-is-effectively-unpinned-supply-chain-and-reproducibility-risk) is the upstream cause of why `pip-audit`'s answer changes between runs.

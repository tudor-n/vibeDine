# Segment 15 — AI service: vision extraction — review findings

Scope review against the [Segment 15 checklist](CODE_REVIEW_ROADMAP.md#segment-15--ai-service--vision-extraction) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). Two files: [services/menu_extraction_service.py](backend/ai-scripts/services/menu_extraction_service.py) (the coordinator) and [services/vision_extractor.py](backend/ai-scripts/services/vision_extractor.py) (the provider implementations: `ClaudeExtractor`, `GeminiExtractor`, and `FakeVisionExtractor`). Cross-references: the response model in [models/menu_extraction.py](backend/ai-scripts/models/menu_extraction.py), the router in [routers/menu_extraction.py](backend/ai-scripts/routers/menu_extraction.py), and the C# upstream caller in [MenuScanController.cs](backend/RestaurantApi/Controllers/MenuScanController.cs).

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**8 findings, three of them HIGH.** The 🔥 item passes cleanly: both providers enforce structured output through the SDK's first-class mechanism. Claude uses tool-use with `tool_choice={"type": "tool", "name": "emit_menu_items"}` ([vision_extractor.py:111-112](backend/ai-scripts/services/vision_extractor.py#L111-L112)) which forces the model to call `emit_menu_items` with the typed `input_schema` at [line 47-69](backend/ai-scripts/services/vision_extractor.py#L47-L69). Gemini uses `response_mime_type="application/json"` with `response_schema=GEMINI_RESPONSE_SCHEMA` ([line 214-217](backend/ai-scripts/services/vision_extractor.py#L214-L217)). Neither path regex-parses free text. Both code paths additionally double-check `isinstance(payload, dict) and "items" in payload` at the boundary ([line 135-136, 229-230](backend/ai-scripts/services/vision_extractor.py#L135-L230)) so a schema-conforming-but-misshapen response still produces a `VisionExtractionError`. That's the strongest part of the segment.

The prompt is constant — `EXTRACTION_PROMPT` ([line 71-82](backend/ai-scripts/services/vision_extractor.py#L71-L82)) and `SYSTEM_PROMPT` ([line 84-87](backend/ai-scripts/services/vision_extractor.py#L84-L87)) take no user input. The filename never reaches the LLM; the C# side passes `photos` as the form-field name but the filename is dropped on the Python side. The roadmap's prompt-injection concern is structurally absent.

The findings cluster around three themes:

1. **PDFs are not really handled.** [F1](#f1--high--pdfs-are-not-page-split-and-the-claude-path-passes-applicationpdf-as-an-image-source-) — `_sniff_media_type` returns `"application/pdf"` ([line 142-143](backend/ai-scripts/services/vision_extractor.py#L142-L143)) and `ClaudeExtractor` then sends it as the `media_type` *inside* an `image` content block ([line 117-120](backend/ai-scripts/services/vision_extractor.py#L117-L120)) — Anthropic's images API rejects that; PDFs need the `document` content type. The Gemini path accepts PDFs as bytes but treats every PDF as one logical page: `source_page` is the upload-list index ([menu_extraction_service.py:27](backend/ai-scripts/services/menu_extraction_service.py#L27)), not the document page. A 20-page PDF produces 20 items all with `source_page = 0`. Roadmap's *"PDFs are page-split (or rejected past N pages) — confirm the limit"* — neither happens; the C# `MenuScanController` accepts `application/pdf` ([MenuScanController.cs:18](backend/RestaurantApi/Controllers/MenuScanController.cs#L18)) and forwards it here.
2. **Concurrency and timeouts are uneven.** [F2](#f2--high--unbounded-asynciogather-over-per-page-extractionsno-asynciosemaphore-) — `tasks = [...]; await asyncio.gather(*tasks)` ([menu_extraction_service.py:27-28](backend/ai-scripts/services/menu_extraction_service.py#L27-L28)) fans every uploaded image out at once with no `asyncio.Semaphore` cap; the C# side allows up to 10 files per request, so a single user request can issue 10 parallel LLM calls. [F3](#f3--high--geminiextractor-has-no-sdk-level-timeout-claudeextractor-does) — `ClaudeExtractor` sets `timeout=httpx.Timeout(60.0, connect=10.0)` ([line 125](backend/ai-scripts/services/vision_extractor.py#L125)); `GeminiExtractor` sets none, and the C# `MenuExtractionClient`'s 6-minute outer timeout ([Program.cs:127](backend/RestaurantApi/Program.cs#L127)) is the only ceiling on a stuck Gemini call.
3. **Observability and operational gaps.** [F5](#f5--medium--no-per-request-token-or-cost-logging-on-either-provider) — neither provider records `response.usage.input_tokens` / `usage.output_tokens` (Anthropic) or `usage_metadata` (Gemini), so the roadmap's *"per-request token estimate logged so spend can be attributed"* is unmet. [F6](#f6--medium--both-providers-can-be-configured-but-only-one-runs-no-fallback-no-comparison-no-cost-trade-off) — `AI_VISION_PROVIDER` picks exactly one; the unselected provider is dead capacity even when its key is present. The roadmap's `↳` *"If both providers are configured, what's the fallback strategy?"* — there isn't one.

The remaining items are smaller: [F4](#f4--medium--fakevisionextractor-lives-in-the-production-module) — `FakeVisionExtractor` is defined in [vision_extractor.py:21-40](backend/ai-scripts/services/vision_extractor.py#L21-L40), the same module the production factory imports from; moving it under `tests/` would make "wired only in tests" structural rather than habitual. [F7](#f7--low--_sniff_media_type-falls-back-to-imagejpeg-when-bytes-dont-match-any-known-magic-and-the-comment-is-now-wrong) — `_sniff_media_type` defaults to `"image/jpeg"` for unrecognised bytes with a comment that says *"caller already validated mime upstream"*, which Segment 14 [F2](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f2--high--menuextract-has-no-file-size-count-or-mime-type-limits-in-the-python-service) just established is not true; the Python router itself validates nothing. [F8](#f8--low--one-invalid-item-discards-the-entire-pages-extraction-rather-than-emitting-partial-success) — a single `ValidationError` inside the per-page loop ([menu_extraction_service.py:62-75](backend/ai-scripts/services/menu_extraction_service.py#L62-L75)) discards every valid item the LLM extracted from that page; the roadmap describes a "partial data" success model that the service half-implements.

What does check out, beyond the verdict opener: failure paths produce `FailedPage` rows with one of the three `Literal` reasons ([menu_extraction_service.py:53-59, 73-75](backend/ai-scripts/services/menu_extraction_service.py#L53-L75)), the overall response succeeds with partial data ([line 40-45](backend/ai-scripts/services/menu_extraction_service.py#L40-L45)), and `logger.warning("vision extractor failed for page %s", page_number, exc_info=True)` ([line 54](backend/ai-scripts/services/menu_extraction_service.py#L54)) emits a traceback to stderr without leaking raw image bytes. `FakeVisionExtractor.__init__` enforces "exactly one of canned_response or raises" ([line 30-31](backend/ai-scripts/services/vision_extractor.py#L30-L31)) — a small but earned-its-keep invariant. Both real extractors raise `VisionExtractionError` (a typed exception) rather than the SDK's raw exception ([line 127-130, 219-220](backend/ai-scripts/services/vision_extractor.py#L127-L220)), so the coordinator at [menu_extraction_service.py:53](backend/ai-scripts/services/menu_extraction_service.py#L53) can catch a precise type.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| 🔥 Both providers (Claude, Gemini) return structured output **enforced by schema** — never free-text parsed by regex | ✓ Pass | Claude: `tool_choice={"type": "tool", ...}` + `MENU_EXTRACTION_TOOL_SCHEMA` ([line 47-69, 111-112](backend/ai-scripts/services/vision_extractor.py#L47-L112)); reads `block.input` directly from the `tool_use` block. Gemini: `response_mime_type="application/json"` + `response_schema=GEMINI_RESPONSE_SCHEMA` ([line 165-217](backend/ai-scripts/services/vision_extractor.py#L165-L217)); `json.loads(response.text)` is a JSON-deserialise, not a regex parse. |
| `AI_VISION_PROVIDER` env switch is the only way to pick a provider; default is the more reliable one | ✓ Pass | [menu_extraction.py:14-20](backend/ai-scripts/routers/menu_extraction.py#L14-L20) — defaults to `"gemini"`, accepts `"claude"`, raises on anything else (see also Segment 14 [F7](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f7--low--unsupported-ai_vision_provider-raises-runtimeerror-which-becomes-a-default-500-with-a-traceback)). |
| Per-page extraction runs in parallel with bounded concurrency (`asyncio.Semaphore`) — uncapped `gather` exhausts API quotas | ❌ Fail — see [F2](#f2--high--unbounded-asynciogather-over-per-page-extractionsno-asynciosemaphore-) | `await asyncio.gather(*tasks)` ([menu_extraction_service.py:28](backend/ai-scripts/services/menu_extraction_service.py#L28)). No `Semaphore`. Fan-out equals `len(image_bytes_list)`. |
| Failures: a failed page produces a `FailedPage` entry with reason; the overall request succeeds with partial data | ✓ Pass (mostly — see [F8](#f8--low--one-invalid-item-discards-the-entire-pages-extraction-rather-than-emitting-partial-success)) | Three `FailedPage` reasons emitted: `vision_api_error` on `VisionExtractionError` ([line 55](backend/ai-scripts/services/menu_extraction_service.py#L55)), `no_items_detected` on empty `items` ([line 59](backend/ai-scripts/services/menu_extraction_service.py#L59)), `vision_api_error` again on validation failure ([line 75](backend/ai-scripts/services/menu_extraction_service.py#L75)). Overall response is built unconditionally ([line 40-45](backend/ai-scripts/services/menu_extraction_service.py#L40-L45)). |
| Media-type sniffing: file headers (magic bytes) checked, not just filename extension | ✓ Pass (with caveat — see [F7](#f7--low--_sniff_media_type-falls-back-to-imagejpeg-when-bytes-dont-match-any-known-magic-and-the-comment-is-now-wrong)) | `_sniff_media_type` checks `%PDF-`, `\xff\xd8\xff`, `\x89PNG\r\n`, `RIFF...WEBP` ([line 141-150](backend/ai-scripts/services/vision_extractor.py#L141-L150)). Filename is never read on the Python side. |
| PDFs are page-split (or rejected past N pages) — confirm the limit | ❌ Fail — see [F1](#f1--high--pdfs-are-not-page-split-and-the-claude-path-passes-applicationpdf-as-an-image-source-) | Neither. No PDF split, no rejection. C# accepts `application/pdf` ([MenuScanController.cs:18](backend/RestaurantApi/Controllers/MenuScanController.cs#L18)) and forwards as a single page. |
| No PII or sensitive data is logged in raw form (image content is fine to log path/hash, not bytes) | ✓ Pass | `logger.warning("vision extractor failed for page %s", page_number, exc_info=True)` ([line 54](backend/ai-scripts/services/menu_extraction_service.py#L54)) — only the page index leaks; `exc_info` captures the exception but the SDK exceptions don't contain raw bytes. |
| Prompt is constant (no user-controlled prompt injection via filename, etc.) | ✓ Pass | `EXTRACTION_PROMPT` and `SYSTEM_PROMPT` are module-level constants. Filename never reaches the extractor — `MenuExtractionService.extract` takes `List[bytes]`, not `List[UploadFile]`. |
| Costs: per-request token estimate logged so spend can be attributed | ❌ Fail — see [F5](#f5--medium--no-per-request-token-or-cost-logging-on-either-provider) | Anthropic response has `usage.input_tokens`/`output_tokens` — never accessed. Gemini response has `usage_metadata` — never accessed. No logging anywhere reports tokens. |
| `FakeVisionExtractor` is wired only via dependency override in tests, never in prod | ⚠ Partial — see [F4](#f4--medium--fakevisionextractor-lives-in-the-production-module) | The class is defined in the production module ([vision_extractor.py:21-40](backend/ai-scripts/services/vision_extractor.py#L21-L40)). Production wiring path (`_default_extractor_factory`) does not reach it — but the structural barrier the roadmap wants is missing. |
| If both providers are configured, what's the fallback strategy? (roadmap `↳`) | ❌ Fail — see [F6](#f6--medium--both-providers-can-be-configured-but-only-one-runs-no-fallback-no-comparison-no-cost-trade-off) | None. `_default_extractor_factory` picks exactly one. A failure in Gemini does not retry against Claude. |

---

## F1 — HIGH — PDFs are not page-split, and the Claude path passes `application/pdf` as an `image` source 🔥

Two related defects in the same flow.

**The page-split that the model implies is absent.** [menu_extraction.py:35](backend/ai-scripts/routers/menu_extraction.py#L35):

```python
image_bytes_list = [await p.read() for p in photos]
return await service.extract(image_bytes_list)
```

And [menu_extraction_service.py:27](backend/ai-scripts/services/menu_extraction_service.py#L27):

```python
tasks = [self._extract_one(idx, b) for idx, b in enumerate(image_bytes_list)]
```

`idx` is the position in the multipart upload list — the *file index*, not the document page. The coordinator then writes `source_page=page_number` on every `ExtractedMenuItem` ([line 68](backend/ai-scripts/services/menu_extraction_service.py#L68)) using that file index. The `MenuExtractionResponse.items[].source_page` semantics promised by [ExtractedMenuItem.source_page](backend/ai-scripts/models/menu_extraction.py#L9) (`Field(ge=0)` — *"0-based reading order within this page"* per the extraction prompt at [vision_extractor.py:80](backend/ai-scripts/services/vision_extractor.py#L80)) silently breaks for the multi-page PDF case: every item on every page of a single uploaded PDF carries `source_page = 0`.

The downstream admin SPA's import wizard ([frontend-admin/src/views/MenuImport.vue](frontend-admin/src/views/MenuImport.vue)) shows the source page alongside the item for human verification (Segment 22's window). For a multi-page PDF, that UX is degraded — every item appears to have come from page 0.

**The Claude path probably 400s on PDFs.** [vision_extractor.py:103-126](backend/ai-scripts/services/vision_extractor.py#L103-L126):

```python
async def extract(self, image_bytes: bytes, page_number: int) -> dict[str, Any]:
    media_type = _sniff_media_type(image_bytes)
    encoded = base64.standard_b64encode(image_bytes).decode("ascii")
    try:
        response = await self._client.messages.create(
            ...
            messages=[
                {
                    "role": "user",
                    "content": [
                        {
                            "type": "image",
                            "source": {"type": "base64", "media_type": media_type, "data": encoded},
                        },
                        {"type": "text", "text": EXTRACTION_PROMPT},
                    ],
                }
            ],
            ...
        )
```

`_sniff_media_type` returns `"application/pdf"` for PDF bytes. The content block then declares `"type": "image"` with `media_type: "application/pdf"`. Anthropic's messages API requires `"type": "document"` for PDF content (the `image` block accepts only `image/jpeg`, `image/png`, `image/gif`, `image/webp`). Result: Anthropic returns an `APIStatusError` (400), the extractor catches it ([line 127-128](backend/ai-scripts/services/vision_extractor.py#L127-L128)), raises `VisionExtractionError`, and the page records `vision_api_error`. The user sees "page 0 unreadable" with no further signal.

Gemini's `Part.from_bytes(data=image_bytes, mime_type="application/pdf")` does accept PDFs natively, so the Gemini path silently "works" — but only as a single page (per the previous half of this finding), and the upper bound on Gemini's PDF page count is unverified.

**The roadmap's ask.** *"PDFs are page-split (or rejected past N pages) — confirm the limit."* Neither alternative is implemented. The minimum table-stakes fix is:

1. Reject PDFs at the router with a 415 if there's no page-split capability today (add `application/pdf` to a "rejected" list in [routers/menu_extraction.py:30](backend/ai-scripts/routers/menu_extraction.py#L30), remove it from `AllowedContentTypes` in [MenuScanController.cs:18](backend/RestaurantApi/Controllers/MenuScanController.cs#L18)).
2. Or, split with `pypdf` / `pdf2image` at the router, feed each page as a separate `image_bytes` entry to the service, and update the prompt to clarify page semantics.

Out of scope: the right long-term shape uses Anthropic's `document` content type (Claude can ingest up to 100-page PDFs natively) and Gemini's `Part.from_bytes(..., mime_type="application/pdf")` with a multi-page response prompt — but neither is implemented today.

---

## F2 — HIGH — Unbounded `asyncio.gather` over per-page extractions; no `asyncio.Semaphore` 🔥

[menu_extraction_service.py:26-45](backend/ai-scripts/services/menu_extraction_service.py#L26-L45):

```python
async def extract(self, image_bytes_list: List[bytes]) -> MenuExtractionResponse:
    tasks = [self._extract_one(idx, b) for idx, b in enumerate(image_bytes_list)]
    per_page = await asyncio.gather(*tasks)
    ...
```

The fan-out equals `len(image_bytes_list)`. The C# `MenuScanController` allows up to 10 files per request ([MenuScanController.cs:14](backend/RestaurantApi/Controllers/MenuScanController.cs#L14)), so a single admin request fans out 10 concurrent vision-LLM calls. Combined with Segment 14 [F2](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f2--high--menuextract-has-no-file-size-count-or-mime-type-limits-in-the-python-service) (no Python-side limit either), and Segment 14 [F3](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f3--high--no-trust-boundary-the-service-binds-00008000-is-published-to-the-host-and-has-no-auth-on-llm-spending-endpoints) (no auth), a direct caller of the Python service is bounded only by the multipart parser.

Three practical consequences:

- **Provider rate-limit storms.** Anthropic's free tier is 5 requests per minute on some models; even paid tiers have RPS caps. A single admin uploading 10 menu photos issues 10 calls within ~50 ms of each other.
- **Quota exhaustion.** When several admins import menus in the same minute, the service competes with itself for the same API key.
- **Wasted spend on a failing batch.** If Anthropic returns a 5xx on the first call, the other 9 are already inflight and complete before the failure surfaces. With a `Semaphore(1)` (sequential) or `Semaphore(2)` (mild parallelism) the first failure could short-circuit the rest.

The roadmap names the exact fix: `asyncio.Semaphore`. Sketch (out of scope):

```python
async def extract(self, image_bytes_list: List[bytes]) -> MenuExtractionResponse:
    sem = asyncio.Semaphore(self._concurrency or 3)
    async def bounded(idx, b):
        async with sem:
            return await self._extract_one(idx, b)
    tasks = [bounded(idx, b) for idx, b in enumerate(image_bytes_list)]
    per_page = await asyncio.gather(*tasks)
```

`Semaphore(3)` is a reasonable default — it preserves parallelism for the common 1-2 photo case while capping disaster for the 10-photo case.

---

## F3 — HIGH — `GeminiExtractor` has no SDK-level timeout; `ClaudeExtractor` does

[vision_extractor.py:107-126](backend/ai-scripts/services/vision_extractor.py#L107-L126) — Claude:

```python
response = await self._client.messages.create(
    ...
    timeout=httpx.Timeout(60.0, connect=10.0),
)
```

[vision_extractor.py:205-218](backend/ai-scripts/services/vision_extractor.py#L205-L218) — Gemini:

```python
response = await self._client.aio.models.generate_content(
    model=self.model_name,
    contents=[...],
    config=genai_types.GenerateContentConfig(
        response_mime_type="application/json",
        response_schema=GEMINI_RESPONSE_SCHEMA,
    ),
)
```

No timeout configured for Gemini. The `google-genai` SDK's default timeout depends on the underlying HTTP transport — `httpx` defaults to *no timeout at all* (`None`) if not specified, meaning the call hangs indefinitely on a stuck server. The C# `MenuExtractionClient` provides a 6-minute outer ceiling ([Program.cs:127](backend/RestaurantApi/Program.cs#L127)), which is the worst-case backstop.

The asymmetry between the two providers is the actionable issue:

- A user request that hits Claude and the provider is slow → fails fast at 60 s with a clear `VisionExtractionError`.
- A user request that hits Gemini and the provider is slow → hangs for 6 minutes from the C# side, returning a 502 with no breakdown of where the time went. The Python process holds the connection (and the file bytes) open for that duration.

Combined with [F2](#f2--high--unbounded-asynciogather-over-per-page-extractionsno-asynciosemaphore-): 10 stuck Gemini requests = 10 connections × 6 minutes × ~3 MB image bytes = process RSS grows to ~30 MB held open uselessly.

Fix shape: every Gemini SDK call needs an `http_options=genai_types.HttpOptions(timeout=60_000)` (millis, per the SDK API) or wrap the await in `asyncio.wait_for(..., timeout=60.0)`. The Claude path already shows the pattern.

A related concern: the Anthropic timeout is hard-coded; on a slow provider day, 60 s may be too tight for a complex menu. A single `VISION_TIMEOUT_SECONDS` env var, defaulting to 60, would be the right shape for both providers.

---

## F4 — MEDIUM — `FakeVisionExtractor` lives in the production module

[vision_extractor.py:21-40](backend/ai-scripts/services/vision_extractor.py#L21-L40):

```python
class FakeVisionExtractor:
    """In-memory stand-in for tests. Either returns canned_response or raises `raises`."""

    def __init__(
        self,
        canned_response: Optional[dict[str, Any]] = None,
        raises: Optional[Exception] = None,
        model_name: str = "fake-test-model",
    ):
        if (canned_response is None) == (raises is None):
            raise ValueError("Provide exactly one of canned_response or raises.")
        ...
```

The class is defined in the same module that `routers/menu_extraction.py` imports from ([menu_extraction.py:8](backend/ai-scripts/routers/menu_extraction.py#L8) — `from services.vision_extractor import ClaudeExtractor, GeminiExtractor, VisionExtractor`). The router doesn't import `FakeVisionExtractor`, but it's importable from anywhere via `from services.vision_extractor import FakeVisionExtractor`.

The roadmap says: *"FakeVisionExtractor is wired only via dependency override in tests, never in prod."* The wiring is correct (the production factory at [menu_extraction.py:14-20](backend/ai-scripts/routers/menu_extraction.py#L14-L20) never returns one), but the structural barrier is missing. Concretely:

- A future change like *"add a debug toggle to fake the extractor"* lands a single line `if os.environ.get("FAKE_VISION"): return FakeVisionExtractor(...)` and the production-grade barrier is gone.
- The Python OpenAPI export at `/openapi.json` doesn't expose the class, but `mypy` / IDE autocomplete suggests it as a callable next to the real extractors — the discoverability is wrong.

The standard fix is to move the class under `tests/` (e.g., `tests/fakes.py`) and import it from there in every test (the tests already pass it via `extractor_factory=lambda: FakeVisionExtractor(...)`, so the import boundary is the only thing that changes). A weaker but still useful alternative is to prefix the class with `_FakeVisionExtractor` (Python's convention for private) — but the right answer is module move.

`FakeVisionExtractor.__init__`'s `if (canned_response is None) == (raises is None)` invariant ([line 30-31](backend/ai-scripts/services/vision_extractor.py#L30-L31)) is well-shaped; that part survives any reorganisation.

---

## F5 — MEDIUM — No per-request token or cost logging on either provider

The roadmap calls for *"Costs: per-request token estimate logged so spend can be attributed."* Anthropic's response has `response.usage.input_tokens` and `response.usage.output_tokens`; Gemini's response has `response.usage_metadata.prompt_token_count` and `response.usage_metadata.candidates_token_count`. Neither is read.

The `ClaudeExtractor.extract` exit at [line 132-137](backend/ai-scripts/services/vision_extractor.py#L132-L137):

```python
for block in response.content:
    if block.type == "tool_use" and block.name == "emit_menu_items":
        payload = block.input
        ...
        return payload  # type: ignore[return-value]
raise VisionExtractionError("Model did not return a tool_use block")
```

Reads `response.content` only. `response.usage` is discarded.

The `GeminiExtractor.extract` exit at [line 222-231](backend/ai-scripts/services/vision_extractor.py#L222-L231) is the same shape.

Consequences:

- **Cost attribution is impossible.** When the LLM bill arrives at the end of the month, there's no way to slice by restaurant, by admin, by upload — the only signal is the bill itself.
- **Pricing decisions are flying blind.** The product team can't answer "how many tokens does a typical menu photo cost us to extract?" without external instrumentation.
- **Failure correlation is harder.** A `vision_api_error` on a page that triggered a 4k-token response is plausibly a context-window issue; one on a 200-token response is almost certainly a different bug. Without the count, the two failure modes look identical.

The fix is two lines per extractor — `logger.info("claude tokens in=%d out=%d page=%d", response.usage.input_tokens, response.usage.output_tokens, page_number)` and the equivalent for Gemini. A structured-log middleware (Segment 14 [F5](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f5--medium--mainpy-has-no-global-exception-handler-no-error-envelope-no-request_id-propagation)) would make this even better, but a bare log line is the table stake.

---

## F6 — MEDIUM — Both providers can be configured but only one runs; no fallback, no comparison, no cost trade-off

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

The selection is mutually exclusive. Once `AI_VISION_PROVIDER` is set, the other provider's key is dead weight (if configured) or absent (if not). The `.env.example` advertises both `GEMINI_API_KEY` and `ANTHROPIC_API_KEY` slots ([.env.example:5, 12](backend/ai-scripts/.env.example#L5-L12)) — a reader assumes both are used in production, but they aren't.

The roadmap's `↳` is direct: *"If both providers are configured, what's the fallback strategy? (None? Explicit? Document it.)"*. The answer is "none, not documented." Three options worth flagging:

- **Documented "no fallback".** Adding a one-line comment in `_default_extractor_factory` saying "intentional single-provider; if the chosen provider is down, fail" closes the roadmap item with negligible code change.
- **Sequential fallback.** Wrap the factory result in a chain: try the primary, catch `VisionExtractionError`, retry with the secondary. Adds latency on every failure but recovers from provider outages.
- **A/B comparison.** Useful for early product life — the AI team needs to know "Claude got 23/24 items, Gemini got 24/24" before settling on one provider. Out of scope but the right place to land that comparison.

Today the system makes the implicit decision and offers no path to revisit it without redeploying.

---

## F7 — LOW — `_sniff_media_type` falls back to `image/jpeg` when bytes don't match any known magic, and the comment is now wrong

[vision_extractor.py:141-150](backend/ai-scripts/services/vision_extractor.py#L141-L150):

```python
def _sniff_media_type(image_bytes: bytes) -> str:
    if image_bytes.startswith(b"%PDF-"):
        return "application/pdf"
    if image_bytes.startswith(b"\xff\xd8\xff"):
        return "image/jpeg"
    if image_bytes.startswith(b"\x89PNG\r\n"):
        return "image/png"
    if image_bytes[:4] == b"RIFF" and image_bytes[8:12] == b"WEBP":
        return "image/webp"
    return "image/jpeg"  # default — caller already validated mime upstream
```

Two related concerns:

- **The fallback is `image/jpeg`.** A non-image, non-PDF blob (a Windows `.exe`, a UTF-16 text file, a gzip archive) is labelled as JPEG and sent to the LLM. The LLM will reject it and the extractor records `vision_api_error` — non-catastrophic, but the labeling is wrong.
- **The comment lies.** *"caller already validated mime upstream"* — per Segment 14 [F2](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f2--high--menuextract-has-no-file-size-count-or-mime-type-limits-in-the-python-service), the Python router validates nothing. Only the C# `MenuScanController` does, and only for requests that come through C#. A direct caller of the Python service can submit any byte stream.

The right fallback is `raise VisionExtractionError("Unrecognised content type")` (or a typed `media_type=None` and a router-level 415). The current behaviour silently masks an upstream validation gap.

`image/heic` is also missing from the magic-byte list. The C# side accepts `image/heic` ([MenuScanController.cs:18](backend/RestaurantApi/Controllers/MenuScanController.cs#L18)) — HEIC's magic starts at byte 4 (`ftyphe..` / `ftypmif1` / `ftypmsf1`). A HEIC upload from an iPhone admin reaches the Python service and gets labelled `image/jpeg`, where Anthropic rejects the mismatched content type.

---

## F8 — LOW — One invalid item discards the entire page's extraction rather than emitting partial success

[menu_extraction_service.py:61-75](backend/ai-scripts/services/menu_extraction_service.py#L61-L75):

```python
parsed: list[ExtractedMenuItem] = []
try:
    for raw_item in raw_items:
        parsed.append(
            ExtractedMenuItem(
                name=raw_item["name"],
                price=float(raw_item["price"]),
                source_page=page_number,
                source_index=int(raw_item["source_index"]),
                category_hint=raw_item.get("category_hint"),
            )
        )
except (KeyError, TypeError, ValueError, ValidationError):
    logger.warning("invalid item payload for page %s", page_number, exc_info=True)
    return [], FailedPage(source_page=page_number, reason="vision_api_error"), extractor.model_name

return parsed, None, extractor.model_name
```

The `try` wraps the entire loop. The first item that fails Pydantic validation (e.g. a `price=0` against the `gt=0` constraint, or a missing `source_index`) aborts the loop and discards every item that was successfully constructed before it.

The realistic failure pattern: an LLM extracts 12 items from a page, 11 are valid and one has `price: 0` (the LLM saw "GRATUIT" / "FREE" and emitted zero). The current behaviour throws away all 12. The roadmap's "Failures: a failed page produces a `FailedPage` entry with reason; the overall request succeeds with partial data" is half-right — partial across pages works, but within a page it's all-or-nothing.

A per-item try-except — collect the valid ones, emit a `FailedPage` only if zero items survived — is the typical shape. Or accept the bad items and let the Pydantic model coerce (with `model_validate` and a custom validator that logs and skips). Either keeps useful data flowing.

This is the lowest-priority finding in the segment — the failure mode is "menu import requires re-running the page" rather than "wrong data persisted" — but it costs the admin time and a re-upload that didn't need to happen.

---

## What's solid

| Item | Why it's good |
|---|---|
| Schema-enforced output on both providers ([line 47-69, 165-183, 111-112, 214-217](backend/ai-scripts/services/vision_extractor.py#L47-L217)) | Tool-use (Claude) and `response_schema` (Gemini) — both first-class SDK features for structured output, not best-effort prompting. The 🔥 roadmap item passes cleanly. |
| Prompt is module-level constant ([line 71-87, 185-189](backend/ai-scripts/services/vision_extractor.py#L71-L189)) | No filename, no user-controlled string interpolated. Prompt-injection surface is structurally absent. |
| Magic-byte MIME sniff for PDF/JPEG/PNG/WEBP ([line 141-149](backend/ai-scripts/services/vision_extractor.py#L141-L149)) | Filename extension is never trusted; the four supported types have proper magic-byte checks. |
| Typed exception (`VisionExtractionError`) ([line 9-10](backend/ai-scripts/services/vision_extractor.py#L9-L10)) wrapping all SDK failures | The coordinator catches a precise type at [menu_extraction_service.py:53](backend/ai-scripts/services/menu_extraction_service.py#L53). No bare `except Exception` in the success path. |
| Partial-success response shape ([menu_extraction_service.py:40-45](backend/ai-scripts/services/menu_extraction_service.py#L40-L45)) | The response is built unconditionally; one bad page doesn't kill the request. The downstream `MenuExtractionResponse.failed_pages` carries the per-page reasons. |
| Logging is sanitary | `logger.warning("vision extractor failed for page %s", page_number, exc_info=True)` — page index leaks but no raw image bytes. |
| `FakeVisionExtractor.__init__` invariant | `if (canned_response is None) == (raises is None): raise ValueError(...)` — "exactly one" enforced at construction. Small but a worthwhile gateway against test confusion. |
| Anthropic provider raises `VisionExtractionError` with `APIStatusError.status_code` ([line 127-128](backend/ai-scripts/services/vision_extractor.py#L127-L128)) | The status code is preserved on the exception chain; future logging or retry policy can read it back out. |

---

## Cross-segment follow-ups raised here

- **[Segment 08](CODE_REVIEW_ROADMAP.md#segment-08--controllers--admin-surface):** [F1](#f1--high--pdfs-are-not-page-split-and-the-claude-path-passes-applicationpdf-as-an-image-source-) — the right fix touches the C# `MenuScanController`'s allowed-MIME list ([MenuScanController.cs:18](backend/RestaurantApi/Controllers/MenuScanController.cs#L18)) too. Either drop `application/pdf` from the allowlist or implement page-split.
- **[Segment 10](CODE_REVIEW_ROADMAP.md#segment-10--background--domain-services):** the timeout asymmetry in [F3](#f3--high--geminiextractor-has-no-sdk-level-timeout-claudeextractor-does) intersects [CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md F4](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f4--medium--aiservice-httpclient-has-no-retry-policy-and-no-circuit-breaker) (no retry/circuit-breaker on the AI-bound HttpClient). Even when the Python side hardens, the C# side should still circuit-break.
- **[Segment 14](CODE_REVIEW_ROADMAP.md#segment-14--ai-service--bootstrap--routers):** [F2](#f2--high--unbounded-asynciogather-over-per-page-extractionsno-asynciosemaphore-) compounds Segment 14's [F2](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f2--high--menuextract-has-no-file-size-count-or-mime-type-limits-in-the-python-service) (no file-count limit). Either fix alone caps blast radius; both together close it.
- **[Segment 17](CODE_REVIEW_ROADMAP.md#segment-17--ai-service-tests):** [F4](#f4--medium--fakevisionextractor-lives-in-the-production-module) — moving `FakeVisionExtractor` under `tests/` aligns with Segment 17's "FakeVisionExtractor wired via dependency override — no real API calls in CI" item. The tests at [test_menu_extraction_service.py:4](backend/ai-scripts/tests/test_menu_extraction_service.py#L4) and [test_menu_extraction_router.py:9](backend/ai-scripts/tests/test_menu_extraction_router.py#L9) are the only importers and would migrate trivially.
- **[Segment 22](CODE_REVIEW_ROADMAP.md#segment-22--admin-spa--menu-management--import):** [F1](#f1--high--pdfs-are-not-page-split-and-the-claude-path-passes-applicationpdf-as-an-image-source-) — the admin SPA wizard relies on `source_page` to render the per-page view; the PDF-collapse means that view is wrong for any multi-page PDF. The fix lands here (Segment 15), the symptom is visible in Segment 22.
- **[Segment 24](CODE_REVIEW_ROADMAP.md#segment-24--shared-contracts-shared):** the [MenuExtractionResponse](backend/ai-scripts/models/menu_extraction.py) and the C#-side `MenuScanResponse` shape need to agree on what `source_page` means once page-split lands. Today the Python side promises one thing and the C# side just forwards it, but Segment 22 reads it.

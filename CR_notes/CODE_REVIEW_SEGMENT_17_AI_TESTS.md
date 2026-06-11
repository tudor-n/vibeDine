# Segment 17 — AI service tests — review findings

Scope review against the [Segment 17 checklist](CODE_REVIEW_ROADMAP.md#segment-17--ai-service-tests) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). Five `*.py` files in [backend/ai-scripts/tests/](backend/ai-scripts/tests/): [conftest.py](backend/ai-scripts/tests/conftest.py), [test_placeholder.py](backend/ai-scripts/tests/test_placeholder.py), [test_menu_extraction_parsing.py](backend/ai-scripts/tests/test_menu_extraction_parsing.py), [test_menu_extraction_service.py](backend/ai-scripts/tests/test_menu_extraction_service.py), [test_menu_extraction_router.py](backend/ai-scripts/tests/test_menu_extraction_router.py); plus the test config in [pytest.ini](backend/ai-scripts/pytest.ini) and the CI invocation in [.github/workflows/backend.yml:28-43](.github/workflows/backend.yml#L28-L43).

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**7 findings, two HIGH.** This segment is the inverse of Segment 13 in shape: the tests that *do* exist actually run (the Python CI job correctly invokes `pytest --tb=short` from `backend/ai-scripts/`, [backend.yml:33, 43](.github/workflows/backend.yml#L33-L43)) and the menu-extraction surface is genuinely well covered. The problem is the coverage map: 100% of the existing tests live on the segment-15 surface, while three of the four other routers (and two of the three engines) have zero tests.

Concretely, by router / engine:

| Surface | Tests | Notes |
|---|---|---|
| `/menu/extract` ([routers/menu_extraction.py](backend/ai-scripts/routers/menu_extraction.py)) | 4 router tests + 5 service-layer tests + 7 Pydantic-model tests | Strong. The `FakeVisionExtractor` + `app.dependency_overrides` pattern is exactly the roadmap shape. |
| `/recommend` ([routers/recommendations.py](backend/ai-scripts/routers/recommendations.py)) | 0 | — |
| `/similarity/auto-generate` ([routers/similarity.py](backend/ai-scripts/routers/similarity.py)) | 0 | — |
| `/directives/parse` ([routers/directives.py:14](backend/ai-scripts/routers/directives.py#L14)) | 0 | — |
| `/recommendation-directives/parse` ([routers/directives.py:39](backend/ai-scripts/routers/directives.py#L39)) | 0 | — |
| `/health` ([main.py:14](backend/ai-scripts/main.py#L14)) | 0 | Trivial — but the `test_placeholder.py` shape is the natural home. |
| `recommendation_engine.get_recommendations` ([services/recommendation_engine.py](backend/ai-scripts/services/recommendation_engine.py)) | 0 | — |
| `similarity_engine.generate_similarity_rules` ([services/similarity_engine.py](backend/ai-scripts/services/similarity_engine.py)) | 0 | — |
| `directive_parser.parse_directive` ([services/directive_parser.py](backend/ai-scripts/services/directive_parser.py)) | 0 | Untested *and* dead code (Segment 16 [F1](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md#f1--high--directive_parserpy-is-unreachable-dead-code-the-tworeg-paths-the-roadmap-describes-doesnt-exist-)). |

The findings cluster around two themes:

1. **The coverage map is concentrated on one segment.** [F1](#f1--high--four-of-five-real-routers-have-zero-tests-only-menuextract-is-covered-) — `/recommend`, `/similarity/auto-generate`, `/directives/parse`, `/recommendation-directives/parse` have zero router-level tests. The roadmap's "Tests for each router exist (only `test_menu_extraction_router.py` currently — file follow-ups for the others)" item is unmet exactly as the roadmap predicted. [F2](#f2--high--two-of-three-live-engines-recommendation-similarity-have-zero-unit-tests) — `recommendation_engine` and `similarity_engine` have no unit tests; the regex `directive_parser` is also untested but Segment 16 [F1](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md#f1--high--directive_parserpy-is-unreachable-dead-code-the-tworeg-paths-the-roadmap-describes-doesnt-exist-) makes that moot.
2. **The kinds of tests that catch the cross-cutting bugs are missing.** [F3](#f3--medium--no-roen-directive-parsing-test-the-roadmaps-explicit-ask-from-segment-16) — Romanian test strings (`"Ciorbă de burtă"`, `"Salată Caesar"`, `"APERITIVE"`) appear in the vision tests but the directive parser has no RO/EN test even though it's the segment where bilingualism actually matters. [F4](#f4--medium--no-malformed-llm-response-test-on-the-engine-side) — the roadmap names *"Schema validation tests: a malformed LLM response is rejected, not propagated"*. The vision side has the moral equivalent via `test_invalid_provider_payload_treated_as_vision_api_error` ([test_menu_extraction_service.py:70-78](backend/ai-scripts/tests/test_menu_extraction_service.py#L70-L78)), but the engines have nothing equivalent — and Segment 16 [F2](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md#f2--high--similarity_engine-has-no-response_model-no-id-intersection-no-pydantic-validation) is exactly the kind of bug a malformed-response test would catch. [F5](#f5--medium--_sniff_media_type-has-no-unit-test-segment-15-f7--missing-heic-would-have-surfaced-) — the magic-byte sniff function ([vision_extractor.py:141-150](backend/ai-scripts/services/vision_extractor.py#L141-L150)) has no dedicated test; the missing HEIC handler (Segment 15 [F7](CODE_REVIEW_SEGMENT_15_VISION.md#f7--low--_sniff_media_type-falls-back-to-imagejpeg-when-bytes-dont-match-any-known-magic-and-the-comment-is-now-wrong)) would have surfaced as a one-line failing assertion.

The remaining items: [F6](#f6--low--conftestpys-fixture-is-unused-and-test_placeholderpy-is-effectively-dead-weight) — the only consumer of `sample_cart_items` is `test_placeholder.py` itself, which is itself a `assert True` smoke test; the fixture exists for engine tests that were never written. [F7](#f7--low--test_extract_one_page_fails_others_succeed-depends-on-asynciogather-task-scheduling-order) — `test_extract_one_page_fails_others_succeed` uses a shared `state["calls"]` counter that survives only because `asyncio.gather` happens to schedule tasks in list order; an `await` insert in `_extract_one` before the factory call would break the test silently.

What does check out — and meaningfully — is the segment-15 surface. The `FakeVisionExtractor` is wired exclusively through `app.dependency_overrides[router_module.get_extraction_service]` ([test_menu_extraction_router.py:21, 30](backend/ai-scripts/tests/test_menu_extraction_router.py#L21-L30)), with a `yield`-based fixture that clears the override after each test ([line 22-23, 31-32](backend/ai-scripts/tests/test_menu_extraction_router.py#L22-L32)) — no test leakage. The five service-layer tests cover all four expected shapes — single-page success, multi-page success preserving page order, mixed success/failure, all-empty, and validation-error-as-failed-page — that the roadmap names. The Pydantic model tests assert both happy and unhappy paths against the schema. None of the tests touches a real API key (every test runs against `FakeVisionExtractor`), so the CI workflow correctly omits `GEMINI_API_KEY` from the job env ([backend.yml:28-43](.github/workflows/backend.yml#L28-L43)). `asyncio_mode = auto` ([pytest.ini:2](backend/ai-scripts/pytest.ini#L2)) means the async tests don't need `@pytest.mark.asyncio` boilerplate — they have it anyway, which is harmless. The CI invocation is correct, in pleasant contrast to [Segment 13 F1](CODE_REVIEW_SEGMENT_13_TESTS.md#f1--critical--ci-runs-dotnet-test-from-the-web-projects-directory-and-discovers-no-tests-the-suite-is-silently-green-): the working-directory is `backend/ai-scripts`, `requirements.txt` is correctly cached, `pytest` runs from a directory that contains `tests/`. The Python tests *actually run*.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| `FakeVisionExtractor` wired via dependency override — no real API calls in CI | ✓ Pass | Every test wires through `app.dependency_overrides[router_module.get_extraction_service] = ...` ([test_menu_extraction_router.py:21, 30](backend/ai-scripts/tests/test_menu_extraction_router.py#L21-L30)). Service-layer tests pass `FakeVisionExtractor` via `extractor_factory=` directly ([test_menu_extraction_service.py:17, 35, 48-49, 61, 72-73](backend/ai-scripts/tests/test_menu_extraction_service.py)). No real SDK call anywhere. |
| Tests cover: single-page success, multi-page success, mixed success/failure, all-failure (full request fails appropriately) | ⚠ Partial | Single-page success ✓ ([test_menu_extraction_service.py:14-28](backend/ai-scripts/tests/test_menu_extraction_service.py#L14-L28)). Multi-page success ✓ ([line 31-38](backend/ai-scripts/tests/test_menu_extraction_service.py#L31-L38)). Mixed success/failure ✓ ([line 41-55](backend/ai-scripts/tests/test_menu_extraction_service.py#L41-L55)). All-empty (not the same as all-failure) ✓ ([line 58-66](backend/ai-scripts/tests/test_menu_extraction_service.py#L58-L66)). **All-failure** — no test where every page fails. The router-level "no photos uploaded" test ([test_menu_extraction_router.py:81-84](backend/ai-scripts/tests/test_menu_extraction_router.py#L81-L84)) is a 400-validation test, not an all-fail integration test. |
| Tests for each router exist (only `test_menu_extraction_router.py` currently — file follow-ups for the others) | ❌ Fail — see [F1](#f1--high--four-of-five-real-routers-have-zero-tests-only-menuextract-is-covered-) | Five routers total ([routers/menu_extraction.py](backend/ai-scripts/routers/menu_extraction.py), [routers/recommendations.py](backend/ai-scripts/routers/recommendations.py), [routers/similarity.py](backend/ai-scripts/routers/similarity.py), [routers/directives.py](backend/ai-scripts/routers/directives.py) × 2). One has tests. The roadmap predicted this. |
| Recommendation/similarity/directive engines have unit tests with deterministic LLM mocks | ❌ Fail — see [F2](#f2--high--two-of-three-live-engines-recommendation-similarity-have-zero-unit-tests) | Zero unit tests for `services/recommendation_engine.py`, `services/similarity_engine.py`, `services/directive_parser.py`. Grep `recommendation_engine\|similarity_engine\|directive_parser` across [tests/](backend/ai-scripts/tests/) returns nothing. The `FakeVisionExtractor` pattern for the vision side has no analogue (no `FakeGeminiClient` or similar). |
| Schema validation tests: a malformed LLM response is rejected, not propagated | ⚠ Partial — see [F4](#f4--medium--no-malformed-llm-response-test-on-the-engine-side) | Vision side ✓ ([test_menu_extraction_service.py:70-78](backend/ai-scripts/tests/test_menu_extraction_service.py#L70-L78), [test_menu_extraction_parsing.py:35-37, 40-42](backend/ai-scripts/tests/test_menu_extraction_parsing.py#L35-L42)). Engines and directives: nothing — Segment 16 [F2](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md#f2--high--similarity_engine-has-no-response_model-no-id-intersection-no-pydantic-validation) would have failed visibly. |
| No test depends on a real `GEMINI_API_KEY` | ✓ Pass | CI workflow ([backend.yml:28-43](.github/workflows/backend.yml#L28-L43)) sets no API-key env vars. All tests use `FakeVisionExtractor`. Module-level `client = genai.Client(api_key=os.getenv("GEMINI_API_KEY", ""))` constructions in [routers/directives.py:9](backend/ai-scripts/routers/directives.py#L9), [services/recommendation_engine.py:7](backend/ai-scripts/services/recommendation_engine.py#L7), [services/similarity_engine.py:7](backend/ai-scripts/services/similarity_engine.py#L7) are dormant — the SDKs don't validate the key at construction. |
| Tests run via `pytest -q` in CI without network access | ✓ Pass | [backend.yml:43](.github/workflows/backend.yml#L43) — `pytest --tb=short`. Working-directory is `backend/ai-scripts` ([line 33](.github/workflows/backend.yml#L33)). The job has no `services:` block, no internet calls in tests (verified — every test uses local fakes). |
| Romanian-specific test strings exist in tests | ⚠ Partial — see [F3](#f3--medium--no-roen-directive-parsing-test-the-roadmaps-explicit-ask-from-segment-16) | Vision tests ✓ — `"Ciorbă de burtă"`, `"Salată Caesar"`, `"Bucățele de pui pe pat de salată romana"`, `"APERITIVE"` ([test_menu_extraction_parsing.py:12, 22, 26-27](backend/ai-scripts/tests/test_menu_extraction_parsing.py), [test_menu_extraction_service.py:18, 28](backend/ai-scripts/tests/test_menu_extraction_service.py)). Directive parsing — nothing. The segment where it matters most is the one without coverage. |

---

## F1 — HIGH — Four of five real routers have zero tests; only `/menu/extract` is covered 🔥

The roadmap names this exact gap: *"Tests for each router exist (only `test_menu_extraction_router.py` currently — file follow-ups for the others)"*. The follow-up was never filed; the gap remains.

Grep `import routers\|from routers` across [tests/](backend/ai-scripts/tests/):

```
tests/test_menu_extraction_router.py:7: from routers import menu_extraction as router_module
```

One match. Four routers are absent:

- **`/recommend`** — Segment 16 [F4](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md#f4--medium--recommendation-engine-does-not-exclude-items-already-in-cart-from-candidate-set) (cart-dedupe missing) and [F5](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md#f5--medium--confidence--score-floats-are-not-constrained-to-01) (confidence not in [0,1]) are both single-test-away from being demonstrated. A test that mocks Gemini to return `confidence=5.0` would surface F5; one with the same product_id in both `cart_items` and `menu_items` would surface F4.
- **`/similarity/auto-generate`** — Segment 16 [F2](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md#f2--high--similarity_engine-has-no-response_model-no-id-intersection-no-pydantic-validation) (no `response_model`, no ID intersection) is provable with one test: mock the Gemini client to return a product_id absent from `order_history` and assert it's rejected. Today nothing catches it.
- **`/directives/parse`** — Segment 14 [F1](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-) (silent 200 OK on every failure) is provable with one test: monkey-patch the Gemini client to raise and assert the response is not 200. Today the silent-failure mode is invisible.
- **`/recommendation-directives/parse`** — same shape as `/directives/parse`. The prompt's RO/EN handling claim ([directives.py:56-58](backend/ai-scripts/routers/directives.py#L56-L58) — *"handle Romanian diacritics, plurals, abbreviations, and synonyms"*) is entirely unverified; cross-references [F3](#f3--medium--no-roen-directive-parsing-test-the-roadmaps-explicit-ask-from-segment-16).

Why this is HIGH, not MEDIUM: every Segment 16 finding (eight of them) is reachable by a single router-level test. The absence of those tests is the upstream cause of the cluster. The same is true for Segment 14 [F1](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-).

Concretely, four new files (estimated 30-50 lines each) close the gap:

- `tests/test_recommendations_router.py` — happy path + cart-dedupe + invalid ID rejection + empty `menu_items` short-circuit.
- `tests/test_similarity_router.py` — happy path + invalid ID rejection + score-out-of-range.
- `tests/test_directives_router.py` — happy path + Gemini raises → response is 502 (or whatever the intended error envelope is) + malformed-JSON response.
- `tests/test_recommendation_directives_router.py` — RO prompt + EN prompt + mixed-language menu items + unresolved-name flow.

The override pattern is already established in [test_menu_extraction_router.py](backend/ai-scripts/tests/test_menu_extraction_router.py) — each new file can follow the same shape. The blocker is that the four routers each use a *module-level* Gemini client (`client = genai.Client(...)` at module top in [routers/directives.py:9](backend/ai-scripts/routers/directives.py#L9), [services/recommendation_engine.py:7](backend/ai-scripts/services/recommendation_engine.py#L7), [services/similarity_engine.py:7](backend/ai-scripts/services/similarity_engine.py#L7)) rather than the DI-friendly factory the vision router uses — so the test author would need to introduce a `get_gemini_client()` dependency first. That refactor is the prerequisite to making this segment's tests writable in the same style as Segment 15's.

---

## F2 — HIGH — Two of three live engines (recommendation, similarity) have zero unit tests

[services/recommendation_engine.py](backend/ai-scripts/services/recommendation_engine.py) and [services/similarity_engine.py](backend/ai-scripts/services/similarity_engine.py) have no unit tests. The third "engine," [services/directive_parser.py](backend/ai-scripts/services/directive_parser.py), is dead code per Segment 16 [F1](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md#f1--high--directive_parserpy-is-unreachable-dead-code-the-tworeg-paths-the-roadmap-describes-doesnt-exist-) — its 110 lines of well-shaped regex parsing are uncovered too, but only because nothing exercises the module at all.

This is distinct from [F1](#f1--high--four-of-five-real-routers-have-zero-tests-only-menuextract-is-covered-) — router tests exercise the full HTTP path; engine tests exercise the function in isolation. Both shapes matter:

- **Engine tests catch composition bugs.** The recommendation engine's `valid_ids = {item.product_id for item in request.menu_items}` ([recommendation_engine.py:16](backend/ai-scripts/services/recommendation_engine.py#L16)) intersection logic is correct today, but a refactor that changes `validated = [r for r in recs if r.get("product_id") in valid_ids]` to use a different comparison (case-sensitive vs insensitive UUIDs, for instance) has no test to flag it.
- **Engine tests catch boundary bugs without HTTP overhead.** The "empty menu_items short-circuits to `{"recommendations": []}`" branch at [line 13-14](backend/ai-scripts/services/recommendation_engine.py#L13-L14) is a single-call assertion; routing it through FastAPI's TestClient is overkill.

The path to engine tests requires the same prerequisite as [F1](#f1--high--four-of-five-real-routers-have-zero-tests-only-menuextract-is-covered-): a `get_gemini_client()` dependency or a per-engine `client` parameter that tests can substitute. Today both engines hold their Gemini client as a module-level global, which makes substitution awkward (every test has to `monkeypatch.setattr(recommendation_engine, "client", ...)` rather than receive a dependency cleanly). The vision extractor solves this by taking `client: AsyncAnthropic | None = None` / `client: genai.Client | None = None` in the constructor ([vision_extractor.py:95-101, 195-203](backend/ai-scripts/services/vision_extractor.py#L95-L203)) — the same shape would suit the engines.

Once the substitution path exists, the test set the roadmap implies is small: one per known engine behaviour (happy path, validation filter, broad-exception fallback, empty menu short-circuit). Four tests per engine, two engines — eight tests close the gap.

---

## F3 — MEDIUM — No RO/EN directive parsing test (the roadmap's explicit ask from Segment 16)

The roadmap says: *"Directive parser handles RO **and** EN (per the explore report) — verify with a few real prompts in each language."* It says it specifically because Romanian is the product's primary language and the parser's robustness on it is load-bearing.

The vision side has Romanian strings woven through its tests ("Ciorbă de burtă" at [test_menu_extraction_parsing.py:12](backend/ai-scripts/tests/test_menu_extraction_parsing.py#L12), "Salată Caesar" at [line 22](backend/ai-scripts/tests/test_menu_extraction_parsing.py#L22), "APERITIVE" at [line 27](backend/ai-scripts/tests/test_menu_extraction_parsing.py#L27) — these confirm UTF-8 round-trips through Pydantic). The directive side has *nothing*: no test exercises the RO-handling branch of either the regex parser (dead code, Segment 16 [F1](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md#f1--high--directive_parserpy-is-unreachable-dead-code-the-tworeg-paths-the-roadmap-describes-doesnt-exist-)) or the live LLM router prompts.

Concretely, the unverified claims:

- The regex parser at [directive_parser.py:4-37](backend/ai-scripts/services/directive_parser.py#L4-L37) lists RO+EN equivalents: `recomand[ăa]|recommend`, `sugereaz[ăa]|suggest`, `ofer[ăa]|offer`, `promoveaz[ăa]|promote`, `asociaz[ăa]|pair`, `și`/`şi`/`and`, `peste|over|at least`, `la finalul|la sfâr[șs]itul|at the end|end of`. The character-class variants (`ă|a`, `ș|s`) handle missing-diacritics input — but no test verifies the non-diacritic forms parse identically to the diacritic ones.
- The LLM router prompt at [directives.py:56-58](backend/ai-scripts/routers/directives.py#L56-L58) tells the model *"Match item names semantically (handle Romanian diacritics, plurals, abbreviations, and synonyms — e.g. 'limonada cu menta' should match 'Limonadă cu mentă' or 'Mint Lemonade')"*. This is the kind of behavioural promise that's testable with a mocked LLM that returns hand-crafted outputs for hand-crafted inputs — but no such test exists.

The fix is small: 4-6 parametrised tests against the regex parser (once it's wired in per Segment 16 [F1](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md#f1--high--directive_parserpy-is-unreachable-dead-code-the-tworeg-paths-the-roadmap-describes-doesnt-exist-)) covering the RO/EN pairs, plus 2-3 mocked-LLM tests against the recommendation-directives router for the semantic-matching contract. Out of scope here; the gap is documented for the test author.

---

## F4 — MEDIUM — No malformed-LLM-response test on the engine side

The vision surface has this exact shape — `test_invalid_provider_payload_treated_as_vision_api_error` ([test_menu_extraction_service.py:70-78](backend/ai-scripts/tests/test_menu_extraction_service.py#L70-L78)):

```python
async def test_invalid_provider_payload_treated_as_vision_api_error():
    service = MenuExtractionService(
        extractor_factory=lambda: FakeVisionExtractor(
            canned_response={"items": [{"name": "", "price": -1.0, "source_index": 0}]}
        )
    )
    result = await service.extract([b"\x00"])
    assert result.items == []
    assert result.failed_pages[0].reason == "vision_api_error"
```

A FakeVisionExtractor produces a *schema-conforming-but-content-invalid* response; the test asserts the service translates it into a `FailedPage`. The pattern is correct.

The engines have nothing equivalent. The roadmap names the gap directly: *"Schema validation tests: a malformed LLM response is rejected, not propagated."* Concrete scenarios that would land in the suite:

- **Recommendation engine**: mock Gemini to return `{"recommendations": [{"product_id": "<not-in-menu>", "reason": "...", "confidence": 0.85}]}` and assert the returned list is empty (today the `validated` filter at [recommendation_engine.py:49](backend/ai-scripts/services/recommendation_engine.py#L49) catches this; tomorrow a regression silently un-catches it).
- **Recommendation engine**: mock Gemini to return `{"recommendations": [{"product_id": "valid-uuid", "reason": "...", "confidence": 5.0}]}` and assert what happens — today the route's `response_model=RecommendationResponse` ([recommendations.py:7](backend/ai-scripts/routers/recommendations.py#L7)) Pydantic-validates but the `confidence: float` is unconstrained (Segment 16 [F5](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md#f5--medium--confidence--score-floats-are-not-constrained-to-01)), so the malformed value flows through. The test would fail loudly and document the gap.
- **Similarity engine**: mock Gemini to return `{"rules": [{"product_a": "<not-in-history>", ...}]}` and assert rejection. Segment 16 [F2](CODE_REVIEW_SEGMENT_16_AI_ENGINES.md#f2--high--similarity_engine-has-no-response_model-no-id-intersection-no-pydantic-validation) lives or dies on whether this test exists.
- **Directive routers**: mock Gemini to return malformed JSON (e.g. `"not really json"`). The current `except Exception: return {"parsed_rules": {}}` at [directives.py:35-36](backend/ai-scripts/routers/directives.py#L35-L36) swallows the parse error; a test that asserts a 502 (or some typed error envelope) would directly drive the [Segment 14 F1](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-) fix.

The test count is small (one per scenario, ~5 tests total) and the pattern is already proven by the vision tests. The blocker is the same as [F2](#f2--high--two-of-three-live-engines-recommendation-similarity-have-zero-unit-tests): the engines need a substitution seam for the Gemini client.

---

## F5 — MEDIUM — `_sniff_media_type` has no unit test; Segment 15 F7 (missing HEIC) would have surfaced

[services/vision_extractor.py:141-150](backend/ai-scripts/services/vision_extractor.py#L141-L150):

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

This function is the segment-15 instance of "structurally critical but invisible." It runs on every vision call, decides the `media_type` claim that the LLM sees, and silently mis-labels everything that doesn't match a known prefix. Segment 15 [F7](CODE_REVIEW_SEGMENT_15_VISION.md#f7--low--_sniff_media_type-falls-back-to-imagejpeg-when-bytes-dont-match-any-known-magic-and-the-comment-is-now-wrong) names two concrete bugs:

- HEIC magic-byte detection is missing (the C# side allows `image/heic` but Python labels HEIC bytes as `image/jpeg`).
- The `image/jpeg` fallback for unrecognised content is wrong.

A six-line `tests/test_sniff_media_type.py` parametrised test would have caught both at first failure:

```python
@pytest.mark.parametrize("magic_bytes, expected", [
    (b"%PDF-1.7", "application/pdf"),
    (b"\xff\xd8\xff", "image/jpeg"),
    (b"\x89PNG\r\n\x1a\n", "image/png"),
    (b"RIFF\x00\x00\x00\x00WEBP", "image/webp"),
    # Segment 15 F7 — these would fail and document the gap
    (b"\x00\x00\x00\x20ftypheic", "image/heic"),
    (b"\x00\x00\x00\x20ftypmif1", "image/heic"),
])
def test_sniff_media_type(magic_bytes, expected):
    assert _sniff_media_type(magic_bytes) == expected
```

The tests for the existing four supported types would pass; the two HEIC tests would fail. The fallback case is also testable — `_sniff_media_type(b"random garbage")` returning `image/jpeg` is a documented misbehaviour that a `pytest.raises` or an explicit `is None` assertion would have made visible.

A class of finding worth tagging: every "low-priority" finding in Segments 14-16 lives in code that has no unit test. Tests are the cheapest way to surface that class of bug.

---

## F6 — LOW — `conftest.py`'s fixture is unused, and `test_placeholder.py` is effectively dead weight

[tests/conftest.py:4-9](backend/ai-scripts/tests/conftest.py#L4-L9):

```python
@pytest.fixture
def sample_cart_items() -> list[dict]:
    return [
        {"menu_item_id": "00000000-0000-0000-0000-000000000001", "name": "Grătar mixt", "category": "Feluri principale", "tags": ["meat", "grill"]},
        {"menu_item_id": "00000000-0000-0000-0000-000000000002", "name": "Salată Caesar", "category": "Salate", "tags": ["salad", "cold"]},
    ]
```

Grep `sample_cart_items` across [tests/](backend/ai-scripts/tests/):

```
tests/conftest.py:5: def sample_cart_items() -> list[dict]:
tests/test_placeholder.py:5: def test_sample_fixture(sample_cart_items):
```

One consumer, `test_placeholder.py`. The fixture was created to support `/recommend` tests that were never written ([F1](#f1--high--four-of-five-real-routers-have-zero-tests-only-menuextract-is-covered-)). Note also that the fixture's shape (`menu_item_id` field, `category` + `tags` fields) does not match the actual `CartItem` model ([schemas.py:5-7](backend/ai-scripts/models/schemas.py#L5-L7) — `product_id`, `quantity`) — so the fixture is also *stale*: a future test author would copy-paste it and produce malformed test data. The fixture matches [shared/contracts/api-contract-ai.json:18-22](shared/contracts/api-contract-ai.json#L18-L22) instead, which is itself fiction per Segment 14 [F6](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f6--medium--api-contract-aijson-describes-an-api-that-does-not-exist).

[tests/test_placeholder.py:1-8](backend/ai-scripts/tests/test_placeholder.py):

```python
def test_ai_service_placeholder():
    assert True


def test_sample_fixture(sample_cart_items):
    assert len(sample_cart_items) == 2
    assert sample_cart_items[0]["category"] == "Feluri principale"
```

`test_ai_service_placeholder` is the same idea as [Segment 13](CODE_REVIEW_SEGMENT_13_TESTS.md)'s `SanityTest.cs` — proves the runner is alive. Once real tests exist it has no further purpose. `test_sample_fixture` tests the fixture, not the system; if the fixture is removed (it should be), this test goes with it.

The action item is small: delete `conftest.py` and `test_placeholder.py`. Each adds noise to the test report — `pytest` output reads "9 passed" today; "7 passed" once the placeholders go and only real tests remain. Or — better — replace the fixture with one that matches the actual `RecommendationRequest` shape and write the `/recommend` test that needs it.

---

## F7 — LOW — `test_extract_one_page_fails_others_succeed` depends on `asyncio.gather` task-scheduling order

[test_menu_extraction_service.py:41-55](backend/ai-scripts/tests/test_menu_extraction_service.py#L41-L55):

```python
async def test_one_page_fails_others_succeed():
    state = {"calls": 0}

    def factory():
        state["calls"] += 1
        if state["calls"] == 2:  # second page (index 1) fails
            return FakeVisionExtractor(raises=VisionExtractionError("boom"))
        return FakeVisionExtractor(canned_response={"items": _items(("Foo", 10.0))})

    service = MenuExtractionService(extractor_factory=factory)
    result = await service.extract([b"\x00", b"\x01", b"\x02"])
    assert {fp.source_page for fp in result.failed_pages} == {1}
```

And [test_menu_extraction_router.py:87-107](backend/ai-scripts/tests/test_menu_extraction_router.py#L87-L107) has the same shape on the router side, with `state["calls"] == 1` triggering the failure for page 0.

The factory is synchronous. `MenuExtractionService._extract_one` ([menu_extraction_service.py:47-50](backend/ai-scripts/services/menu_extraction_service.py#L47-L50)) calls `self._extractor_factory()` synchronously *at the start of each per-page task*, before any `await`. `asyncio.gather` ([line 28](backend/ai-scripts/services/menu_extraction_service.py#L28)) schedules tasks in list order, so the factory call sequence happens to be `page 0, page 1, page 2`.

The test works *only because* of two implementation details that no contract guarantees:

- `_extract_one`'s first line is the synchronous factory call. If a future change inserts an `await some_setup()` before line 50, the per-page tasks all reach the `await` checkpoint before any of them call the factory, and the call order becomes nondeterministic.
- `asyncio.gather` schedules in list order. Documented behaviour, but the runtime *executes* concurrently; under a different scheduler (e.g. `uvloop` with a specific config) the order isn't strictly preserved across the board.

The tests pass deterministically today and probably will for a long time. But the failure mode is silent: the assertion `{fp.source_page for fp in result.failed_pages} == {1}` would start failing intermittently in CI without any code change, just under load or after an asyncio version bump.

The fix is to make the failure deterministic *by content*, not by call order. One shape:

```python
def factory_for_page(page_number: int):
    if page_number == 1:
        return FakeVisionExtractor(raises=VisionExtractionError("boom"))
    return FakeVisionExtractor(canned_response={"items": _items(("Foo", 10.0))})
```

`MenuExtractionService` doesn't currently support that — the factory is `Callable[[], VisionExtractor]` ([menu_extraction_service.py:21](backend/ai-scripts/services/menu_extraction_service.py#L21)), zero-argument. Extending it to `Callable[[int], VisionExtractor]` (passing page_number to the factory) is a small refactor that makes per-page test fakes deterministic by construction.

LOW because the test passes deterministically today; tagged here so a future flake doesn't blindside the on-call.

---

## What's solid

| Item | Why it's good |
|---|---|
| `app.dependency_overrides` pattern ([test_menu_extraction_router.py:21, 30](backend/ai-scripts/tests/test_menu_extraction_router.py#L21-L30)) | The roadmap's "wired only via dependency override" item is met with the right FastAPI idiom; `yield` + `app.dependency_overrides.clear()` prevents leakage between tests. |
| CI invocation is correct ([backend.yml:33, 41-43](.github/workflows/backend.yml#L33-L43)) | Working-directory is `backend/ai-scripts`, dependency cache is wired, `pytest --tb=short` actually runs. Sharp contrast with [Segment 13 F1](CODE_REVIEW_SEGMENT_13_TESTS.md#f1--critical--ci-runs-dotnet-test-from-the-web-projects-directory-and-discovers-no-tests-the-suite-is-silently-green-) on the dotnet side. |
| Vision test surface is well-shaped | Five service-layer tests cover the four roadmap-named scenarios; the missing all-failure case is the only gap. The router tests are 4 well-chosen cases. The Pydantic model tests cover both happy and unhappy schema paths. |
| Romanian strings round-trip through UTF-8 + Pydantic ([test_menu_extraction_parsing.py:12, 22, 26](backend/ai-scripts/tests/test_menu_extraction_parsing.py)) | Latin-Extended characters survive Pydantic, JSON, and HTTP. Small but earned-its-keep. |
| `asyncio_mode = auto` ([pytest.ini:2](backend/ai-scripts/pytest.ini#L2)) | No `@pytest.mark.asyncio` decorator noise needed. The existing decorators are harmless redundancy. |
| `FakeVisionExtractor.__init__` invariant transferred cleanly into tests | The "exactly one of `canned_response` or `raises`" constraint (Segment 15) is exercised by [test_menu_extraction_parsing.py:55-76](backend/ai-scripts/tests/test_menu_extraction_parsing.py#L55-L76) so the constructor's `ValueError` is at least implicitly verified. |
| No network access required | `pytest` runs offline; `requirements.txt` is pip-cached; `FakeVisionExtractor` substitutes everywhere. CI cost is bounded. |

---

## Cross-segment follow-ups raised here

- **[Segment 02](CODE_REVIEW_ROADMAP.md#segment-02--ci-workflows--build-hygiene):** the python-ai CI job is correctly configured (in contrast to dotnet — see [Segment 13 F1](CODE_REVIEW_SEGMENT_13_TESTS.md#f1--critical--ci-runs-dotnet-test-from-the-web-projects-directory-and-discovers-no-tests-the-suite-is-silently-green-)). Segment 02's "Backend workflow runs `dotnet restore` → `build` → `test`" check should treat the dotnet job as failing and the python-ai job as the model.
- **[Segment 14](CODE_REVIEW_ROADMAP.md#segment-14--ai-service--bootstrap--routers):** [F1](#f1--high--four-of-five-real-routers-have-zero-tests-only-menuextract-is-covered-) and [F4](#f4--medium--no-malformed-llm-response-test-on-the-engine-side) — every router/engine finding from Segment 14 is reachable by a single mocked-Gemini test. The blocker (module-level `client = genai.Client(...)`) is itself a Segment 14 / 16 finding.
- **[Segment 15](CODE_REVIEW_ROADMAP.md#segment-15--ai-service--vision-extraction):** [F5](#f5--medium--_sniff_media_type-has-no-unit-test-segment-15-f7--missing-heic-would-have-surfaced-) — Segment 15 [F7](CODE_REVIEW_SEGMENT_15_VISION.md#f7--low--_sniff_media_type-falls-back-to-imagejpeg-when-bytes-dont-match-any-known-magic-and-the-comment-is-now-wrong) (missing HEIC, wrong fallback) is six lines of test away from being demonstrated.
- **[Segment 16](CODE_REVIEW_ROADMAP.md#segment-16--ai-service--recommendationsimilaritydirectives):** [F2](#f2--high--two-of-three-live-engines-recommendation-similarity-have-zero-unit-tests), [F3](#f3--medium--no-roen-directive-parsing-test-the-roadmaps-explicit-ask-from-segment-16), [F4](#f4--medium--no-malformed-llm-response-test-on-the-engine-side) — eight of the eight Segment 16 findings have at least one test that would prove them. The current zero coverage is what allowed them to land unobserved.
- **[Segment 24](CODE_REVIEW_ROADMAP.md#segment-24--shared-contracts-shared):** [F6](#f6--low--conftestpys-fixture-is-unused-and-test_placeholderpy-is-effectively-dead-weight) — the `sample_cart_items` fixture matches the [api-contract-ai.json](shared/contracts/api-contract-ai.json) shape, not the actual `CartItem` shape ([schemas.py:5-7](backend/ai-scripts/models/schemas.py#L5-L7)). The contract drift is documented in Segment 14 [F6](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f6--medium--api-contract-aijson-describes-an-api-that-does-not-exist); this is one more downstream symptom.

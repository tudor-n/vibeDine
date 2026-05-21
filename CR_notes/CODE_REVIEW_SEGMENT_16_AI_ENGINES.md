# Segment 16 — AI service: recommendation / similarity / directives — review findings

Scope review against the [Segment 16 checklist](CODE_REVIEW_ROADMAP.md#segment-16--ai-service--recommendationsimilaritydirectives) of [CODE_REVIEW_ROADMAP.md](CODE_REVIEW_ROADMAP.md). Three files: [services/recommendation_engine.py](backend/ai-scripts/services/recommendation_engine.py), [services/similarity_engine.py](backend/ai-scripts/services/similarity_engine.py), [services/directive_parser.py](backend/ai-scripts/services/directive_parser.py). Cross-references: the routers in [routers/recommendations.py](backend/ai-scripts/routers/recommendations.py), [routers/similarity.py](backend/ai-scripts/routers/similarity.py), [routers/directives.py](backend/ai-scripts/routers/directives.py); the Pydantic models in [models/schemas.py](backend/ai-scripts/models/schemas.py); the C# consumers in [RecommendationService.cs](backend/RestaurantApi/Services/RecommendationService.cs), [SimilarityService.cs](backend/RestaurantApi/Services/SimilarityService.cs), [AdminController.cs](backend/RestaurantApi/Controllers/AdminController.cs); and the persistence target in [Models/SimilarityRules.cs](backend/RestaurantApi/Models/SimilarityRules.cs).

Reviewer: Claude (Opus 4.7). Date: 2026-05-21. Branch reviewed: `main` at `fb59c7e`.

## Verdict

**8 findings, two of them HIGH.** The recommendation engine is the strongest of the three: it validates the LLM-returned IDs against the request's menu (`validated = [r for r in recs if r.get("product_id") in valid_ids]`, [recommendation_engine.py:49](backend/ai-scripts/services/recommendation_engine.py#L49)), its router declares `response_model=RecommendationResponse` ([recommendations.py:7](backend/ai-scripts/routers/recommendations.py#L7)) which forces Pydantic validation of every field on the way out, and the C# caller adds a *second* intersection at [RecommendationService.cs:80-81](backend/RestaurantApi/Services/RecommendationService.cs#L80-L81) so a hallucinated UUID never reaches the customer. That's the defense-in-depth shape the roadmap asks for.

The findings cluster around three themes:

1. **The "two-path directive parser" the roadmap describes doesn't exist.** [F1](#f1--high--directive_parserpy-is-unreachable-dead-code-the-tworeg-paths-the-roadmap-describes-doesnt-exist-) — [services/directive_parser.py](backend/ai-scripts/services/directive_parser.py) is a 110-line regex-based parser with no callers. The two `/directives/parse` endpoints in [routers/directives.py](backend/ai-scripts/routers/directives.py) inline their own Gemini call instead. The "documented when each is used" requirement is structurally impossible because the regex path is never used. The two parsers also return *different shapes* — `{"rules": [{action, item_hint, with_hint, priority, condition, item_hints, min_order_amount}]}` from the regex module, `{"parsed_rules": {...}}` from the live router — so wiring one in would break the consumer.
2. **The similarity engine has none of the guards the recommendation engine has.** [F2](#f2--high--similarity_engine-has-no-response_model-no-id-intersection-no-pydantic-validation) — `/similarity/auto-generate` has no `response_model` ([similarity.py:7-9](backend/ai-scripts/routers/similarity.py#L7-L9)), no input-product-id intersection on the engine's output ([similarity_engine.py:42](backend/ai-scripts/services/similarity_engine.py#L42)), and no Pydantic validation. A hallucinated product_id flows straight through to the C# caller — and the consumer side ([SimilarityService.cs:16-37](backend/RestaurantApi/Services/SimilarityService.cs#L16-L37), or its [dead-code twin](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f6--medium--similarityservice-is-dead-code-the-live-similarityauto-generate-path-duplicates-logic-in-admincontroller-and-skips-validation) in `AdminController.AutoGenerateRules`) doesn't intersect either. Segment 10 [F6](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f6--medium--similarityservice-is-dead-code-the-live-similarityauto-generate-path-duplicates-logic-in-admincontroller-and-skips-validation) already named this on the C# side; this is the Python end of the same gap.
3. **Configuration and validation hygiene is uneven.** [F3](#f3--medium--both-engines-hardcode-gemini-20-flash-ignoring-gemini_vision_model-env) — both engines hardcode `GEMINI_MODEL = "gemini-2.0-flash"`, bypassing the `GEMINI_VISION_MODEL` env override the rest of the service honours. [F4](#f4--medium--recommendation-engine-does-not-exclude-items-already-in-cart-from-candidate-set) — the recommendation prompt says "pair well with the cart" but doesn't forbid recommending what's already ordered (roadmap explicit ask). [F5](#f5--medium--confidence--score-floats-are-not-constrained-to-01) — `Recommendation.confidence: float` ([schemas.py:25](backend/ai-scripts/models/schemas.py#L25)) has no `Field(ge=0.0, le=1.0)`; the LLM can return `5.0` or `-1.0` and Pydantic accepts it. [F6](#f6--medium--no-directive-conflict-resolution-when-rules-contradict) — no conflict-resolution rule when directives contradict (roadmap's `↳`).

The remaining items: [F7](#f7--low--both-engines-silently-swallow-exception-into-empty-results) — `except Exception: return {...empty...}` repeats Segment 14 [F1](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-)'s pattern scoped to the engines; [F8](#f8--low--similarity-engine-output-shape-doesnt-match-similarityrule-columns-and-cant-carry-restaurantid) — the engine output is `{product_a, product_b, score}` but the persistence target [SimilarityRule](backend/RestaurantApi/Models/SimilarityRules.cs) wants `ProductAId`, `ProductBId`, `Score`, plus `RestaurantId` and `RuleType` that the Python side has no path to populate. The roadmap's "decimal(5,4) precision matching" item is moot today — the AI response is never persisted (Segment 10 [F6](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f6--medium--similarityservice-is-dead-code-the-live-similarityauto-generate-path-duplicates-logic-in-admincontroller-and-skips-validation)) — but if/when persistence lands, the shape needs to change.

What does check out, beyond the verdict opener: the recommendation engine's `valid_ids = {item.product_id for item in request.menu_items}` set at [line 16](backend/ai-scripts/services/recommendation_engine.py#L16) and the post-LLM filter at [line 49](backend/ai-scripts/services/recommendation_engine.py#L49) close the most-cited risk — *"return results referencing only those products — never invent product IDs"*. Returns `{"recommendations": []}` (an empty list, not `null`) when `request.menu_items` is empty ([line 13-14](backend/ai-scripts/services/recommendation_engine.py#L13-L14)) or when validation eliminates every candidate ([line 50](backend/ai-scripts/services/recommendation_engine.py#L50)) — the "empty list not null" roadmap item passes. The `/recommend` endpoint's Pydantic response model catches schema drift before the response leaves the process. The regex `directive_parser` is itself well-shaped — sentence splitting, item splitting on Romanian conjunctions (`și`/`şi`), threshold extraction with comma-decimal RO support, RO/EN keyword equivalence on `recomand[ăa] | recommend`, `sugereaz[ăa] | suggest`, `promoveaz[ăa] | promote`, `peste | over | at least` — but it's not wired in, so its quality is moot.

---

## Checklist coverage

| Item | Status | Evidence |
|---|---|---|
| All three engines accept the **menu in the request** and return results referencing only those products — never invent product IDs | ⚠ Partial — see [F2](#f2--high--similarity_engine-has-no-response_model-no-id-intersection-no-pydantic-validation) | Recommendation ✓ — `valid_ids` set built from request, post-LLM filter at [recommendation_engine.py:49](backend/ai-scripts/services/recommendation_engine.py#L49). Similarity ❌ — engine returns the raw Gemini response, no intersection with `order_history`. Directives — the routers receive menu in `ParseRecommendationDirectiveRequest.menu_items` ([schemas.py:46-48](backend/ai-scripts/models/schemas.py#L46-L48)) and the prompt asks the LLM to match against them ([directives.py:49-59](backend/ai-scripts/routers/directives.py#L49-L59)), but no post-LLM intersection guard. |
| Recommendation returns 2–3 items max, never duplicates of items already in cart | ⚠ Partial — see [F4](#f4--medium--recommendation-engine-does-not-exclude-items-already-in-cart-from-candidate-set) | 2-3 max ✓ — prompt says *"Recommend 2 to 3 products"* ([line 27](backend/ai-scripts/services/recommendation_engine.py#L27)) and the C# caller `.Take(3)` ([RecommendationService.cs:102](backend/RestaurantApi/Services/RecommendationService.cs#L102)). Cart-dedupe ❌ — the prompt receives `cart_context` but doesn't tell the model to exclude those product_ids; the engine's `validated` filter only checks the menu set, not the cart set. |
| Confidence scores are normalized [0,1] and consistent across providers | ❌ Fail — see [F5](#f5--medium--confidence--score-floats-are-not-constrained-to-01) | `Recommendation.confidence: float` ([schemas.py:25](backend/ai-scripts/models/schemas.py#L25)) — no `Field(ge=0.0, le=1.0)`. Similarity `score` is undeclared (no schema at all). Directive `priority: 0.8 / 0.9` is hardcoded in the regex parser ([directive_parser.py:89, 104](backend/ai-scripts/services/directive_parser.py#L89-L104)) so it's safe by construction, but unreachable per [F1](#f1--high--directive_parserpy-is-unreachable-dead-code-the-tworeg-paths-the-roadmap-describes-doesnt-exist-). |
| Directive parser handles RO **and** EN — verify with a few real prompts in each language | ❌ Fail (silently passes) | The regex parser at [directive_parser.py:4-37](backend/ai-scripts/services/directive_parser.py#L4-L37) does cover RO + EN keyword equivalents (`recomandă/recommend`, `sugerează/suggest`, `și/and`, `peste/over`, `la finalul/at the end`, etc.). But the parser is dead code ([F1](#f1--high--directive_parserpy-is-unreachable-dead-code-the-tworeg-paths-the-roadmap-describes-doesnt-exist-)). The live router-side Gemini prompt at [directives.py:44-72](backend/ai-scripts/routers/directives.py#L44-L72) is in English but the prompt explicitly tells the model *"Admin rule (Romanian or English)"* and *"handle Romanian diacritics, plurals, abbreviations, and synonyms"* — relies entirely on the model's ability to do so, with no tests to verify (Segment 17). |
| Directive parser has both LLM and regex paths — document when each is used and ensure they don't disagree silently | ❌ Fail — see [F1](#f1--high--directive_parserpy-is-unreachable-dead-code-the-tworeg-paths-the-roadmap-describes-doesnt-exist-) | Only the LLM path runs. The regex module exists and is more capable than the LLM prompt for several cases (`min_order_amount`, `condition: end_of_order`), but never executes. |
| Similarity engine outputs scores rounded to match `SimilarityRules.Score` precision (decimal(5,4)) — otherwise EF truncates and loses meaning | ⚠ Moot — see [F8](#f8--low--similarity-engine-output-shape-doesnt-match-similarityrule-columns-and-cant-carry-restaurantid) | The AI response is never persisted (Segment 10 [F6](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f6--medium--similarityservice-is-dead-code-the-live-similarityauto-generate-path-duplicates-logic-in-admincontroller-and-skips-validation)). [AdminController.AutoGenerateRules:233-245](backend/RestaurantApi/Controllers/AdminController.cs#L233-L245) returns the raw `JsonElement` to the admin client and stops there. If persistence is added later, the engine's `0.9`-style scores would need explicit `round(score, 4)`. |
| LLM JSON outputs go through Pydantic validation before being returned | ⚠ Partial — see [F2](#f2--high--similarity_engine-has-no-response_model-no-id-intersection-no-pydantic-validation) | `/recommend` ✓ — `response_model=RecommendationResponse` ([recommendations.py:7](backend/ai-scripts/routers/recommendations.py#L7)). `/similarity/auto-generate` ❌. `/directives/parse` and `/recommendation-directives/parse` ❌ — already named in Segment 14 [F1](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-). |
| When the LLM returns an empty result, the engine returns an empty list, not `null` | ✓ Pass | Recommendation: `{"recommendations": []}` ([line 14, 50](backend/ai-scripts/services/recommendation_engine.py#L14-L50)). Similarity: `{"rules": []}` ([similarity_engine.py:14, 42](backend/ai-scripts/services/similarity_engine.py#L14-L42)). Directive regex: `{"rules": []}` by construction. |
| Romanian-specific test strings exist in tests (segment 17) | ❌ Fail — see [F7](#f7--low--both-engines-silently-swallow-exception-into-empty-results) | No tests for any of the three engines (`grep "recommendation_engine\|similarity_engine\|directive_parser"` in [tests/](backend/ai-scripts/tests/) → zero matches). The only tests are menu-extraction shaped (Segment 15). Segment 17's "Gaps likely" list calls this out and it's confirmed. |
| How are directive conflicts resolved? (roadmap `↳`) | ❌ Fail — see [F6](#f6--medium--no-directive-conflict-resolution-when-rules-contradict) | No resolution logic. The regex parser produces a flat list of `rules`; the LLM path produces a flat list of `rules` or `parsed_rules`. Nothing reconciles `{"action": "recommend", "item_hints": ["X"]}` against a later sentence `{"action": "suppress", "item_hints": ["X"]}` (the latter pattern isn't even modelled). |

---

## F1 — HIGH — `directive_parser.py` is unreachable dead code; the two-path design the roadmap describes doesn't exist 🔥

[services/directive_parser.py](backend/ai-scripts/services/directive_parser.py) is a 110-line module exposing `parse_directive(raw_prompt: str) -> dict`. It has zero callers in the production code:

```
$ grep -r "directive_parser\|from services.directive_parser" backend/ai-scripts/
backend/ai-scripts/PLAN.md:25:    │   └── directive_parser.py        CREATE - keyword/regex parser
backend/ai-scripts/PLAN.md:108: ### `services/directive_parser.py`
backend/ai-scripts/PLAN.md:143: def parse_directive(request: DirectiveRequest): ...
backend/ai-scripts/services/directive_parser.py:72: def parse_directive(raw_prompt: str) -> dict:
backend/ai-scripts/routers/directives.py:15: def parse_directive(request: ParseDirectiveRequest):
```

The hits in `PLAN.md` are design notes; the hit in `routers/directives.py:15` is an unrelated function of the same name in the LLM-based router. Neither the router, the recommendation engine, nor any test imports the regex parser.

The router-side LLM path inlines a Gemini call:

[routers/directives.py:14-36](backend/ai-scripts/routers/directives.py#L14-L36):

```python
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
    ...
```

Two distinct schemas across the two paths:

| Path | Output shape |
|---|---|
| `directive_parser.parse_directive` (regex, dead code) | `{"rules": [{"action": "recommend"|"pair", "item_hint": str, "item_hints": [str], "with_hint": str, "condition": "end_of_order"|"always", "priority": 0.8|0.9, "min_order_amount": float}]}` |
| `routers/directives.py:parse_directive` (LLM, live) | `{"parsed_rules": {"keywords": [str], "action": "boost_category"|"recommend_specific", "target": str}}` |

Wiring the regex module in would not be a one-line change — the consumer ([AdminController.CreateDirective](backend/RestaurantApi/Controllers/AdminController.cs#L67-L90), which persists into `AIDirective.ParsedRules`) expects the LLM-shaped `parsed_rules` object, and `FlashDealService` reads it at [FlashDealService.cs:166](backend/RestaurantApi/Services/FlashDealService.cs#L166) looking for `action == "recommend"` and `item_hints` — a third shape, half-aligned with the regex one. Three shapes, two of them dead.

Three downstream concerns:

- **The roadmap item is structurally unsatisfiable.** *"Directive parser has both LLM and regex paths — document when each is used and ensure they don't disagree silently"* requires both paths to be live; today, "silent disagreement" is the only possible outcome since the regex path can never disagree because it never runs.
- **The regex parser is more capable than the live LLM prompt for a non-trivial subset.** `min_order_amount` extraction (`peste 190 de lei` / `over 190 RON` / `at least 50 lei` → `190.0` / `50.0`), `condition: end_of_order` detection (`la finalul comenzii` / `at the end of order`), and discount-phrase stripping (`la 30% reducere`, `with 30% off`) are explicit features of the regex module ([directive_parser.py:33-69](backend/ai-scripts/services/directive_parser.py#L33-L69)) — and absent from the LLM prompt at [directives.py:16-26](backend/ai-scripts/routers/directives.py#L16-L26). Anyone who reads the docstring and assumes both paths are running gets a misleading picture of which features are implemented.
- **The dead module is import-fragile.** A future refactor of [models/schemas.py](backend/ai-scripts/models/schemas.py) or a change to `Optional` semantics in Pydantic can break the dead module's imports — and since nothing imports it, CI won't notice until someone tries to revive it.

Fix options, all out of scope here: (1) delete `directive_parser.py` and reflect "LLM-only" in the docs, (2) wire it in as a pre-filter (regex first; only call Gemini if the regex fails), or (3) keep both paths but reconcile the output shapes through a typed Pydantic `ParsedDirective` model. The current state is the worst of the three.

---

## F2 — HIGH — `similarity_engine` has no `response_model`, no ID intersection, no Pydantic validation

[services/similarity_engine.py:12-43](backend/ai-scripts/services/similarity_engine.py#L12-L43):

```python
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
```

And [routers/similarity.py:7-9](backend/ai-scripts/routers/similarity.py#L7-L9):

```python
@router.post("/similarity/auto-generate")
def auto_generate(request: AutoGenerateRequest):
    return generate_similarity_rules(request)
```

Three guards the recommendation engine has, that this one doesn't:

1. **No `response_model` on the route.** A `dict` straight from `json.loads` flows to the wire. Schema drift in the LLM output reaches the C# caller unchecked.
2. **No post-LLM ID intersection.** The prompt says *"Only include product_id values that appear in the order history above"* — best-effort instruction, no enforcement. The recommendation engine's [validated = [r for r in recs if r.get("product_id") in valid_ids]](backend/ai-scripts/services/recommendation_engine.py#L49) has no twin here. A hallucinated UUID, or one belonging to a different restaurant's product if the C# caller ever multiplexed inputs, passes through.
3. **No `score` range constraint.** The example shows `0.9` but the engine doesn't validate `0.0 <= score <= 1.0` — combined with [F5](#f5--medium--confidence--score-floats-are-not-constrained-to-01) the value could be `1.5` or `-0.1` and the engine ships it.

The downstream story makes this less acute *today* because nothing persists the result — `AdminController.AutoGenerateRules` ([AdminController.cs:223-246](backend/RestaurantApi/Controllers/AdminController.cs#L223-L246)) just returns the raw `JsonElement` to the admin client, which presumably renders it for human review before any `POST /admin/similarity-rules` call. But the roadmap covers the consumer-side gap separately in [Segment 10 F6](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f6--medium--similarityservice-is-dead-code-the-live-similarityauto-generate-path-duplicates-logic-in-admincontroller-and-skips-validation), so this finding is specifically the Python-side absence of the producer guard.

Fix shape (out of scope): declare a Pydantic `SimilarityRulesResponse` with `rules: list[SimilarityRule]` where `SimilarityRule` has `product_a: str`, `product_b: str`, `score: float = Field(ge=0.0, le=1.0)`, set it as `response_model` on the route, and add a post-LLM intersection against the union of all UUIDs in `request.order_history`.

---

## F3 — MEDIUM — Both engines hardcode `gemini-2.0-flash`, ignoring `GEMINI_VISION_MODEL` env

[services/recommendation_engine.py:9](backend/ai-scripts/services/recommendation_engine.py#L9):

```python
GEMINI_MODEL = "gemini-2.0-flash"
```

[services/similarity_engine.py:9](backend/ai-scripts/services/similarity_engine.py#L9) — identical.

The vision extractor reads the env override ([vision_extractor.py:202](backend/ai-scripts/services/vision_extractor.py#L202)):

```python
self.model_name = model_name or os.environ.get("GEMINI_VISION_MODEL", GEMINI_VISION_DEFAULT_MODEL)
```

And the [.env.example:10](backend/ai-scripts/.env.example#L10) advertises `GEMINI_VISION_MODEL=gemini-2.0-flash` as the knob. The two engines bypass it. Concrete consequences:

- A team that switches to `gemini-1.5-pro` for better recommendation quality has to edit code, not config.
- The env var implies "vision model" but in practice the same key controls every Gemini call — except the two endpoints that ignore it.
- The choice of model is split across `services/` files (recommendation, similarity, directives router) with three identical literals — change-one-forget-the-others is the natural failure mode.

There's a related, distinct naming issue: the env var is called `GEMINI_VISION_MODEL` but is overloaded in concept. A clean separation would be `GEMINI_MODEL` for default text-only flows (recommendations, similarity, directives) and `GEMINI_VISION_MODEL` for the multimodal vision flow — or one env var for everything, named neutrally. Today neither shape exists; the code uses three constants and the env var controls only one of them.

This is one of three places this pattern shows up (Segment 14 [F1](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-) flagged the directives router instance). The fix is one config helper and three call-site updates.

---

## F4 — MEDIUM — Recommendation engine does not exclude items already in cart from candidate set

[recommendation_engine.py:17-39](backend/ai-scripts/services/recommendation_engine.py#L17-L39):

```python
menu_context = [{"product_id": item.product_id, "name": item.name} for item in request.menu_items]
cart_context = [{"product_id": item.product_id, "quantity": item.quantity} for item in request.cart_items]

prompt = f"""You are a restaurant upsell recommendation engine.

Customer cart: {json.dumps(cart_context)}

Available products to recommend (use ONLY these exact product_id values):
{json.dumps(menu_context)}

Recommend 2 to 3 products from the available list that pair well with the cart.
...
"""
```

The `menu_context` is the full menu. The `cart_context` is informational (the LLM is supposed to use it for context — *"pair well with the cart"*) but the engine does not exclude cart items from the candidate set, and the prompt does not say *"do not recommend items the customer already ordered"*. Result: the LLM is free to return a product_id that's already in `cart_items`, and the `validated` filter at [line 49](backend/ai-scripts/services/recommendation_engine.py#L49) lets it through (the ID is in `valid_ids` because it's part of `menu_items`).

The C# side has no second-line dedupe either; [RecommendationService.cs:98-103](backend/RestaurantApi/Services/RecommendationService.cs#L98-L103) groups by `ProductId` to remove *internal* duplicates but doesn't subtract the cart:

```csharp
var combined = ruleDriven
    .Concat(aiRecs)
    .GroupBy(r => r.ProductId)
    .Select(g => g.First())
    .Take(3)
    .ToList();
```

So the customer can be recommended an item they just ordered. The roadmap calls this out explicitly: *"Recommendation returns 2–3 items max, never duplicates of items already in cart"* — the count cap is enforced; the dedupe is not.

The fix is small in three places:

- **Prompt-level:** add *"Do not recommend any product_id that appears in the Customer cart above"*.
- **Engine-level:** `valid_ids = {item.product_id for item in request.menu_items} - {item.product_id for item in request.cart_items}` ([line 16](backend/ai-scripts/services/recommendation_engine.py#L16)).
- **Consumer-level:** the C# `.Concat(aiRecs)` step could `Except(cartProductIds)` for defense in depth.

Any one of the three fixes addresses the headline; all three is the right shape.

---

## F5 — MEDIUM — `confidence` / `score` floats are not constrained to [0,1]

[models/schemas.py:22-30](backend/ai-scripts/models/schemas.py#L22-L30):

```python
class Recommendation(BaseModel):
    product_id: str
    reason: str
    confidence: float


class RecommendationResponse(BaseModel):
    recommendations: List[Recommendation]
```

`confidence: float` — no `Field(ge=0.0, le=1.0)`. The LLM is told *"confidence: 0.85"* in the prompt example ([recommendation_engine.py:34](backend/ai-scripts/services/recommendation_engine.py#L34)) but the model could return `5.0`, `-0.2`, `NaN`, or `Infinity` (Python `float("inf")` survives Pydantic v2 with no further constraint), and the response Pydantic model accepts it. The downstream C# record `RecommendationResponse(string ProductId, string Reason, double Confidence)` ([RecommendationService.cs](backend/RestaurantApi/Services/RecommendationService.cs)) also has no range constraint.

The similarity engine has neither a Pydantic model nor a manual check (see [F2](#f2--high--similarity_engine-has-no-response_model-no-id-intersection-no-pydantic-validation)) — `score: 0.9` is example-only.

Compare with [models/menu_extraction.py:13](backend/ai-scripts/models/menu_extraction.py#L13), which gets this right for the vision side:

```python
confidence: Optional[float] = Field(default=None, ge=0.0, le=1.0)
```

The fix is replicating that single line into `Recommendation.confidence`. Roadmap explicit ask: *"Confidence scores are normalized [0,1] and consistent across providers."* Today, neither.

The contract file [shared/contracts/api-contract-ai.json:47](shared/contracts/api-contract-ai.json#L47) does name the range — *"confidence: 'number (0.0 to 1.0 — how confident the system is in this rec)'"* — but the contract is fiction per Segment 14 [F6](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f6--medium--api-contract-aijson-describes-an-api-that-does-not-exist) and nothing enforces it.

---

## F6 — MEDIUM — No directive conflict resolution when rules contradict

The roadmap's `↳`: *"How are directive conflicts resolved? (Two rules both saying 'recommend X with Y' vs 'never recommend X' — who wins?)"*. The answer is that the system cannot model the conflict in the first place:

- The regex parser ([directive_parser.py](backend/ai-scripts/services/directive_parser.py)) only emits `action: "recommend"` ([line 86](backend/ai-scripts/services/directive_parser.py#L86)) or `action: "pair"` ([line 102](backend/ai-scripts/services/directive_parser.py#L102)). There's no `action: "suppress"` / `"never"` / `"exclude"`.
- The LLM directive router prompts for `action: "boost_category" | "recommend_specific"` ([directives.py:21](backend/ai-scripts/routers/directives.py#L21)) — again, no suppression action.
- The recommendation-directive router prompts for `trigger_product_ids` + `recommend_product_ids` ([directives.py:62-69](backend/ai-scripts/routers/directives.py#L62-L69)) — pairing-rule shape only.

So the conflict between *"recommend X"* and *"never recommend X"* doesn't manifest in this system because the latter can't be expressed. That's a smaller surface than the roadmap implies — but two real conflicts still exist and aren't resolved:

- **Same product recommended by two rules.** If `rule_1.recommend_product_ids = [X]` and `rule_2.recommend_product_ids = [X]`, the consumer at [FlashDealService.cs:166](backend/RestaurantApi/Services/FlashDealService.cs#L166) iterates rules and matches whatever fires first. No priority field, no de-duplication.
- **Triggered by overlapping carts.** Two `RecommendationDirective` rows with overlapping `trigger_product_ids` produce overlapping `recommend_product_ids`. The recommendation engine combines them via `.Concat(aiRecs).GroupBy(r => r.ProductId)` ([RecommendationService.cs:98-103](backend/RestaurantApi/Services/RecommendationService.cs#L98-L103)) which de-duplicates by ID but doesn't preserve which directive "won."

The minimum-effort answer to the roadmap follow-up is to document: *"Conflicts are not modelled. Two rules recommending the same product are de-duplicated; rules with opposite intent (recommend vs suppress) are not expressible today."*. The longer answer is to add a `priority` or `weight` field that the regex parser already proposes ([directive_parser.py:89, 104](backend/ai-scripts/services/directive_parser.py#L89-L104)) and use it for ordering — but that means wiring the regex parser in, which [F1](#f1--high--directive_parserpy-is-unreachable-dead-code-the-tworeg-paths-the-roadmap-describes-doesnt-exist-) blocks.

---

## F7 — LOW — Both engines silently swallow `Exception` into empty results

[recommendation_engine.py:41-52](backend/ai-scripts/services/recommendation_engine.py#L41-L52):

```python
try:
    response = client.models.generate_content(...)
    result = json.loads(response.text)
    recs = result.get("recommendations", [])
    validated = [r for r in recs if r.get("product_id") in valid_ids]
    return {"recommendations": validated}
except Exception:
    return {"recommendations": []}
```

[similarity_engine.py:35-42](backend/ai-scripts/services/similarity_engine.py#L35-L42):

```python
try:
    response = client.models.generate_content(...)
    return json.loads(response.text)
except Exception:
    return {"rules": []}
```

This is the same pattern as Segment 14 [F1](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-), now repeated in two more places. Three identical knock-on effects:

- The C# caller can't distinguish "no products are similar" from "Gemini timed out" from "API key is wrong" from "JSON parse failed."
- No log line is emitted (no `logger.exception`, no `print` — neither engine creates a logger).
- The customer-facing path falls back to *"random products"* ([RecommendationService.cs:107-111](backend/RestaurantApi/Services/RecommendationService.cs#L107-L111)) which masks the failure to the user but also masks it from the on-call.

The narrowing fix shape: catch a discriminated set (`google.api_core.exceptions.*`, `json.JSONDecodeError`, `httpx.HTTPError`) and raise typed errors that the route layer can translate to 502/503. Or — to match the partial-success pattern the vision extractor uses ([Segment 15 F8 notes](CODE_REVIEW_SEGMENT_15_VISION.md#f8--low--one-invalid-item-discards-the-entire-pages-extraction-rather-than-emitting-partial-success)) — wrap each error mode in a domain-typed `RecommendationEngineError` so the router can return a meaningful status code.

LOW because the failure mode (silent empty result) is bounded and the C# side falls back gracefully; HIGH would be appropriate if the directive flow had the same pattern (it does, in Segment 14 [F1](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-), counted there).

---

## F8 — LOW — Similarity engine output shape doesn't match `SimilarityRule` columns and can't carry `RestaurantId`

Engine output ([similarity_engine.py:24-30](backend/ai-scripts/services/similarity_engine.py#L24-L30)):

```json
{"rules": [{"product_a": "uuid", "product_b": "uuid", "score": 0.9}]}
```

Persistence target ([Models/SimilarityRules.cs:5-22](backend/RestaurantApi/Models/SimilarityRules.cs#L5-L22)):

```csharp
public class SimilarityRule : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RestaurantId { get; set; }
    public Guid ProductAId { get; set; }
    public Product? ProductA { get; set; }
    public Guid ProductBId { get; set; }
    public Product? ProductB { get; set; }
    [Column(TypeName = "decimal(5,4)")]
    public decimal Score { get; set; }
    public string? RuleType { get; set; } = "manual";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

Three mismatches:

- **Field naming.** `product_a` / `product_b` vs `ProductAId` / `ProductBId`. Trivial to map; worth a note for the future.
- **Numeric precision.** `score: 0.9` is a `float` from Python, going to a `decimal(5,4)` column. EF Core's Npgsql provider rounds — but only after explicit `Math.Round(score, 4)` in C#, otherwise the conversion is provider-default. Roadmap explicit ask.
- **Tenancy.** The Python side has no `restaurant_id` (correctly, per Segment 14 [F6 cross-ref](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#what-does-check-out)). When/if the C# side starts persisting, it must inject `RestaurantId` from the calling admin's tenant context — not from the AI response.

The "moot" qualifier matters: today, the only consumer is [AdminController.AutoGenerateRules:233-245](backend/RestaurantApi/Controllers/AdminController.cs#L233-L245), which returns the raw `JsonElement` to the admin client. The admin sees a list of suggested rules and (presumably, in some future UI) chooses which to persist via a separate `POST /admin/similarity-rules` endpoint. That endpoint doesn't exist yet — no `[HttpPost("similarity-rules")]` is present in the admin controller. So the full loop is incomplete.

When the loop closes, the engine's response shape needs to grow (or a translation layer needs to land) before persistence works. The roadmap's decimal-precision ask becomes live then.

---

## What's solid

| Item | Why it's good |
|---|---|
| Recommendation engine validates LLM IDs against the request menu ([recommendation_engine.py:16, 49](backend/ai-scripts/services/recommendation_engine.py#L16-L49)) | The most-cited risk in this segment — hallucinated UUIDs — is closed at the producer. |
| `/recommend` declares `response_model=RecommendationResponse` ([recommendations.py:7](backend/ai-scripts/routers/recommendations.py#L7)) | FastAPI validates the engine's `dict` against Pydantic on the way out; schema drift in the LLM JSON is caught. |
| Empty inputs short-circuit ([recommendation_engine.py:13-14](backend/ai-scripts/services/recommendation_engine.py#L13-L14), [similarity_engine.py:13-14](backend/ai-scripts/services/similarity_engine.py#L13-L14)) | Saves an LLM call when nothing's to do; returns `[]` (not `null`) — the roadmap's "empty list, not null" item passes. |
| Defense-in-depth on the C# consumer ([RecommendationService.cs:80-81](backend/RestaurantApi/Services/RecommendationService.cs#L80-L81)) | Even if the Python guard ever drops, the consumer intersects again. The combination is right. |
| The regex `directive_parser` itself is well-shaped | Sentence splitting, RO/EN keyword equivalence, comma-decimal threshold parsing, discount-phrase stripping, end-of-order condition detection — all well-built. The shame is that it's unreachable. |
| Pydantic request models are tight | `RecommendationRequest`, `AutoGenerateRequest`, `ParseDirectiveRequest`, `ParseRecommendationDirectiveRequest` ([schemas.py](backend/ai-scripts/models/schemas.py)) all validate inbound shape. Outbound is the gap. |

---

## Cross-segment follow-ups raised here

- **[Segment 06](CODE_REVIEW_ROADMAP.md#segment-06--data-layer--migrations):** [F8](#f8--low--similarity-engine-output-shape-doesnt-match-similarityrule-columns-and-cant-carry-restaurantid) — the `decimal(5,4)` precision question is moot today but lands here when the persistence path is added.
- **[Segment 08](CODE_REVIEW_ROADMAP.md#segment-08--controllers--admin-surface):** the missing `POST /admin/similarity-rules` endpoint and the related auto-generate persistence path live in [AdminController](backend/RestaurantApi/Controllers/AdminController.cs); [F8](#f8--low--similarity-engine-output-shape-doesnt-match-similarityrule-columns-and-cant-carry-restaurantid) is the AI-side half of that conversation.
- **[Segment 10](CODE_REVIEW_ROADMAP.md#segment-10--background--domain-services):** [F2](#f2--high--similarity_engine-has-no-response_model-no-id-intersection-no-pydantic-validation) is the producer-side companion of [Segment 10 F6](CODE_REVIEW_SEGMENT_10_BACKGROUND_SERVICES.md#f6--medium--similarityservice-is-dead-code-the-live-similarityauto-generate-path-duplicates-logic-in-admincontroller-and-skips-validation) (consumer-side missing validation). Either fix alone closes half of the picture; both together close it cleanly.
- **[Segment 14](CODE_REVIEW_ROADMAP.md#segment-14--ai-service--bootstrap--routers):** [F3](#f3--medium--both-engines-hardcode-gemini-20-flash-ignoring-gemini_vision_model-env) extends Segment 14's hardcoded-model finding to a second and third call site; [F7](#f7--low--both-engines-silently-swallow-exception-into-empty-results) extends Segment 14 [F1](CODE_REVIEW_SEGMENT_14_AI_BOOTSTRAP.md#f1--high--directivespy-swallows-every-failure-into-an-empty-200-ok-no-response_model-no-error-signal-)'s swallow-exception pattern across the engines.
- **[Segment 17](CODE_REVIEW_ROADMAP.md#segment-17--ai-service-tests):** [F1](#f1--high--directive_parserpy-is-unreachable-dead-code-the-tworeg-paths-the-roadmap-describes-doesnt-exist-) and the missing RO/EN directive tests are exactly what Segment 17's "Gaps likely" list calls out. Tests would have caught the dead-code reality at first glance.
- **[Segment 23](CODE_REVIEW_ROADMAP.md#segment-23--admin-spa--orders-metrics-ai-config):** [F6](#f6--medium--no-directive-conflict-resolution-when-rules-contradict) — once a UI for previewing parsed directives lands, the lack of a `suppress` action becomes visible. The admin SPA's directive-management view will need to either show "rules are additive, conflicts are not modelled" or wait for the engine to support both shapes.

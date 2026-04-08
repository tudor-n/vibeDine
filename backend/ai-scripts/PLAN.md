# Plan: Implement ai-scripts Python Service

## Context

The `backend/ai-scripts/` Python FastAPI service is a skeleton — three endpoints (`/recommend`, `/similarity/auto-generate`, `/directives/parse`) exist but return empty results. The dev guide specifies the intended file structure (routers/, services/, models/). The C# `RecommendationService` is already wired to call this Python service at `http://localhost:8000`. Tudor owns all three Python endpoints. Goal: implement the full Python service using scikit-learn/numpy for ML and keyword/regex for directive parsing (no LLM).

---

## File Structure to Build

```
backend/ai-scripts/
├── main.py                        UPDATE - register routers
├── models/
│   ├── __init__.py                CREATE (empty)
│   └── schemas.py                 CREATE - all Pydantic models
├── routers/
│   ├── __init__.py                CREATE (empty)
│   ├── recommendations.py         CREATE - POST /recommend
│   └── similarity.py              CREATE - POST /similarity/auto-generate + /directives/parse
├── services/
│   ├── __init__.py                CREATE (empty)
│   ├── recommendation_engine.py   CREATE - scoring logic
│   ├── similarity_engine.py       CREATE - co-occurrence + Jaccard
│   └── directive_parser.py        CREATE - keyword/regex parser
└── tests/
    └── conftest.py                EXISTING (sample_cart_items fixture)
```

---

## Schemas (`models/schemas.py`)

Following the dev guide sample + practical extensions:

```python
# /recommend input
class CartItem(BaseModel):
    product_id: str
    quantity: int
    category: Optional[str] = None
    tags: Optional[list[str]] = []

class MenuItem(BaseModel):
    product_id: str
    name: str
    category: str
    tags: Optional[list[str]] = []

class RecommendationRequest(BaseModel):
    cart_items: list[CartItem]
    table_id: str
    order_history: Optional[list[list[str]]] = []   # list of orders, each = list[product_id]
    menu_items: Optional[list[MenuItem]] = []        # full available menu
    active_directives: Optional[list[dict]] = []    # parsed_rules from active AIDirectives

# /recommend output
class Recommendation(BaseModel):
    product_id: str
    reason: str          # "directive" | "pairing" | "category" | "upsell"
    confidence: float    # 0.0–1.0

class RecommendationResponse(BaseModel):
    recommendations: list[Recommendation]

# /similarity/auto-generate
class SimilarityRequest(BaseModel):
    order_history: list[list[str]]   # list of orders, each = list[product_id]

class SimilarityRule(BaseModel):
    item_a_id: str
    item_b_id: str
    score: float

class SimilarityResponse(BaseModel):
    rules: list[SimilarityRule]

# /directives/parse
class DirectiveRequest(BaseModel):
    raw_prompt: str

class DirectiveResponse(BaseModel):
    parsed_rules: dict   # {"rules": [...]}
```

---

## Implementation Details

### `services/similarity_engine.py`
- `build_co_occurrence(order_history)` → `dict[(a,b), count]`
- `generate_similarity_rules(order_history, threshold=0.1)` → Jaccard similarity per pair: `score = count / (count_a + count_b - count)`
- Returns sorted list of `{item_a_id, item_b_id, score}` filtered by threshold

### `services/recommendation_engine.py`
Three-layer scoring on candidates (menu items not in cart):

1. **Directive boost** — for each active directive rule:
   - `action=recommend, condition=always` → match `item_hint` against candidate name → `+priority`
   - `action=pair, with_hint` → if `with_hint` matches a cart item's category/name → match `item_hint` candidate → `+priority`
2. **Co-occurrence boost** — for each cart item, check co-occurrence dict; add normalized score × 0.5 to candidate
3. **Category complement** — heuristic boosts:
   - If cart has `Feluri principale` or `Salate` → `Băuturi` candidates get `+0.2`
   - Any non-empty cart → `Deserturi` get `+0.15`

Returns top N sorted by total score with `reason` = highest-contributing layer.

### `services/directive_parser.py`
Regex patterns for Romanian + English:
- **Recommend verbs**: `recomand[ăa]`, `recommend`, `sugereaz[ăa]`
- **Pair verbs + prepositions**: `promoveaz[ăa].*cu`, `promote.*with`
- **End-of-order condition**: `la finalul`, `la sfâr[șs]itul`, `at the end`

Split prompt on `.!?`, classify each sentence, extract item names by stripping the verb and condition phrases, split items on `și|and|,`.

Output structure:
```json
{
  "rules": [
    {"action": "recommend", "item_hints": ["papanași", "tiramisu"], "condition": "always", "priority": 0.9},
    {"action": "pair", "item_hint": "vin roșu", "with_hint": "grătar", "priority": 0.8}
  ]
}
```

### `routers/recommendations.py`
```python
@router.post("/recommend", response_model=RecommendationResponse)
def recommend(request: RecommendationRequest):
    results = score_recommendations(
        request.cart_items, request.menu_items,
        request.order_history, request.active_directives
    )
    return RecommendationResponse(recommendations=results)
```

### `routers/similarity.py`
```python
@router.post("/similarity/auto-generate", response_model=SimilarityResponse)
def auto_generate(request: SimilarityRequest): ...

@router.post("/directives/parse", response_model=DirectiveResponse)
def parse_directive(request: DirectiveRequest): ...
```

### `main.py` (updated)
```python
from fastapi import FastAPI
from routers import recommendations, similarity

app = FastAPI(title="Restaurant AI Service")
app.include_router(recommendations.router)
app.include_router(similarity.router)

@app.get("/health")
def health():
    return {"status": "ok"}
```

---

## Critical Files

| File | Action |
|------|--------|
| `backend/ai-scripts/main.py` | Update — include routers |
| `backend/ai-scripts/models/schemas.py` | Create |
| `backend/ai-scripts/routers/recommendations.py` | Create |
| `backend/ai-scripts/routers/similarity.py` | Create |
| `backend/ai-scripts/services/recommendation_engine.py` | Create |
| `backend/ai-scripts/services/similarity_engine.py` | Create |
| `backend/ai-scripts/services/directive_parser.py` | Create |
| `backend/ai-scripts/models/__init__.py` | Create (empty) |
| `backend/ai-scripts/routers/__init__.py` | Create (empty) |
| `backend/ai-scripts/services/__init__.py` | Create (empty) |

---

## Verification

1. Start service: `cd backend/ai-scripts && uvicorn main:app --reload --port 8000`
2. Check `GET http://localhost:8000/health` → `{"status":"ok"}`
3. Test `/recommend` with Swagger UI at `http://localhost:8000/docs` — send a cart with a "Feluri principale" item, expect Deserturi/Băuturi candidates in response
4. Test `/similarity/auto-generate` with `{"order_history": [["id1","id2"],["id1","id3"],["id1","id2"]]}` — expect id1↔id2 with higher score
5. Test `/directives/parse` with the seeded directive: `"Recomandă papanași și tiramisu la finalul oricărei comenzi. Promovează vinul roșu al casei cu preparatele la grătar."` — expect 2 rules extracted

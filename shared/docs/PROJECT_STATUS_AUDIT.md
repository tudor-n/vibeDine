# Web-Restaurant: Full Project Status Audit & Integration Fix Plan

## Context

The web-restaurant project is a monorepo with 5 services: ASP.NET Core backend (C#), Python FastAPI AI service, Vue 3 customer frontend, Vue 3 admin frontend, and PostgreSQL. The user states frontend-customer is complete and backend is "complete-ish". This audit identifies every gap preventing the system from functioning as one entity.

---

## Architecture Overview

```
frontend-customer (Vue3, :3000) ──┐
frontend-admin    (Vue3, :3001) ──┤── HTTP ──► backend-api (ASP.NET Core, :5005)
                                  │                    │
                                  │               HTTP │
                                  │                    ▼
                                  │           ai-service (FastAPI, :8000)
                                  │                    │
                                  └── SignalR WS ◄─────┘
                                                       │
                                                  PostgreSQL (:5433)
```

---

## A. CRITICAL INTEGRATION BUGS (frontend ↔ backend contract mismatches)

These are the biggest blockers — the frontend and backend exist but **cannot talk to each other correctly**.

### A1. Recommendations endpoint response shape mismatch
- **Backend** `CustomerController.GetRecommendation` (CustomerController.cs:247-260): Returns `Ok(recommendations)` — a flat `List<RecommendationResponse>` where each item is `{ productId, reason, confidence }`
- **Frontend** `useRecommendations.ts:36-37`: Expects `response.pairings` and `response.upsells` — arrays of `Recommendation` objects with nested `menu_item { id, name, price, image_url }`
- **Shared type** `api-responses.ts:37-41`: Defines `RecommendationsResponse` as `{ pairings: Recommendation[], upsells: Recommendation[], flash_deal: FlashDeal | null }`
- **Impact**: Recommendations will ALWAYS appear empty. `response.pairings` will be `undefined`.
- **Fix**: Restructure the backend response to match the shared type, OR adapt the frontend to handle the flat list. The backend needs to: (a) fetch full product info for each recommended productId, (b) split results into pairings vs upsells, (c) return the nested structure.

### A2. Metrics endpoint response field names mismatch
- **Backend** `AdminController.GetMetrics` (AdminController.cs:126-132): Returns anonymous object with camelCase fields: `totalDealsShown`, `totalDealsClaimed`, `totalDealsExpired`, `upSellRevenue`
- **Frontend** `PerformanceMetrics.vue:27-59`: Expects: `flashDealsShown`, `flashDealsClaimed`, `upsellRevenue`, `flashDealRevenue`
- **Also**: `PerformanceMetrics.vue:11` has typo `flashDealsShow` (missing 'n') for computing claim rate
- **Shared type** `models.ts:149-155`: `DailyMetrics` uses snake_case: `flash_deals_shown`, `flash_deals_claimed`, `recommendation_clicks`, `upsell_revenue`
- **Impact**: All 5 metric cards will show 0. The dashboard is completely broken.
- **Fix**: Align backend response field names to match what frontend expects. The backend should return consistent camelCase names matching frontend expectations.

### A3. Directives type mismatch
- **Backend** returns `AIDirective` entity directly: `{ id, rawPrompt, parsedRules, createdAt, isActive }` (camelCase via C# JsonNamingPolicy)
- **Frontend** `useDirectivesStore.ts:9` types directives as `AdminAIConfig[]` which has completely different fields: `date`, `daily_prompt`, `promoted_items`, `suppressed_items`, `flash_deal_config`, `created_by`, `created_at`
- **Frontend** `useDirectivesStore.ts:13` computed `activeDirective` checks `d.is_active` — but backend sends `isActive` (camelCase). This will always be `undefined`, so no directive is ever "active".
- **Impact**: Directives list renders with empty/undefined fields. Active directive detection is broken.
- **Fix**: Either (a) use the correct `AIDirective` shared type in the store, or (b) create a proper DTO on the backend that matches `AdminAIConfig` shape

### A4. POST directive request body snake_case vs camelCase
- **Frontend** `api-client.ts:37`: Sends `{ raw_prompt: prompt }` (snake_case)
- **Backend** `CreateDirectiveRequest` (AdminController.cs:164-167): Property is `RawPrompt`. With CamelCase JSON policy, it expects `{ "rawPrompt": "..." }`
- `raw_prompt` will NOT map to `RawPrompt` — System.Text.Json case-insensitive matching only ignores casing, not underscores
- **Impact**: Every directive created through the admin panel will have an EMPTY prompt
- **Fix**: Frontend should send `{ rawPrompt: prompt }`, OR add `[JsonPropertyName("raw_prompt")]` to the C# property

### A5. Admin API default URL mismatch
- **File**: `frontend-admin/src/services/api-client.ts:4`
- **Current**: `http://localhost:4000` (fallback when no env var)
- **Should be**: `http://localhost:5005`
- **Impact**: Admin frontend broken when running without docker-compose
- **Fix**: Change default to `http://localhost:5005`

---

## B. MISSING BACKEND ENDPOINTS & MODELS

### B1. No similarity-rules endpoints or model
- **Frontend calls**: `GET /api/admin/similarity-rules` (api-client.ts:47-48)
- **Backend**: No controller action for this route. No `SimilarityRule` entity in the database. No migration for a similarity_rules table.
- **SimilarityService** (SimilarityService.cs): Empty class — constructor only, zero methods
- **Impact**: Admin rules page will 404
- **Fix**: 
  1. Add `SimilarityRule` entity model to match shared type
  2. Add migration to create the `similarity_rules` table
  3. Add CRUD endpoints to AdminController: GET, POST, DELETE for similarity-rules
  4. Implement `SimilarityService` methods
  5. Wire up frontend store methods (`addRule`, `deleteRule`)
  6. Add `autoGenerate` endpoint call from frontend

### B2. Frontend store methods are stubs
- `useSimilarityRulesStore.addRule()` (line 31-46): Commented out API call, just does `fetchRules()`
- `useSimilarityRulesStore.deleteRule()` (line 48-61): Commented out API call, only removes locally
- Missing API client methods: `postSimilarityRule`, `deleteSimilarityRule`, `autoGenerateRules`

---

## C. INCOMPLETE BACKEND IMPLEMENTATIONS

### C1. RecommendationService always sends empty order_history
- **File**: `RecommendationService.cs:37`
- **Current**: `order_history = new List<string>()` — always empty
- **Impact**: AI service has no history to base recommendations on
- **Fix**: Query completed orders for the table before calling AI service

### C2. AdminController.CreateDirective has empty catch block
- **File**: `AdminController.cs:57-59`
- **Current**: `catch { }` — swallows all exceptions silently
- **Impact**: If AI directive parsing fails, no error is logged, no feedback to user
- **Fix**: Add logging and optionally return a warning to the client

### C3. ToggleDirective ignores request body
- **File**: `AdminController.cs:76-86`
- Frontend sends `{ is_active: boolean }` to set a specific value
- Backend reads no body — just flips `directive.IsActive = !directive.IsActive`
- **Impact**: Minor — works as toggle but doesn't respect frontend's intent
- **Fix**: Accept `[FromBody]` parameter with desired `isActive` value

---

## D. AI SERVICE ISSUES

### D1. AI endpoints hallucinate UUIDs
- **File**: `services/recommendation_engine.py:10-25`
- The recommendation engine tells Gemini to recommend products but never provides the actual product catalog
- Gemini generates fake UUIDs that don't match any real product in the database
- **Same issue** in `similarity_engine.py` — uses order history UUIDs but can't generate valid product pairs
- **Impact**: Even with a valid GEMINI_API_KEY, recommendations will reference non-existent products
- **Fix**: The backend should pass the full product catalog (or at least available products) to the AI service, and the AI service should include them in the prompt

### D2. Missing GEMINI_API_KEY in .env.example
- **File**: `backend/ai-scripts/.env.example` — only has `HOST`, `PORT`, `APP_ENV`, `LOG_LEVEL`
- `GEMINI_API_KEY` is required by all 3 routers
- **Fix**: Add `GEMINI_API_KEY=your-key-here` to `.env.example`

### D3. No error handling in AI service
- All 3 Python service functions have bare `except:` that return empty results
- No logging of Gemini API failures
- **Fix**: Add proper exception handling with logging

---

## E. COMPONENT STATUS SUMMARY

| Component | Status | Blocking Issues |
|-----------|--------|----------------|
| **PostgreSQL + Migrations** | 95% Complete | Missing similarity_rules table |
| **Seed Data** | Complete | - |
| **MenuController** | Complete | - |
| **CustomerController** | 90% Complete | Recommendations response shape (A1) |
| **AdminController** | 70% Complete | Missing similarity CRUD (B1), empty catch (C2), metrics naming (A2) |
| **FlashDealService** | 85% Complete | Background task state loss on restart (low priority) |
| **RecommendationService** | 60% Complete | Empty order_history (C1), response shape (A1) |
| **SimilarityService** | 0% Complete | Empty class (B1) |
| **SignalR Hubs** | Complete | - |
| **Python /recommend** | 40% Complete | No product catalog context (D1) |
| **Python /similarity** | 40% Complete | No product catalog context (D1) |
| **Python /directives/parse** | 70% Complete | Missing GEMINI_API_KEY config (D2) |
| **frontend-customer** | 90% Complete | Recommendations display broken (A1) |
| **frontend-admin** | 60% Complete | API URL (A5), metrics broken (A2), directives broken (A3, A4), similarity stubs (B2) |
| **Shared Types** | 80% Complete | Types don't match actual backend JSON contract |
| **Docker Compose** | Complete | - |

---

## F. PRIORITY-ORDERED FIX LIST

### Priority 1 — Make existing features actually work (contract alignment)

1. **Fix admin API default URL** — `frontend-admin/src/services/api-client.ts:4`: change `4000` to `5005`
2. **Fix metrics response** — `AdminController.cs:126-132`: rename fields to match frontend expectations OR update `PerformanceMetrics.vue` to match backend field names
3. **Fix directives type usage** — `useDirectivesStore.ts`: use `AIDirective` type instead of `AdminAIConfig`, fix `is_active` → `isActive` check
4. **Fix POST directive body** — `frontend-admin/api-client.ts:37`: change `raw_prompt` to `rawPrompt`
5. **Fix recommendations response** — Either:
   - (a) Restructure `CustomerController.GetRecommendation` to return `{ pairings: [...], upsells: [...] }` with full product info, OR
   - (b) Adapt `useRecommendations.ts` and `RecommendationWidget.vue` to handle flat `{ productId, reason, confidence }` list

### Priority 2 — Complete missing pieces

6. **Add SimilarityRule model + migration** — New entity, new table
7. **Add similarity-rules CRUD to AdminController** — GET, POST, DELETE endpoints
8. **Implement SimilarityService** — At minimum, proxy to AI service
9. **Wire up frontend similarity store** — Uncomment and fix `addRule`, `deleteRule`, add `autoGenerate` to api-client
10. **Pass product catalog to AI service** — Backend should include menu items in recommendation/similarity requests

### Priority 3 — Robustness improvements

11. Fix empty catch in `AdminController.CreateDirective`
12. Populate `order_history` in `RecommendationService`
13. Add `GEMINI_API_KEY` to `.env.example`
14. Add proper error handling/logging in Python AI service
15. Remove unused imports in `MenuResponse.cs`

---

## G. VERIFICATION PLAN

After fixes, test the full flow:

1. **Start all services**: `docker-compose up --build`
2. **Customer flow**: Navigate to `http://localhost:3000/11111111-1111-1111-1111-111111111111`
   - Menu should load with 5 categories, 17 available products
   - Add items to cart → verify PATCH sync works
   - Verify recommendations appear (even if fallback/random)
   - Submit order → verify flash deal banner appears via WebSocket
   - Claim flash deal → verify item added to order
3. **Admin flow**: Navigate to `http://localhost:3001`
   - Dashboard: metrics cards should show real numbers (not all zeros)
   - AI Config: create directive → verify prompt is saved (not empty)
   - Toggle directive active/inactive → verify state changes
   - Rules page: should load (not 404) once backend endpoints exist
4. **API verification**: Hit `http://localhost:5005/swagger` to test endpoints directly

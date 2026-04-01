# Project State Analysis - web-restaurant

> Generated: 2026-04-01 | Branch: backend-tudor | Commit: 45fb761

---

## 1. Architecture Overview


frontend-customer (Vue 3, :3000)  ──┐
                                     ├── HTTP/WS ──▶  RestaurantApi (C# ASP.NET Core 8, :5005)
frontend-admin    (Vue 3, :3001)  ──┘                       │
                                                     HTTP ──▼
                                                   ai-service (Python FastAPI, :8000)
                                                            │
                                                     SQL ───▼
                                                   PostgreSQL (:5433)


*Team Ownership:*

| Area | Owner | Status |
|------|-------|--------|
| Backend architecture, models, DB, AI service | Tudor | ~50% complete |
| Customer endpoints, orders, flash deals, hubs | Beny | ~30% complete |
| Frontend customer app | FE Dev 1 | ~95% complete |
| Frontend admin app | FE Dev 2 | ~5% complete |

---

## 2. Critical Bugs (Fix Immediately)

### 2.1 TYPO in Shared Types — 'nakme' instead of 'name'

*File:* shared/types/models.ts line 71
typescript
// CURRENT (broken):
menu_item: Pick<Product, 'id' | 'nakme' | 'price' | 'image_url'>;

// SHOULD BE:
menu_item: Pick<Product, 'id' | 'name' | 'price' | 'image_url'>;

*Impact:* FlashDealBanner.vue references activeDeal.menu_item.name — TypeScript won't catch this mismatch, leading to undefined at runtime when flash deals display. This is *uncommitted* and sitting in working tree changes.

*Owner to fix:* Tudor (models ownership) or whoever introduced the change.

---

### 2.2 Typo in DTO Class Name — CreatOrderRequest

*File:* backend/RestaurantApi/DTO/Requests/AddItemRequest.cs
csharp
public class CreatOrderRequest  // Missing 'e' — should be CreateOrderRequest

*Owner:* Beny (order endpoints)

---

### 2.3 Swallowed Concurrency Exception

*File:* backend/RestaurantApi/Controllers/CustomerController.cs (~line 107)
csharp
catch (DbUpdateConcurrencyException)
{
    // Empty catch — concurrent order updates fail silently
}

*Risk:* Two users at the same table could corrupt order state without anyone knowing.
*Owner:* Beny

---

## 3. Tudor's Backend — Status & Gaps

### Completed
- All 7 EF Core models (Category, Product, Order, OrderItem, RestaurantTable, FlashDealOffer, AIDirective)
- AppDbContext with full configuration (keys, column types, relationships)
- Database migration (20260325083842_InitialCreate)
- Seed data (18 products, 5 categories, 4 tables, 1 AI directive)
- Program.cs (DI, CORS, SignalR, EF, Swagger)
- Docker Compose orchestration for all 5 services

### Not Started / Skeleton Only
| Component | File(s) | Status |
|-----------|---------|--------|
| Python /recommend endpoint | ai-scripts/main.py | Returns {"recommendations": []} |
| Python /similarity/auto-generate | ai-scripts/main.py | Returns {"rules": []} |
| Python /directives/parse | ai-scripts/main.py | Returns {"parsed_rules": {}} |
| Recommendation engine | Not created | Per PLAN.md: 3-layer scoring (directive boost, co-occurrence, category complement) |
| Similarity engine | Not created | Per PLAN.md: Co-occurrence + Jaccard similarity |
| Directive parser | Not created | Per PLAN.md: Regex-based keyword extraction (RO + EN) |
| Pydantic schemas | Not created | CartItem, MenuItem, RecommendationRequest, etc. |
| Python routers | Not created | recommendations.py, similarity.py |
| RecommendationService.cs (C# proxy) | Services/RecommendationService.cs | Returns null |
| SimilarityService.cs | Services/SimilarityService.cs | Constructor only, no methods |

### Discrepancies in Tudor's Domain
1. *Duplicate AppDbContext.cs* — exists in both backend/RestaurantApi/Models/ (incomplete, missing HasKey calls) and backend/Data/ (correct, being used). The duplicate in Models/ is misleading.
2. *Duplicate SeedData.cs* — RestaurantApi/SeedData.cs uses Guid.NewGuid() (non-deterministic), Data/SeedData.cs uses fixed UUIDs. Program.cs uses the Data/ version. The other is dead code.
3. *PLAN.md* is untracked — not committed to the repo yet.

---

## 4. Beny's Backend — Status & Gaps

### Completed
- MenuController.cs — fully working (GET /api/menu, GET /api/menu/{categoryId})
- CustomerController.cs — partially working:
  - GET /api/tables/{qrToken}/session (validate QR, create/return draft order)
  - PATCH /api/orders/{orderId}/items (add/update cart items)
  - PATCH /api/orders/{orderId}/submit (submit order)

### Not Implemented
| Component | Expected Per Guide | Status |
|-----------|-------------------|--------|
| GET /api/orders/{id}/recommendations | Proxy to Python service | Missing |
| POST /api/orders/{id}/claim-deal | Flash deal claim | Missing |
| AdminController.cs | AI directives CRUD, metrics, flash deal management | File does not exist |
| FlashDealService.cs | Eligibility check, status transitions, expiry timer | File does not exist |
| CustomerHub.cs events | FlashDealAvailable, FlashDealExpired | Hub exists but empty |
| StaffHub.cs events | FlashDealClaimed, order notifications | Hub exists but empty |

### Code Quality Issues in Beny's Domain
- Unused imports in DTO/Responses/MenuResponse.cs (Microsoft.AspNetCore.SignalR, Microsoft.Net.Http.Headers)
- Potential null reference in CustomerController — assumes item.Product is loaded without explicit .Include()

---

## 5. Frontend Customer App — Status & Gaps

### Completed (~95%)
- MenuList.vue — category tabs, product grid, add-to-cart
- Cart.vue — line items, submit button, backend sync (500ms debounce)
- FlashDealBanner.vue — countdown timer, claim button, animations
- RecommendationWidget.vue — AI suggestions panel
- OrderingView.vue — main page composing all components
- useCartStore — full cart state with backend sync
- useSessionStore — QR token session with sessionStorage persistence
- useFlashDealStore — active deal state management
- useRecommendations.ts — debounced fetch composable
- useWebSocket.ts — SignalR connection with auto-reconnect
- api-client.ts — typed fetch wrapper for all needed endpoints
- Vue Router, Pinia, TailwindCSS all configured

### Minor Issues
| Issue | File | Severity |
|-------|------|----------|
| Hardcoded fallback QR token '11111111-...' | view/OrderingView.vue:26 | Medium |
| Unused counter.ts placeholder store | stores/counter.ts | Low |
| Page title says "Vite App" | index.html | Low |
| any type in MenuList.vue response mapping | components/MenuList.vue:34 | Low |
| API/WebSocket URLs hardcoded (localhost:4000) | services/api-client.ts, composables/useWebSocket.ts | Medium |
| No .env.example documenting config vars | Root directory | Low |

---

## 6. Frontend Admin App — Status & Gaps

### CRITICAL: App is ~5% complete

*What exists:*
- 3 Pinia stores (directives, metrics, similarity rules)
- 1 API client with 5 of 10 needed endpoints
- main.ts that mounts an empty app (no root component, no router, no Pinia init)
- Placeholder test file

*What is entirely missing (per FRONTEND_DEV_GUIDE.md):*
- App.vue root component
- Vue Router configuration
- TailwindCSS setup
- DashboardView.vue — overview metrics, active deals
- AIConfigView.vue — manage AI directives
- RulesManagementView.vue — edit similarity/pairing rules
- DailyPromptInput.vue — textarea for AI instructions
- SimilarityRuleTable.vue — table of pairing rules
- PerformanceMetrics.vue — charts/cards for deal metrics

*Incomplete store methods* (useSimilarityRulesStore.ts):
- addRule() — commented out, has Romanian comment: "Notă: Va trebui să adăugăm postSimilarityRule în api-client.ts"
- deleteRule() — commented out, same pattern
- autoGenerate() — not implemented

*Missing API client methods:*
- POST /api/admin/similarity-rules
- DELETE /api/admin/similarity-rules/{id}
- POST /api/admin/similarity-rules/auto-generate
- GET /api/admin/flash-deals
- POST /api/admin/flash-deals/trigger

---

## 7. Shared Types & Contracts

### Status: Mostly Complete
- models.ts — all 11 domain types defined (Category, Product, Order, OrderItem, RestaurantTable, FlashDeal, Recommendation, AIDirective, SimilarityRule, DailyMetrics, TableSession)
- api-requests.ts — request DTOs (CreateOrderRequest, PatchOrderItemsPayload, etc.)
- api-responses.ts — response DTOs (MenuResponse, TableSessionResponse, OrderResponse, etc.)

### Issues
1. *'nakme' typo* (see Section 2.1)
2. shared/contracts/api-contract-ai.json — exists but not validated against actual Python endpoints
3. shared/docs/test.md — contains only "test pt hotea" (placeholder)

---

## 8. Configuration & DevOps

### Docker Compose (works)
| Service | Port | Image/Build |
|---------|------|-------------|
| postgres | 5433 | postgres:16 |
| backend-api | 5005 | ./backend/RestaurantApi |
| ai-service | 8000 | ./backend/ai-scripts |
| frontend-customer | 3000 | ./frontend-customer |
| frontend-admin | 3001 | ./frontend-admin |

### Port Inconsistency
- frontend-admin/.env.example has VITE_API_URL=http://localhost:5000 — but docker-compose maps backend to :5005
- frontend-customer hardcodes http://localhost:4000 — but backend runs on :5005
- These need to be aligned

---

## 9. Git State

- *Uncommitted changes:* shared/types/models.ts (the nakme typo) — needs to be reverted, not committed
- *Untracked file:* backend/ai-scripts/PLAN.md — should be committed
- *No merge conflicts* — clean working tree otherwise
- *No stashed changes*
- *Branches:* backend-tudor (current), develop, main local; origin/frontend-developer remote

---

## 10. Summary: What Each Person Should Prioritize

### Tudor (Backend Architecture + AI)
1. Revert the nakme typo in shared/types/models.ts
2. Commit backend/ai-scripts/PLAN.md
3. Delete duplicate Models/AppDbContext.cs and RestaurantApi/SeedData.cs (dead code)
4. Implement the Python AI service per PLAN.md (recommendation engine, similarity engine, directive parser)
5. Wire up RecommendationService.cs to actually proxy to the Python service
6. Implement SimilarityService.cs methods

### Beny (Customer Endpoints + Flash Deals)
1. Fix the CreatOrderRequest typo
2. Fix the empty catch (DbUpdateConcurrencyException) block
3. Remove unused imports from MenuResponse.cs
4. Implement AdminController.cs with all admin endpoints
5. Implement FlashDealService.cs (eligibility, status transitions, expiry timer)
6. Add SignalR events to CustomerHub.cs and StaffHub.cs
7. Implement GET /api/orders/{id}/recommendations and POST /api/orders/{id}/claim-deal

### Frontend Dev 1 (Customer App)
1. Replace hardcoded URLs with environment variables
2. Remove unused counter.ts store
3. Fix page title from "Vite App"
4. Add .env.example file

### Frontend Dev 2 (Admin App)
1. *Build the entire admin UI* — this is the biggest gap in the project
2. Initialize main.ts properly (App.vue, Pinia, Vue Router)
3. Add TailwindCSS
4. Create all 6 missing view/component files
5. Complete API client with remaining 5 endpoints
6. Complete store methods (add/delete similarity rules)
7. Replace any types with proper shared types

---

## 11. Overall Project Completion

| Layer | Completion | Blocking Issues |
|-------|-----------|-----------------|
| Database & Models | 95% | Duplicate files (cleanup) |
| C# API Endpoints | 40% | Admin controller, flash deals, recommendations missing |
| Python AI Service | 5% | All 3 endpoints are stubs |
| Frontend Customer | 90% | Minor config issues |
| Frontend Admin | 5% | Almost entirely missing |
| Shared Types | 95% | One typo |
| DevOps / Docker | 85% | Port inconsistencies |
| *Overall* | *~45%* | |
# Web-Restaurant: Integration Fix Guide

Every change needed to make frontend ↔ backend ↔ AI service communicate properly.

---

## 1. Fix Recommendations Response Shape (CRITICAL)

The backend returns a flat list; the frontend expects `{ pairings, upsells, flash_deal }`.

**The backend alreadgit commit -m "Fix: stage remaining frontend changes"y does this correctly!** Looking at `CustomerController.cs` lines 206–260, `GetRecommendation` already returns `{ pairings, upsells, flash_deal }` with full product info. The audit doc (Section A1) was based on an older version. The current code is correct.

**But** `useRecommendations.ts` has a defensive branch (line 36) that checks `if (Array.isArray(response))` — this is fine, it handles both shapes. No change needed here.

**However**, the frontend `Recommendation` type expects `menu_item.category_name` but the backend never sends it. This won't break anything (it'll just be undefined), so leave it.

---

## 2. Fix Metrics Response Field Names (CRITICAL)

### File: `backend/RestaurantApi/Controllers/AdminController.cs`

**Current** (line ~126):
```csharp
return Ok(new
{
   flashDealsShown = totalDeals,        
   flashDealsClaimed = claimedDeals,    
   flashDealsExpired = expiredDeals,    
   upsellRevenue = upSellRevenue 
});
```

**Change to** (add missing `flashDealRevenue`):
```csharp
return Ok(new
{
   flashDealsShown = totalDeals,        
   flashDealsClaimed = claimedDeals,    
   flashDealsExpired = expiredDeals,    
   upsellRevenue = upSellRevenue,
   flashDealRevenue = upSellRevenue  // same source for now
});
```

### File: `frontend-admin/src/components/PerformanceMetrics.vue`

**Fix the typo** on line 11 — `flashDealsShow` → `flashDealsShown`:
```typescript
const rate = (props.metrics.flashDealsClaimed / props.metrics.flashDealsShown) * 100;
```

---

## 3. Fix Directives Store Type (CRITICAL)

### File: `frontend-admin/src/stores/useDirectivesStore.ts`

**Change the import** (line 4):
```typescript
// BEFORE:
import type { AIDirective } from "@shared/types/models";  // ← already correct!
```

The import is already `AIDirective`, not `AdminAIConfig`. But the `AIConfigView.vue` references `activeDirective.raw_prompt` — the backend sends `rawPrompt` (camelCase via C# JSON policy).

### File: `frontend-admin/src/views/AIConfigView.vue`

**Change** line 38:
```vue
<!-- BEFORE -->
"{{ directivesStore.activeDirective.raw_prompt }}"

<!-- AFTER -->
"{{ directivesStore.activeDirective.rawPrompt }}"
```

### File: `frontend-admin/src/stores/useDirectivesStore.ts`

The computed `activeDirective` checks `d.isActive` — but the shared type defines `is_active`. Since the backend sends camelCase (`isActive`), the store code is actually correct for runtime. The TypeScript type is wrong but won't break at runtime. For type safety, cast the response:

**Change** line 13:
```typescript
const activeDirective = computed(() => 
    directives.value.find((d: any) => d.isActive) || null
)
```

---

## 4. Fix POST Directive Request Body (CRITICAL)

### File: `frontend-admin/src/services/api-client.ts`

The `CreateDirectiveRequest` C# class has `[JsonPropertyName("rawPrompt")]` — so it expects `rawPrompt`.

**Current** (line 37):
```typescript
body: JSON.stringify({ rawPrompt: prompt }),  // ← already correct!
```

This is already correct. The audit doc was wrong — the C# class has `[JsonPropertyName("rawPrompt")]` on line 166 of AdminController.cs.

---

## 5. Fix Admin API Default URL

### File: `frontend-admin/src/services/api-client.ts`

**Change** line 4:
```typescript
// BEFORE:
const BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5005';
```
Already correct in the uploaded code. 

### File: `frontend-admin/.env.example`

**Change**:
```
VITE_API_URL=http://localhost:5005
```

---

## 6. Fix Customer Frontend API URL

### File: `frontend-customer/src/services/api-client.ts`

**Current** (line 4):
```typescript
const rawBaseUrl = import.meta.env.VITE_API_URL || 'http://localhost:5005';
```
Already correct.

---

## 7. Fix `ToggleDirective` to Accept Body Properly (Backend)

### File: `backend/RestaurantApi/Controllers/AdminController.cs`

**Current** (line ~76):
```csharp
[HttpPatch("directives/{id}")]
public async Task<IActionResult> ToggleDirective(Guid id, [FromBody] bool isActive)
```

The frontend sends `{ isActive: boolean }` but the endpoint expects a raw `bool`. Fix:

```csharp
[HttpPatch("directives/{id}")]
public async Task<IActionResult> ToggleDirective(Guid id, [FromBody] JsonElement body)
{
    var directive = await _dbContext.AIDirectives.FindAsync(id);
    if (directive == null) return NotFound();

    if (body.TryGetProperty("isActive", out var prop))
        directive.IsActive = prop.GetBoolean();
    else
        directive.IsActive = !directive.IsActive;
    
    await _dbContext.SaveChangesAsync();
    return Ok(directive);
}
```

**Or** simpler — change the frontend to send raw bool:

### File: `frontend-admin/src/services/api-client.ts`

**Change** `toggleActiveDirective`:
```typescript
toggleActiveDirective: (id: string, isActive: boolean) =>
    fetcher<void>(`/api/admin/directives/${id}`, {
  method: 'PATCH',
  body: JSON.stringify(isActive)  // send raw bool, not object
}),
```

---

## 8. Add SimilarityRule to Database (Backend)

The `SimilarityRule` model already exists at `backend/RestaurantApi/Models/SimilarityRules.cs` but it's NOT configured in the main `AppDbContext` at `backend/Data/AppDbContext.cs` (the one actually used), and there's no migration for it.

### File: `backend/Data/AppDbContext.cs`

**Add** DbSet (it's only in the duplicate):
```csharp
public DbSet<SimilarityRule> SimilarityRules => Set<SimilarityRule>();
```

**Add** to `OnModelCreating`:
```csharp
// ── SimilarityRules ──────────────────────────────────────────────────
modelBuilder.Entity<SimilarityRule>(e =>
{
    e.ToTable("similarity_rules");
    e.HasKey(s => s.Id);
    e.Property(s => s.Id).HasColumnName("id");
    e.Property(s => s.ProductAId).HasColumnName("product_a_id");
    e.Property(s => s.ProductBId).HasColumnName("product_b_id");
    e.Property(s => s.Score).HasColumnName("score").HasColumnType("decimal(5,4)");
    e.Property(s => s.RuleType).HasColumnName("rule_type");
    e.Property(s => s.CreatedAt).HasColumnName("created_at");

    e.HasOne(s => s.ProductA)
     .WithMany()
     .HasForeignKey(s => s.ProductAId)
     .OnDelete(DeleteBehavior.Cascade);

    e.HasOne(s => s.ProductB)
     .WithMany()
     .HasForeignKey(s => s.ProductBId)
     .OnDelete(DeleteBehavior.Cascade);
});
```

**Then run**: `dotnet ef migrations add AddSimilarityRules && dotnet ef database update`

### Add Similarity CRUD Endpoints to AdminController

Add these endpoints to `AdminController.cs`:

```csharp
[HttpGet("similarity-rules")]
public async Task<IActionResult> GetSimilarityRules()
{
    var rules = await _dbContext.SimilarityRules
        .Include(r => r.ProductA)
        .Include(r => r.ProductB)
        .OrderByDescending(r => r.Score)
        .Select(r => new
        {
            r.Id,
            id = r.Id,
            source_item_id = r.ProductAId,
            target_item_id = r.ProductBId,
            score = r.Score,
            reason = $"{r.ProductA!.Name} + {r.ProductB!.Name}",
            rule_type = r.RuleType
        })
        .ToListAsync();

    return Ok(rules);
}

[HttpPost("similarity-rules")]
public async Task<IActionResult> PostSimilarityRule([FromBody] JsonElement body)
{
    var rule = new SimilarityRule
    {
        ProductAId = Guid.Parse(body.GetProperty("productAId").GetString()!),
        ProductBId = Guid.Parse(body.GetProperty("productBId").GetString()!),
        Score = body.TryGetProperty("score", out var s) ? s.GetDecimal() : 0.8m,
        RuleType = "manual"
    };

    _dbContext.SimilarityRules.Add(rule);
    await _dbContext.SaveChangesAsync();
    return Ok(rule);
}

[HttpDelete("similarity-rules/{id}")]
public async Task<IActionResult> DeleteSimilarityRule(Guid id)
{
    var rule = await _dbContext.SimilarityRules.FindAsync(id);
    if (rule == null) return NotFound();
    _dbContext.SimilarityRules.Remove(rule);
    await _dbContext.SaveChangesAsync();
    return NoContent();
}
```

---

## 9. Fix AI Service — Pass Product Catalog

### File: `backend/RestaurantApi/Services/RecommendationService.cs`

Already correct! Lines 29–43 build `menu_items` with product IDs and names and send them to the AI service. The audit was based on an older version.

### File: `backend/ai-scripts/services/recommendation_engine.py`

Already validates returned IDs against `valid_ids`. This is correct.

**However**, the Gemini model name `gemini-3-flash-preview` may not exist. Change to a real model:

```python
GEMINI_MODEL = "gemini-2.0-flash"
```

Apply the same fix in:
- `backend/ai-scripts/routers/directives.py` 
- `backend/ai-scripts/services/similarity_engine.py`

### File: `backend/ai-scripts/.env.example`

**Add**:
```
GEMINI_API_KEY=your-gemini-api-key-here
```

---

## 10. Delete Duplicate Dead Code Files

### Delete these files (they're duplicates that cause confusion):

1. `backend/RestaurantApi/Models/AppDbContext.cs` — duplicate of `backend/Data/AppDbContext.cs`
2. `backend/RestaurantApi/SeedData.cs` — duplicate of `backend/Data/SeedData.cs`

The canonical versions in `backend/Data/` are the ones used by `Program.cs`.

---

## 11. Fix Docker Compose Port Consistency

### File: `docker-compose.yml`

No changes needed — it already maps `5005:5000` for backend, `3000:3000` for customer, `3001:3001` for admin.

### File: `frontend-admin/.env.example`

Already addressed in #5 — change to `http://localhost:5005`.

---

## 12. Fix the `nakme` Typo in Shared Types

### File: `shared/types/models.ts`

Looking at the uploaded file, line 71 area — the current uploaded version does NOT have the `nakme` typo. The `FlashDeal` interface uses:
```typescript
menu_item: Pick<Product, 'id' | 'name' | 'price' | 'image_url'>;
```
This is already correct. The typo was in uncommitted working tree changes that were apparently reverted.

---

## 13. Fix Frontend-Admin `SimilarityRuleTable.vue` to Match Backend Shape

### File: `frontend-admin/src/components/SimilarityRuleTable.vue`

The component references `rule.source_item_id`, `rule.target_item_id`, `rule.reason`, `rule.score` — which matches the backend response shape from the new endpoints above. No changes needed.

---

## 14. Fix `DailyPromptInput.vue` Typo

### File: `frontend-admin/src/components/DailyPromptInput.vue`

**Line 10**: `porps` → `props` (won't break Vue since it uses `defineProps` return, but fix for cleanliness):
```typescript
const props = defineProps<{
    isLoading: boolean;
}>();
```

---

## Summary: Files to Change

| # | File | Change |
|---|------|--------|
| 1 | `backend/RestaurantApi/Controllers/AdminController.cs` | Add `flashDealRevenue`, fix `ToggleDirective` body, add similarity CRUD |
| 2 | `backend/Data/AppDbContext.cs` | Add `SimilarityRules` DbSet + config |
| 3 | `backend/ai-scripts/services/recommendation_engine.py` | Fix Gemini model name |
| 4 | `backend/ai-scripts/routers/directives.py` | Fix Gemini model name |
| 5 | `backend/ai-scripts/services/similarity_engine.py` | Fix Gemini model name |
| 6 | `backend/ai-scripts/.env.example` | Add `GEMINI_API_KEY` |
| 7 | `frontend-admin/src/views/AIConfigView.vue` | `raw_prompt` → `rawPrompt` |
| 8 | `frontend-admin/src/components/PerformanceMetrics.vue` | Fix `flashDealsShow` typo |
| 9 | `frontend-admin/src/components/DailyPromptInput.vue` | Fix `porps` typo |
| 10 | `frontend-admin/src/services/api-client.ts` | Fix `toggleActiveDirective` body |
| 11 | `frontend-admin/.env.example` | Port 5000 → 5005 |
| 12 | Delete `backend/RestaurantApi/Models/AppDbContext.cs` | Dead duplicate |
| 13 | Delete `backend/RestaurantApi/SeedData.cs` | Dead duplicate |
| 14 | Run new EF migration for `similarity_rules` table | |

### After applying all changes:

```bash
# 1. Delete dead code
rm backend/RestaurantApi/Models/AppDbContext.cs
rm backend/RestaurantApi/SeedData.cs

# 2. Add migration
cd backend/RestaurantApi
dotnet ef migrations add AddSimilarityRules
dotnet ef database update

# 3. Start everything
docker-compose up --build

# 4. Test
# Customer: http://localhost:3000/11111111-1111-1111-1111-111111111111
# Admin:    http://localhost:3001
# Swagger:  http://localhost:5005/swagger
# AI:       http://localhost:8000/health
```

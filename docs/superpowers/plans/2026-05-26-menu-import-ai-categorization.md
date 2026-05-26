# Menu Import AI Categorization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an AI organizer step after extraction so menu imports produce a Category → Subcategory → Product hierarchy, persist it via a new `subcategories` table, and review it through a tree UI.

**Architecture:** Schema is additive (new `subcategories` table, nullable `products.subcategory_id`). Python pipeline gains one post-dedup LLM call with a hint-fallback safety net. C# gains a `bulk-create-hierarchy` endpoint and surfaces subcategories on GETs; existing endpoints stay in place. Frontend gains `StepReviewHierarchy.vue` and a hierarchy-aware store; existing rendering keeps working.

**Tech Stack:** ASP.NET Core 8 + EF Core + PostgreSQL; Python FastAPI + pytest + Anthropic / Gemini SDKs; Vue 3 + Pinia + Vitest.

**Spec:** `docs/superpowers/specs/2026-05-26-menu-import-ai-categorization-design.md`

**Conventions verified against current code:**
- C# integration tests: `IAsyncLifetime` + `PostgreSqlContainer` per class; use `MenuImportTestFactory.CreateAuthenticatedClient(userId, restaurantId)` and `MenuImportTestFixtures.SeedTwoTenantsAsync` for tenant-aware tests.
- C# admin routes are already `/api/restaurants/{restaurantId:guid}/admin/menu/...` and customer routes `/api/restaurants/{restaurantId:guid}/menu`.
- C# entities implement `ITenantScoped`; `RestaurantId` is auto-stamped by `AppDbContext.StampTenant` from `ICurrentTenant`.
- Python pipeline serializes Pydantic models with snake_case keys; the C# job stores the Python response as opaque JSONB in `result_json`. The frontend reads `job.result.items` directly; the existing flow proves snake_case keys travel through unchanged — match that style for `hierarchy`.
- Python tests use plain `pytest` + `pytest-asyncio`; mock anthropic/gemini clients with `unittest.mock`.
- Frontend uses Vitest + Pinia; existing store tests mock `menuImportApi` via `vi.mock`.

**Git:** User-managed. Each task ends with a Checkpoint listing files + a suggested commit message — do not run `git add/commit` automatically.

---

## File Structure

**Create:**
- `backend/RestaurantApi/Models/Subcategory.cs`
- `backend/RestaurantApi/DTO/Requests/BulkCreateHierarchyRequest.cs` (namespace `RestaurantApi.DTOs.Requests`)
- `backend/RestaurantApi/DTO/Responses/BulkCreateHierarchyResponse.cs` (namespace `RestaurantApi.DTOs.Responses`)
- `backend/RestaurantApi/Migrations/<timestamp>_AddSubcategories.cs` (EF-generated)
- `backend/RestaurantApi.Tests/SubcategoryEntityTests.cs`
- `backend/RestaurantApi.Tests/BulkCreateHierarchyTests.cs`
- `backend/RestaurantApi.Tests/MenuReadHierarchyTests.cs`
- `backend/ai-scripts/models/menu_organizer.py`
- `backend/ai-scripts/services/menu_organizer.py`
- `backend/ai-scripts/tests/test_menu_organizer_models.py`
- `backend/ai-scripts/tests/test_menu_organizer_fallback.py`
- `backend/ai-scripts/tests/test_claude_menu_organizer.py`
- `backend/ai-scripts/tests/test_gemini_menu_organizer.py`
- `backend/ai-scripts/tests/test_menu_organizer_factory.py`
- `backend/ai-scripts/tests/test_pdf_job_request_locale.py`
- `frontend-admin/src/components/menu-import/StepReviewHierarchy.vue`
- `frontend-admin/src/__tests__/StepReviewHierarchy.spec.ts`

**Modify:**
- `backend/RestaurantApi/Models/Product.cs` (add `SubcategoryId`)
- `backend/RestaurantApi/Data/AppDbContext.cs` (Subcategory mapping + tenant filter)
- `backend/RestaurantApi/Controllers/MenuManagementController.cs` (new endpoint + admin GET shape)
- `backend/RestaurantApi/Controllers/MenuController.cs` (customer GET shape)
- `backend/RestaurantApi/DTO/Responses/AdminMenuResponse.cs`, `MenuCategoryResponse.cs`, `MenuProductResponse.cs`, `AdminProductResponse.cs` (add subcategory fields)
- `backend/ai-scripts/models/menu_extraction.py` (`MenuExtractionResponse.hierarchy`, `PdfJobRequest.locale`, `schema_version=2`)
- `backend/ai-scripts/services/pdf_menu_extraction_service.py` (call organizer after `dedup_items`)
- `backend/ai-scripts/services/menu_extraction_service.py` (call organizer after photo extraction)
- `backend/ai-scripts/routers/menu_extraction.py` (wire factory; add `locale` form field on photo route)
- `backend/ai-scripts/tests/test_pdf_pipeline_integration.py` (assert hierarchy)
- `backend/ai-scripts/tests/test_menu_extraction_router.py` (assert hierarchy on photo route)
- `shared/types/menu-import.ts` (hierarchy types, bump `BulkCreate*` for hierarchy)
- `frontend-admin/src/services/menuImportApi.ts` (`bulkCreateHierarchy`)
- `frontend-admin/src/stores/useMenuImportStore.ts` (hierarchy state, mutations, save serialization)
- `frontend-admin/src/__tests__/useMenuImportStore.spec.ts` (cover new behavior)
- `frontend-admin/src/views/MenuImportView.vue` (route step 3 to new component when hierarchy present)

---

## Task 1: C# Subcategory model, EF mapping, migration

**Files:**
- Create: `backend/RestaurantApi/Models/Subcategory.cs`
- Create: `backend/RestaurantApi.Tests/SubcategoryEntityTests.cs`
- Modify: `backend/RestaurantApi/Models/Product.cs`
- Modify: `backend/RestaurantApi/Data/AppDbContext.cs`
- Create: `backend/RestaurantApi/Migrations/<timestamp>_AddSubcategories.cs` (EF-generated)

- [ ] **Step 1: Write the failing entity tests**

`backend/RestaurantApi.Tests/SubcategoryEntityTests.cs`:
```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Tests.Helpers;
using RestaurantApi.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

public class SubcategoryEntityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private MenuImportTestFactory _factory = null!;
    private Guid _restaurantId;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory { PostgresConnectionString = _pg.GetConnectionString() };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var (restA, _, _, _) = await MenuImportTestFixtures.SeedTwoTenantsAsync(_factory);
        _restaurantId = restA;
    }

    public async Task DisposeAsync()
    {
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task RoundTrip_subcategory_persists_with_parent_category()
    {
        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cat = new Category { Id = Guid.NewGuid(), Name = "Pizza" };
        var sub = new Subcategory { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Classic", SortOrder = 0 };
        db.Categories.Add(cat);
        db.Subcategories.Add(sub);
        await db.SaveChangesAsync();

        var loaded = await db.Subcategories.SingleAsync(s => s.Id == sub.Id);
        loaded.Name.Should().Be("Classic");
        loaded.CategoryId.Should().Be(cat.Id);
        loaded.RestaurantId.Should().Be(_restaurantId);
    }

    [Fact]
    public async Task Product_subcategory_id_set_null_on_subcategory_delete()
    {
        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cat = new Category { Id = Guid.NewGuid(), Name = "Pizza" };
        var sub = new Subcategory { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Classic" };
        var prod = new Product { Id = Guid.NewGuid(), CategoryId = cat.Id, SubcategoryId = sub.Id, Name = "Margherita", Price = 28m };
        db.AddRange(cat, sub, prod);
        await db.SaveChangesAsync();

        db.Subcategories.Remove(sub);
        await db.SaveChangesAsync();

        var reloaded = await db.Products.SingleAsync(p => p.Id == prod.Id);
        reloaded.SubcategoryId.Should().BeNull();
        reloaded.CategoryId.Should().Be(cat.Id);
    }

    [Fact]
    public async Task Unique_index_blocks_duplicate_subcategory_name_within_category()
    {
        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cat = new Category { Id = Guid.NewGuid(), Name = "Pizza" };
        db.Categories.Add(cat);
        db.Subcategories.Add(new Subcategory { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Classic" });
        db.Subcategories.Add(new Subcategory { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Classic" });

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
```

- [ ] **Step 2: Run, verify RED**

```
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter "FullyQualifiedName~SubcategoryEntityTests"
```
Expected: compile failure (`Subcategory` not found, `SubcategoryId` not on `Product`).

- [ ] **Step 3: Add `Subcategory` model**

`backend/RestaurantApi/Models/Subcategory.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace RestaurantApi.Models;

public class Subcategory : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RestaurantId { get; set; }
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    [Required]
    public string Name { get; set; } = string.Empty;

    public int? SortOrder { get; set; }
    public string? ImageUrl { get; set; }

    public ICollection<Product> Products { get; set; } = [];
}
```

- [ ] **Step 4: Add `SubcategoryId` to `Product`**

Append to `backend/RestaurantApi/Models/Product.cs` after the `CategoryId`/`Category` pair:
```csharp
public Guid? SubcategoryId { get; set; }
public Subcategory? Subcategory { get; set; }
```

- [ ] **Step 5: Add EF mapping and tenant filter**

In `backend/RestaurantApi/Data/AppDbContext.cs`:

Add the DbSet near the existing `Categories`/`Products`:
```csharp
public DbSet<Subcategory> Subcategories => Set<Subcategory>();
```

Inside `OnModelCreating`, after the `Category` mapping block, add:
```csharp
modelBuilder.Entity<Subcategory>(e =>
{
    e.ToTable("subcategories");
    e.Property(s => s.Id).HasColumnName("id");
    e.Property(s => s.RestaurantId).HasColumnName("restaurant_id");
    e.Property(s => s.CategoryId).HasColumnName("category_id");
    e.Property(s => s.Name).HasColumnName("name");
    e.Property(s => s.SortOrder).HasColumnName("sort_order");
    e.Property(s => s.ImageUrl).HasColumnName("image_url");
    e.HasIndex(s => s.RestaurantId);
    e.HasIndex(s => new { s.RestaurantId, s.CategoryId, s.Name }).IsUnique();

    e.HasOne(s => s.Category)
     .WithMany()
     .HasForeignKey(s => s.CategoryId)
     .OnDelete(DeleteBehavior.Cascade);
});
```

Inside the existing `modelBuilder.Entity<Product>(e => { ... })` block, after the `CategoryId` property mapping:
```csharp
e.Property(p => p.SubcategoryId).HasColumnName("subcategory_id");
e.HasOne(p => p.Subcategory)
 .WithMany(s => s.Products)
 .HasForeignKey(p => p.SubcategoryId)
 .OnDelete(DeleteBehavior.SetNull)
 .IsRequired(false);
```

Add the tenant query filter alongside the existing ones near the bottom of `OnModelCreating`:
```csharp
modelBuilder.Entity<Subcategory>().HasQueryFilter(x => _tenant.IsResolutionSuppressed || x.RestaurantId == _tenant.RestaurantId);
```

- [ ] **Step 6: Generate the migration**

```
dotnet ef migrations add AddSubcategories --project backend/RestaurantApi --output-dir Migrations
```

Open the generated migration file. Verify `Up()`:
- Creates `subcategories` (id, restaurant_id, category_id, name, sort_order, image_url) with composite unique index `(restaurant_id, category_id, name)` and FK to categories ON DELETE CASCADE.
- Adds nullable `subcategory_id` to `products` with FK to subcategories ON DELETE SET NULL.

- [ ] **Step 7: Run, verify GREEN**

```
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter "FullyQualifiedName~SubcategoryEntityTests"
```
Expected: 3 passing.

- [ ] **Step 8: Checkpoint**

Files ready:
- `backend/RestaurantApi/Models/Subcategory.cs`
- `backend/RestaurantApi/Models/Product.cs`
- `backend/RestaurantApi/Data/AppDbContext.cs`
- `backend/RestaurantApi/Migrations/<timestamp>_AddSubcategories.cs`
- `backend/RestaurantApi/Migrations/<timestamp>_AddSubcategories.Designer.cs`
- `backend/RestaurantApi/Migrations/AppDbContextModelSnapshot.cs`
- `backend/RestaurantApi.Tests/SubcategoryEntityTests.cs`

Suggested commit: `feat(menu): add subcategories table and product.subcategory_id`

---

## Task 2: C# `bulk-create-hierarchy` endpoint + DTOs

**Files:**
- Create: `backend/RestaurantApi/DTO/Requests/BulkCreateHierarchyRequest.cs`
- Create: `backend/RestaurantApi/DTO/Responses/BulkCreateHierarchyResponse.cs`
- Modify: `backend/RestaurantApi/Controllers/MenuManagementController.cs`
- Create: `backend/RestaurantApi.Tests/BulkCreateHierarchyTests.cs`

- [ ] **Step 1: Write failing tests**

`backend/RestaurantApi.Tests/BulkCreateHierarchyTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Tests.Helpers;
using RestaurantApi.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

public class BulkCreateHierarchyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private MenuImportTestFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _restaurantId;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory { PostgresConnectionString = _pg.GetConnectionString() };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var (restA, _, clientA, _) = await MenuImportTestFixtures.SeedTwoTenantsAsync(_factory);
        _restaurantId = restA;
        _client = clientA;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task HappyPath_creates_categories_subcategories_and_products()
    {
        var req = new
        {
            categories = new[]
            {
                new
                {
                    tempId = "c1", name = "Pizza", sortOrder = 0,
                    subcategories = new[] { new { tempId = "s1", name = "Classic", sortOrder = 0 } }
                }
            },
            products = new[]
            {
                new { name = "Margherita", price = 28m, description = "", categoryTempId = "c1", subcategoryTempId = "s1", sortOrder = 0, isAvailable = true }
            }
        };

        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await res.Content.ReadFromJsonAsync<BulkCreateHierarchyResponse>();
        body!.CreatedCategories.Should().HaveCount(1);
        body.CreatedSubcategories.Should().HaveCount(1);
        body.CreatedProducts.Should().HaveCount(1);

        using var scope = _factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var products = await db.Products.ToListAsync();
        products.Should().HaveCount(1);
        products[0].SubcategoryId.Should().NotBeNull();
    }

    [Fact]
    public async Task SubcategoryTempId_referencing_other_category_is_rejected()
    {
        var req = new
        {
            categories = new object[]
            {
                new { tempId = "c1", name = "Pizza", sortOrder = 0,
                      subcategories = new[] { new { tempId = "s1", name = "Classic", sortOrder = 0 } } },
                new { tempId = "c2", name = "Salads", sortOrder = 1, subcategories = Array.Empty<object>() }
            },
            products = new[]
            {
                new { name = "Caesar", price = 15m, description = "", categoryTempId = "c2", subcategoryTempId = "s1", sortOrder = 0, isAvailable = true }
            }
        };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DuplicateCategoryTempIds_rejected()
    {
        var req = new
        {
            categories = new[]
            {
                new { tempId = "c1", name = "Pizza", sortOrder = 0, subcategories = Array.Empty<object>() },
                new { tempId = "c1", name = "Pasta", sortOrder = 1, subcategories = Array.Empty<object>() }
            },
            products = Array.Empty<object>()
        };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EmptyRequest_returns_BadRequest()
    {
        var req = new { categories = Array.Empty<object>(), products = Array.Empty<object>() };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tenant_is_stamped_on_all_created_entities()
    {
        var req = new
        {
            categories = new[]
            {
                new { tempId = "c1", name = "Pizza", sortOrder = 0,
                      subcategories = new[] { new { tempId = "s1", name = "Classic", sortOrder = 0 } } }
            },
            products = new[]
            {
                new { name = "Margherita", price = 28m, description = "", categoryTempId = "c1", subcategoryTempId = "s1", sortOrder = 0, isAvailable = true }
            }
        };
        var res = await _client.PostAsJsonAsync($"/api/restaurants/{_restaurantId}/admin/menu/bulk-create-hierarchy", req);
        res.IsSuccessStatusCode.Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        using var _ = tenant.SuppressResolution();
        (await db.Categories.IgnoreQueryFilters().AllAsync(c => c.RestaurantId == _restaurantId)).Should().BeTrue();
        (await db.Subcategories.IgnoreQueryFilters().AllAsync(s => s.RestaurantId == _restaurantId)).Should().BeTrue();
        (await db.Products.IgnoreQueryFilters().AllAsync(p => p.RestaurantId == _restaurantId)).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run, verify RED**

```
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter "FullyQualifiedName~BulkCreateHierarchyTests"
```
Expected: all five fail (endpoint not found / DTOs missing).

- [ ] **Step 3: Add request DTO**

`backend/RestaurantApi/DTO/Requests/BulkCreateHierarchyRequest.cs`:
```csharp
namespace RestaurantApi.DTOs.Requests;

public class BulkCreateHierarchyRequest
{
    public List<BulkHierarchyCategory> Categories { get; set; } = new();
    public List<BulkHierarchyProduct> Products { get; set; } = new();
}

public class BulkHierarchyCategory
{
    public string TempId { get; set; } = "";
    public string Name { get; set; } = "";
    public int? SortOrder { get; set; }
    public List<BulkHierarchySubcategory> Subcategories { get; set; } = new();
}

public class BulkHierarchySubcategory
{
    public string TempId { get; set; } = "";
    public string Name { get; set; } = "";
    public int? SortOrder { get; set; }
}

public class BulkHierarchyProduct
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string? Description { get; set; }
    public string CategoryTempId { get; set; } = "";
    public string? SubcategoryTempId { get; set; }
    public int SortOrder { get; set; }
    public bool IsAvailable { get; set; } = true;
}
```

- [ ] **Step 4: Add response DTO**

`backend/RestaurantApi/DTO/Responses/BulkCreateHierarchyResponse.cs`:
```csharp
namespace RestaurantApi.DTOs.Responses;

public class BulkCreateHierarchyResponse
{
    public List<TempIdMap> CreatedCategories { get; set; } = new();
    public List<TempIdMap> CreatedSubcategories { get; set; } = new();
    public List<CreatedHierarchyProduct> CreatedProducts { get; set; } = new();
}

public class TempIdMap
{
    public string TempId { get; set; } = "";
    public Guid Id { get; set; }
}

public class CreatedHierarchyProduct
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid CategoryId { get; set; }
    public Guid? SubcategoryId { get; set; }
}
```

- [ ] **Step 5: Implement controller action**

In `backend/RestaurantApi/Controllers/MenuManagementController.cs`, add after the existing `BulkCreate` action and before `NormalizeCategoryName`:
```csharp
[HttpPost("bulk-create-hierarchy")]
public async Task<IActionResult> BulkCreateHierarchy(
    [FromBody] BulkCreateHierarchyRequest request,
    CancellationToken ct)
{
    if (request.Categories.Count == 0 && request.Products.Count == 0)
        return BadRequest(new { message = "At least one category or product is required." });

    var catTempIds = request.Categories.Select(c => c.TempId).ToList();
    if (catTempIds.Any(string.IsNullOrWhiteSpace))
        return BadRequest(new { message = "Every category needs a tempId." });
    if (catTempIds.Distinct().Count() != catTempIds.Count)
        return BadRequest(new { message = "Duplicate category tempId." });

    var subTempIds = request.Categories.SelectMany(c => c.Subcategories.Select(s => s.TempId)).ToList();
    if (subTempIds.Any(string.IsNullOrWhiteSpace))
        return BadRequest(new { message = "Every subcategory needs a tempId." });
    if (subTempIds.Distinct().Count() != subTempIds.Count)
        return BadRequest(new { message = "Duplicate subcategory tempId." });

    var subToCat = new Dictionary<string, string>();
    foreach (var c in request.Categories)
        foreach (var s in c.Subcategories)
            subToCat[s.TempId] = c.TempId;

    var catTempSet = catTempIds.ToHashSet();
    foreach (var p in request.Products)
    {
        if (!catTempSet.Contains(p.CategoryTempId))
            return BadRequest(new { message = $"Product references unknown categoryTempId '{p.CategoryTempId}'." });
        if (p.SubcategoryTempId is not null)
        {
            if (!subToCat.TryGetValue(p.SubcategoryTempId, out var parent) || parent != p.CategoryTempId)
                return BadRequest(new { message = $"Subcategory '{p.SubcategoryTempId}' does not belong to category '{p.CategoryTempId}'." });
        }
        var nv = ProductValidator.ValidateName(p.Name);
        if (!nv.IsValid) return BadRequest(new { field = "name", message = nv.Error });
        var pv = ProductValidator.ValidatePrice(p.Price);
        if (!pv.IsValid) return BadRequest(new { field = "price", message = pv.Error });
    }

    foreach (var c in request.Categories)
    {
        var nv = ProductValidator.ValidateName(c.Name);
        if (!nv.IsValid) return BadRequest(new { field = "category.name", message = nv.Error });
        foreach (var s in c.Subcategories)
        {
            var sv = ProductValidator.ValidateName(s.Name);
            if (!sv.IsValid) return BadRequest(new { field = "subcategory.name", message = sv.Error });
        }
    }

    await using var tx = await _db.Database.BeginTransactionAsync(ct);

    var catTempToId = new Dictionary<string, Guid>();
    foreach (var c in request.Categories)
    {
        var entity = new Category
        {
            Id = Guid.NewGuid(),
            Name = c.Name.Trim(),
            SortOrder = c.SortOrder
        };
        _db.Categories.Add(entity);
        catTempToId[c.TempId] = entity.Id;
    }
    await _db.SaveChangesAsync(ct);

    var subTempToId = new Dictionary<string, Guid>();
    foreach (var c in request.Categories)
    {
        foreach (var s in c.Subcategories)
        {
            var entity = new Subcategory
            {
                Id = Guid.NewGuid(),
                CategoryId = catTempToId[c.TempId],
                Name = s.Name.Trim(),
                SortOrder = s.SortOrder
            };
            _db.Subcategories.Add(entity);
            subTempToId[s.TempId] = entity.Id;
        }
    }
    if (subTempToId.Count > 0) await _db.SaveChangesAsync(ct);

    var createdProducts = new List<CreatedHierarchyProduct>();
    foreach (var p in request.Products)
    {
        var entity = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = catTempToId[p.CategoryTempId],
            SubcategoryId = p.SubcategoryTempId is null ? null : subTempToId[p.SubcategoryTempId],
            Name = p.Name.Trim(),
            Description = p.Description ?? string.Empty,
            Price = p.Price,
            IsAvailable = p.IsAvailable,
            SortOrder = p.SortOrder
        };
        _db.Products.Add(entity);
        createdProducts.Add(new CreatedHierarchyProduct
        {
            Id = entity.Id,
            Name = entity.Name,
            CategoryId = entity.CategoryId,
            SubcategoryId = entity.SubcategoryId
        });
    }
    if (createdProducts.Count > 0) await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    _logger.LogInformation(
        "Bulk-created hierarchy: {Categories} categories, {Subcategories} subcategories, {Products} products",
        catTempToId.Count, subTempToId.Count, createdProducts.Count);

    return Ok(new BulkCreateHierarchyResponse
    {
        CreatedCategories = catTempToId.Select(kv => new TempIdMap { TempId = kv.Key, Id = kv.Value }).ToList(),
        CreatedSubcategories = subTempToId.Select(kv => new TempIdMap { TempId = kv.Key, Id = kv.Value }).ToList(),
        CreatedProducts = createdProducts
    });
}
```

The existing controller already imports `RestaurantApi.DTOs.Requests` and `RestaurantApi.DTOs.Responses`; no additional `using`s are needed.

- [ ] **Step 6: Run, verify GREEN**

```
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter "FullyQualifiedName~BulkCreateHierarchyTests"
```
Expected: 5 passing.

- [ ] **Step 7: Checkpoint**

Files ready:
- `backend/RestaurantApi/DTO/Requests/BulkCreateHierarchyRequest.cs`
- `backend/RestaurantApi/DTO/Responses/BulkCreateHierarchyResponse.cs`
- `backend/RestaurantApi/Controllers/MenuManagementController.cs`
- `backend/RestaurantApi.Tests/BulkCreateHierarchyTests.cs`

Suggested commit: `feat(menu): bulk-create-hierarchy endpoint`

---

## Task 3: C# GET menu returns subcategories + product.subcategoryId

**Files:**
- Modify: `backend/RestaurantApi/DTO/Responses/MenuCategoryResponse.cs`, `MenuProductResponse.cs`, `AdminMenuResponse.cs`, `AdminProductResponse.cs`
- Modify: `backend/RestaurantApi/Controllers/MenuController.cs`
- Modify: `backend/RestaurantApi/Controllers/MenuManagementController.cs` (the `GetAdminMenu` action)
- Create: `backend/RestaurantApi.Tests/MenuReadHierarchyTests.cs`

- [ ] **Step 1: Write the failing tests**

`backend/RestaurantApi.Tests/MenuReadHierarchyTests.cs`:
```csharp
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantApi.Data;
using RestaurantApi.Models;
using RestaurantApi.Tests.Helpers;
using RestaurantApi.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace RestaurantApi.Tests;

public class MenuReadHierarchyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private MenuImportTestFactory _factory = null!;
    private HttpClient _adminClient = null!;
    private Guid _restaurantId;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        _factory = new MenuImportTestFactory { PostgresConnectionString = _pg.GetConnectionString() };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }
        var (restA, _, clientA, _) = await MenuImportTestFixtures.SeedTwoTenantsAsync(_factory);
        _restaurantId = restA;
        _adminClient = clientA;

        using var seedScope = _factory.Services.CreateScope();
        var tenant = seedScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetRestaurant(_restaurantId);
        var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cat = new Category { Id = Guid.NewGuid(), Name = "Pizza", SortOrder = 0 };
        var s1 = new Subcategory { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Classic", SortOrder = 0 };
        var s2 = new Subcategory { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Specialty", SortOrder = 1 };
        seedDb.AddRange(cat, s1, s2,
            new Product { Id = Guid.NewGuid(), CategoryId = cat.Id, Name = "Calzone", Price = 30m, IsAvailable = true },
            new Product { Id = Guid.NewGuid(), CategoryId = cat.Id, SubcategoryId = s1.Id, Name = "Margherita", Price = 28m, IsAvailable = true },
            new Product { Id = Guid.NewGuid(), CategoryId = cat.Id, SubcategoryId = s2.Id, Name = "Truffle", Price = 45m, IsAvailable = true });
        await seedDb.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        _factory.Dispose();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task CustomerMenu_returns_subcategories_per_category_and_subcategoryId_per_product()
    {
        using var anon = _factory.CreateClient();
        var res = await anon.GetAsync($"/api/restaurants/{_restaurantId}/menu");
        res.IsSuccessStatusCode.Should().BeTrue();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var category = doc.RootElement.GetProperty("categories")[0];
        category.GetProperty("subcategories").GetArrayLength().Should().Be(2);
        var products = category.GetProperty("products");
        var withSub = products.EnumerateArray()
            .Count(p => p.TryGetProperty("subcategoryId", out var v) && v.ValueKind == JsonValueKind.String);
        withSub.Should().Be(2);
    }

    [Fact]
    public async Task AdminMenu_returns_subcategories_per_category_and_subcategoryId_per_product()
    {
        var res = await _adminClient.GetAsync($"/api/restaurants/{_restaurantId}/admin/menu");
        res.IsSuccessStatusCode.Should().BeTrue();
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var category = doc.RootElement.GetProperty("categories")[0];
        category.GetProperty("subcategories").GetArrayLength().Should().Be(2);
        var products = category.GetProperty("products");
        var withSub = products.EnumerateArray()
            .Count(p => p.TryGetProperty("subcategoryId", out var v) && v.ValueKind == JsonValueKind.String);
        withSub.Should().Be(2);
    }
}
```

- [ ] **Step 2: Run, verify RED**

```
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter "FullyQualifiedName~MenuReadHierarchyTests"
```

- [ ] **Step 3: Add subcategory fields to response DTOs**

In `backend/RestaurantApi/DTO/Responses/MenuCategoryResponse.cs` add `Subcategories` and a sibling `MenuSubcategoryResponse`. Add `Guid? SubcategoryId` to `MenuProductResponse`. Mirror the same additions in the admin DTOs (`AdminMenuResponse.cs` / `AdminMenuCategory` and `AdminProductResponse.cs`).

- [ ] **Step 4: Update customer `MenuController.GetMenu`**

Replace the projection so each category includes its subcategories and each product includes `SubcategoryId`. Use `_context.Subcategories.Where(s => s.CategoryId == c.Id).OrderBy(s => s.SortOrder).ThenBy(s => s.Name)` for the subcategory projection.

- [ ] **Step 5: Update admin `GetAdminMenu`**

Update the projection in `MenuManagementController.GetAdminMenu` to populate `Subcategories` and `SubcategoryId`.

- [ ] **Step 6: Run, verify GREEN**

```
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj --filter "FullyQualifiedName~MenuReadHierarchyTests"
```

- [ ] **Step 7: Checkpoint**

Suggested commit: `feat(menu): expose subcategories and product.subcategoryId on menu reads`

---

## Task 4: Python organizer Pydantic models

**Files:**
- Create: `backend/ai-scripts/models/menu_organizer.py`
- Create: `backend/ai-scripts/tests/test_menu_organizer_models.py`

- [ ] **Step 1: Write failing tests**

```python
# backend/ai-scripts/tests/test_menu_organizer_models.py
import pytest
from pydantic import ValidationError
from models.menu_organizer import (
    ItemRef, OrganizedSubcategory, OrganizedCategory, OrganizerResult,
)

def test_itemref_confidence_range():
    ItemRef(source_page=0, source_index=1, confidence=0.5)
    with pytest.raises(ValidationError):
        ItemRef(source_page=0, source_index=1, confidence=1.5)

def test_organizer_result_minimum_valid():
    r = OrganizerResult(source="ai", categories=[], uncategorized_item_refs=[], model_notes=None)
    assert r.source == "ai"

def test_organizer_result_rejects_bad_source():
    with pytest.raises(ValidationError):
        OrganizerResult(source="random", categories=[], uncategorized_item_refs=[])

def test_organized_subcategory_requires_name_and_sort_order():
    OrganizedSubcategory(name="Classic", sort_order=0, item_refs=[])
    with pytest.raises(ValidationError):
        OrganizedSubcategory(name="", sort_order=0, item_refs=[])

def test_organized_category_full_shape():
    cat = OrganizedCategory(
        name="Pizza",
        sort_order=0,
        subcategories=[OrganizedSubcategory(name="Classic", sort_order=0, item_refs=[])],
        direct_item_refs=[ItemRef(source_page=0, source_index=2, confidence=0.6)],
    )
    assert cat.name == "Pizza"
    assert len(cat.subcategories) == 1
```

- [ ] **Step 2: Run, verify RED**

```
pytest backend/ai-scripts/tests/test_menu_organizer_models.py -v
```

- [ ] **Step 3: Implement models**

```python
# backend/ai-scripts/models/menu_organizer.py
from typing import List, Literal, Optional
from pydantic import BaseModel, Field

class ItemRef(BaseModel):
    source_page: int = Field(ge=0)
    source_index: int = Field(ge=0)
    confidence: float = Field(ge=0.0, le=1.0)

class OrganizedSubcategory(BaseModel):
    name: str = Field(min_length=1, max_length=80)
    sort_order: int = Field(ge=0)
    item_refs: List[ItemRef] = []

class OrganizedCategory(BaseModel):
    name: str = Field(min_length=1, max_length=80)
    sort_order: int = Field(ge=0)
    subcategories: List[OrganizedSubcategory] = []
    direct_item_refs: List[ItemRef] = []

class OrganizerResult(BaseModel):
    source: Literal["ai", "hint-fallback"]
    categories: List[OrganizedCategory] = []
    uncategorized_item_refs: List[ItemRef] = []
    model_notes: Optional[str] = None
```

- [ ] **Step 4: Run, verify GREEN**

- [ ] **Step 5: Checkpoint**

Suggested commit: `feat(ai): organizer pydantic models`

---

## Task 5: Python hint-fallback builder + organizer Protocol + Fake

**Files:**
- Create: `backend/ai-scripts/services/menu_organizer.py`
- Create: `backend/ai-scripts/tests/test_menu_organizer_fallback.py`

- [ ] **Step 1: Write failing tests**

```python
# backend/ai-scripts/tests/test_menu_organizer_fallback.py
from models.menu_extraction import ExtractedMenuItem
from services.menu_organizer import OrganizerOptions, build_hint_fallback

def _item(page, idx, hint, name="Item", price=10.0):
    return ExtractedMenuItem(name=name, price=price, source_page=page, source_index=idx,
                             description=None, category_hint=hint)

def test_hint_fallback_groups_by_distinct_hints():
    items = [
        _item(0, 0, "Pizza", "Margherita"),
        _item(0, 1, "Pizza", "Quattro"),
        _item(0, 2, "Salads", "Caesar"),
    ]
    out = build_hint_fallback(items, OrganizerOptions())
    assert out.source == "hint-fallback"
    names = {c.name for c in out.categories}
    assert names == {"Pizza", "Salads"}
    assert sum(len(c.direct_item_refs) for c in out.categories) == 3
    for c in out.categories:
        assert c.subcategories == []

def test_hint_fallback_noise_hints_become_uncategorized():
    items = [_item(0, 0, "OTHERS", "X"), _item(0, 1, None, "Y")]
    out = build_hint_fallback(items, OrganizerOptions())
    assert len(out.categories) == 0
    assert len(out.uncategorized_item_refs) == 2
    assert all(r.confidence == 0.0 for r in out.uncategorized_item_refs)

def test_hint_fallback_strips_diacritics_for_blocklist_matching():
    items = [_item(0, 0, "Altele", "Z")]
    out = build_hint_fallback(items, OrganizerOptions())
    assert len(out.uncategorized_item_refs) == 1

def test_hint_fallback_short_hint_is_noise():
    items = [_item(0, 0, "X", "A")]
    out = build_hint_fallback(items, OrganizerOptions())
    assert len(out.categories) == 0
    assert len(out.uncategorized_item_refs) == 1
```

- [ ] **Step 2: Run, verify RED**

```
pytest backend/ai-scripts/tests/test_menu_organizer_fallback.py -v
```

- [ ] **Step 3: Implement options, protocol, fallback**

```python
# backend/ai-scripts/services/menu_organizer.py
from __future__ import annotations

import unicodedata
from dataclasses import dataclass, field
from typing import List, Protocol, Sequence

from models.menu_extraction import ExtractedMenuItem
from models.menu_organizer import (
    ItemRef, OrganizedCategory, OrganizedSubcategory, OrganizerResult,
)


class MenuOrganizerError(Exception):
    """Raised when the underlying organizer provider fails."""


@dataclass
class OrganizerOptions:
    max_items_per_subcategory: int = 12
    min_items_to_introduce_subcategory: int = 5
    noise_hint_blocklist: List[str] = field(
        default_factory=lambda: ["others", "menu", "produse", "altele"]
    )
    locale: str = "ro-RO"


def _normalise(s: str) -> str:
    nfd = unicodedata.normalize("NFD", s)
    stripped = "".join(ch for ch in nfd if unicodedata.category(ch) != "Mn")
    return stripped.strip().lower()


def _is_noise_hint(hint: str | None, opts: OrganizerOptions) -> bool:
    if hint is None:
        return True
    n = _normalise(hint)
    if len(n) < 3:
        return True
    return n in {_normalise(b) for b in opts.noise_hint_blocklist}


def build_hint_fallback(
    items: Sequence[ExtractedMenuItem],
    opts: OrganizerOptions,
) -> OrganizerResult:
    groups: dict[str, list[ExtractedMenuItem]] = {}
    uncategorized: list[ItemRef] = []
    for it in items:
        if _is_noise_hint(it.category_hint, opts):
            uncategorized.append(
                ItemRef(source_page=it.source_page, source_index=it.source_index, confidence=0.0)
            )
            continue
        key = _normalise(it.category_hint or "")
        groups.setdefault(key, []).append(it)

    categories: list[OrganizedCategory] = []
    for sort_order, (_, group_items) in enumerate(groups.items()):
        display = (group_items[0].category_hint or "").strip()
        categories.append(OrganizedCategory(
            name=display,
            sort_order=sort_order,
            subcategories=[],
            direct_item_refs=[
                ItemRef(source_page=it.source_page, source_index=it.source_index, confidence=0.5)
                for it in group_items
            ],
        ))

    return OrganizerResult(
        source="hint-fallback",
        categories=categories,
        uncategorized_item_refs=uncategorized,
        model_notes=None,
    )


class MenuOrganizer(Protocol):
    model_name: str

    async def organize(
        self,
        items: Sequence[ExtractedMenuItem],
        opts: OrganizerOptions,
    ) -> OrganizerResult: ...


class FakeMenuOrganizer:
    """Test stand-in. Returns canned result or raises."""

    def __init__(
        self,
        canned: OrganizerResult | None = None,
        raises: Exception | None = None,
        model_name: str = "fake-organizer",
    ):
        if (canned is None) == (raises is None):
            raise ValueError("Provide exactly one of canned or raises.")
        self._canned = canned
        self._raises = raises
        self.model_name = model_name

    async def organize(self, items, opts):
        if self._raises:
            raise self._raises
        return self._canned  # type: ignore[return-value]
```

- [ ] **Step 4: Run, verify GREEN**

- [ ] **Step 5: Checkpoint**

Suggested commit: `feat(ai): hint-fallback organizer and protocol`

---

## Task 6: Python `ClaudeMenuOrganizer`

**Files:**
- Modify: `backend/ai-scripts/services/menu_organizer.py`
- Create: `backend/ai-scripts/tests/test_claude_menu_organizer.py`

- [ ] **Step 1: Write failing tests**

```python
# backend/ai-scripts/tests/test_claude_menu_organizer.py
import json
import pytest
from unittest.mock import AsyncMock, MagicMock

from models.menu_extraction import ExtractedMenuItem
from services.menu_organizer import ClaudeMenuOrganizer, OrganizerOptions, MenuOrganizerError


def _items(n=3):
    return [
        ExtractedMenuItem(
            name=f"Item{i}", price=10 + i, source_page=0, source_index=i,
            description=None, category_hint="Pizza",
        )
        for i in range(n)
    ]


def _tool_use_response(payload):
    block = MagicMock()
    block.type = "tool_use"
    block.name = "organize_menu"
    block.input = payload
    resp = MagicMock()
    resp.content = [block]
    return resp


@pytest.mark.asyncio
async def test_claude_organizer_returns_valid_result():
    payload = {
        "source": "ai",
        "categories": [{
            "name": "Pizza", "sort_order": 0, "subcategories": [],
            "direct_item_refs": [
                {"source_page": 0, "source_index": 0, "confidence": 0.9},
                {"source_page": 0, "source_index": 1, "confidence": 0.85},
                {"source_page": 0, "source_index": 2, "confidence": 0.8},
            ],
        }],
        "uncategorized_item_refs": [],
        "model_notes": None,
    }
    client = MagicMock()
    client.messages.create = AsyncMock(return_value=_tool_use_response(payload))
    org = ClaudeMenuOrganizer(client=client, model_name="claude-test")
    out = await org.organize(_items(3), OrganizerOptions())
    assert out.source == "ai"
    assert len(out.categories) == 1
    client.messages.create.assert_awaited_once()


@pytest.mark.asyncio
async def test_claude_organizer_raises_on_provider_error():
    client = MagicMock()
    client.messages.create = AsyncMock(side_effect=RuntimeError("boom"))
    org = ClaudeMenuOrganizer(client=client, model_name="claude-test")
    with pytest.raises(MenuOrganizerError):
        await org.organize(_items(3), OrganizerOptions())


@pytest.mark.asyncio
async def test_claude_organizer_raises_on_schema_invalid_payload():
    payload = {"source": "ai", "categories": "not-a-list"}
    client = MagicMock()
    client.messages.create = AsyncMock(return_value=_tool_use_response(payload))
    org = ClaudeMenuOrganizer(client=client, model_name="claude-test")
    with pytest.raises(MenuOrganizerError):
        await org.organize(_items(3), OrganizerOptions())


@pytest.mark.asyncio
async def test_locale_is_threaded_into_prompt():
    payload = {"source": "ai", "categories": [], "uncategorized_item_refs": [], "model_notes": None}
    client = MagicMock()
    client.messages.create = AsyncMock(return_value=_tool_use_response(payload))
    org = ClaudeMenuOrganizer(client=client, model_name="claude-test")
    await org.organize(_items(2), OrganizerOptions(locale="en-US"))
    call_kwargs = client.messages.create.call_args.kwargs
    full_text = json.dumps(call_kwargs, default=str)
    assert "en-US" in full_text
```

- [ ] **Step 2: Run, verify RED**

- [ ] **Step 3: Implement `ClaudeMenuOrganizer`**

Append to `backend/ai-scripts/services/menu_organizer.py`:
```python
import os
from typing import Any

import httpx
from anthropic import AsyncAnthropic, APIStatusError
from pydantic import ValidationError


ORGANIZER_TOOL_SCHEMA: dict[str, Any] = {
    "name": "organize_menu",
    "description": "Emit the structured category/subcategory hierarchy.",
    "input_schema": {
        "type": "object",
        "properties": {
            "source": {"type": "string", "enum": ["ai"]},
            "categories": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "name": {"type": "string"},
                        "sort_order": {"type": "integer", "minimum": 0},
                        "subcategories": {
                            "type": "array",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "name": {"type": "string"},
                                    "sort_order": {"type": "integer", "minimum": 0},
                                    "item_refs": {"type": "array", "items": {"$ref": "#/definitions/itemRef"}},
                                },
                                "required": ["name", "sort_order", "item_refs"],
                            },
                        },
                        "direct_item_refs": {"type": "array", "items": {"$ref": "#/definitions/itemRef"}},
                    },
                    "required": ["name", "sort_order", "subcategories", "direct_item_refs"],
                },
            },
            "uncategorized_item_refs": {"type": "array", "items": {"$ref": "#/definitions/itemRef"}},
            "model_notes": {"type": ["string", "null"]},
        },
        "required": ["source", "categories", "uncategorized_item_refs"],
        "definitions": {
            "itemRef": {
                "type": "object",
                "properties": {
                    "source_page": {"type": "integer", "minimum": 0},
                    "source_index": {"type": "integer", "minimum": 0},
                    "confidence": {"type": "number", "minimum": 0, "maximum": 1},
                },
                "required": ["source_page", "source_index", "confidence"],
            },
        },
    },
}

ORGANIZER_SYSTEM_PROMPT = (
    "You organize extracted restaurant menu items into a coherent "
    "Category -> Subcategory -> Item hierarchy. Use the provided tool to return the result."
)


def _build_user_prompt(items: Sequence[ExtractedMenuItem], opts: OrganizerOptions) -> str:
    lines = [
        "Organize these items into categories and (optionally) subcategories.",
        "",
        f"Output language: {opts.locale}",
        f"Subcategories: introduce only when a category has at least "
        f"{opts.min_items_to_introduce_subcategory} items AND items split into 2+ natural clusters. "
        "A single subcategory containing every item of a category is meaningless — "
        "put those items in direct_item_refs instead.",
        f"Hints to treat as absent (noise): {opts.noise_hint_blocklist}, plus any hint shorter than 3 chars.",
        "",
        "Rules:",
        "1. Trust hints first. Items sharing a clear category_hint go into that category, "
        "   using the hint verbatim (case-normalised) as the category name.",
        "2. For items with noise/absent hints, infer category from item name/description.",
        "3. Confidence: 1.0 = explicit match; 0.7-0.9 = strong; 0.4-0.6 = plausible; "
        "   below 0.4 = put in uncategorized_item_refs.",
        "",
        "Items (page,index | hint | name | price):",
    ]
    for it in items:
        lines.append(f"{it.source_page},{it.source_index} | {it.category_hint or '-'} | {it.name} | {it.price}")
    return "\n".join(lines)


class ClaudeMenuOrganizer:
    def __init__(
        self,
        api_key: str | None = None,
        model_name: str | None = None,
        client: AsyncAnthropic | None = None,
    ):
        resolved = api_key or os.environ.get("ANTHROPIC_API_KEY")
        if not resolved and client is None:
            raise RuntimeError("ANTHROPIC_API_KEY required for ClaudeMenuOrganizer.")
        self.model_name = model_name or os.environ.get("ANTHROPIC_ORGANIZER_MODEL", "claude-sonnet-4-6")
        self._client = client or AsyncAnthropic(api_key=resolved)

    async def organize(
        self,
        items: Sequence[ExtractedMenuItem],
        opts: OrganizerOptions,
    ) -> OrganizerResult:
        try:
            resp = await self._client.messages.create(
                model=self.model_name,
                max_tokens=8192,
                system=ORGANIZER_SYSTEM_PROMPT,
                tools=[ORGANIZER_TOOL_SCHEMA],
                tool_choice={"type": "tool", "name": "organize_menu"},
                messages=[{"role": "user", "content": _build_user_prompt(items, opts)}],
                timeout=httpx.Timeout(60.0, connect=10.0),
            )
        except APIStatusError as exc:
            raise MenuOrganizerError(f"Anthropic API error: {exc.status_code}") from exc
        except Exception as exc:
            raise MenuOrganizerError("Organizer provider call failed") from exc

        for block in resp.content:
            if getattr(block, "type", None) == "tool_use" and getattr(block, "name", None) == "organize_menu":
                payload = block.input
                if not isinstance(payload, dict):
                    raise MenuOrganizerError("Tool-use payload not a dict")
                try:
                    return OrganizerResult.model_validate(payload)
                except ValidationError as exc:
                    raise MenuOrganizerError("Organizer payload failed schema") from exc
        raise MenuOrganizerError("Model did not return a tool_use block")
```

- [ ] **Step 4: Run, verify GREEN**

- [ ] **Step 5: Checkpoint**

Suggested commit: `feat(ai): Claude menu organizer provider`

---

## Task 7: Python `GeminiMenuOrganizer`

**Files:**
- Modify: `backend/ai-scripts/services/menu_organizer.py`
- Create: `backend/ai-scripts/tests/test_gemini_menu_organizer.py`

- [ ] **Step 1: Write failing tests**

```python
# backend/ai-scripts/tests/test_gemini_menu_organizer.py
import json
import pytest
from unittest.mock import AsyncMock, MagicMock

from models.menu_extraction import ExtractedMenuItem
from services.menu_organizer import GeminiMenuOrganizer, OrganizerOptions, MenuOrganizerError


def _items(n=3):
    return [
        ExtractedMenuItem(
            name=f"Item{i}", price=10 + i, source_page=0, source_index=i,
            description=None, category_hint="Pizza",
        )
        for i in range(n)
    ]


def _text_response(payload_dict):
    resp = MagicMock()
    resp.text = json.dumps(payload_dict)
    return resp


@pytest.mark.asyncio
async def test_gemini_organizer_returns_valid_result():
    payload = {
        "source": "ai",
        "categories": [{
            "name": "Pizza", "sort_order": 0, "subcategories": [],
            "direct_item_refs": [
                {"source_page": 0, "source_index": 0, "confidence": 0.9},
                {"source_page": 0, "source_index": 1, "confidence": 0.85},
                {"source_page": 0, "source_index": 2, "confidence": 0.8},
            ],
        }],
        "uncategorized_item_refs": [],
        "model_notes": None,
    }
    client = MagicMock()
    client.aio.models.generate_content = AsyncMock(return_value=_text_response(payload))
    org = GeminiMenuOrganizer(client=client, model_name="gemini-test")
    out = await org.organize(_items(3), OrganizerOptions())
    assert out.source == "ai"
    assert len(out.categories) == 1
    client.aio.models.generate_content.assert_awaited_once()


@pytest.mark.asyncio
async def test_gemini_organizer_raises_on_provider_error():
    client = MagicMock()
    client.aio.models.generate_content = AsyncMock(side_effect=RuntimeError("boom"))
    org = GeminiMenuOrganizer(client=client, model_name="gemini-test")
    with pytest.raises(MenuOrganizerError):
        await org.organize(_items(3), OrganizerOptions())


@pytest.mark.asyncio
async def test_gemini_organizer_raises_on_invalid_json():
    bad = MagicMock()
    bad.text = "this is not json"
    client = MagicMock()
    client.aio.models.generate_content = AsyncMock(return_value=bad)
    org = GeminiMenuOrganizer(client=client, model_name="gemini-test")
    with pytest.raises(MenuOrganizerError):
        await org.organize(_items(3), OrganizerOptions())


@pytest.mark.asyncio
async def test_gemini_locale_threaded_into_contents():
    payload = {"source": "ai", "categories": [], "uncategorized_item_refs": [], "model_notes": None}
    client = MagicMock()
    client.aio.models.generate_content = AsyncMock(return_value=_text_response(payload))
    org = GeminiMenuOrganizer(client=client, model_name="gemini-test")
    await org.organize(_items(2), OrganizerOptions(locale="en-US"))
    call_kwargs = client.aio.models.generate_content.call_args.kwargs
    contents = call_kwargs["contents"]
    joined = "\n".join(str(c) for c in contents)
    assert "en-US" in joined
```

- [ ] **Step 2: Run, verify RED**

- [ ] **Step 3: Implement `GeminiMenuOrganizer`**

Append to `backend/ai-scripts/services/menu_organizer.py`:
```python
import json as _json

from google import genai
from google.genai import types as genai_types


GEMINI_ORGANIZER_RESPONSE_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "source": {"type": "string", "enum": ["ai"]},
        "categories": {"type": "array", "items": {
            "type": "object",
            "properties": {
                "name": {"type": "string"},
                "sort_order": {"type": "integer"},
                "subcategories": {"type": "array", "items": {
                    "type": "object",
                    "properties": {
                        "name": {"type": "string"},
                        "sort_order": {"type": "integer"},
                        "item_refs": {"type": "array", "items": {
                            "type": "object",
                            "properties": {
                                "source_page": {"type": "integer"},
                                "source_index": {"type": "integer"},
                                "confidence": {"type": "number"},
                            },
                            "required": ["source_page", "source_index", "confidence"],
                        }},
                    },
                    "required": ["name", "sort_order", "item_refs"],
                }},
                "direct_item_refs": {"type": "array", "items": {
                    "type": "object",
                    "properties": {
                        "source_page": {"type": "integer"},
                        "source_index": {"type": "integer"},
                        "confidence": {"type": "number"},
                    },
                    "required": ["source_page", "source_index", "confidence"],
                }},
            },
            "required": ["name", "sort_order", "subcategories", "direct_item_refs"],
        }},
        "uncategorized_item_refs": {"type": "array", "items": {
            "type": "object",
            "properties": {
                "source_page": {"type": "integer"},
                "source_index": {"type": "integer"},
                "confidence": {"type": "number"},
            },
            "required": ["source_page", "source_index", "confidence"],
        }},
        "model_notes": {"type": "string", "nullable": True},
    },
    "required": ["source", "categories", "uncategorized_item_refs"],
}


class GeminiMenuOrganizer:
    def __init__(
        self,
        api_key: str | None = None,
        model_name: str | None = None,
        client: genai.Client | None = None,
    ):
        resolved = api_key or os.environ.get("GEMINI_API_KEY")
        if not resolved and client is None:
            raise RuntimeError("GEMINI_API_KEY required for GeminiMenuOrganizer.")
        self.model_name = model_name or os.environ.get("GEMINI_ORGANIZER_MODEL", "gemini-2.0-flash")
        self._client = client or genai.Client(api_key=resolved)

    async def organize(
        self,
        items: Sequence[ExtractedMenuItem],
        opts: OrganizerOptions,
    ) -> OrganizerResult:
        prompt = (
            "Return ONLY the JSON object — no markdown fences or commentary.\n\n"
            + _build_user_prompt(items, opts)
        )
        try:
            resp = await self._client.aio.models.generate_content(
                model=self.model_name,
                contents=[prompt],
                config=genai_types.GenerateContentConfig(
                    response_mime_type="application/json",
                    response_schema=GEMINI_ORGANIZER_RESPONSE_SCHEMA,
                ),
            )
        except Exception as exc:
            raise MenuOrganizerError("Organizer provider call failed") from exc

        raw = getattr(resp, "text", None)
        if not raw:
            raise MenuOrganizerError("Empty response from Gemini organizer")
        try:
            payload = _json.loads(raw)
        except (ValueError, TypeError) as exc:
            raise MenuOrganizerError("Failed to parse Gemini organizer JSON") from exc
        try:
            return OrganizerResult.model_validate(payload)
        except ValidationError as exc:
            raise MenuOrganizerError("Organizer payload failed schema") from exc
```

- [ ] **Step 4: Run, verify GREEN**

- [ ] **Step 5: Checkpoint**

Suggested commit: `feat(ai): Gemini menu organizer provider`

---

## Task 8: Python organizer factory + env wiring

**Files:**
- Modify: `backend/ai-scripts/services/menu_organizer.py`
- Create: `backend/ai-scripts/tests/test_menu_organizer_factory.py`

- [ ] **Step 1: Write failing tests**

```python
# backend/ai-scripts/tests/test_menu_organizer_factory.py
import pytest
from services.menu_organizer import resolve_organizer, ClaudeMenuOrganizer, GeminiMenuOrganizer


def test_factory_returns_gemini_default(monkeypatch):
    monkeypatch.delenv("AI_ORGANIZER_PROVIDER", raising=False)
    monkeypatch.setenv("GEMINI_API_KEY", "fake")
    org = resolve_organizer()
    assert isinstance(org, GeminiMenuOrganizer)


def test_factory_returns_claude(monkeypatch):
    monkeypatch.setenv("AI_ORGANIZER_PROVIDER", "claude")
    monkeypatch.setenv("ANTHROPIC_API_KEY", "fake")
    org = resolve_organizer()
    assert isinstance(org, ClaudeMenuOrganizer)


def test_factory_returns_none_when_disabled(monkeypatch):
    monkeypatch.setenv("AI_ORGANIZER_PROVIDER", "none")
    assert resolve_organizer() is None
```

- [ ] **Step 2: Run, verify RED**

- [ ] **Step 3: Add factory**

Append to `backend/ai-scripts/services/menu_organizer.py`:
```python
def resolve_organizer() -> "MenuOrganizer | None":
    provider = os.environ.get("AI_ORGANIZER_PROVIDER", "gemini").lower()
    if provider == "none":
        return None
    if provider == "claude":
        return ClaudeMenuOrganizer()
    return GeminiMenuOrganizer()
```

- [ ] **Step 4: Run, verify GREEN**

- [ ] **Step 5: Checkpoint**

Suggested commit: `feat(ai): organizer factory and provider selection`

---

## Task 9: Python — `locale` field on request DTOs

**Files:**
- Modify: `backend/ai-scripts/models/menu_extraction.py`
- Modify: `backend/ai-scripts/routers/menu_extraction.py`
- Create: `backend/ai-scripts/tests/test_pdf_job_request_locale.py`

- [ ] **Step 1: Write failing test**

```python
# backend/ai-scripts/tests/test_pdf_job_request_locale.py
from uuid import uuid4
from models.menu_extraction import PdfJobRequest


def test_pdf_job_request_locale_defaults_to_ro():
    req = PdfJobRequest(
        job_id=uuid4(), restaurant_id=uuid4(),
        storage_path="x", progress_callback_url="http://x",
        hmac_secret="abcdefgh",
    )
    assert req.locale == "ro-RO"


def test_pdf_job_request_locale_override():
    req = PdfJobRequest(
        job_id=uuid4(), restaurant_id=uuid4(),
        storage_path="x", progress_callback_url="http://x",
        hmac_secret="abcdefgh", locale="en-US",
    )
    assert req.locale == "en-US"
```

- [ ] **Step 2: Run, verify RED**

- [ ] **Step 3: Add field to `PdfJobRequest`**

In `backend/ai-scripts/models/menu_extraction.py`, append to `PdfJobRequest`:
```python
    locale: str = "ro-RO"
```

- [ ] **Step 4: Add `locale` form field to the photo route**

In `backend/ai-scripts/routers/menu_extraction.py`, change the photo route signature so callers can override locale (default keeps existing behavior). Update the FastAPI import line to include `Form`, then change:
```python
@router.post("/extract", response_model=MenuExtractionResponse)
async def extract_menu(
    photos: List[UploadFile] = File(default=[]),
    service: MenuExtractionService = Depends(get_extraction_service),
) -> MenuExtractionResponse:
```
to:
```python
@router.post("/extract", response_model=MenuExtractionResponse)
async def extract_menu(
    photos: List[UploadFile] = File(default=[]),
    locale: str = Form("ro-RO"),
    service: MenuExtractionService = Depends(get_extraction_service),
) -> MenuExtractionResponse:
```

Update `service.extract(...)` call to forward `locale` (see Task 11).

- [ ] **Step 5: Run, verify GREEN**

- [ ] **Step 6: Checkpoint**

Suggested commit: `feat(ai): thread locale through extraction request DTOs`

---

## Task 10: Python — wire organizer into PDF pipeline

**Files:**
- Modify: `backend/ai-scripts/models/menu_extraction.py`
- Modify: `backend/ai-scripts/services/pdf_menu_extraction_service.py`
- Modify: `backend/ai-scripts/routers/menu_extraction.py` (pass organizer factory)
- Modify: `backend/ai-scripts/tests/test_pdf_pipeline_integration.py` (extend with two assertions)

- [ ] **Step 1: Inspect the existing integration test helpers**

Open `backend/ai-scripts/tests/test_pdf_pipeline_integration.py`. Identify the helper that runs the pipeline against a fake (existing pattern uses something like `run_pdf_pipeline_with_fakes` or a per-test in-place fixture). If the helper does not yet accept an `organizer` argument, plan to add one in this task; otherwise wire to the existing signature.

- [ ] **Step 2: Write failing integration tests**

Append to `backend/ai-scripts/tests/test_pdf_pipeline_integration.py`:
```python
import pytest
from models.menu_organizer import (
    ItemRef, OrganizedCategory, OrganizerResult,
)
from services.menu_organizer import FakeMenuOrganizer, MenuOrganizerError


@pytest.mark.asyncio
async def test_pipeline_includes_hierarchy_from_organizer():
    items_payload = {"items": [
        {"name": "Margherita", "price": 28, "source_page": 0, "source_index": 0, "category_hint": "Pizza"},
        {"name": "Quattro", "price": 32, "source_page": 0, "source_index": 1, "category_hint": "Pizza"},
    ]}
    organizer = FakeMenuOrganizer(canned=OrganizerResult(
        source="ai",
        categories=[OrganizedCategory(
            name="Pizza", sort_order=0, subcategories=[],
            direct_item_refs=[
                ItemRef(source_page=0, source_index=0, confidence=0.9),
                ItemRef(source_page=0, source_index=1, confidence=0.9),
            ],
        )],
        uncategorized_item_refs=[],
    ))
    result = await run_pdf_pipeline_with_fakes(
        vision=FakeBatchVisionExtractor(items_payload),
        organizer=organizer,
    )
    assert result.schema_version == 2
    assert result.hierarchy is not None
    assert result.hierarchy.source == "ai"
    assert result.hierarchy.categories[0].name == "Pizza"


@pytest.mark.asyncio
async def test_pipeline_falls_back_to_hint_when_organizer_raises():
    items_payload = {"items": [
        {"name": "Margherita", "price": 28, "source_page": 0, "source_index": 0, "category_hint": "Pizza"},
    ]}
    organizer = FakeMenuOrganizer(raises=MenuOrganizerError("boom"))
    result = await run_pdf_pipeline_with_fakes(
        vision=FakeBatchVisionExtractor(items_payload),
        organizer=organizer,
    )
    assert result.hierarchy is not None
    assert result.hierarchy.source == "hint-fallback"
    assert result.hierarchy.categories[0].name.lower() == "pizza"
```

Update `run_pdf_pipeline_with_fakes` (or its in-line equivalent) to accept and forward `organizer`. Default `None`; when `None`, the service should resolve via `resolve_organizer()`.

- [ ] **Step 3: Run, verify RED**

- [ ] **Step 4: Extend `MenuExtractionResponse`**

In `backend/ai-scripts/models/menu_extraction.py`:
```python
from models.menu_organizer import OrganizerResult

class MenuExtractionResponse(BaseModel):
    items: List[ExtractedMenuItem]
    failed_pages: List[FailedPage] = []
    model: str
    schema_version: int = 2
    hierarchy: Optional[OrganizerResult] = None
```

- [ ] **Step 5: Call organizer after dedup in `PdfMenuExtractionService`**

In `backend/ai-scripts/services/pdf_menu_extraction_service.py`:

Add imports near the top:
```python
from services.menu_organizer import (
    MenuOrganizer, MenuOrganizerError, OrganizerOptions,
    build_hint_fallback, resolve_organizer,
)
```

Add a constructor parameter `organizer_factory: Callable[[], MenuOrganizer | None] | None = None` and store it:
```python
self._organizer_factory = organizer_factory or resolve_organizer
```

After `deduped = dedup_items(state.items)` (currently line 315), call the organizer and include the hierarchy + log telemetry:
```python
hierarchy = await self._run_organizer(deduped, request)
```

Update the return:
```python
return MenuExtractionResponse(
    items=deduped,
    failed_pages=state.failed_pages,
    model=model_name,
    schema_version=2,
    hierarchy=hierarchy,
)
```

Add the helper method on the service:
```python
async def _run_organizer(
    self,
    items: list[ExtractedMenuItem],
    request: PdfJobRequest,
) -> "OrganizerResult":
    opts = OrganizerOptions(locale=request.locale)
    organizer = self._organizer_factory()
    started = time.perf_counter()
    if organizer is None or not items:
        result = build_hint_fallback(items, opts)
    else:
        semaphore = _get_provider_semaphore(self._provider_concurrency)
        try:
            async with semaphore:
                result = await organizer.organize(items, opts)
        except MenuOrganizerError as exc:
            logger.warning("Organizer failed, falling back to hint-fallback: %s", exc)
            result = build_hint_fallback(items, opts)
    elapsed_ms = int((time.perf_counter() - started) * 1000)
    cat_count = len(result.categories)
    sub_count = sum(len(c.subcategories) for c in result.categories)
    low_conf = sum(
        1 for c in result.categories
        for ref in (
            *[r for s in c.subcategories for r in s.item_refs],
            *c.direct_item_refs,
        )
        if ref.confidence < 0.7
    )
    logger.info(
        "Organizer outcome: source=%s items=%d categories=%d subcategories=%d low_confidence=%d elapsed_ms=%d",
        result.source, len(items), cat_count, sub_count, low_conf, elapsed_ms,
    )
    return result
```

In `routers/menu_extraction.py`, when building the service inside `get_pdf_extraction_service_factory`, pass `organizer_factory=resolve_organizer`.

- [ ] **Step 6: Run, verify GREEN**

- [ ] **Step 7: Checkpoint**

Suggested commit: `feat(ai): organizer step in pdf pipeline`

---

## Task 11: Python — wire organizer into photo pipeline

**Files:**
- Modify: `backend/ai-scripts/services/menu_extraction_service.py`
- Modify: `backend/ai-scripts/routers/menu_extraction.py`
- Modify: `backend/ai-scripts/tests/test_menu_extraction_router.py`

- [ ] **Step 1: Write failing router tests**

Append two tests to `backend/ai-scripts/tests/test_menu_extraction_router.py` that mirror the existing test setup (use `app.dependency_overrides[get_extraction_service]` to inject a `MenuExtractionService` whose `_extractor_factory` returns a `FakeVisionExtractor` and whose `_organizer_factory` returns a `FakeMenuOrganizer`):

1. `test_photo_extract_includes_hierarchy_from_organizer` — `FakeMenuOrganizer` returns an AI hierarchy; assert response JSON contains `schema_version == 2` and `hierarchy.source == "ai"`.
2. `test_photo_extract_falls_back_to_hint_when_organizer_raises` — `FakeMenuOrganizer(raises=MenuOrganizerError(...))`; assert `hierarchy.source == "hint-fallback"`.

> Implementer note: follow the existing test pattern (mocked extractor, `TestClient` from FastAPI). Don't invent new infrastructure.

- [ ] **Step 2: Run, verify RED**

- [ ] **Step 3: Extend `MenuExtractionService.extract`**

In `backend/ai-scripts/services/menu_extraction_service.py`:
```python
from services.menu_organizer import (
    MenuOrganizer, MenuOrganizerError, OrganizerOptions,
    build_hint_fallback, resolve_organizer,
)

class MenuExtractionService:
    def __init__(
        self,
        extractor_factory: Callable[[], VisionExtractor],
        organizer_factory: Callable[[], MenuOrganizer | None] | None = None,
    ):
        self._extractor_factory = extractor_factory
        self._organizer_factory = organizer_factory or resolve_organizer

    async def extract(
        self,
        image_bytes_list: List[bytes],
        locale: str = "ro-RO",
    ) -> MenuExtractionResponse:
        tasks = [self._extract_one(idx, b) for idx, b in enumerate(image_bytes_list)]
        per_page = await asyncio.gather(*tasks)

        items: list[ExtractedMenuItem] = []
        failed: list[FailedPage] = []
        model_name = ""
        for page_items, page_failure, page_model in per_page:
            items.extend(page_items)
            if page_failure is not None:
                failed.append(page_failure)
            if not model_name and page_model:
                model_name = page_model

        opts = OrganizerOptions(locale=locale)
        organizer = self._organizer_factory()
        if organizer is None or not items:
            hierarchy = build_hint_fallback(items, opts)
        else:
            try:
                hierarchy = await organizer.organize(items, opts)
            except MenuOrganizerError:
                logger.warning("Photo organizer failed, falling back to hints", exc_info=True)
                hierarchy = build_hint_fallback(items, opts)

        return MenuExtractionResponse(
            items=items,
            failed_pages=failed,
            model=model_name or "unknown",
            schema_version=2,
            hierarchy=hierarchy,
        )
```

- [ ] **Step 4: Pass `locale` from router**

In `routers/menu_extraction.py`, update the `extract_menu` route to call `await service.extract(image_bytes_list, locale=locale)`.

- [ ] **Step 5: Run, verify GREEN**

- [ ] **Step 6: Checkpoint**

Suggested commit: `feat(ai): organizer step in photo pipeline`

---

## Task 12: C# — thread locale on the import job + expand E2E coverage

**Files:**
- Modify: `backend/RestaurantApi/Services/MenuImportPythonClient.cs` (carry `locale` in payload)
- Modify: `backend/RestaurantApi/Services/MenuImportBackgroundService.cs` (pass restaurant locale)
- Modify: `backend/RestaurantApi/Models/Restaurant.cs` if no `Locale` property exists (add `string Locale { get; set; } = "ro-RO";` + EF mapping + migration); otherwise use the existing field
- Modify: `backend/RestaurantApi.Tests/Helpers/FakePdfExtractor.cs` (support a canned `hierarchy` field)
- Modify: `backend/RestaurantApi.Tests/MenuImportEndToEndTests.cs` (assert hierarchy round-trips through result)

- [ ] **Step 1: Survey Restaurant entity for a locale-like field**

Open `backend/RestaurantApi/Models/Restaurant.cs`. If a `Locale`-style property exists, use it. Otherwise add:
```csharp
public string Locale { get; set; } = "ro-RO";
```
and the mapping `e.Property(r => r.Locale).HasColumnName("locale");` plus an EF migration `AddRestaurantLocale`.

- [ ] **Step 2: Extend `PdfJobPayload`**

In `backend/RestaurantApi/Services/MenuImportPythonClient.cs`:
```csharp
public sealed record PdfJobPayload(
    Guid JobId,
    Guid RestaurantId,
    string StoragePath,
    string ProgressCallbackUrl,
    string HmacSecret,
    string Provider = "gemini",
    string Locale = "ro-RO");
```

- [ ] **Step 3: Populate locale in the background worker**

In `backend/RestaurantApi/Services/MenuImportBackgroundService.cs`, in `WorkOneJobAsync` where `PdfJobPayload` is constructed, look up the restaurant's locale by `RestaurantId` (use `tenant.SuppressResolution()` + `IgnoreQueryFilters()`) and pass it as the new `Locale` argument.

- [ ] **Step 4: Teach `FakePdfExtractor` to return a `hierarchy`**

Inspect `backend/RestaurantApi.Tests/Helpers/FakePdfExtractor.cs`. Add an optional `Hierarchy` property (object/`JsonElement?` depending on existing pattern) that gets included in the canned response under the `hierarchy` key. Default behavior unchanged (omits the key).

- [ ] **Step 5: Extend E2E to assert hierarchy on the job result**

Add to `MenuImportEndToEndTests`:
```csharp
[Fact]
public async Task HappyPath_persists_hierarchy_on_completed_job_result()
{
    // Configure FakePdfExtractor with:
    //   items + hierarchy { source: "ai", categories: [{ name: "Pizza", sort_order: 0, subcategories: [], direct_item_refs: [] }], uncategorized_item_refs: [], model_notes: null }
    // ... existing setup: post the PDF, run the background tick ...

    var jobJson = await _clientA.GetFromJsonAsync<JsonElement>(
        $"/api/restaurants/{_restA}/admin/menu/imports/{jobId}");
    var result = jobJson.GetProperty("result");
    result.GetProperty("hierarchy").GetProperty("source").GetString().Should().Be("ai");
    result.GetProperty("hierarchy").GetProperty("categories").GetArrayLength().Should().BeGreaterThan(0);
    result.GetProperty("schema_version").GetInt32().Should().Be(2);
}
```

- [ ] **Step 6: Run, verify GREEN**

```
dotnet test backend/RestaurantApi.Tests/RestaurantApi.Tests.csproj
```

- [ ] **Step 7: Checkpoint**

Suggested commit: `feat(menu-import): thread locale and surface hierarchy on job result`

---

## Task 13: Shared TypeScript types for hierarchy

**Files:**
- Modify: `shared/types/menu-import.ts`

- [ ] **Step 1: Verify JSON casing first**

Before writing the TS types, inspect the JSON the FE actually receives. Pick one of:
- Run `BulkCreateHierarchyTests` and add a `Console.WriteLine` of the response body during a manual run; or
- Check `MenuImportEndToEndTests.HappyPath_persists_hierarchy_on_completed_job_result` (Task 12) — the test asserts via `JsonDocument` against `result.hierarchy.source` directly. If those property names work (snake_case `schema_version`), the FE receives snake_case keys and the TS types below must match. If they fail and need `result.hierarchy.GetProperty("source")` to be camelCase, the JSON is being re-cased — switch to camelCase TS types instead.

Use whichever style the inspection confirms. The default working assumption below is snake_case in the JSONB blob (matches Python serialization) re-emitted as snake_case by ASP.NET when serializing a `JsonElement`.

- [ ] **Step 2: Add hierarchy types and new bulk-create types**

Append to `shared/types/menu-import.ts` (snake_case variant):
```ts
export interface ItemRef {
  source_page: number
  source_index: number
  confidence: number
}

export interface OrganizedSubcategory {
  name: string
  sort_order: number
  item_refs: ItemRef[]
}

export interface OrganizedCategory {
  name: string
  sort_order: number
  subcategories: OrganizedSubcategory[]
  direct_item_refs: ItemRef[]
}

export interface MenuHierarchy {
  source: 'ai' | 'hint-fallback'
  categories: OrganizedCategory[]
  uncategorized_item_refs: ItemRef[]
  model_notes?: string | null
}

// bulk-create-hierarchy request body — sent TO the C# endpoint, which uses camelCase
export interface BulkHierarchySubcategoryRequest {
  tempId: string
  name: string
  sortOrder?: number | null
}

export interface BulkHierarchyCategoryRequest {
  tempId: string
  name: string
  sortOrder?: number | null
  subcategories: BulkHierarchySubcategoryRequest[]
}

export interface BulkHierarchyProductRequest {
  name: string
  price: number
  description?: string | null
  categoryTempId: string
  subcategoryTempId?: string | null
  sortOrder: number
  isAvailable: boolean
}

export interface BulkCreateHierarchyRequest {
  categories: BulkHierarchyCategoryRequest[]
  products: BulkHierarchyProductRequest[]
}

export interface TempIdMap {
  tempId: string
  id: string
}

export interface BulkCreateHierarchyResponse {
  createdCategories: TempIdMap[]
  createdSubcategories: TempIdMap[]
  createdProducts: { id: string; name: string; categoryId: string; subcategoryId: string | null }[]
}
```

Also extend `MenuScanResponse` to optionally include hierarchy (with the same casing decision):
```ts
export interface MenuScanResponse {
  items: ExtractedMenuItem[]
  failedPages: FailedPage[]
  model: string
  schemaVersion: number
  hierarchy?: MenuHierarchy
}
```

> **Sanity check:** `failedPages` on `MenuScanResponse` is already camelCase in shared types. If the existing flow works in production, JSON keys may actually be re-cased somewhere — verify in Step 1. If keys are actually camelCase, switch the new `MenuHierarchy`/`ItemRef`/etc. fields to camelCase too (`sourcePage`, `sourceIndex`, `sortOrder`, `directItemRefs`, `uncategorizedItemRefs`, `itemRefs`, `modelNotes`).

- [ ] **Step 3: Checkpoint**

Suggested commit: `feat(shared): menu hierarchy types and bulk-create-hierarchy DTOs`

---

## Task 14: Frontend store — hierarchy state, mutations, save

**Files:**
- Modify: `frontend-admin/src/services/menuImportApi.ts`
- Modify: `frontend-admin/src/stores/useMenuImportStore.ts`
- Modify: `frontend-admin/src/__tests__/useMenuImportStore.spec.ts`

> All field accesses below use the **camelCase variant** (matching the existing `MenuScanResponse.failedPages`). If Task 13 Step 1 confirmed snake_case, rename the field accesses below correspondingly.

- [ ] **Step 1: Write failing store tests**

Add to `frontend-admin/src/__tests__/useMenuImportStore.spec.ts`:
```ts
import type { MenuHierarchy } from '@shared/types/menu-import'

const hierarchyFixture: MenuHierarchy = {
  source: 'ai',
  categories: [
    {
      name: 'Pizza',
      sortOrder: 0,
      subcategories: [
        { name: 'Classic', sortOrder: 0, itemRefs: [{ sourcePage: 0, sourceIndex: 0, confidence: 0.9 }] },
      ],
      directItemRefs: [{ sourcePage: 0, sourceIndex: 1, confidence: 0.85 }],
    },
  ],
  uncategorizedItemRefs: [],
  modelNotes: null,
}

describe('useMenuImportStore — hierarchy', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  it('hydrateHierarchy stores the tree and surfaces source', () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'Margherita', price: 28, sourcePage: 0, sourceIndex: 0, categoryHint: 'Pizza' },
      { name: 'Calzone', price: 30, sourcePage: 0, sourceIndex: 1, categoryHint: 'Pizza' },
    ]
    store.hydrateHierarchy(hierarchyFixture)
    expect(store.hierarchy!.source).toBe('ai')
    expect(store.hierarchy!.categories[0].subcategories[0].name).toBe('Classic')
  })

  it('moveItem moves an item from a subcategory to directItemRefs', () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'Margherita', price: 28, sourcePage: 0, sourceIndex: 0, categoryHint: 'Pizza' },
      { name: 'Calzone', price: 30, sourcePage: 0, sourceIndex: 1, categoryHint: 'Pizza' },
    ]
    store.hydrateHierarchy(hierarchyFixture)
    store.moveItem({ sourcePage: 0, sourceIndex: 0, confidence: 0.9 }, { kind: 'direct', categoryName: 'Pizza' })
    const cat = store.hierarchy!.categories[0]
    expect(cat.subcategories[0].itemRefs).toHaveLength(0)
    expect(cat.directItemRefs).toHaveLength(2)
  })

  it('renameCategory updates the category name', () => {
    const store = useMenuImportStore()
    store.hydrateHierarchy(hierarchyFixture)
    store.renameCategory('Pizza', 'Pizze')
    expect(store.hierarchy!.categories[0].name).toBe('Pizze')
  })

  it('addSubcategory + removeSubcategory keep items intact', () => {
    const store = useMenuImportStore()
    store.hydrateHierarchy(hierarchyFixture)
    store.addSubcategory('Pizza', 'Specialty')
    expect(store.hierarchy!.categories[0].subcategories).toHaveLength(2)
    store.removeSubcategory('Pizza', 'Classic')
    expect(store.hierarchy!.categories[0].subcategories.map(s => s.name)).toEqual(['Specialty'])
    expect(store.hierarchy!.categories[0].directItemRefs).toHaveLength(2)
  })

  it('buildHierarchyPayload serialises with tempIds and correct subcategoryTempId', () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'Margherita', price: 28, sourcePage: 0, sourceIndex: 0, categoryHint: 'Pizza' },
      { name: 'Calzone', price: 30, sourcePage: 0, sourceIndex: 1, categoryHint: 'Pizza' },
    ]
    store.hydrateHierarchy(hierarchyFixture)
    const payload = store.buildHierarchyPayload()
    expect(payload.categories).toHaveLength(1)
    expect(payload.categories[0].subcategories).toHaveLength(1)
    expect(payload.products).toHaveLength(2)
    const margherita = payload.products.find(p => p.name === 'Margherita')!
    expect(margherita.subcategoryTempId).toBe(payload.categories[0].subcategories[0].tempId)
    const calzone = payload.products.find(p => p.name === 'Calzone')!
    expect(calzone.subcategoryTempId).toBeNull()
    expect(calzone.categoryTempId).toBe(payload.categories[0].tempId)
  })

  it('importHierarchy posts to bulkCreateHierarchy', async () => {
    const store = useMenuImportStore()
    store.extractedItems = [
      { name: 'Margherita', price: 28, sourcePage: 0, sourceIndex: 0, categoryHint: 'Pizza' },
    ]
    store.hydrateHierarchy(hierarchyFixture)
    ;(menuImportApi.bulkCreateHierarchy as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      createdCategories: [], createdSubcategories: [], createdProducts: [],
    })
    await store.importHierarchy()
    expect(menuImportApi.bulkCreateHierarchy).toHaveBeenCalledTimes(1)
  })
})
```

Also extend the existing `vi.mock('@/services/menuImportApi', ...)` block at the top of the file to include `bulkCreateHierarchy: vi.fn()`.

- [ ] **Step 2: Run, verify RED**

```
cd frontend-admin && npm run test -- useMenuImportStore
```

- [ ] **Step 3: Add `bulkCreateHierarchy` API client**

In `frontend-admin/src/services/menuImportApi.ts`, add a method named `bulkCreateHierarchy` matching the existing `scan`/`bulkCreate` patterns (same auth/http client, hitting `POST /api/restaurants/{restaurantId}/admin/menu/bulk-create-hierarchy`).

- [ ] **Step 4: Add hierarchy state and mutations to the store**

In `frontend-admin/src/stores/useMenuImportStore.ts`:

Add to imports:
```ts
import type {
  BulkCreateHierarchyRequest, MenuHierarchy, OrganizedCategory, OrganizedSubcategory, ItemRef,
} from '@shared/types/menu-import'
```

Add state:
```ts
const hierarchy = ref<MenuHierarchy | null>(null)
const categoryTempIds = reactive<Record<string, string>>({})
const subcategoryTempIds = reactive<Record<string, string>>({}) // key: `${catName}|${subName}`
```

Implement actions (`hydrateHierarchy`, `moveItem`, `renameCategory`, `renameSubcategory`, `addSubcategory`, `removeSubcategory`, `buildHierarchyPayload`, `importHierarchy`) — see the design in the spec. Items belonging to a removed subcategory move to the parent category's `directItemRefs` (no data loss).

Modify `hydrateCompletedPdfJob` to also hydrate the hierarchy if present:
```ts
if (job.result.hierarchy) {
  hydrateHierarchy(job.result.hierarchy)
  step.value = 3
} else {
  // existing path
}
```

Also extend `$reset` to clear `hierarchy.value = null` and the tempId maps.

Expose all new state/actions in the store's return.

- [ ] **Step 5: Run, verify GREEN**

- [ ] **Step 6: Checkpoint**

Suggested commit: `feat(menu-import-store): hierarchy state, mutations, and save`

---

## Task 15: Frontend — `StepReviewHierarchy.vue`

**Files:**
- Create: `frontend-admin/src/components/menu-import/StepReviewHierarchy.vue`
- Create: `frontend-admin/src/__tests__/StepReviewHierarchy.spec.ts`

- [ ] **Step 1: Write failing component tests**

```ts
// frontend-admin/src/__tests__/StepReviewHierarchy.spec.ts
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

import StepReviewHierarchy from '@/components/menu-import/StepReviewHierarchy.vue'
import { useMenuImportStore } from '@/stores/useMenuImportStore'

vi.mock('@/services/menuImportApi', () => ({
  menuImportApi: { scan: vi.fn(), bulkCreate: vi.fn(), bulkCreateHierarchy: vi.fn() },
}))

beforeEach(() => setActivePinia(createPinia()))

it('renders categories, subcategories, and direct items', () => {
  const store = useMenuImportStore()
  store.extractedItems = [
    { name: 'Margherita', price: 28, sourcePage: 0, sourceIndex: 0, categoryHint: 'Pizza' },
    { name: 'Calzone', price: 30, sourcePage: 0, sourceIndex: 1, categoryHint: 'Pizza' },
  ]
  store.hydrateHierarchy({
    source: 'ai',
    categories: [{
      name: 'Pizza', sortOrder: 0,
      subcategories: [{ name: 'Classic', sortOrder: 0, itemRefs: [{ sourcePage: 0, sourceIndex: 0, confidence: 0.9 }] }],
      directItemRefs: [{ sourcePage: 0, sourceIndex: 1, confidence: 0.85 }],
    }],
    uncategorizedItemRefs: [],
    modelNotes: null,
  })
  const wrapper = mount(StepReviewHierarchy)
  expect(wrapper.text()).toContain('Pizza')
  expect(wrapper.text()).toContain('Classic')
  expect(wrapper.text()).toContain('Margherita')
  expect(wrapper.text()).toContain('Calzone')
})

it('shows amber dot only for items with confidence < 0.7', () => {
  const store = useMenuImportStore()
  store.extractedItems = [
    { name: 'A', price: 1, sourcePage: 0, sourceIndex: 0, categoryHint: 'C' },
    { name: 'B', price: 2, sourcePage: 0, sourceIndex: 1, categoryHint: 'C' },
  ]
  store.hydrateHierarchy({
    source: 'ai',
    categories: [{
      name: 'C', sortOrder: 0, subcategories: [],
      directItemRefs: [
        { sourcePage: 0, sourceIndex: 0, confidence: 0.9 },
        { sourcePage: 0, sourceIndex: 1, confidence: 0.4 },
      ],
    }],
    uncategorizedItemRefs: [],
    modelNotes: null,
  })
  const wrapper = mount(StepReviewHierarchy)
  expect(wrapper.findAll('[data-test="confidence-dot"]')).toHaveLength(1)
})

it('shows hint-fallback banner only when source is hint-fallback', () => {
  const store = useMenuImportStore()
  store.hydrateHierarchy({
    source: 'hint-fallback', categories: [], uncategorizedItemRefs: [], modelNotes: null,
  })
  const wrapper = mount(StepReviewHierarchy)
  expect(wrapper.find('[data-test="hint-fallback-banner"]').exists()).toBe(true)
})
```

- [ ] **Step 2: Run, verify RED**

- [ ] **Step 3: Implement component**

Create `frontend-admin/src/components/menu-import/StepReviewHierarchy.vue` rendering:
- Header with title and a source banner (hint-fallback styling when applicable).
- For each category: rename input, "+ subcategory" button, list of subcategories (rename + remove), direct items section.
- Uncategorized section when present.
- Per item: amber dot when `confidence < 0.7` (use `data-test="confidence-dot"`), then item name and price.
- Footer with a single "Save" button calling `store.importHierarchy()`.

Drag/drop is out of scope; renames and add/remove subcategory cover the required surface for tests. A "move-to" affordance can be added in a follow-up.

- [ ] **Step 4: Run, verify GREEN**

```
cd frontend-admin && npm run test -- StepReviewHierarchy
```

- [ ] **Step 5: Checkpoint**

Suggested commit: `feat(menu-import): StepReviewHierarchy tree review component`

---

## Task 16: Frontend — wire `StepReviewHierarchy` into `MenuImportView`

**Files:**
- Modify: `frontend-admin/src/views/MenuImportView.vue`

- [ ] **Step 1: Import the new component**

```ts
import StepReviewHierarchy from '@/components/menu-import/StepReviewHierarchy.vue'
```

- [ ] **Step 2: Conditional rendering in the wizard**

Replace the `<StepReviewItems v-else />` line with:
```html
<StepReviewHierarchy v-else-if="importStore.hierarchy" />
<StepReviewItems v-else />
```

This preserves the legacy flow when no hierarchy is present.

- [ ] **Step 3: Manual smoke**

Run the dev server (`npm run dev` in `frontend-admin/`) and walk through a PDF import end-to-end with a stubbed Python response that includes a `hierarchy` field. Confirm:
- The tree renders.
- Save triggers `POST /admin/menu/bulk-create-hierarchy`.
- On success the wizard exits to `/menu` as before.

If the dev server cannot be started in your environment, note this explicitly and report.

- [ ] **Step 4: Checkpoint**

Suggested commit: `feat(menu-import): wire hierarchy review into the import wizard`

---

## Self-Review

- **Spec coverage:** Persist two-level hierarchy → Tasks 1, 2. Organizer LLM call + confidence → Tasks 4-8. Hints-first/semantic-fallback → Tasks 5-7. Tree review screen → Tasks 14-15. Hint-fallback safety net → Tasks 5, 10, 11. Backwards-compatible read API → Task 3. Telemetry → Task 10 (INFO log inside `_run_organizer`).
- **Open caveat (JSON key casing):** the Python pipeline emits snake_case keys; the C# stores the response as opaque JSONB. Task 13 Step 1 forces a verification step before the TS types are finalized. If the verified casing differs from the assumed style, switch the TS interfaces and the store readers correspondingly. The plan is otherwise complete.
- **No placeholders, no TBDs.**

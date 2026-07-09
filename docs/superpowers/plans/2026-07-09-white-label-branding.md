# White-Label Branding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let each restaurant optionally white-label vibeDine with a 3-colour scheme, a logo, and a presentation photo that re-skin the admin console and the customer app.

**Architecture:** Store branding as scalar columns on the `restaurants` row. A tenant-scoped admin API reads/writes it and accepts logo/photo uploads. A single shared, unit-tested derivation turns the 3 colours into the full ~20-token palette; an admin adapter writes CSS custom properties onto `document.documentElement`, a customer adapter feeds a `ThemeTokens` object through `useTheme`. When branding is disabled, both apps behave exactly as today (classic light/dark).

**Tech Stack:** .NET 8 / EF Core / PostgreSQL (backend), TypeScript in `shared/` (derivation), Vue 3 + Pinia + Vite (both frontends), xUnit + Testcontainers (backend tests), Vitest + MSW (admin tests).

**Spec:** `docs/superpowers/specs/2026-07-09-white-label-branding-design.md`

## Global Constraints

- **Multi-tenancy is the #1 invariant.** Admin branding endpoints live under `api/restaurants/{restaurantId:guid}/admin` so `TenantResolutionMiddleware` resolves + membership-checks the tenant. No endpoint accepts a client-supplied `restaurantId` in the body. Customer endpoints trust `qrToken` only.
- **File uploads validate by magic bytes, not client MIME.** Reuse the sniffer.
- **Contract changes are full-stack slices.** C# DTOs ↔ `shared/types/*.ts` (edit `.ts`, regenerate the committed mirror) ↔ both frontends, in the same change.
- **Money is `decimal`** — not touched here.
- **Order status literals** — not touched here.
- **SignalR names PascalCase / payloads camelCase** — not touched here.
- **UI language:** admin strings are **Romanian**; customer strings need **RO and EN** via `useLocale.ts`.
- **Colours** are stored/validated as `#RRGGBB` (`^#[0-9A-Fa-f]{6}$`).
- **Backend tests require Docker** (Testcontainers Postgres). Customer app has **no test harness** (manual smoke only).
- Commit messages: loose Conventional Commits, ending with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

# Phase 1 — Backend foundation + contract

## Task 1: Branding columns on `Restaurant` + migration

**Files:**
- Modify: `backend/RestaurantApi/Models/Restaurant.cs`
- Modify: `backend/RestaurantApi/Data/AppDbContext.cs` (Restaurant block, ~lines 424–443)
- Create: `backend/RestaurantApi/Migrations/<timestamp>_AddRestaurantBranding.cs` (generated)

**Interfaces:**
- Produces: `Restaurant.BrandingEnabled`, `.BrandPrimary`, `.BrandSecondary`, `.BrandBackground`, `.LogoUrl`, `.PhotoUrl`, `.BrandPresetKey`.

- [ ] **Step 1: Add the properties to the entity**

In `Restaurant.cs`, after `IsActive` (line 27):

```csharp
    // --- White-label branding (all optional; disabled by default) ---
    public bool BrandingEnabled { get; set; } = false;
    public string? BrandPrimary { get; set; }
    public string? BrandSecondary { get; set; }
    public string? BrandBackground { get; set; }
    public string? LogoUrl { get; set; }
    public string? PhotoUrl { get; set; }
    public string? BrandPresetKey { get; set; }
```

- [ ] **Step 2: Map the columns snake_case**

In `AppDbContext.OnModelCreating`, inside the `modelBuilder.Entity<Restaurant>(e => { ... })` block (near the existing `e.Property(r => r.Locale).HasColumnName("locale")...` lines), add:

```csharp
        e.Property(r => r.BrandingEnabled).HasColumnName("branding_enabled").HasDefaultValue(false);
        e.Property(r => r.BrandPrimary).HasColumnName("brand_primary");
        e.Property(r => r.BrandSecondary).HasColumnName("brand_secondary");
        e.Property(r => r.BrandBackground).HasColumnName("brand_background");
        e.Property(r => r.LogoUrl).HasColumnName("logo_url");
        e.Property(r => r.PhotoUrl).HasColumnName("photo_url");
        e.Property(r => r.BrandPresetKey).HasColumnName("brand_preset_key");
```

- [ ] **Step 3: Generate the migration**

Run (Docker Postgres up first: `docker compose up -d postgres`):

```bash
dotnet ef migrations add AddRestaurantBranding --project backend/RestaurantApi
```

Expected: a new migration file adding 7 columns to `restaurants` (`branding_enabled` NOT NULL default false, the rest nullable `text`). Open it and confirm it matches the `20260526123031_AddRestaurantLocale` shape.

- [ ] **Step 4: Apply + verify it builds**

```bash
dotnet ef database update --project backend/RestaurantApi
dotnet build backend/RestaurantApi
```

Expected: migration applies, build succeeds.

- [ ] **Step 5: Commit**

```bash
git add backend/RestaurantApi/Models/Restaurant.cs backend/RestaurantApi/Data/AppDbContext.cs backend/RestaurantApi/Migrations/
git commit -m "feat(branding): add branding columns to restaurants

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Extract the image magic-byte sniffer into a shared helper

**Files:**
- Create: `backend/RestaurantApi/Services/ImageValidation.cs`
- Modify: `backend/RestaurantApi/Controllers/ProfileController.cs` (use the helper; delete the private `SniffImage`)
- Test: `backend/RestaurantApi.Tests/Services/ImageValidationTests.cs`

**Interfaces:**
- Produces: `ImageValidation.SniffImage(ReadOnlySpan<byte>) → "image/png" | "image/jpeg" | "image/webp" | null`; `ImageValidation.ExtensionFor(string mime) → ".png"|".jpg"|".webp"|".bin"`; `ImageValidation.AllowedImageMimes` (a `HashSet<string>`).

- [ ] **Step 1: Write the failing test**

Create `backend/RestaurantApi.Tests/Services/ImageValidationTests.cs`:

```csharp
using RestaurantApi.Services;
using FluentAssertions;
using Xunit;

namespace RestaurantApi.Tests.Services;

public class ImageValidationTests
{
    [Fact]
    public void SniffImage_DetectsPng()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        ImageValidation.SniffImage(png).Should().Be("image/png");
    }

    [Fact]
    public void SniffImage_DetectsJpeg()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 };
        ImageValidation.SniffImage(jpeg).Should().Be("image/jpeg");
    }

    [Fact]
    public void SniffImage_DetectsWebp()
    {
        var webp = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };
        ImageValidation.SniffImage(webp).Should().Be("image/webp");
    }

    [Fact]
    public void SniffImage_RejectsTextMasqueradingAsImage()
    {
        var text = System.Text.Encoding.ASCII.GetBytes("<html>not an ");
        ImageValidation.SniffImage(text).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test backend/RestaurantApi.Tests --filter "FullyQualifiedName~ImageValidationTests"`
Expected: FAIL — `ImageValidation` does not exist.

- [ ] **Step 3: Create the helper**

Create `backend/RestaurantApi/Services/ImageValidation.cs`:

```csharp
namespace RestaurantApi.Services;

/// <summary>
/// Content-based image validation. We never trust the client-declared MIME type;
/// callers sniff the first bytes and compare against a known signature.
/// </summary>
public static class ImageValidation
{
    public static readonly HashSet<string> AllowedImageMimes =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp" };

    /// <summary>Returns the real MIME from the leading bytes, or null if unrecognised.</summary>
    public static string? SniffImage(ReadOnlySpan<byte> probe)
    {
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (probe.Length >= 8 && probe[0] == 0x89 && probe[1] == 0x50 && probe[2] == 0x4E && probe[3] == 0x47)
            return "image/png";
        // JPEG: FF D8 FF
        if (probe.Length >= 3 && probe[0] == 0xFF && probe[1] == 0xD8 && probe[2] == 0xFF)
            return "image/jpeg";
        // WebP: 'RIFF' .... 'WEBP'
        if (probe.Length >= 12 &&
            probe[0] == (byte)'R' && probe[1] == (byte)'I' && probe[2] == (byte)'F' && probe[3] == (byte)'F' &&
            probe[8] == (byte)'W' && probe[9] == (byte)'E' && probe[10] == (byte)'B' && probe[11] == (byte)'P')
            return "image/webp";
        return null;
    }

    public static string ExtensionFor(string mime) => mime switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        _ => ".bin",
    };
}
```

- [ ] **Step 4: Point `ProfileController` at the helper**

In `ProfileController.cs`: delete the private `SniffImage` method (lines 147–161) and the `AllowedAvatarMimes` field (lines 20–21). Replace `AllowedAvatarMimes.Contains(...)` with `ImageValidation.AllowedImageMimes.Contains(...)`, `SniffImage(...)` with `ImageValidation.SniffImage(...)`, and the inline `ext` switch (lines 88–94) with `ImageValidation.ExtensionFor(detected)`. Add `using RestaurantApi.Services;` at the top.

- [ ] **Step 5: Run tests + build**

Run: `dotnet test backend/RestaurantApi.Tests --filter "FullyQualifiedName~ImageValidationTests"` → PASS.
Run: `dotnet build backend/RestaurantApi` → succeeds (ProfileController still compiles).

- [ ] **Step 6: Commit**

```bash
git add backend/RestaurantApi/Services/ImageValidation.cs backend/RestaurantApi/Controllers/ProfileController.cs backend/RestaurantApi.Tests/Services/ImageValidationTests.cs
git commit -m "refactor(branding): extract reusable image magic-byte sniffer

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Branding DTOs + `BrandingController` GET/PUT (colours)

**Files:**
- Create: `backend/RestaurantApi/DTO/Responses/BrandingResponse.cs`
- Create: `backend/RestaurantApi/DTO/Requests/BrandingRequests.cs`
- Create: `backend/RestaurantApi/Controllers/BrandingController.cs`
- Test: `backend/RestaurantApi.Tests/Branding/BrandingApiTests.cs`

**Interfaces:**
- Consumes: `ICurrentTenant.RestaurantId` (set by middleware), `AppDbContext`.
- Produces: `BrandingResponse { bool Enabled; string? Primary; string? Secondary; string? Background; string? LogoUrl; string? PhotoUrl; string? PresetKey; }`; `UpdateBrandingRequest { bool Enabled; string? Primary; string? Secondary; string? Background; string? PresetKey; }`; routes `GET/PUT /api/restaurants/{restaurantId:guid}/admin/branding`.

- [ ] **Step 1: Write the failing test**

Look at an existing admin integration test in `backend/RestaurantApi.Tests/` (e.g. an `InventoryController` or `TablesController` test) for the exact auth/tenant setup helper and copy that pattern. Create `backend/RestaurantApi.Tests/Branding/BrandingApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using RestaurantApi.Tests.Helpers;
using Xunit;

namespace RestaurantApi.Tests.Branding;

[Collection("integration")] // match the collection other integration tests use
public class BrandingApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public BrandingApiTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Put_then_get_roundtrips_branding()
    {
        // ARRANGE: use the test factory's helper to create an org + restaurant and
        // an authenticated RestaurantAdmin client scoped to that restaurant.
        var ctx = await _factory.CreateRestaurantAdminAsync();

        var payload = new
        {
            enabled = true,
            primary = "#E51F4E",
            secondary = "#E8A33B",
            background = "#F5EEE3",
            presetKey = "supporter",
        };

        var put = await ctx.Client.PutAsJsonAsync(
            $"/api/restaurants/{ctx.RestaurantId}/admin/branding", payload);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await ctx.Client.GetFromJsonAsync<BrandingDtoShape>(
            $"/api/restaurants/{ctx.RestaurantId}/admin/branding");
        get!.enabled.Should().BeTrue();
        get.primary.Should().Be("#E51F4E");
        get.presetKey.Should().Be("supporter");
    }

    [Fact]
    public async Task Put_rejects_invalid_hex()
    {
        var ctx = await _factory.CreateRestaurantAdminAsync();
        var bad = new { enabled = true, primary = "red", secondary = "#E8A33B", background = "#F5EEE3" };
        var put = await ctx.Client.PutAsJsonAsync(
            $"/api/restaurants/{ctx.RestaurantId}/admin/branding", bad);
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_rejects_enabled_with_missing_colour()
    {
        var ctx = await _factory.CreateRestaurantAdminAsync();
        var bad = new { enabled = true, primary = (string?)null, secondary = "#E8A33B", background = "#F5EEE3" };
        var put = await ctx.Client.PutAsJsonAsync(
            $"/api/restaurants/{ctx.RestaurantId}/admin/branding", bad);
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cannot_read_another_restaurants_branding()
    {
        var a = await _factory.CreateRestaurantAdminAsync();
        var b = await _factory.CreateRestaurantAdminAsync(); // different tenant + membership
        // a's client hitting b's restaurant id must be blocked by the middleware.
        var res = await a.Client.GetAsync($"/api/restaurants/{b.RestaurantId}/admin/branding");
        res.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    private record BrandingDtoShape(bool enabled, string? primary, string? secondary,
        string? background, string? logoUrl, string? photoUrl, string? presetKey);
}
```

> If `CreateRestaurantAdminAsync` does not exist on the factory, add a small helper following the existing auth-setup pattern in `Helpers/` (create org, create restaurant, register+confirm a user, add an `active` `RestaurantAdmin` membership, return a client with the bearer token and the `RestaurantId`). Reuse whatever the other integration tests already use — do not invent a parallel harness.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test backend/RestaurantApi.Tests --filter "FullyQualifiedName~BrandingApiTests"`
Expected: FAIL — controller/routes do not exist (404).

- [ ] **Step 3: Create the DTOs**

`backend/RestaurantApi/DTO/Responses/BrandingResponse.cs`:

```csharp
namespace RestaurantApi.DTOs.Responses;

public class BrandingResponse
{
    public bool Enabled { get; set; }
    public string? Primary { get; set; }
    public string? Secondary { get; set; }
    public string? Background { get; set; }
    public string? LogoUrl { get; set; }
    public string? PhotoUrl { get; set; }
    public string? PresetKey { get; set; }
}
```

`backend/RestaurantApi/DTO/Requests/BrandingRequests.cs`:

```csharp
namespace RestaurantApi.DTOs.Requests;

public class UpdateBrandingRequest
{
    public bool Enabled { get; set; }
    public string? Primary { get; set; }
    public string? Secondary { get; set; }
    public string? Background { get; set; }
    public string? PresetKey { get; set; }
}
```

- [ ] **Step 4: Create the controller**

`backend/RestaurantApi/Controllers/BrandingController.cs`:

```csharp
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Controllers;

/// <summary>
/// Per-restaurant white-label branding. Tenant-scoped: the middleware resolves the
/// restaurant from the route and verifies membership, so any non-waiter member of
/// this restaurant may read/write its branding. Colour PUT and image uploads are
/// separate (uploads live in <see cref="BrandingUploadController"/> / same class).
/// </summary>
[ApiController]
[Authorize]
[Route("api/restaurants/{restaurantId:guid}/admin/branding")]
public class BrandingController : ControllerBase
{
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _tenant;

    public BrandingController(AppDbContext db, ICurrentTenant tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<ActionResult<BrandingResponse>> Get()
    {
        var r = await _db.Restaurants.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == _tenant.RestaurantId);
        if (r is null) return NotFound();
        return Ok(ToResponse(r));
    }

    [HttpPut]
    public async Task<ActionResult<BrandingResponse>> Update([FromBody] UpdateBrandingRequest req)
    {
        var r = await _db.Restaurants.FirstOrDefaultAsync(x => x.Id == _tenant.RestaurantId);
        if (r is null) return NotFound();

        if (req.Enabled)
        {
            foreach (var (name, value) in new[]
                     {
                         ("primary", req.Primary), ("secondary", req.Secondary), ("background", req.Background),
                     })
            {
                if (string.IsNullOrWhiteSpace(value) || !HexColor.IsMatch(value))
                    return BadRequest(new { message = $"Culoare invalidă: {name}." });
            }
        }
        else
        {
            // When disabling, tolerate but still validate any provided colours.
            foreach (var value in new[] { req.Primary, req.Secondary, req.Background })
                if (!string.IsNullOrWhiteSpace(value) && !HexColor.IsMatch(value))
                    return BadRequest(new { message = "Culoare invalidă." });
        }

        r.BrandingEnabled = req.Enabled;
        r.BrandPrimary = req.Primary;
        r.BrandSecondary = req.Secondary;
        r.BrandBackground = req.Background;
        r.BrandPresetKey = req.PresetKey;
        await _db.SaveChangesAsync();

        return Ok(ToResponse(r));
    }

    internal static BrandingResponse ToResponse(Models.Restaurant r) => new()
    {
        Enabled = r.BrandingEnabled,
        Primary = r.BrandPrimary,
        Secondary = r.BrandSecondary,
        Background = r.BrandBackground,
        LogoUrl = r.LogoUrl,
        PhotoUrl = r.PhotoUrl,
        PresetKey = r.BrandPresetKey,
    };
}
```

> Confirm the exact `ICurrentTenant` interface name/namespace and `RestaurantId` property against `backend/RestaurantApi/Tenancy/` (the middleware sets it). Match the injection pattern used by other `.../admin/...` controllers.

- [ ] **Step 5: Run tests + build**

Run: `dotnet test backend/RestaurantApi.Tests --filter "FullyQualifiedName~BrandingApiTests"`
Expected: the roundtrip, invalid-hex, missing-colour, and cross-tenant tests PASS (upload tests come in Task 4).

- [ ] **Step 6: Commit**

```bash
git add backend/RestaurantApi/DTO/ backend/RestaurantApi/Controllers/BrandingController.cs backend/RestaurantApi.Tests/Branding/BrandingApiTests.cs
git commit -m "feat(branding): tenant-scoped GET/PUT branding colours endpoint

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Logo + photo upload endpoints

**Files:**
- Modify: `backend/RestaurantApi/Controllers/BrandingController.cs` (add two upload actions)
- Modify: `backend/RestaurantApi/Program.cs` (create `wwwroot/uploads/branding` at startup, ~line 263)
- Test: `backend/RestaurantApi.Tests/Branding/BrandingUploadTests.cs`

**Interfaces:**
- Produces: `POST …/admin/branding/logo` and `POST …/admin/branding/photo` (multipart, field name `file`), each returning `{ logoUrl }` / `{ photoUrl }` (root-relative `/uploads/branding/{rid:N}/logo.<ext>`).

- [ ] **Step 1: Write the failing test**

Create `backend/RestaurantApi.Tests/Branding/BrandingUploadTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using RestaurantApi.Tests.Helpers;
using Xunit;

namespace RestaurantApi.Tests.Branding;

[Collection("integration")]
public class BrandingUploadTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public BrandingUploadTests(TestWebApplicationFactory factory) => _factory = factory;

    private static readonly byte[] MinimalPng =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0 };

    [Fact]
    public async Task Upload_logo_returns_url()
    {
        var ctx = await _factory.CreateRestaurantAdminAsync();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(MinimalPng);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "logo.png");

        var res = await ctx.Client.PostAsync(
            $"/api/restaurants/{ctx.RestaurantId}/admin/branding/logo", form);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<LogoShape>();
        body!.logoUrl.Should().StartWith("/uploads/branding/");
    }

    [Fact]
    public async Task Upload_rejects_non_image_by_content()
    {
        var ctx = await _factory.CreateRestaurantAdminAsync();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes("not really a png"));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png"); // lies
        form.Add(file, "file", "evil.png");

        var res = await ctx.Client.PostAsync(
            $"/api/restaurants/{ctx.RestaurantId}/admin/branding/logo", form);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private record LogoShape(string logoUrl);
}
```

> Add a `[Fact]` asserting a Waiter membership gets 403 on the upload route, mirroring however other tests assert waiter path-restriction.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test backend/RestaurantApi.Tests --filter "FullyQualifiedName~BrandingUploadTests"` → FAIL (routes 404).

- [ ] **Step 3: Add the upload actions**

Add to `BrandingController` (inject `IWebHostEnvironment env` and `ILogger<BrandingController> logger` via the constructor). Add `using RestaurantApi.Services;`:

```csharp
    private const long MaxLogoBytes = 2 * 1024 * 1024;   // 2 MB
    private const long MaxPhotoBytes = 5 * 1024 * 1024;  // 5 MB

    [HttpPost("logo")]
    [RequestSizeLimit(MaxLogoBytes + 64 * 1024)]
    public Task<IActionResult> UploadLogo(IFormFile file) => SaveAssetAsync(file, "logo", MaxLogoBytes, isLogo: true);

    [HttpPost("photo")]
    [RequestSizeLimit(MaxPhotoBytes + 64 * 1024)]
    public Task<IActionResult> UploadPhoto(IFormFile file) => SaveAssetAsync(file, "photo", MaxPhotoBytes, isLogo: false);

    private async Task<IActionResult> SaveAssetAsync(IFormFile file, string kind, long maxBytes, bool isLogo)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Fișier lipsă." });
        if (file.Length > maxBytes)
            return BadRequest(new { message = $"Fișierul depășește {maxBytes / (1024 * 1024)} MB." });
        if (!ImageValidation.AllowedImageMimes.Contains(file.ContentType))
            return BadRequest(new { message = "Format nepermis. Folosește PNG, JPG sau WebP." });

        await using var stream = file.OpenReadStream();
        var probe = new byte[12];
        var read = await stream.ReadAsync(probe.AsMemory(0, probe.Length));
        stream.Position = 0;
        var detected = ImageValidation.SniffImage(probe.AsSpan(0, read));
        if (detected is null || !ImageValidation.AllowedImageMimes.Contains(detected))
            return BadRequest(new { message = "Conținutul fișierului nu este o imagine validă." });

        var ext = ImageValidation.ExtensionFor(detected);
        var rid = _tenant.RestaurantId;
        var dir = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads", "branding", rid.ToString("N"));
        Directory.CreateDirectory(dir);

        // Remove any prior asset of this kind (any extension) so we don't accumulate dead files.
        foreach (var old in Directory.EnumerateFiles(dir, $"{kind}.*"))
        {
            try { System.IO.File.Delete(old); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete old branding asset {Path}", old); }
        }

        var fileName = $"{kind}{ext}";
        var fullPath = Path.Combine(dir, fileName);
        await using (var output = System.IO.File.Create(fullPath))
            await stream.CopyToAsync(output);

        var relative = $"/uploads/branding/{rid:N}/{fileName}";
        var r = await _db.Restaurants.FirstOrDefaultAsync(x => x.Id == rid);
        if (r is null) return NotFound();
        if (isLogo) r.LogoUrl = relative; else r.PhotoUrl = relative;
        await _db.SaveChangesAsync();

        return Ok(isLogo ? new { logoUrl = relative } : (object)new { photoUrl = relative });
    }
```

- [ ] **Step 4: Ensure the upload dir exists at startup**

In `Program.cs`, next to the existing `Directory.CreateDirectory(...avatars...)` / `...menu-items...` calls (~line 263), add:

```csharp
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads", "branding"));
```

- [ ] **Step 5: Run tests + build**

Run: `dotnet test backend/RestaurantApi.Tests --filter "FullyQualifiedName~BrandingUploadTests"` → PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/RestaurantApi/Controllers/BrandingController.cs backend/RestaurantApi/Program.cs backend/RestaurantApi.Tests/Branding/BrandingUploadTests.cs
git commit -m "feat(branding): logo and photo upload endpoints (magic-byte validated)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Branding on the customer bootstrap

**Files:**
- Modify: `backend/RestaurantApi/Controllers/PartyController.cs` (`Join`, ~line 119)
- Modify: `backend/RestaurantApi/DTO/Responses/TableSessionResponse.cs`
- Modify: `backend/RestaurantApi/Controllers/CustomerController.cs` (`GetTableSession`, ~lines 343–365)
- Test: `backend/RestaurantApi.Tests/Branding/CustomerBootstrapBrandingTests.cs`

**Interfaces:**
- Produces: a `branding` object on both bootstrap responses: `{ enabled, primary, secondary, background, logoUrl, photoUrl }` (no `presetKey`).

- [ ] **Step 1: Write the failing test**

Create `backend/RestaurantApi.Tests/Branding/CustomerBootstrapBrandingTests.cs` that: creates a restaurant, sets branding enabled with colours + a logo URL directly in the DB (via the factory's `AppDbContext` accessor), seeds a table, `POST`s `/api/tables/{qrToken}/join`, and asserts the JSON contains `branding.enabled == true` and `branding.primary == "#E51F4E"`. Add a second case where branding is disabled and asserts `branding.enabled == false`. Follow the existing customer/party test setup in `backend/RestaurantApi.Tests/` for seeding a table + qrToken.

- [ ] **Step 2: Run it to verify it fails** — FAIL (no `branding` key).

- [ ] **Step 3: Add branding to `PartyController.Join`**

In the `return Ok(new { ... })` (line 119), add before `activeOrder`:

```csharp
            branding = new
            {
                enabled = restaurant?.BrandingEnabled ?? false,
                primary = restaurant?.BrandPrimary,
                secondary = restaurant?.BrandSecondary,
                background = restaurant?.BrandBackground,
                logoUrl = restaurant?.LogoUrl,
                photoUrl = restaurant?.PhotoUrl,
            },
```

- [ ] **Step 4: Add branding to the legacy session DTO**

In `TableSessionResponse.cs` add a `BrandingInfo? Branding { get; set; }` property and a nested type:

```csharp
    public BrandingInfo? Branding { get; set; }
}

public class BrandingInfo
{
    public bool Enabled { get; set; }
    public string? Primary { get; set; }
    public string? Secondary { get; set; }
    public string? Background { get; set; }
    public string? LogoUrl { get; set; }
    public string? PhotoUrl { get; set; }
```

(close the brace appropriately). Then in `CustomerController.GetTableSession`, populate `Branding = new BrandingInfo { Enabled = restaurant?.BrandingEnabled ?? false, Primary = restaurant?.BrandPrimary, ... }` in the inline initializer (the restaurant is already `.Include`d).

- [ ] **Step 5: Run tests + build** → PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/RestaurantApi/Controllers/PartyController.cs backend/RestaurantApi/Controllers/CustomerController.cs backend/RestaurantApi/DTO/Responses/TableSessionResponse.cs backend/RestaurantApi.Tests/Branding/CustomerBootstrapBrandingTests.cs
git commit -m "feat(branding): expose branding on customer bootstrap responses

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Shared wire type

**Files:**
- Modify: `shared/types/models.ts` (add `RestaurantBranding`)
- Modify: `shared/types/models.d.ts` / `.js` (regenerate or hand-sync the committed mirror)

**Interfaces:**
- Produces: `export interface RestaurantBranding { enabled: boolean; primary: string | null; secondary: string | null; background: string | null; logoUrl: string | null; photoUrl: string | null; presetKey?: string | null }`.

- [ ] **Step 1: Add the interface** to `shared/types/models.ts`:

```ts
export interface RestaurantBranding {
  enabled: boolean
  primary: string | null
  secondary: string | null
  background: string | null
  logoUrl: string | null
  photoUrl: string | null
  presetKey?: string | null
}
```

- [ ] **Step 2: Regenerate the compiled mirror**

Check `shared/package.json` for a build script (e.g. `npm run build` inside `shared/`). If present, run it. If not, hand-edit `shared/types/models.d.ts` to add the interface declaration and `models.js` (the interface has no runtime output, so `models.js` likely needs no change — confirm by diffing against how other interfaces appear).

- [ ] **Step 3: Verify both frontends still type-check**

Run: `cd frontend-admin && npm run type-check` and `cd frontend-customer && npm run type-check`. Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add shared/types/models.ts shared/types/models.d.ts shared/types/models.js
git commit -m "feat(branding): add RestaurantBranding shared wire type

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

# Phase 2 — Theming engine

## Task 7: Shared palette derivation + tests

**Files:**
- Create: `shared/branding/derive.ts`
- Test: `frontend-admin/src/__tests__/brandDerive.spec.ts` (runs the shared module under the admin vitest harness)

**Interfaces:**
- Produces:
  - `interface BrandInput { primary: string; secondary: string; background: string }`
  - `interface BrandPalette { mode: 'light'|'dark'; paper; paperHi; card; ink; ink70; ink40; ink12; ink06; accent; accentDk; accentSoft; accentBorder; onAccent; lime; onLime; gold }` (all `string` except `mode`)
  - `function deriveBrandPalette(input: BrandInput): BrandPalette`
  - helpers: `hexToRgb`, `relativeLuminance`, `contrastRatio`, `lighten`, `darken`, `bestOnColor`, `withAlpha`.

- [ ] **Step 1: Write the failing test**

Create `frontend-admin/src/__tests__/brandDerive.spec.ts`:

```ts
import { describe, it, expect } from 'vitest'
import {
  deriveBrandPalette, contrastRatio, bestOnColor, relativeLuminance,
} from '@shared/branding/derive'

const PRESETS = {
  supporter: { primary: '#E51F4E', secondary: '#E8A33B', background: '#F5EEE3' },
  classic:   { primary: '#FF4A1C', secondary: '#D9F54A', background: '#F4EEE2' },
  oliva:     { primary: '#5E7A4C', secondary: '#C96A3E', background: '#F3F0E6' },
  azur:      { primary: '#3E7BFA', secondary: '#F0A836', background: '#14161C' },
}

describe('deriveBrandPalette', () => {
  it('marks a dark background as dark mode and a light one as light', () => {
    expect(deriveBrandPalette(PRESETS.azur).mode).toBe('dark')
    expect(deriveBrandPalette(PRESETS.supporter).mode).toBe('light')
  })

  it('onAccent meets WCAG AA (>= 4.5) against primary for every preset', () => {
    for (const p of Object.values(PRESETS)) {
      const pal = deriveBrandPalette(p)
      expect(contrastRatio(pal.onAccent, pal.accent)).toBeGreaterThanOrEqual(4.5)
    }
  })

  it('onLime meets AA against secondary for every preset', () => {
    for (const p of Object.values(PRESETS)) {
      const pal = deriveBrandPalette(p)
      expect(contrastRatio(pal.onLime, pal.lime)).toBeGreaterThanOrEqual(4.5)
    }
  })

  it('ink meets AA against paper for every preset (incl. Azur dark)', () => {
    for (const p of Object.values(PRESETS)) {
      const pal = deriveBrandPalette(p)
      expect(contrastRatio(pal.ink, pal.paper)).toBeGreaterThanOrEqual(4.5)
    }
  })

  it('bestOnColor picks white on a dark colour and ink on a light one', () => {
    expect(bestOnColor('#111111', '#15140F')).toBe('#FFFFFF')
    expect(bestOnColor('#F5EEE3', '#15140F')).toBe('#15140F')
  })
})
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd frontend-admin && npx vitest run src/__tests__/brandDerive.spec.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement the derivation**

Create `shared/branding/derive.ts`:

```ts
// Deterministic 3-colour -> full semantic palette derivation for white-label
// branding. Pure functions, shared by both frontends and unit-tested for WCAG AA.

export interface BrandInput {
  primary: string
  secondary: string
  background: string
}

export interface BrandPalette {
  mode: 'light' | 'dark'
  paper: string
  paperHi: string
  card: string
  ink: string
  ink70: string
  ink40: string
  ink12: string
  ink06: string
  accent: string
  accentDk: string
  accentSoft: string
  accentBorder: string
  onAccent: string
  lime: string
  onLime: string
  gold: string
}

const LIGHT_INK = '#15140F'
const DARK_INK = '#F5F0E8'
const WHITE = '#FFFFFF'
const DARK_MODE_LUMINANCE = 0.35 // background below this is treated as a dark theme

export function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const h = hex.replace('#', '')
  const n = h.length === 3 ? h.split('').map((c) => c + c).join('') : h
  return {
    r: parseInt(n.slice(0, 2), 16),
    g: parseInt(n.slice(2, 4), 16),
    b: parseInt(n.slice(4, 6), 16),
  }
}

function rgbToHex(r: number, g: number, b: number): string {
  const c = (v: number) => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0')
  return `#${c(r)}${c(g)}${c(b)}`.toUpperCase()
}

function rgbToHsl(r: number, g: number, b: number): { h: number; s: number; l: number } {
  r /= 255; g /= 255; b /= 255
  const max = Math.max(r, g, b), min = Math.min(r, g, b)
  let h = 0, s = 0
  const l = (max + min) / 2
  if (max !== min) {
    const d = max - min
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min)
    switch (max) {
      case r: h = (g - b) / d + (g < b ? 6 : 0); break
      case g: h = (b - r) / d + 2; break
      default: h = (r - g) / d + 4
    }
    h /= 6
  }
  return { h, s, l }
}

function hslToRgb(h: number, s: number, l: number): { r: number; g: number; b: number } {
  if (s === 0) return { r: l * 255, g: l * 255, b: l * 255 }
  const hue2rgb = (p: number, q: number, t: number) => {
    if (t < 0) t += 1
    if (t > 1) t -= 1
    if (t < 1 / 6) return p + (q - p) * 6 * t
    if (t < 1 / 2) return q
    if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6
    return p
  }
  const q = l < 0.5 ? l * (1 + s) : l + s - l * s
  const p = 2 * l - q
  return {
    r: hue2rgb(p, q, h + 1 / 3) * 255,
    g: hue2rgb(p, q, h) * 255,
    b: hue2rgb(p, q, h - 1 / 3) * 255,
  }
}

/** amount is 0..1 of lightness to add/subtract. */
export function lighten(hex: string, amount: number): string {
  const { r, g, b } = hexToRgb(hex)
  const { h, s, l } = rgbToHsl(r, g, b)
  const out = hslToRgb(h, s, Math.min(1, l + amount))
  return rgbToHex(out.r, out.g, out.b)
}

export function darken(hex: string, amount: number): string {
  const { r, g, b } = hexToRgb(hex)
  const { h, s, l } = rgbToHsl(r, g, b)
  const out = hslToRgb(h, s, Math.max(0, l - amount))
  return rgbToHex(out.r, out.g, out.b)
}

export function withAlpha(hex: string, alpha: number): string {
  const { r, g, b } = hexToRgb(hex)
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}

export function relativeLuminance(hex: string): number {
  const { r, g, b } = hexToRgb(hex)
  const chan = (v: number) => {
    const s = v / 255
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4)
  }
  return 0.2126 * chan(r) + 0.7152 * chan(g) + 0.0722 * chan(b)
}

export function contrastRatio(a: string, b: string): number {
  const la = relativeLuminance(a), lb = relativeLuminance(b)
  const hi = Math.max(la, lb), lo = Math.min(la, lb)
  return (hi + 0.05) / (lo + 0.05)
}

/** White or the given ink — whichever has the higher contrast on `bg`. */
export function bestOnColor(bg: string, ink: string): string {
  return contrastRatio(bg, WHITE) >= contrastRatio(bg, ink) ? WHITE : ink
}

export function deriveBrandPalette(input: BrandInput): BrandPalette {
  const { primary, secondary, background } = input
  const isDark = relativeLuminance(background) < DARK_MODE_LUMINANCE
  const mode: 'light' | 'dark' = isDark ? 'dark' : 'light'
  const ink = isDark ? DARK_INK : LIGHT_INK

  return {
    mode,
    paper: background,
    paperHi: lighten(background, isDark ? 0.06 : 0.04),
    card: isDark ? lighten(background, 0.1) : WHITE,
    ink,
    ink70: withAlpha(ink, 0.62),
    ink40: withAlpha(ink, 0.34),
    ink12: withAlpha(ink, 0.12),
    ink06: withAlpha(ink, 0.05),
    accent: primary,
    accentDk: darken(primary, 0.14),
    accentSoft: withAlpha(primary, 0.1),
    accentBorder: withAlpha(primary, 0.3),
    onAccent: bestOnColor(primary, ink),
    lime: secondary,
    onLime: bestOnColor(secondary, ink),
    gold: secondary,
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd frontend-admin && npx vitest run src/__tests__/brandDerive.spec.ts`
Expected: PASS. If any AA assertion fails for a preset, that preset's colours are genuinely low-contrast — the derivation is correct; note it for the QA task (a business could still pick such colours; `bestOnColor` already chooses the better of white/ink, so this only fails for truly borderline pairs).

- [ ] **Step 5: Confirm the `@shared/branding` import resolves in a build**

Run: `cd frontend-admin && npm run type-check`. If the `@shared/branding/derive` import fails to resolve (alias configured for `types` only), confirm `@shared → ../shared` covers subpaths (it does in `vite.config.ts` / `tsconfig.json` / `vitest.config.ts`). If a compiled mirror is required by convention, add `shared/branding/derive.d.ts` + `derive.js` the same way `types/` is mirrored.

- [ ] **Step 6: Commit**

```bash
git add shared/branding/derive.ts frontend-admin/src/__tests__/brandDerive.spec.ts
git commit -m "feat(branding): shared 3-colour palette derivation with AA tests

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: Admin theme adapter + branding store + API client

**Files:**
- Create: `frontend-admin/src/composables/useBrandTheme.ts`
- Create: `frontend-admin/src/stores/useBrandingStore.ts`
- Modify: `frontend-admin/src/services/api-client.ts` (add `brandingApi`)
- Test: `frontend-admin/src/__tests__/useBrandingStore.spec.ts`

**Interfaces:**
- Consumes: `deriveBrandPalette`, `withAlpha`, `hexToRgb` from `@shared/branding/derive`; `RestaurantBranding` from `@shared/types/models`.
- Produces:
  - `useBrandTheme()` → `{ brandVarMap(colors: BrandInput): Record<string,string>, applyBrand(colors: BrandInput): void, clearBrand(): void }`
  - `useBrandingStore` (Pinia) → state `branding: RestaurantBranding | null`, getter `isEnabled`, actions `load()`, `save(draft)`, `uploadLogo(file)`, `uploadPhoto(file)`, `disable()`.
  - `brandingApi` on api-client: `get()`, `update(payload)`, `uploadLogo(file)`, `uploadPhoto(file)`.

- [ ] **Step 1: Add `brandingApi` to `api-client.ts`**

Using the existing `scoped(suffix)` helper (prefixes `/api/restaurants/${activeRestaurantId}`) and `authedFetcher`, add:

```ts
export const brandingApi = {
  get: () =>
    authedFetcher<RestaurantBranding>(scoped('/admin/branding')),
  update: (payload: {
    enabled: boolean
    primary: string | null
    secondary: string | null
    background: string | null
    presetKey: string | null
  }) =>
    authedFetcher<RestaurantBranding>(scoped('/admin/branding'), {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    }),
  uploadLogo: (file: File) => uploadBrandingAsset('logo', file),
  uploadPhoto: (file: File) => uploadBrandingAsset('photo', file),
}

async function uploadBrandingAsset(kind: 'logo' | 'photo', file: File) {
  const form = new FormData()
  form.append('file', file)
  return authedFetcher<{ logoUrl?: string; photoUrl?: string }>(
    scoped(`/admin/branding/${kind}`),
    { method: 'POST', body: form },
  )
}
```

Import `RestaurantBranding` from `@shared/types/models`. Match the exact `authedFetcher` signature already in the file (it may take `(path, init?)` — mirror the other `apiClient` methods). Do **not** set `Content-Type` on the multipart upload — the browser sets the boundary.

- [ ] **Step 2: Write the failing store test**

Create `frontend-admin/src/__tests__/useBrandingStore.spec.ts`:

```ts
// @vitest-environment node
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

vi.mock('@/services/api-client', () => ({
  brandingApi: {
    get: vi.fn(),
    update: vi.fn(),
    uploadLogo: vi.fn(),
    uploadPhoto: vi.fn(),
  },
}))

import { brandingApi } from '@/services/api-client'
import { useBrandingStore } from '@/stores/useBrandingStore'

describe('useBrandingStore', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('loads branding and exposes isEnabled', async () => {
    ;(brandingApi.get as any).mockResolvedValue({
      enabled: true, primary: '#E51F4E', secondary: '#E8A33B',
      background: '#F5EEE3', logoUrl: null, photoUrl: null, presetKey: 'supporter',
    })
    const store = useBrandingStore()
    await store.load()
    expect(store.isEnabled).toBe(true)
    expect(store.branding?.primary).toBe('#E51F4E')
  })

  it('disable() saves enabled:false', async () => {
    ;(brandingApi.get as any).mockResolvedValue({
      enabled: true, primary: '#E51F4E', secondary: '#E8A33B',
      background: '#F5EEE3', logoUrl: null, photoUrl: null, presetKey: null,
    })
    ;(brandingApi.update as any).mockResolvedValue({
      enabled: false, primary: '#E51F4E', secondary: '#E8A33B',
      background: '#F5EEE3', logoUrl: null, photoUrl: null, presetKey: null,
    })
    const store = useBrandingStore()
    await store.load()
    await store.disable()
    expect(brandingApi.update).toHaveBeenCalledWith(expect.objectContaining({ enabled: false }))
    expect(store.isEnabled).toBe(false)
  })
})
```

- [ ] **Step 3: Run it to verify it fails** — FAIL (store missing).

- [ ] **Step 4: Implement `useBrandTheme`**

Create `frontend-admin/src/composables/useBrandTheme.ts`:

```ts
import { deriveBrandPalette, type BrandInput, type BrandPalette } from '@shared/branding/derive'

// Order matters only for readability; keys are the authoritative list used by
// both applyBrand (write) and clearBrand (remove) so they stay symmetric.
function paletteToVarMap(p: BrandPalette): Record<string, string> {
  const { r, g, b } = hexToRgbLocal(p.ink)
  const bg = hexToRgbLocal(p.paper)
  return {
    '--paper': p.paper,
    '--paperHi': p.paperHi,
    '--card': p.card,
    '--ink': p.ink,
    '--ink-rgb': `${r}, ${g}, ${b}`,
    '--paper-rgb': `${bg.r}, ${bg.g}, ${bg.b}`,
    '--ink70': p.ink70,
    '--ink40': p.ink40,
    '--ink12': p.ink12,
    '--ink06': p.ink06,
    '--accent': p.accent,
    '--accentDk': p.accentDk,
    '--accentSoft': p.accentSoft,
    '--accentBorder': p.accentBorder,
    '--onAccent': p.onAccent,
    '--lime': p.lime,
    '--onLime': p.onLime,
    '--gold': p.gold,
  }
}

function hexToRgbLocal(hex: string) {
  const h = hex.replace('#', '')
  return {
    r: parseInt(h.slice(0, 2), 16),
    g: parseInt(h.slice(2, 4), 16),
    b: parseInt(h.slice(4, 6), 16),
  }
}

const VAR_KEYS = [
  '--paper', '--paperHi', '--card', '--ink', '--ink-rgb', '--paper-rgb',
  '--ink70', '--ink40', '--ink12', '--ink06',
  '--accent', '--accentDk', '--accentSoft', '--accentBorder', '--onAccent',
  '--lime', '--onLime', '--gold',
]

export function useBrandTheme() {
  function brandVarMap(colors: BrandInput): Record<string, string> {
    return paletteToVarMap(deriveBrandPalette(colors))
  }

  function applyBrand(colors: BrandInput): void {
    if (typeof document === 'undefined') return
    const map = brandVarMap(colors)
    const style = document.documentElement.style
    for (const [k, v] of Object.entries(map)) style.setProperty(k, v)
  }

  function clearBrand(): void {
    if (typeof document === 'undefined') return
    const style = document.documentElement.style
    for (const k of VAR_KEYS) style.removeProperty(k)
  }

  return { brandVarMap, applyBrand, clearBrand }
}
```

- [ ] **Step 5: Implement `useBrandingStore`**

Create `frontend-admin/src/stores/useBrandingStore.ts`:

```ts
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { brandingApi } from '@/services/api-client'
import type { RestaurantBranding } from '@shared/types/models'

export const useBrandingStore = defineStore('branding', () => {
  const branding = ref<RestaurantBranding | null>(null)
  const isEnabled = computed(() => !!branding.value?.enabled)

  async function load() {
    branding.value = await brandingApi.get()
  }

  async function save(draft: {
    enabled: boolean
    primary: string | null
    secondary: string | null
    background: string | null
    presetKey: string | null
  }) {
    branding.value = await brandingApi.update(draft)
    return branding.value
  }

  async function uploadLogo(file: File) {
    const res = await brandingApi.uploadLogo(file)
    if (branding.value && res.logoUrl) branding.value.logoUrl = res.logoUrl
    return res.logoUrl ?? null
  }

  async function uploadPhoto(file: File) {
    const res = await brandingApi.uploadPhoto(file)
    if (branding.value && res.photoUrl) branding.value.photoUrl = res.photoUrl
    return res.photoUrl ?? null
  }

  async function disable() {
    const b = branding.value
    return save({
      enabled: false,
      primary: b?.primary ?? null,
      secondary: b?.secondary ?? null,
      background: b?.background ?? null,
      presetKey: b?.presetKey ?? null,
    })
  }

  return { branding, isEnabled, load, save, uploadLogo, uploadPhoto, disable }
})
```

- [ ] **Step 6: Run tests + type-check** → PASS + `npm run type-check` clean.

- [ ] **Step 7: Commit**

```bash
git add frontend-admin/src/composables/useBrandTheme.ts frontend-admin/src/stores/useBrandingStore.ts frontend-admin/src/services/api-client.ts frontend-admin/src/__tests__/useBrandingStore.spec.ts
git commit -m "feat(branding): admin brand-theme adapter, store, and api client

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: Apply branding on admin bootstrap + hide the theme toggle

**Files:**
- Modify: `frontend-admin/src/App.vue` (bootstrap watch ~lines 44–60; theme-toggle buttons ~lines 221–235 and ~290–305)

**Interfaces:**
- Consumes: `useBrandingStore`, `useBrandTheme`.

- [ ] **Step 1: Wire load + apply into the existing restaurant watch**

In `App.vue` `<script setup>`, add:

```ts
import { useBrandingStore } from '@/stores/useBrandingStore'
import { useBrandTheme } from '@/composables/useBrandTheme'

const branding = useBrandingStore()
const brandTheme = useBrandTheme()

async function refreshBranding() {
  try {
    await branding.load()
    const b = branding.branding
    if (b?.enabled && b.primary && b.secondary && b.background) {
      brandTheme.applyBrand({ primary: b.primary, secondary: b.secondary, background: b.background })
    } else {
      brandTheme.clearBrand()
    }
  } catch {
    brandTheme.clearBrand()
  }
}
```

In the existing `watch(() => [auth.isAuthenticated, auth.activeRestaurantId] ...)` callback, when `isAuth && rid`, add `void refreshBranding()`; in the `else` branch add `brandTheme.clearBrand()`.

- [ ] **Step 2: Hide the toggles when branded**

Add `v-if="!branding.isEnabled"` to the mobile-top-bar theme toggle button (~line 221) and the account-chip theme toggle `.icon-btn` (~line 290). (Injected vars already override `data-theme`, so even if `toggle()` fired nothing would visually change — but hiding it is the intended UX.)

- [ ] **Step 3: Manual verify**

Run the admin app (`cd frontend-admin && npm run dev`, backend on 5000/5005). With a restaurant whose branding is enabled in the DB (set via Task 3's PUT or directly), reload: the palette should re-skin and the sun/moon toggle should be gone. Disable branding → classic light/dark returns and the toggle reappears.

- [ ] **Step 4: Type-check + commit**

```bash
cd frontend-admin && npm run type-check
git add frontend-admin/src/App.vue
git commit -m "feat(branding): apply tenant palette on admin bootstrap, hide theme toggle when branded

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: Customer theme brand path + session + api + media

**Files:**
- Modify: `frontend-customer/src/composables/useTheme.ts` (brand token path + `mode` + `applyBrand`)
- Modify: `frontend-customer/src/stores/useSessionStore.ts` (carry `branding`; call `applyBrand`)
- Modify: `frontend-customer/src/services/api-client.ts` (add `branding` to `JoinTableResponse`/`TableSession`)
- Modify: `frontend-customer/src/services/mediaUrl.ts` (resolve `logoUrl`/`photoUrl`)

**Interfaces:**
- Consumes: `deriveBrandPalette` from `@shared/branding/derive`; `RestaurantBranding` from `@shared/types/models`.
- Produces: `useTheme()` gains `mode` (computed `'light'|'dark'`), `isBranded` (computed), `applyBrand(colors | null)`. `useSessionStore` gains `branding: RestaurantBranding | null`.

- [ ] **Step 1: Extend `useTheme`**

Rewrite `frontend-customer/src/composables/useTheme.ts` to add a brand-token override:

```ts
import { ref, computed, watch } from 'vue'
import { T, TD, type ThemeTokens } from '@/design/tokens'
import { deriveBrandPalette, type BrandInput } from '@shared/branding/derive'

export type ThemeMode = 'light' | 'dark'

const STORAGE_KEY = 'clarifi.theme'

function readInitial(): ThemeMode {
  if (typeof localStorage === 'undefined') return 'light'
  const stored = localStorage.getItem(STORAGE_KEY)
  if (stored === 'light' || stored === 'dark') return stored
  return 'light'
}

const theme = ref<ThemeMode>(readInitial())
const brandTokens = ref<ThemeTokens | null>(null)
const brandMode = ref<ThemeMode | null>(null)

const tokens = computed<ThemeTokens>(() =>
  brandTokens.value ?? (theme.value === 'dark' ? TD : T),
)
const mode = computed<ThemeMode>(() => brandMode.value ?? theme.value)
const isBranded = computed(() => brandTokens.value !== null)

watch(theme, (v) => {
  try { localStorage.setItem(STORAGE_KEY, v) } catch { /* ignore */ }
})

function toggle() {
  theme.value = theme.value === 'dark' ? 'light' : 'dark'
}
function setTheme(m: ThemeMode) {
  theme.value = m
}

/** Enable branded tokens from 3 colours, or pass null to fall back to light/dark. */
function applyBrand(colors: BrandInput | null) {
  if (!colors) {
    brandTokens.value = null
    brandMode.value = null
    return
  }
  const p = deriveBrandPalette(colors)
  const base = p.mode === 'dark' ? TD : T
  brandTokens.value = {
    ...base, // keeps sans/display/mono/statusClear/blue
    paper: p.paper,
    paperHi: p.paperHi,
    card: p.card,
    ink: p.ink,
    ink70: p.ink70,
    ink40: p.ink40,
    ink12: p.ink12,
    ink06: p.ink06,
    accent: p.accent,
    accentDk: p.accentDk,
    lime: p.lime,
    onAccent: p.onAccent,
  }
  brandMode.value = p.mode
}

export function useTheme() {
  return { theme, tokens, mode, isBranded, toggle, setTheme, applyBrand }
}
```

- [ ] **Step 2: Add `branding` to the customer bootstrap interfaces**

In `frontend-customer/src/services/api-client.ts`, add to the local `JoinTableResponse` interface (and `TableSession` if you keep it in sync) a field `branding: RestaurantBranding | null` (import the type from `@shared/types/models`). Confirm the fetcher still runs responses through `resolveResponseMediaUrls` (it does — see Step 4 so the nested `logoUrl`/`photoUrl` resolve).

- [ ] **Step 3: Carry branding in the session store + apply it**

In `frontend-customer/src/stores/useSessionStore.ts` add a `branding` ref (typed `RestaurantBranding | null`, default null; it does not need sessionStorage persistence — it re-loads on every `join`). In `initializeSession`, after copying the other fields:

```ts
    branding.value = response.branding ?? null
    const b = branding.value
    const { applyBrand } = useTheme()
    if (b?.enabled && b.primary && b.secondary && b.background) {
      applyBrand({ primary: b.primary, secondary: b.secondary, background: b.background })
    } else {
      applyBrand(null)
    }
```

Import `useTheme` from `@/composables/useTheme` and `RestaurantBranding` from `@shared/types/models`. Expose `branding` from the store's return.

- [ ] **Step 4: Resolve branding media URLs**

In `frontend-customer/src/services/mediaUrl.ts`, extend the key check in `resolveResponseMediaUrls` (line 20):

```ts
      if ((key === 'imageUrl' || key === 'avatarUrl' || key === 'logoUrl' || key === 'photoUrl') && typeof nested === 'string') {
```

- [ ] **Step 5: Type-check**

Run: `cd frontend-customer && npm run type-check && npm run lint`. Expected: PASS. (No unit tests exist here.)

- [ ] **Step 6: Commit**

```bash
git add frontend-customer/src/composables/useTheme.ts frontend-customer/src/stores/useSessionStore.ts frontend-customer/src/services/api-client.ts frontend-customer/src/services/mediaUrl.ts
git commit -m "feat(branding): customer theme brand-token path + bootstrap wiring

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 11: Point `PhoneScreen`/`BigButton` at `useTheme`

**Files:**
- Modify: `frontend-customer/src/components/ui/PhoneScreen.vue`
- Modify: `frontend-customer/src/components/ui/BigButton.vue`

**Interfaces:**
- Consumes: `useTheme().tokens`, `useTheme().mode`.
- Note: both keep their existing props (`theme`, `bg`, `color`, `flush`, `arrow`, …) for call-site compatibility, but token selection now comes from `useTheme` so brand tokens reach them automatically. The `theme` prop is retained (deprecated) and no longer drives token choice.

- [ ] **Step 1: Refactor `PhoneScreen.vue`**

Replace the token source: instead of `const t = computed(() => props.theme === 'dark' ? TD : T)`, use:

```ts
import { useTheme } from '@/composables/useTheme'
const { tokens: t, mode } = useTheme()
```

Remove the `T, TD` import if now unused. In `noiseSvg`, replace `props.theme === 'dark'` with `mode.value === 'dark'`. In the template `:style`, replace `theme === 'dark'` (noise opacity/blend) with `mode === 'dark'`, and keep `props.bg ?? t.paper` (t is now the reactive tokens ref — `t.paper` auto-unwraps in template). Keep the `theme`/`flush`/`bg` props declared.

- [ ] **Step 2: Refactor `BigButton.vue`**

Replace `const t = computed(() => props.theme === 'dark' ? TD : T)` with:

```ts
import { useTheme } from '@/composables/useTheme'
const { tokens: t, mode } = useTheme()
```

Update the computed defaults to read `t.value`:

```ts
const bg = computed(() => props.bg ?? t.value.accent)
const color = computed(() => props.color ?? t.value.onAccent)
const arrowBg = computed(() => {
  if (props.arrowBg) return props.arrowBg
  if (!props.bg) return mode.value === 'dark' ? t.value.ink : t.value.onAccent
  return color.value
})
```

In the template `:style`, `fontFamily: t.sans` becomes `fontFamily: t.sans` (still valid — `t` auto-unwraps in template). Keep all props declared for compatibility.

- [ ] **Step 3: Manual verify (unbranded regression)**

Run `cd frontend-customer && npm run dev`. On an **unbranded** table, the language screen, buttons, and dark-mode toggle must look and behave exactly as before (this proves `mode`/`tokens` mirror the old `theme`-prop behaviour). Type-check: `npm run type-check`.

- [ ] **Step 4: Commit**

```bash
git add frontend-customer/src/components/ui/PhoneScreen.vue frontend-customer/src/components/ui/BigButton.vue
git commit -m "refactor(branding): PhoneScreen/BigButton read tokens from useTheme

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

# Phase 3 — Admin authoring UI

> **Visual reference for all Phase 3–4 UI:** `White label branding feature/design_handoff_white_label/White Label Concept.dc.html` (open in a browser; sections `#setup`, `#dashboard`, `#welcome`) and the token table in the spec. Reuse admin design-system classes (`pill-btn`, `btn-ghost`, `field-input`, `field-label`, `eyebrow`, `card-white`, `section-title`) and CSS vars. All copy Romanian.

## Task 12: `BrandControls` — the reusable editor + live preview

**Files:**
- Create: `frontend-admin/src/components/branding/BrandControls.vue`
- Create: `frontend-admin/src/components/branding/BrandPreview.vue`
- Create: `frontend-admin/src/components/branding/presets.ts`

**Interfaces:**
- Produces: `BrandControls` — `v-model` of a `BrandDraft { enabled: boolean; primary: string; secondary: string; background: string; presetKey: string | null; logoUrl: string | null; photoUrl: string | null }`; emits `upload-logo(file)` / `upload-photo(file)`. `BrandPreview` — props `{ colors: BrandInput; logoUrl?: string|null; photoUrl?: string|null }`, renders a mini phone welcome + mini dashboard chip tinted from `useBrandTheme().brandVarMap(colors)` on a scoped wrapper.
- `presets.ts` — `export const BRAND_PRESETS: { key: string; name: string; primary: string; secondary: string; background: string }[]`.

- [ ] **Step 1: Create the presets**

`frontend-admin/src/components/branding/presets.ts`:

```ts
export interface BrandPreset {
  key: string
  name: string
  primary: string
  secondary: string
  background: string
}

export const BRAND_PRESETS: BrandPreset[] = [
  { key: 'supporter', name: 'Supporter', primary: '#E51F4E', secondary: '#E8A33B', background: '#F5EEE3' },
  { key: 'classic',   name: 'vibeDine',  primary: '#FF4A1C', secondary: '#D9F54A', background: '#F4EEE2' },
  { key: 'oliva',     name: 'Oliva',     primary: '#5E7A4C', secondary: '#C96A3E', background: '#F3F0E6' },
  { key: 'azur',      name: 'Azur',      primary: '#3E7BFA', secondary: '#F0A836', background: '#14161C' },
]
```

- [ ] **Step 2: Build `BrandPreview.vue`**

A wrapper `div` with `:style="varMap"` where `varMap = computed(() => useBrandTheme().brandVarMap(props.colors))`. Inside, render a small mock: a `196×392`-ish phone card using `var(--paper)`/`var(--accent)` with the logo (if any) on a light plate and a primary CTA pill, plus a mini dashboard chip using `var(--card)`/`var(--ink)`/`var(--lime)`. Use `resolveMediaUrl` for `logoUrl`/`photoUrl`. Because the vars are scoped to this wrapper, the preview re-tints live without touching the real document. Pixel details from the handoff `#setup` preview panel.

- [ ] **Step 3: Build `BrandControls.vue`**

Script: `defineProps<{ modelValue: BrandDraft }>()` + `defineEmits<{ 'update:modelValue', 'upload-logo': [File], 'upload-photo': [File] }>()`. Helpers: `selectPreset(p)` sets primary/secondary/background/presetKey and emits the updated draft; three colour inputs (`<input type="color">` plus a mono hex label) each set the field and `presetKey = null` (custom); two dropzones reusing the `ProfileTab.vue` hidden-`<input type="file" accept="image/png,image/jpeg,image/webp">` pattern that emit `upload-logo`/`upload-photo`. Template: eyebrow + preset swatch grid (each card = three chips + name, selected outline = its primary) + custom colour row + logo dropzone (56×56 thumb via `resolveMediaUrl`) + photo dropzone. Layout: two columns — left the form, right `<BrandPreview :colors="{primary,secondary,background}" :logo-url :photo-url />`. Copy: `Schemă de culori`, `Sau personalizat`, `Principal`/`Secundar`/`Fundal`, `Logo`, `Fotografie de prezentare`, `Înlocuiește`.

- [ ] **Step 4: Type-check + commit**

```bash
cd frontend-admin && npm run type-check
git add frontend-admin/src/components/branding/
git commit -m "feat(branding): reusable BrandControls editor + live preview

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 13: Settings → Brand tab

**Files:**
- Create: `frontend-admin/src/views/Settings/BrandTab.vue`
- Modify: `frontend-admin/src/views/SettingsLayout.vue` (add a tile)
- Modify: `frontend-admin/src/router/index.ts` (add the child route)

**Interfaces:**
- Consumes: `useBrandingStore`, `useBrandTheme`, `BrandControls`.

- [ ] **Step 1: Register the route + tile**

In `router/index.ts`, under the `/settings` children, add `{ path: 'brand', name: 'settings-brand', component: () => import('@/views/Settings/BrandTab.vue') }`. In `SettingsLayout.vue`'s `computed<Tile[]>`, push (in the `!auth.isWaiter` group) `{ name: 'settings-brand', to: '/settings/brand', label: 'Brand', description: 'Personalizează culorile, logo-ul și fotografia' }`.

- [ ] **Step 2: Build `BrandTab.vue`**

Script: `const store = useBrandingStore()`, `const brandTheme = useBrandTheme()`. On mount, `await store.load()` and seed a local `draft` ref from `store.branding` (fall back to the `classic` preset colours when null). A master toggle `Personalizare brand` bound to `draft.enabled`. `BrandControls v-model="draft"` with `@upload-logo="onLogo"`/`@upload-photo="onPhoto"` (call `store.uploadLogo/uploadPhoto`, write the returned URL into `draft`). A save button `Salvează` → `await store.save({ enabled, primary, secondary, background, presetKey })`; then re-apply immediately: if enabled `brandTheme.applyBrand({primary,secondary,background})` else `brandTheme.clearBrand()`. Toggling off + save must `clearBrand()` and (the sidebar/toggle react via `store.isEnabled`). Use `page-head`/`section-title`/`pill-btn` classes; all copy Romanian; show a success toast using whatever the other Settings tabs use.

- [ ] **Step 3: Manual verify**

`npm run dev`: Settings → Brand. Pick a preset → live preview tints → Salvează → the whole console re-skins and the theme toggle disappears. Toggle `Personalizare brand` off → Salvează → classic light/dark returns, toggle reappears. Upload a logo/photo → thumbnails appear.

- [ ] **Step 4: Type-check + commit**

```bash
cd frontend-admin && npm run type-check
git add frontend-admin/src/views/Settings/BrandTab.vue frontend-admin/src/views/SettingsLayout.vue frontend-admin/src/router/index.ts
git commit -m "feat(branding): Settings Brand tab (edit + enable/disable)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 14: Onboarding brand step

**Files:**
- Modify: `frontend-admin/src/views/OnboardingView.vue`

**Interfaces:**
- Consumes: `useBrandingStore`, `BrandControls`.

- [ ] **Step 1: Introduce a step machine**

Add `const step = ref<1 | 2>(1)`. Keep step 1 as today's org+restaurant form, but change its `submit()` so that after `createOrganization` + `createRestaurant` + `loadAccessibleOrganizations()` it sets the newly created restaurant as active (so `brandingApi` scoping resolves) and advances `step.value = 2` **instead of** routing to `/select-organization`.

- [ ] **Step 2: Add step 2 (brand)**

Render (when `step === 2`) a `Personalizează-ți brandul` heading (eyebrow `Cont nou · pasul 2`), `<BrandControls v-model="draft" @upload-logo @upload-photo />` (seed `draft` from the `classic` preset, `enabled: true`), a primary `Finalizează și intră în cont` button → `await branding.save({ enabled: true, ...draft }); router.replace('/select-organization')`, and a ghost `Sări peste` → `await branding.save({ enabled: false, ... }); router.replace('/select-organization')`. Wire logo/photo uploads through `branding.uploadLogo/uploadPhoto` (they persist to the just-created restaurant).

- [ ] **Step 3: Manual verify**

Fresh account: create org + restaurant → land on the brand step → pick colours + logo + photo → Finalizează → the app is branded from first load. Repeat choosing Sări peste → classic light/dark.

- [ ] **Step 4: Type-check + commit**

```bash
cd frontend-admin && npm run type-check
git add frontend-admin/src/views/OnboardingView.vue
git commit -m "feat(branding): skippable brand step in onboarding

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 15: Sidebar tenant logo + themed "by vibeDine" lockup

**Files:**
- Create: `frontend-admin/src/components/VibeDineLockup.vue`
- Modify: `frontend-admin/src/App.vue` (brand block ~lines 258–264)

**Interfaces:**
- Produces: `VibeDineLockup` — props `{ size?: number; onDark?: boolean }`; renders the pebble (`PEBBLE_PATH` from `BrandLogo.vue`) with `fill: var(--accent)`, outer `stroke: var(--lime)`, and the text `by vibe`+`Dine` where `Dine` uses `var(--accent)` (or a lightened primary when `onDark`).

- [ ] **Step 1: Build `VibeDineLockup.vue`**

Lift `PEBBLE_PATH`, the gradient/spec `<defs>`, and stroke structure verbatim from `BrandLogo.vue` into a compact SVG at `props.size` (default 22). Replace the gradient stops with `var(--accent)`→`var(--accentDk)`, outer stroke `var(--lime)`. Render `<span>by</span>` (`--ink40`, 12px) + the pebble + `<span>vibe</span>` (`--ink70`) + `<span>Dine</span>` (`--accent`, or `#fff`/lightened primary when `onDark`). No hover-image cycling (keep it static/simple).

- [ ] **Step 2: Swap the sidebar brand block**

In `App.vue`, replace the desktop brand block (lines 258–264) with a conditional:

```vue
<div class="hidden md:block mb-6">
  <template v-if="branding.isEnabled && branding.branding?.logoUrl">
    <img :src="resolveMediaUrl(branding.branding.logoUrl)" alt="" style="height:72px;object-fit:contain" class="mb-2" />
    <VibeDineLockup />
  </template>
  <template v-else>
    <BrandLogo class="mb-3" />
    <div class="display-title text-[34px] leading-[0.95]">
      {{ auth.activeRestaurant?.name ?? 'Restaurant' }}
    </div>
  </template>
</div>
```

Import `VibeDineLockup` and `resolveMediaUrl`.

- [ ] **Step 3: Manual verify + type-check**

Branded restaurant → sidebar shows the tenant logo + "by vibeDine" lockup in the tenant colours; unbranded → unchanged. `npm run type-check`.

- [ ] **Step 4: Commit**

```bash
git add frontend-admin/src/components/VibeDineLockup.vue frontend-admin/src/App.vue
git commit -m "feat(branding): sidebar tenant logo + themed by-vibeDine lockup

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

# Phase 4 — Customer welcome

## Task 16: Customer `VibeDineLockup`

**Files:**
- Create: `frontend-customer/src/components/ui/VibeDineLockup.vue`

**Interfaces:**
- Produces: same shape as the admin lockup but driven by `useTheme().tokens` (inline `:style`, since the customer app uses token objects not CSS vars). Props `{ size?: number; onDark?: boolean }`.

- [ ] **Step 1: Build it** — mirror the admin lockup, but colour via `const { tokens: t } = useTheme()`: pebble fill `t.accent`, stroke `t.lime`, `vibe` white/`t.ink70`, `Dine` a lightened primary when `onDark` (compute with `lighten(t.accent, 0.2)` from `@shared/branding/derive`). For the welcome (over the dark photo) pass `on-dark`.

- [ ] **Step 2: Type-check + commit**

```bash
cd frontend-customer && npm run type-check
git add frontend-customer/src/components/ui/VibeDineLockup.vue
git commit -m "feat(branding): customer by-vibeDine lockup component

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 17: Customer Welcome screen + state machine

**Files:**
- Create: `frontend-customer/src/components/screens/WelcomeView.vue`
- Modify: `frontend-customer/src/view/OrderingView.vue` (screen union, initial screen, transition chain, template)
- Modify: `frontend-customer/src/composables/useLocale.ts` (welcome CTA copy if missing)

**Interfaces:**
- Consumes: `useTheme`, `useLocale`, `useSessionStore`, `PhoneScreen`, `BigButton`, `VibeDineLockup`, `resolveMediaUrl`.
- Produces: `WelcomeView` emits `continue` (proceed to `pebbles`); language is picked inline via `useLocale`.

- [ ] **Step 1: Add copy**

In `useLocale.ts`, ensure both `ro` and `en` have `welcome.cta` (`'Vezi meniul'` / `'View the menu'`) and reuse the existing `welcome.line1` (`'Bun venit la'` / `'Welcome to'`), `lang.ro`, `lang.en`.

- [ ] **Step 2: Build `WelcomeView.vue`**

Root `<PhoneScreen flush :bg="'#000'">` (photo bleeds to top). Full-bleed `resolveMediaUrl(session.branding?.photoUrl)` background with the scrim `linear-gradient(180deg, rgba(0,0,0,.25), rgba(0,0,0,.28) 40%, rgba(0,0,0,.86))`. Centred column: eyebrow `t('welcome.line1')` (mono, uppercase, white), the tenant logo on a translucent plate (`rgba(255,255,255,.95)`, radius 26, `backdrop-filter: blur(10px)`), `<VibeDineLockup on-dark />`. Bottom: `<BigButton :bg="tokens.accent" @click="$emit('continue')">{{ t('welcome.cta') }}</BigButton>`, and **beneath it** a compact RO/EN control — two small pill buttons (flag + label) bound to `useLocale().setLocale`, the active one outlined in `tokens.accent`, clearly tappable (min 40px) but visually secondary. Script: `const { tokens } = useTheme()`, `const { t, locale, setLocale } = useLocale()`, `const session = useSessionStore()`.

- [ ] **Step 3: Wire the state machine**

In `OrderingView.vue`: add `'welcome'` to the `Screen` union. Change the initial screen resolution so that when `sessionStore.branding?.enabled && sessionStore.branding?.photoUrl` and there is no restored screen, it starts at `'welcome'` (else `'language'` as today). Add to the `<Transition>` chain: `<WelcomeView v-if="screen === 'welcome'" key="welcome" @continue="screen = 'pebbles'" />`. Keep `'language'` for the unbranded path unchanged. (The welcome subsumes language selection, so branded diners never hit `'language'`.)

- [ ] **Step 4: Manual verify**

Set a restaurant branded with a photo (via Settings Brand tab), open `http://localhost:3000/<qrToken>`: the Welcome screen shows the photo, logo, lockup, primary CTA, and RO/EN underneath. Tapping a language switches copy; CTA → categories. On an **unbranded** table, the classic language-first flow is unchanged.

- [ ] **Step 5: Type-check + lint + commit**

```bash
cd frontend-customer && npm run type-check && npm run lint
git add frontend-customer/src/components/screens/WelcomeView.vue frontend-customer/src/view/OrderingView.vue frontend-customer/src/composables/useLocale.ts
git commit -m "feat(branding): customer welcome screen with inline language control

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

# Phase 5 — QA / polish

## Task 18: Full-feature QA pass

**Files:** none (verification + any fixes surfaced).

- [ ] **Step 1: Backend suite** — `dotnet test backend/RestaurantApi.Tests --filter "FullyQualifiedName~Branding"` (Docker up). All branding tests green, including cross-tenant isolation and magic-byte rejection.

- [ ] **Step 2: Derivation AA** — `cd frontend-admin && npx vitest run src/__tests__/brandDerive.spec.ts`. All four presets pass AA on `onAccent`/`onLime`/`ink`. Manually eyeball the **Azur (dark)** preset in both apps: dashboard "accent" and "lime" metric cards remain legible; customer welcome text over the scrim is legible.

- [ ] **Step 3: Skip path** — new account, Sări peste → both apps classic light/dark; admin theme toggle present.

- [ ] **Step 4: Save path** — Settings → Brand, pick Supporter, upload the example assets (`design_handoff_white_label/assets/supporter-logo.png`, `supporter-photo.jpg` — dev-only, do not commit), Salvează → admin re-skins + toggle hidden + sidebar logo; customer welcome shows photo/logo/CTA.

- [ ] **Step 5: Disable path** — toggle `Personalizare brand` off, Salvează → admin reverts to classic, toggle returns; customer returns to language-first (no welcome).

- [ ] **Step 6: Type-check both frontends + full admin test run** — `cd frontend-admin && npm run type-check && npm test`; `cd frontend-customer && npm run type-check && npm run lint`.

- [ ] **Step 7: Commit any fixes** with `fix(branding): …` and the co-author trailer.

---

## Self-review notes (author)

- **Spec coverage:** persistence (T1) · API GET/PUT + validation (T3) · uploads + magic bytes (T2,T4) · customer bootstrap (T5) · shared type (T6) · derivation + AA (T7) · admin adapter/store (T8) · apply-on-bootstrap + hide toggle (T9) · customer theme/session/media (T10) · primitives refactor (T11) · onboarding step (T14) · Settings tab (T13) · sidebar logo + lockup (T15) · customer welcome + language-underneath (T16,T17) · QA incl. Azur dark + tenant isolation (T18). All spec sections mapped.
- **Type consistency:** `deriveBrandPalette(BrandInput) → BrandPalette` used identically in admin (`useBrandTheme`) and customer (`useTheme.applyBrand`). `RestaurantBranding` (shared) is the single response shape; `BrandDraft` is the admin editor's local shape. `brandingApi` method names match the store's calls.
- **Known verification gaps to resolve during execution:** exact `ICurrentTenant` type + the test factory's admin-auth helper (T3 Step 1 flags this); whether `@shared/branding` needs a compiled mirror (T7 Step 5 flags this).

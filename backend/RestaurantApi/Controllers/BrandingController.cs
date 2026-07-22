using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Services;
using RestaurantApi.Storage;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Controllers;

/// <summary>
/// Per-restaurant white-label branding. Tenant-scoped: <see cref="TenantResolutionMiddleware"/>
/// resolves the restaurant from the route and verifies membership, so any member of this
/// restaurant may read/write its branding. Colour GET/PUT and logo/photo uploads both live
/// here; uploaded assets are stored under the uploads root (<see cref="UploadPaths"/>)
/// at branding/{restaurantId:N}/ and served back at /uploads/branding/….
/// </summary>
[ApiController]
[Authorize]
[Route("api/restaurants/{restaurantId:guid}/admin/branding")]
public class BrandingController : ControllerBase
{
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    // HAND-SYNCED with shared/branding/limits.ts (client-side guard). Change both together.
    private const long MaxLogoBytes = 5 * 1024 * 1024;    // 5 MB
    private const long MaxPhotoBytes = 10 * 1024 * 1024;  // 10 MB
    private const long MaxMenuBackgroundBytes = 10 * 1024 * 1024;  // 10 MB

    // Uploadable / removable asset kinds. The value doubles as the URL segment,
    // the on-disk filename stem ("{kind}.{ext}") and the response field key.
    private static readonly string[] AssetKinds = { "logo", "photo", "menu-background" };

    // Menu-background visibility. HAND-SYNCED with shared/branding/menuBackground.ts.
    private static readonly string[] MenuBackgroundStyles = { "visible", "medium", "subtle" };

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly UploadPaths _uploads;
    private readonly ILogger<BrandingController> _logger;

    public BrandingController(
        AppDbContext db,
        ICurrentTenant tenant,
        UploadPaths uploads,
        ILogger<BrandingController> logger)
    {
        _db = db;
        _tenant = tenant;
        _uploads = uploads;
        _logger = logger;
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
            // When disabling, tolerate missing colours but still validate any provided.
            foreach (var value in new[] { req.Primary, req.Secondary, req.Background })
                if (!string.IsNullOrWhiteSpace(value) && !HexColor.IsMatch(value))
                    return BadRequest(new { message = "Culoare invalidă." });
        }

        // Logo background: null/empty = Auto, the literal "transparent", or a hex.
        if (!string.IsNullOrWhiteSpace(req.LogoBackground)
            && req.LogoBackground != "transparent"
            && !HexColor.IsMatch(req.LogoBackground))
            return BadRequest(new { message = "Fundal logo invalid." });

        // Menu-background visibility: null/empty = default, otherwise a known style.
        if (!string.IsNullOrWhiteSpace(req.MenuBackgroundStyle)
            && !MenuBackgroundStyles.Contains(req.MenuBackgroundStyle))
            return BadRequest(new { message = "Stil fundal meniu invalid." });

        r.BrandingEnabled = req.Enabled;
        r.BrandPrimary = req.Primary;
        r.BrandSecondary = req.Secondary;
        r.BrandBackground = req.Background;
        r.BrandPresetKey = req.PresetKey;
        r.LogoBackground = string.IsNullOrWhiteSpace(req.LogoBackground) ? null : req.LogoBackground;
        r.LogoIsDark = req.LogoIsDark;
        r.MenuBackgroundStyle = string.IsNullOrWhiteSpace(req.MenuBackgroundStyle) ? null : req.MenuBackgroundStyle;
        await _db.SaveChangesAsync();

        return Ok(ToResponse(r));
    }

    [HttpPost("logo")]
    [RequestSizeLimit(MaxLogoBytes + 64 * 1024)]
    public Task<IActionResult> UploadLogo(IFormFile file) => SaveAssetAsync(file, "logo", MaxLogoBytes);

    [HttpPost("photo")]
    [RequestSizeLimit(MaxPhotoBytes + 64 * 1024)]
    public Task<IActionResult> UploadPhoto(IFormFile file) => SaveAssetAsync(file, "photo", MaxPhotoBytes);

    [HttpPost("menu-background")]
    [RequestSizeLimit(MaxMenuBackgroundBytes + 64 * 1024)]
    public Task<IActionResult> UploadMenuBackground(IFormFile file) => SaveAssetAsync(file, "menu-background", MaxMenuBackgroundBytes);

    /// <summary>
    /// Remove a branding asset (logo / photo / menu-background): deletes the
    /// stored file(s) and clears the corresponding URL on the restaurant.
    /// </summary>
    [HttpDelete("{kind}")]
    public async Task<IActionResult> DeleteAsset(string kind)
    {
        if (!AssetKinds.Contains(kind))
            return BadRequest(new { message = "Tip de imagine necunoscut." });

        var rid = _tenant.RestaurantId!.Value;
        var dir = _uploads.Dir("branding", rid.ToString("N"));
        if (Directory.Exists(dir))
        {
            foreach (var old in Directory.EnumerateFiles(dir, $"{kind}.*"))
            {
                try { System.IO.File.Delete(old); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete branding asset {Path}", old); }
            }
        }

        var r = await _db.Restaurants.FirstOrDefaultAsync(x => x.Id == rid);
        if (r is null) return NotFound();
        ClearAssetUrl(r, kind);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Restaurant {RestaurantId} removed {Kind}", rid, kind);
        return NoContent();
    }

    private async Task<IActionResult> SaveAssetAsync(IFormFile file, string kind, long maxBytes)
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
        var rid = _tenant.RestaurantId!.Value;
        var dir = _uploads.Dir("branding", rid.ToString("N"));
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
        SetAssetUrl(r, kind, relative);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Restaurant {RestaurantId} uploaded {Kind} ({Bytes} bytes, {Mime})", rid, kind, file.Length, detected);

        return Ok(AssetResponse(kind, relative));
    }

    private static void SetAssetUrl(Models.Restaurant r, string kind, string? url)
    {
        switch (kind)
        {
            case "logo": r.LogoUrl = url; break;
            case "photo": r.PhotoUrl = url; break;
            case "menu-background": r.MenuBackgroundUrl = url; break;
        }
    }

    // Removing the logo also clears the client-computed "is dark" hint tied to it,
    // so the Auto plate logic resets when a new logo is uploaded later.
    private static void ClearAssetUrl(Models.Restaurant r, string kind)
    {
        SetAssetUrl(r, kind, null);
        if (kind == "logo") r.LogoIsDark = null;
    }

    private static object AssetResponse(string kind, string? url) => kind switch
    {
        "logo" => new { logoUrl = url },
        "photo" => new { photoUrl = url },
        "menu-background" => new { menuBackgroundUrl = url },
        _ => new { },
    };

    internal static BrandingResponse ToResponse(Models.Restaurant r) => new()
    {
        Enabled = r.BrandingEnabled,
        Primary = r.BrandPrimary,
        Secondary = r.BrandSecondary,
        Background = r.BrandBackground,
        LogoUrl = r.LogoUrl,
        PhotoUrl = r.PhotoUrl,
        MenuBackgroundUrl = r.MenuBackgroundUrl,
        MenuBackgroundStyle = r.MenuBackgroundStyle,
        PresetKey = r.BrandPresetKey,
        LogoBackground = r.LogoBackground,
        LogoIsDark = r.LogoIsDark,
    };
}

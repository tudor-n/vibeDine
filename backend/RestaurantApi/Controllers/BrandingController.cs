using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Services;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Controllers;

/// <summary>
/// Per-restaurant white-label branding. Tenant-scoped: <see cref="TenantResolutionMiddleware"/>
/// resolves the restaurant from the route and verifies membership, so any member of this
/// restaurant may read/write its branding. Colour GET/PUT and logo/photo uploads both live
/// here; uploaded assets are stored under wwwroot/uploads/branding/{restaurantId:N}/.
/// </summary>
[ApiController]
[Authorize]
[Route("api/restaurants/{restaurantId:guid}/admin/branding")]
public class BrandingController : ControllerBase
{
    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private const long MaxLogoBytes = 2 * 1024 * 1024;   // 2 MB
    private const long MaxPhotoBytes = 5 * 1024 * 1024;  // 5 MB

    private readonly AppDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<BrandingController> _logger;

    public BrandingController(
        AppDbContext db,
        ICurrentTenant tenant,
        IWebHostEnvironment env,
        ILogger<BrandingController> logger)
    {
        _db = db;
        _tenant = tenant;
        _env = env;
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

        r.BrandingEnabled = req.Enabled;
        r.BrandPrimary = req.Primary;
        r.BrandSecondary = req.Secondary;
        r.BrandBackground = req.Background;
        r.BrandPresetKey = req.PresetKey;
        r.LogoBackground = string.IsNullOrWhiteSpace(req.LogoBackground) ? null : req.LogoBackground;
        r.LogoIsDark = req.LogoIsDark;
        await _db.SaveChangesAsync();

        return Ok(ToResponse(r));
    }

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
        var rid = _tenant.RestaurantId!.Value;
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

        _logger.LogInformation("Restaurant {RestaurantId} uploaded {Kind} ({Bytes} bytes, {Mime})", rid, kind, file.Length, detected);

        return Ok(isLogo ? new { logoUrl = relative } : (object)new { photoUrl = relative });
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
        LogoBackground = r.LogoBackground,
        LogoIsDark = r.LogoIsDark,
    };
}

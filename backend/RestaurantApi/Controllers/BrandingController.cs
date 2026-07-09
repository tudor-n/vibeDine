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
/// Per-restaurant white-label branding. Tenant-scoped: <see cref="TenantResolutionMiddleware"/>
/// resolves the restaurant from the route and verifies membership, so any member of this
/// restaurant may read/write its branding. Colour GET/PUT lives here; logo/photo uploads
/// are added to this same controller in a later task.
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
            // When disabling, tolerate missing colours but still validate any provided.
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

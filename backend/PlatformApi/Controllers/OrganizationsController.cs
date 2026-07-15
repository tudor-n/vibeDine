using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlatformApi.Auth;
using PlatformApi.Common;
using PlatformApi.Data;
using PlatformApi.DTO.Requests;
using PlatformApi.DTO.Responses;
using PlatformApi.Models;

namespace PlatformApi.Controllers;

[ApiController]
[Authorize]
[Route("api/platform/organizations")]
public class OrganizationsController : ControllerBase
{
    private readonly PlatformDbContext _db;
    private readonly ILogger<OrganizationsController> _logger;

    public OrganizationsController(PlatformDbContext db, ILogger<OrganizationsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search)
    {
        var query = _db.Organizations.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(o => o.Name.ToLower().Contains(s) || o.Slug.ToLower().Contains(s));
        }

        var rows = await query
            .OrderBy(o => o.Name)
            .Select(o => new
            {
                o.Id,
                o.Name,
                o.Slug,
                o.CreatedAt,
                LocationCount = o.Restaurants.Count(),
                AdminCount = o.Memberships.Count(m => m.Role == MembershipRole.RestaurantAdmin),
                HireCount = o.Memberships.Count(m => m.Role == MembershipRole.Waiter),
            })
            .ToListAsync();

        var ids = rows.Select(r => r.Id).ToList();
        var owners = await _db.Memberships
            .Where(m => m.Role == MembershipRole.OrgOwner && ids.Contains(m.OrganizationId))
            .Select(m => new { m.OrganizationId, UserId = m.User!.Id, m.User.FullName, m.User.Email })
            .ToListAsync();
        var ownerByOrg = owners
            .GroupBy(o => o.OrganizationId)
            .ToDictionary(g => g.Key, g => g.First());

        var result = rows.Select(r =>
        {
            ownerByOrg.TryGetValue(r.Id, out var ow);
            return new OrganizationListItem(
                r.Id, r.Name, r.Slug, r.CreatedAt, r.LocationCount, r.AdminCount, r.HireCount,
                ow is null ? null : new UserSummary(ow.UserId, ow.FullName, ow.Email));
        });

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == id);
        if (org is null) return NotFound(new { message = "Organization not found." });

        var owners = await _db.Memberships
            .Where(m => m.OrganizationId == id && m.Role == MembershipRole.OrgOwner)
            .Select(m => new UserSummary(m.User!.Id, m.User.FullName, m.User.Email))
            .ToListAsync();

        var locations = await _db.Restaurants
            .Where(r => r.OrganizationId == id)
            .OrderBy(r => r.Name)
            .Select(r => new LocationListItem(
                r.Id, r.OrganizationId, org.Name, r.Name, r.Slug, r.Address, r.IsActive,
                r.Timezone, r.Currency, r.Locale, r.CreatedAt,
                r.Memberships.Count(m => m.Role == MembershipRole.RestaurantAdmin),
                r.Memberships.Count(m => m.Role == MembershipRole.Waiter)))
            .ToListAsync();

        return Ok(new OrganizationDetail(org.Id, org.Name, org.Slug, org.CreatedAt, owners, locations));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrganizationRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Name is required." });

        // PLAT-08: stamp the real acting platform admin, never Guid.Empty, so the audit trail is meaningful.
        var actorId = User.PlatformAdminId();
        if (actorId is null)
            return Unauthorized(new { message = "Could not resolve the acting platform admin." });

        var seed = string.IsNullOrWhiteSpace(request.Slug) ? name : request.Slug!;
        var org = new Organization
        {
            Name = name,
            Slug = await UniqueSlugAsync(Slug.From(seed)),
            CreatedByUserId = actorId.Value,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Created organization {Id} ({Name})", org.Id, org.Name);

        return Created((string?)null,
            new OrganizationListItem(org.Id, org.Name, org.Slug, org.CreatedAt, 0, 0, 0, null));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOrganizationRequest request)
    {
        var org = await _db.Organizations.FindAsync(id);
        if (org is null) return NotFound(new { message = "Organization not found." });

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0) return BadRequest(new { message = "Name cannot be empty." });
            org.Name = name;
        }

        if (request.Slug is not null)
            org.Slug = await UniqueSlugAsync(Slug.From(request.Slug), id);

        await _db.SaveChangesAsync();

        return Ok(new OrganizationListItem(org.Id, org.Name, org.Slug, org.CreatedAt, 0, 0, 0, null));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var org = await _db.Organizations.FindAsync(id);
        if (org is null) return NotFound(new { message = "Organization not found." });

        if (await _db.Restaurants.AnyAsync(r => r.OrganizationId == id))
            return Conflict(new { message = "Delete or move the organization's locations before deleting it." });

        var memberships = await _db.Memberships.Where(m => m.OrganizationId == id).ToListAsync();
        _db.Memberships.RemoveRange(memberships);
        _db.Organizations.Remove(org);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Deleted organization {Id} ({Name})", org.Id, org.Name);

        return NoContent();
    }

    private async Task<string> UniqueSlugAsync(string baseSlug, Guid? excludeId = null)
    {
        var slug = baseSlug;
        var suffix = 1;
        while (await _db.Organizations.AnyAsync(o => o.Slug == slug && (excludeId == null || o.Id != excludeId)))
            slug = $"{baseSlug}-{++suffix}";
        return slug;
    }
}

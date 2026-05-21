using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Models;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations")]
public class OrganizationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantAccess _access;
    private readonly ILogger<OrganizationsController> _logger;

    public OrganizationsController(AppDbContext db, ITenantAccess access, ILogger<OrganizationsController> logger)
    {
        _db = db;
        _access = access;
        _logger = logger;
    }

    private Guid UserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    // ── Organizations ────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> MyOrganizations()
    {
        var orgs = await _db.Memberships
            .Where(m => m.UserId == UserId)
            .Join(_db.Organizations, m => m.OrganizationId, o => o.Id, (m, o) => new OrganizationResponse
            {
                Id = o.Id,
                Name = o.Name,
                Slug = o.Slug,
                Role = m.Role.ToString(),
                CreatedAt = o.CreatedAt,
            })
            .ToListAsync();

        return Ok(orgs);
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrganization([FromBody] CreateOrganizationRequest request)
    {
        var org = new Organization
        {
            Name = request.Name.Trim(),
            Slug = await UniqueSlugAsync(request.Name, isOrg: true),
            CreatedByUserId = UserId,
        };
        _db.Organizations.Add(org);

        // Creator becomes OrgOwner — implicitly owns every restaurant created under it.
        _db.Memberships.Add(new Membership
        {
            UserId = UserId,
            OrganizationId = org.Id,
            RestaurantId = null,
            Role = MembershipRole.OrgOwner,
        });

        await _db.SaveChangesAsync();
        _logger.LogInformation("User {UserId} created organization {OrgId}", UserId, org.Id);

        return CreatedAtAction(nameof(MyOrganizations), null, new OrganizationResponse
        {
            Id = org.Id,
            Name = org.Name,
            Slug = org.Slug,
            Role = MembershipRole.OrgOwner.ToString(),
            CreatedAt = org.CreatedAt,
        });
    }

    // ── Restaurants ──────────────────────────────────────────────────────────

    [HttpGet("{orgId:guid}/restaurants")]
    public async Task<IActionResult> ListRestaurants(Guid orgId)
    {
        if (!await IsMemberOfOrgAsync(orgId)) return Forbid();

        var restaurants = await _db.Restaurants
            .Where(r => r.OrganizationId == orgId)
            .Select(r => ToResponse(r))
            .ToListAsync();

        // RestaurantAdmins only see the locations they were invited to.
        if (!await _access.IsOrgOwnerAsync(UserId, orgId))
        {
            var allowed = await _db.Memberships
                .Where(m => m.UserId == UserId && m.OrganizationId == orgId && m.RestaurantId != null)
                .Select(m => m.RestaurantId!.Value)
                .ToListAsync();
            restaurants = restaurants.Where(r => allowed.Contains(r.Id)).ToList();
        }

        return Ok(restaurants);
    }

    [HttpPost("{orgId:guid}/restaurants")]
    public async Task<IActionResult> CreateRestaurant(Guid orgId, [FromBody] CreateRestaurantRequest request)
    {
        if (!await _access.IsOrgOwnerAsync(UserId, orgId)) return Forbid();

        var restaurant = new Restaurant
        {
            OrganizationId = orgId,
            Name = request.Name.Trim(),
            Slug = await UniqueSlugAsync(request.Name, isOrg: false),
            Address = request.Address,
            Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "Europe/Bucharest" : request.Timezone,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "RON" : request.Currency,
            IsActive = true,
        };
        _db.Restaurants.Add(restaurant);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Org {OrgId}: created restaurant {RestaurantId}", orgId, restaurant.Id);

        return CreatedAtAction(nameof(ListRestaurants), new { orgId }, ToResponse(restaurant));
    }

    // ── Invitations ──────────────────────────────────────────────────────────

    [HttpGet("{orgId:guid}/invitations")]
    public async Task<IActionResult> ListInvitations(Guid orgId)
    {
        if (!await _access.IsOrgOwnerAsync(UserId, orgId)) return Forbid();

        var invites = await _db.Invitations
            .Where(i => i.OrganizationId == orgId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return Ok(invites.Select(ToResponse));
    }

    [HttpPost("{orgId:guid}/invitations")]
    public async Task<IActionResult> InviteCoOwner(Guid orgId, [FromBody] CreateInvitationRequest request)
    {
        if (!await _access.IsOrgOwnerAsync(UserId, orgId)) return Forbid();

        var restaurantInOrg = await _db.Restaurants
            .AnyAsync(r => r.Id == request.RestaurantId && r.OrganizationId == orgId);
        if (!restaurantInOrg)
            return BadRequest(new { message = "Restaurant does not belong to this organization." });

        var invite = new Invitation
        {
            Email = request.Email.Trim().ToLowerInvariant(),
            OrganizationId = orgId,
            RestaurantId = request.RestaurantId,
            Role = MembershipRole.RestaurantAdmin,
            InvitedByUserId = UserId,
        };
        _db.Invitations.Add(invite);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Org {OrgId}: invited {Email} to restaurant {RestaurantId}",
            orgId, invite.Email, request.RestaurantId);

        return CreatedAtAction(nameof(ListInvitations), new { orgId }, ToResponse(invite));
    }

    [HttpDelete("{orgId:guid}/invitations/{inviteId:guid}")]
    public async Task<IActionResult> RevokeInvitation(Guid orgId, Guid inviteId)
    {
        if (!await _access.IsOrgOwnerAsync(UserId, orgId)) return Forbid();

        var invite = await _db.Invitations
            .FirstOrDefaultAsync(i => i.Id == inviteId && i.OrganizationId == orgId);
        if (invite == null) return NotFound();

        invite.Status = "revoked";
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Employees ────────────────────────────────────────────────────────────

    [HttpGet("{orgId:guid}/employees")]
    public async Task<IActionResult> ListEmployees(Guid orgId)
    {
        if (!await _access.IsOrgOwnerAsync(UserId, orgId)) return Forbid();

        var rows = await _db.Memberships
            .Where(m => m.OrganizationId == orgId)
            .OrderByDescending(m => m.CreatedAt)
            .Join(_db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u })
            .GroupJoin(_db.Restaurants, x => x.m.RestaurantId, r => r.Id, (x, rs) => new { x.m, x.u, rs })
            .SelectMany(x => x.rs.DefaultIfEmpty(), (x, r) => new EmployeeResponse
            {
                MembershipId = x.m.Id,
                UserId = x.u.Id,
                FullName = x.u.FullName,
                Email = x.u.Email ?? string.Empty,
                AvatarUrl = x.u.AvatarPath == null ? null : "/" + x.u.AvatarPath,
                Role = x.m.Role.ToString(),
                RestaurantId = x.m.RestaurantId,
                RestaurantName = r == null ? null : r.Name,
                Status = x.m.Status,
                CreatedAt = x.m.CreatedAt,
            })
            .ToListAsync();

        return Ok(rows);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Task<bool> IsMemberOfOrgAsync(Guid orgId) =>
        _db.Memberships.AnyAsync(m => m.UserId == UserId && m.OrganizationId == orgId);

    private static RestaurantResponse ToResponse(Restaurant r) => new()
    {
        Id = r.Id,
        OrganizationId = r.OrganizationId,
        Name = r.Name,
        Slug = r.Slug,
        Address = r.Address,
        Timezone = r.Timezone,
        Currency = r.Currency,
        IsActive = r.IsActive,
    };

    private static InvitationResponse ToResponse(Invitation i) => new()
    {
        Id = i.Id,
        Email = i.Email,
        RestaurantId = i.RestaurantId,
        Role = i.Role.ToString(),
        Status = i.Status,
        Token = i.Token,
        ExpiresAt = i.ExpiresAt,
        AcceptPath = $"/accept-invite?token={i.Token}",
    };

    private async Task<string> UniqueSlugAsync(string name, bool isOrg)
    {
        var baseSlug = Slugify(name);
        var slug = baseSlug;
        var i = 1;

        while (isOrg
            ? await _db.Organizations.AnyAsync(o => o.Slug == slug)
            : await _db.Restaurants.AnyAsync(r => r.Slug == slug))
        {
            slug = $"{baseSlug}-{++i}";
        }
        return slug;
    }

    private static string Slugify(string raw)
    {
        var normalized = raw.Trim().ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or '-' or '_') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return string.IsNullOrEmpty(slug) ? Guid.NewGuid().ToString("n")[..8] : slug;
    }
}

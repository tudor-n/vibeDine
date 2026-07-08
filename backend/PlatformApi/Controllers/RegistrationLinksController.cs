using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlatformApi.Auth;
using PlatformApi.Data;
using PlatformApi.DTO.Requests;
using PlatformApi.DTO.Responses;
using PlatformApi.Models;

namespace PlatformApi.Controllers;

/// <summary>
/// One-time provisioning links the platform team generates (after a paid subscription) so a
/// client can register a new organization or add a location in the restaurant app.
/// </summary>
[ApiController]
[Authorize]
[Route("api/platform/registration-links")]
public class RegistrationLinksController : ControllerBase
{
    private readonly PlatformDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<RegistrationLinksController> _logger;

    public RegistrationLinksController(PlatformDbContext db, IConfiguration config, ILogger<RegistrationLinksController> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    private string BaseUrl => _config["RegistrationLink:BaseUrl"] ?? "http://localhost:3001/register";

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status)
    {
        var query = _db.RegistrationLinks.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(l => l.Status == status);

        var links = await query.OrderByDescending(l => l.CreatedAt).ToListAsync();

        var orgIds = links.Where(l => l.OrganizationId != null).Select(l => l.OrganizationId!.Value).Distinct().ToList();
        var orgNames = await _db.Organizations
            .Where(o => orgIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name);

        var result = links.Select(l =>
            ToDto(l, l.OrganizationId is Guid oid && orgNames.TryGetValue(oid, out var n) ? n : null));

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRegistrationLinkRequest request)
    {
        var type = ParseType(request.Type);
        if (type is null)
            return BadRequest(new { message = "type must be 'new_organization' or 'new_location'." });

        string? orgName = null;
        if (type == RegistrationLinkType.NewLocation)
        {
            if (request.OrganizationId is not Guid oid)
                return BadRequest(new { message = "organizationId is required for a 'new_location' link." });

            var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == oid);
            if (org is null) return BadRequest(new { message = "Organization not found." });
            orgName = org.Name;
        }

        var link = new RegistrationLink
        {
            Type = type.Value,
            OrganizationId = type == RegistrationLinkType.NewLocation ? request.OrganizationId : null,
            Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label!.Trim(),
            IntendedEmail = string.IsNullOrWhiteSpace(request.IntendedEmail) ? null : request.IntendedEmail!.Trim(),
            Status = RegistrationLinkStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(request.ExpiresInDays is > 0 ? request.ExpiresInDays!.Value : 7),
            CreatedByPlatformAdminId = User.PlatformAdminId() ?? Guid.Empty,
            CreatedAt = DateTime.UtcNow,
        };

        _db.RegistrationLinks.Add(link);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Created registration link {Id} ({Type})", link.Id, link.Type);

        return Created((string?)null, ToDto(link, orgName));
    }

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var link = await _db.RegistrationLinks.FindAsync(id);
        if (link is null) return NotFound(new { message = "Link not found." });
        if (link.Status == RegistrationLinkStatus.Used)
            return Conflict(new { message = "This link has already been used." });

        link.Status = RegistrationLinkStatus.Revoked;
        await _db.SaveChangesAsync();

        string? orgName = null;
        if (link.OrganizationId is Guid oid)
            orgName = await _db.Organizations.Where(o => o.Id == oid).Select(o => o.Name).FirstOrDefaultAsync();

        return Ok(ToDto(link, orgName));
    }

    private RegistrationLinkDto ToDto(RegistrationLink l, string? orgName) =>
        new(
            l.Id,
            l.Token,
            TypeToString(l.Type),
            l.OrganizationId,
            orgName,
            l.Label,
            l.IntendedEmail,
            l.Status,
            $"{BaseUrl}?token={l.Token}",
            l.ExpiresAt,
            l.CreatedAt,
            l.UsedAt);

    private static RegistrationLinkType? ParseType(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "new_organization" or "neworganization" => RegistrationLinkType.NewOrganization,
        "new_location" or "newlocation" => RegistrationLinkType.NewLocation,
        _ => null,
    };

    private static string TypeToString(RegistrationLinkType type) => type switch
    {
        RegistrationLinkType.NewOrganization => "new_organization",
        RegistrationLinkType.NewLocation => "new_location",
        _ => "unknown",
    };
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlatformApi.Data;
using PlatformApi.DTO.Requests;
using PlatformApi.DTO.Responses;
using PlatformApi.Models;

namespace PlatformApi.Controllers;

/// <summary>Manages the admins and hires (memberships) scoped to a single location.</summary>
[ApiController]
[Authorize]
[Route("api/platform/locations/{locationId:guid}/members")]
public class MembersController : ControllerBase
{
    private readonly PlatformDbContext _db;
    private readonly ILogger<MembersController> _logger;

    public MembersController(PlatformDbContext db, ILogger<MembersController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid locationId)
    {
        if (!await _db.Restaurants.AnyAsync(r => r.Id == locationId))
            return NotFound(new { message = "Location not found." });

        var rows = await _db.Memberships
            .Where(m => m.RestaurantId == locationId)
            .OrderBy(m => m.Role).ThenBy(m => m.CreatedAt)
            .Select(m => new
            {
                m.Id,
                m.UserId,
                FullName = m.User!.FullName,
                m.User.Email,
                m.Role,
                m.Status,
                m.RestaurantId,
                m.CreatedAt,
            })
            .ToListAsync();

        var members = rows.Select(x =>
            new MemberDto(x.Id, x.UserId, x.FullName, x.Email, x.Role.ToString(), x.Status, x.RestaurantId, x.CreatedAt));

        return Ok(members);
    }

    [HttpPatch("{membershipId:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid locationId, Guid membershipId, [FromBody] UpdateMemberStatusRequest request)
    {
        var allowed = new[] { MembershipStatus.Active, MembershipStatus.Rejected, MembershipStatus.PendingApproval };
        if (!allowed.Contains(request.Status))
            return BadRequest(new { message = "Status must be 'active', 'rejected' or 'pending_approval'." });

        var membership = await _db.Memberships
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.RestaurantId == locationId);
        if (membership is null) return NotFound(new { message = "Member not found." });

        membership.Status = request.Status;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Set membership {Id} status={Status}", membership.Id, membership.Status);

        return Ok(new { membership.Id, membership.Status });
    }

    [HttpDelete("{membershipId:guid}")]
    public async Task<IActionResult> Remove(Guid locationId, Guid membershipId)
    {
        var membership = await _db.Memberships
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.RestaurantId == locationId);
        if (membership is null) return NotFound(new { message = "Member not found." });

        _db.Memberships.Remove(membership);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Removed membership {Id}", membership.Id);

        return NoContent();
    }

    /// <summary>Assigns an existing user as the admin of this location.</summary>
    [HttpPost]
    public async Task<IActionResult> AssignAdmin(Guid locationId, [FromBody] AssignAdminRequest request)
    {
        var location = await _db.Restaurants.FindAsync(locationId);
        if (location is null) return NotFound(new { message = "Location not found." });

        var email = request.Email.Trim().ToLower();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email);
        if (user is null)
            return NotFound(new
            {
                message = "No account with that email exists yet. Send the person a registration link so they can create an account first.",
            });

        var existing = await _db.Memberships
            .FirstOrDefaultAsync(m => m.UserId == user.Id && m.RestaurantId == locationId);

        if (existing is not null)
        {
            if (existing.Role == MembershipRole.RestaurantAdmin && existing.Status == MembershipStatus.Active)
                return Conflict(new { message = "This user is already an admin of this location." });

            existing.Role = MembershipRole.RestaurantAdmin;
            existing.Status = MembershipStatus.Active;
            await _db.SaveChangesAsync();
            return Ok(ToDto(existing, user));
        }

        var membership = new Membership
        {
            UserId = user.Id,
            OrganizationId = location.OrganizationId,
            RestaurantId = location.Id,
            Role = MembershipRole.RestaurantAdmin,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Memberships.Add(membership);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Assigned user {UserId} as admin of location {LocationId}", user.Id, locationId);

        return Created((string?)null, ToDto(membership, user));
    }

    private static MemberDto ToDto(Membership m, AppUser user) =>
        new(m.Id, user.Id, user.FullName, user.Email, m.Role.ToString(), m.Status, m.RestaurantId, m.CreatedAt);
}

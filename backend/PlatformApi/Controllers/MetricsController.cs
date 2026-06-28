using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlatformApi.Data;
using PlatformApi.DTO.Responses;
using PlatformApi.Models;

namespace PlatformApi.Controllers;

[ApiController]
[Authorize]
[Route("api/platform/metrics")]
public class MetricsController : ControllerBase
{
    private readonly PlatformDbContext _db;

    public MetricsController(PlatformDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var organizations = await _db.Organizations.CountAsync();
        var locations = await _db.Restaurants.CountAsync();
        var activeLocations = await _db.Restaurants.CountAsync(r => r.IsActive);
        var admins = await _db.Memberships.CountAsync(m => m.Role == MembershipRole.RestaurantAdmin);
        var hires = await _db.Memberships.CountAsync(m => m.Role == MembershipRole.Waiter);
        var pendingHires = await _db.Memberships.CountAsync(m =>
            m.Role == MembershipRole.Waiter && m.Status == MembershipStatus.PendingApproval);

        return Ok(new MetricsResponse(
            organizations,
            locations,
            activeLocations,
            locations - activeLocations,
            admins,
            hires,
            pendingHires));
    }
}

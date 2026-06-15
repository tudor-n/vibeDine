using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Hubs;
using RestaurantApi.Models;
using RestaurantApi.Tenancy;

namespace RestaurantApi.Controllers;

[ApiController]
[Route("api")]
public class HelpController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly IHubContext<CustomerHub> _customerHub;
    private readonly ITenantAccess _access;
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    public HelpController(AppDbContext db, IHubContext<StaffHub> staffHub, IHubContext<CustomerHub> customerHub, ITenantAccess access)
    {
        _db = db;
        _staffHub = staffHub;
        _customerHub = customerHub;
        _access = access;
    }

    private Guid UserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    [HttpPost("restaurants/{restaurantId:guid}/tables/{tableId:guid}/call-waiter")]
    public async Task<IActionResult> CallWaiter(Guid restaurantId, Guid tableId)
    {
        var table = await _db.Tables.FirstOrDefaultAsync(t => t.Id == tableId);
        if (table is null) return NotFound(new { message = "Table not found." });

        var cutoff = DateTime.UtcNow - Cooldown;
        var existing = await _db.HelpRequests
            .Where(h => h.TableId == tableId && h.Status == "open" && h.CreatedAt > cutoff)
            .OrderByDescending(h => h.CreatedAt)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            var retry = (int)Math.Ceiling((existing.CreatedAt + Cooldown - DateTime.UtcNow).TotalSeconds);
            Response.Headers["Retry-After"] = retry.ToString();
            return StatusCode(429, new { message = "Already called.", retryAfterSeconds = retry });
        }

        // Stamp RestaurantId explicitly so the row is correct even if the ambient
        // tenant ever fails to resolve (StampTenant only fills Guid.Empty values).
        var req = new HelpRequest { TableId = tableId, RestaurantId = restaurantId };
        _db.HelpRequests.Add(req);
        await _db.SaveChangesAsync();

        await _staffHub.Clients
            .Group(TenantGroups.Role(restaurantId, "WaitStaff"))
            .SendAsync("HelpRequested", new
            {
                requestId = req.Id,
                tableId,
                tableNumber = table.TableNumber,
                createdAt = req.CreatedAt
            });

        // Return the cooldown end as an explicit ISO-8601 (round-trip "O") string so the
        // client's Date.parse() is never ambiguous about the timezone.
        return Ok(new
        {
            requestId = req.Id,
            cooldownEndsAt = (req.CreatedAt + Cooldown).ToString("O"),
        });
    }

    // Staff dashboard (admin + waiter) lists the open calls. [Authorize] requires a
    // signed-in user; the explicit tenant-access check stops a signed-in user from
    // reading another restaurant's calls by swapping the {restaurantId} in the URL.
    [Authorize]
    [HttpGet("restaurants/{restaurantId:guid}/help-requests")]
    public async Task<IActionResult> List(Guid restaurantId)
    {
        if (!await _access.CanAccessRestaurantAsync(UserId, restaurantId)) return Forbid();

        var items = await _db.HelpRequests
            .Where(h => h.Status == "open")
            .OrderBy(h => h.CreatedAt)
            .Join(_db.Tables, h => h.TableId, t => t.Id, (h, t) => new
            {
                requestId = h.Id,
                tableId = h.TableId,
                tableNumber = t.TableNumber,
                createdAt = h.CreatedAt
            })
            .ToListAsync();
        return Ok(items);
    }

    [Authorize]
    [HttpPost("restaurants/{restaurantId:guid}/help-requests/{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid restaurantId, Guid id)
    {
        if (!await _access.CanAccessRestaurantAsync(UserId, restaurantId)) return Forbid();

        var req = await _db.HelpRequests.FirstOrDefaultAsync(h => h.Id == id);
        if (req is null) return NotFound();
        if (req.Status != "open") return Ok(new { alreadyAcknowledged = true });

        var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        req.Status = "acknowledged";
        req.AcknowledgedAt = DateTime.UtcNow;
        req.AcknowledgedByUserId = Guid.TryParse(userIdRaw, out var uid) ? uid : null;
        await _db.SaveChangesAsync();

        await _staffHub.Clients
            .Group(TenantGroups.Role(restaurantId, "WaitStaff"))
            .SendAsync("HelpAcknowledged", new { requestId = req.Id });

        await _customerHub.Clients
            .Group(TenantGroups.Table(restaurantId, req.TableId))
            .SendAsync("WaiterOnTheWay", new { requestId = req.Id });

        return Ok(new { alreadyAcknowledged = false });
    }
}

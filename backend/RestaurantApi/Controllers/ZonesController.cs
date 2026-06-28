using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.DTOs.Requests;
using RestaurantApi.DTOs.Responses;
using RestaurantApi.Models;

namespace RestaurantApi.Controllers;

/// <summary>
/// Per-location floor-plan zone (table category) management. All queries are
/// tenant-scoped by the global query filter resolved from the {restaurantId}
/// route segment, so a location only ever sees and mutates its own zones.
/// </summary>
[ApiController]
[Authorize]
[Route("api/restaurants/{restaurantId:guid}/admin/zones")]
public class ZonesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ZonesController> _logger;

    public ZonesController(AppDbContext db, ILogger<ZonesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var zones = await _db.TableZones
            .Where(z => z.DeletedAt == null)
            .OrderBy(z => z.SortOrder)
            .Select(z => new ZoneResponse(z.Id, z.Name, z.SortOrder))
            .ToListAsync();

        return Ok(zones);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateZoneRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Zone name is required." });

        // Append to the end of the tab order. Only live zones contribute, so a
        // soft-deleted zone's slot can be reused.
        var maxSortOrder = await _db.TableZones
            .Where(z => z.DeletedAt == null)
            .Select(z => (int?)z.SortOrder)
            .MaxAsync() ?? -1;

        // RestaurantId is auto-stamped on insert by AppDbContext.StampTenant().
        var zone = new TableZone
        {
            Id = Guid.NewGuid(),
            Name = name,
            SortOrder = maxSortOrder + 1,
        };

        _db.TableZones.Add(zone);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Created zone {Id} ({Name})", zone.Id, name);

        return Created((string?)null, new ZoneResponse(zone.Id, zone.Name, zone.SortOrder));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateZoneRequest request)
    {
        var zone = await _db.TableZones.FindAsync(id);
        if (zone == null || zone.DeletedAt != null)
            return NotFound(new { message = "Zone not found." });

        if (request.Name != null)
        {
            var name = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "Zone name is required." });
            zone.Name = name;
        }

        if (request.SortOrder.HasValue)
            zone.SortOrder = request.SortOrder.Value;

        await _db.SaveChangesAsync();
        _logger.LogInformation("Updated zone {Id}", id);

        return Ok(new ZoneResponse(zone.Id, zone.Name, zone.SortOrder));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var zone = await _db.TableZones.FindAsync(id);
        if (zone == null || zone.DeletedAt != null)
            return NotFound(new { message = "Zone not found." });

        // Unassign every table currently in this zone so they don't dangle.
        var tables = await _db.Tables
            .Where(t => t.ZoneId == id)
            .ToListAsync();
        foreach (var table in tables)
            table.ZoneId = null;

        // Soft-delete: keep the row for reporting, hide it from the admin list.
        zone.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Soft-deleted zone {Id} and unassigned {Count} table(s)", id, tables.Count);

        return NoContent();
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantApi.Data;
using RestaurantApi.Models;

namespace RestaurantApi.Controllers;

/// <summary>
/// Per-location table + QR management. All queries are tenant-scoped by the
/// global query filter resolved from the {restaurantId} route segment, so a
/// location only ever sees and mutates its own tables.
/// </summary>
[ApiController]
[Authorize]
[Route("api/restaurants/{restaurantId:guid}/admin/tables")]
public class TablesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<TablesController> _logger;

    public TablesController(AppDbContext db, ILogger<TablesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public record TableResponse(Guid Id, string? TableNumber, Guid QrToken, Guid? ZoneId, double PositionX, double PositionY, int Capacity, string Shape);
    public record CreateTableRequest(string? TableNumber, Guid? ZoneId, double? PositionX, double? PositionY, int? Capacity, string? Shape);
    public record UpdateTableRequest(string? TableNumber, Guid? ZoneId, double? PositionX, double? PositionY, int? Capacity, string? Shape);

    // Seats a table may sit. The floor-plan draws one chair per seat, so keep the
    // ceiling in step with what the icon can render (FloorPlanView seat layout).
    private const int MinCapacity = 1;
    private const int MaxCapacity = 12;
    private const int DefaultCapacity = 4;

    private static int ClampCapacity(int value) => Math.Clamp(value, MinCapacity, MaxCapacity);

    // Floor-plan shapes the icon can render. Anything else falls back to the default.
    private const string DefaultShape = "square";
    private static readonly HashSet<string> AllowedShapes =
        new(StringComparer.OrdinalIgnoreCase) { "round", "square", "rectangle", "oval" };

    private static string NormalizeShape(string? value)
    {
        var s = value?.Trim().ToLowerInvariant();
        return s != null && AllowedShapes.Contains(s) ? s : DefaultShape;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var tables = await _db.Tables
            .Where(t => t.DeletedAt == null && !t.IsAux)
            .OrderBy(t => t.TableNumber)
            .Select(t => new TableResponse(t.Id, t.TableNumber, t.QrToken ?? Guid.Empty, t.ZoneId, t.PositionX, t.PositionY, t.Capacity, t.Shape))
            .ToListAsync();

        return Ok(tables);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTableRequest request)
    {
        var number = request.TableNumber?.Trim();
        if (string.IsNullOrWhiteSpace(number))
            return BadRequest(new { message = "Table number is required." });

        // Only live tables collide — a soft-deleted "5" must not block recreating "5".
        if (await _db.Tables.AnyAsync(t => t.TableNumber == number && t.DeletedAt == null))
            return Conflict(new { message = $"Table \"{number}\" already exists for this location." });

        // RestaurantId is auto-stamped on insert by AppDbContext.StampTenant().
        var table = new RestaurantTable
        {
            Id = Guid.NewGuid(),
            TableNumber = number,
            QrToken = Guid.NewGuid(),
            ZoneId = request.ZoneId,
            PositionX = request.PositionX ?? 0,
            PositionY = request.PositionY ?? 0,
            Capacity = ClampCapacity(request.Capacity ?? DefaultCapacity),
            Shape = NormalizeShape(request.Shape),
        };

        _db.Tables.Add(table);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Created table {Id} (#{Number})", table.Id, number);

        return Created((string?)null, new TableResponse(table.Id, table.TableNumber, table.QrToken!.Value, table.ZoneId, table.PositionX, table.PositionY, table.Capacity, table.Shape));
    }

    /// <summary>Rotates the QR token, invalidating any previously printed code.</summary>
    [HttpPost("{id:guid}/regenerate")]
    public async Task<IActionResult> Regenerate(Guid id)
    {
        // Aux (parking) tables live off the floor plan and have no QR — they are
        // invisible to this controller (see AuxTablesController).
        var table = await _db.Tables.FindAsync(id);
        if (table == null || table.DeletedAt != null || table.IsAux)
            return NotFound(new { message = "Table not found." });

        table.QrToken = Guid.NewGuid();
        await _db.SaveChangesAsync();
        _logger.LogInformation("Regenerated QR token for table {Id}", id);

        return Ok(new TableResponse(table.Id, table.TableNumber, table.QrToken!.Value, table.ZoneId, table.PositionX, table.PositionY, table.Capacity, table.Shape));
    }

    /// <summary>Updates only the provided (non-null) fields on a table.</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTableRequest request)
    {
        var table = await _db.Tables.FindAsync(id);
        if (table == null || table.DeletedAt != null || table.IsAux)
            return NotFound(new { message = "Table not found." });

        if (request.TableNumber != null)
        {
            var number = request.TableNumber.Trim();
            if (string.IsNullOrWhiteSpace(number))
                return BadRequest(new { message = "Table number is required." });

            // Mirror Create's uniqueness rule: only live tables collide, and a
            // table never conflicts with itself.
            if (number != table.TableNumber &&
                await _db.Tables.AnyAsync(t => t.TableNumber == number && t.DeletedAt == null && t.Id != id))
                return Conflict(new { message = $"Table \"{number}\" already exists for this location." });

            table.TableNumber = number;
        }

        // Only reassign the zone when a value is supplied, so a position-only
        // PATCH (drag-to-reposition, which omits zoneId) never clears the table's
        // zone. Unassigning to "no zone" happens server-side on zone delete.
        if (request.ZoneId.HasValue)
            table.ZoneId = request.ZoneId;

        if (request.PositionX.HasValue)
            table.PositionX = request.PositionX.Value;

        if (request.PositionY.HasValue)
            table.PositionY = request.PositionY.Value;

        if (request.Capacity.HasValue)
            table.Capacity = ClampCapacity(request.Capacity.Value);

        if (request.Shape != null)
            table.Shape = NormalizeShape(request.Shape);

        await _db.SaveChangesAsync();
        _logger.LogInformation("Updated table {Id}", id);

        return Ok(new TableResponse(table.Id, table.TableNumber, table.QrToken ?? Guid.Empty, table.ZoneId, table.PositionX, table.PositionY, table.Capacity, table.Shape));
    }

    // Orders that count as "someone is using this table right now". Drafts (a
    // browsing cart, including the one auto-created the instant a QR is scanned),
    // completed, cancelled and merged orders are NOT active — historically the
    // delete blocked on *any* order, so a table could never be removed once it
    // had ever been scanned. These are the same in-flight states the kitchen
    // kanban and the stale-guest purge treat as live.
    private static readonly string[] ActiveOrderStatuses =
        ["pending_dispatch", "submitted", "preparing", "ready"];

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var table = await _db.Tables.FindAsync(id);
        if (table == null || table.DeletedAt != null || table.IsAux)
            return NotFound(new { message = "Table not found." });

        if (await _db.Orders.AnyAsync(o => o.TableId == id
                && o.DeletedAt == null
                && ActiveOrderStatuses.Contains(o.Status)))
            return Conflict(new { message = "Table has an active order in progress and cannot be deleted." });

        // Soft-delete: keep the row (and its order history) so reporting stays
        // intact, but retire the QR token and hide the table from the admin list.
        table.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Soft-deleted table {Id}", id);

        return NoContent();
    }
}

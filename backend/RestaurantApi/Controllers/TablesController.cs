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

    public record TableResponse(Guid Id, string? TableNumber, Guid QrToken);
    public record CreateTableRequest(string? TableNumber);

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var tables = await _db.Tables
            .OrderBy(t => t.TableNumber)
            .Select(t => new TableResponse(t.Id, t.TableNumber, t.QrToken ?? Guid.Empty))
            .ToListAsync();

        return Ok(tables);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTableRequest request)
    {
        var number = request.TableNumber?.Trim();
        if (string.IsNullOrWhiteSpace(number))
            return BadRequest(new { message = "Table number is required." });

        if (await _db.Tables.AnyAsync(t => t.TableNumber == number))
            return Conflict(new { message = $"Table \"{number}\" already exists for this location." });

        // RestaurantId is auto-stamped on insert by AppDbContext.StampTenant().
        var table = new RestaurantTable
        {
            Id = Guid.NewGuid(),
            TableNumber = number,
            QrToken = Guid.NewGuid(),
        };

        _db.Tables.Add(table);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Created table {Id} (#{Number})", table.Id, number);

        return Created((string?)null, new TableResponse(table.Id, table.TableNumber, table.QrToken!.Value));
    }

    /// <summary>Rotates the QR token, invalidating any previously printed code.</summary>
    [HttpPost("{id:guid}/regenerate")]
    public async Task<IActionResult> Regenerate(Guid id)
    {
        var table = await _db.Tables.FindAsync(id);
        if (table == null) return NotFound(new { message = "Table not found." });

        table.QrToken = Guid.NewGuid();
        await _db.SaveChangesAsync();
        _logger.LogInformation("Regenerated QR token for table {Id}", id);

        return Ok(new TableResponse(table.Id, table.TableNumber, table.QrToken!.Value));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var table = await _db.Tables.FindAsync(id);
        if (table == null) return NotFound(new { message = "Table not found." });

        if (await _db.Orders.AnyAsync(o => o.TableId == id))
            return Conflict(new { message = "Table has orders and cannot be deleted." });

        _db.Tables.Remove(table);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Deleted table {Id}", id);

        return NoContent();
    }
}

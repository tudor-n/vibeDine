using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlatformApi.Common;
using PlatformApi.Data;
using PlatformApi.DTO.Requests;
using PlatformApi.DTO.Responses;
using PlatformApi.Models;

namespace PlatformApi.Controllers;

[ApiController]
[Authorize]
[Route("api/platform/locations")]
public class LocationsController : ControllerBase
{
    private readonly PlatformDbContext _db;
    private readonly ILogger<LocationsController> _logger;

    public LocationsController(PlatformDbContext db, ILogger<LocationsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? organizationId,
        [FromQuery] string? search,
        [FromQuery] string? status)
    {
        var query = _db.Restaurants.AsQueryable();

        if (organizationId is Guid oid)
            query = query.Where(r => r.OrganizationId == oid);

        if (status == "active") query = query.Where(r => r.IsActive);
        else if (status == "inactive") query = query.Where(r => !r.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(r =>
                r.Name.ToLower().Contains(s) ||
                (r.Address != null && r.Address.ToLower().Contains(s)) ||
                r.Organization!.Name.ToLower().Contains(s));
        }

        var items = await query
            .OrderBy(r => r.Organization!.Name).ThenBy(r => r.Name)
            .Select(r => new LocationListItem(
                r.Id, r.OrganizationId, r.Organization!.Name, r.Name, r.Slug, r.Address, r.IsActive,
                r.Timezone, r.Currency, r.Locale, r.CreatedAt,
                r.Memberships.Count(m => m.Role == MembershipRole.RestaurantAdmin),
                r.Memberships.Count(m => m.Role == MembershipRole.Waiter)))
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var r = await _db.Restaurants.Include(x => x.Organization).FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound(new { message = "Location not found." });

        var rows = await _db.Memberships
            .Where(m => m.RestaurantId == id)
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

        var members = rows
            .Select(x => new MemberDto(x.Id, x.UserId, x.FullName, x.Email, x.Role.ToString(), x.Status, x.RestaurantId, x.CreatedAt))
            .ToList();

        var admins = members.Where(m => m.Role == nameof(MembershipRole.RestaurantAdmin)).ToList();
        var hires = members.Where(m => m.Role == nameof(MembershipRole.Waiter)).ToList();

        return Ok(new LocationDetail(
            r.Id, r.OrganizationId, r.Organization!.Name, r.Name, r.Slug, r.Address, r.IsActive,
            r.Timezone, r.Currency, r.Locale, r.CreatedAt, admins, hires));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLocationRequest request)
    {
        var org = await _db.Organizations.FindAsync(request.OrganizationId);
        if (org is null) return BadRequest(new { message = "Organization not found." });

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Name is required." });

        var location = new Restaurant
        {
            OrganizationId = org.Id,
            Name = name,
            Slug = Slug.From(name),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address!.Trim(),
            Timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "Europe/Bucharest" : request.Timezone!.Trim(),
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "RON" : request.Currency!.Trim(),
            Locale = string.IsNullOrWhiteSpace(request.Locale) ? "ro-RO" : request.Locale!.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Restaurants.Add(location);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Created location {Id} ({Name}) under org {OrgId}", location.Id, location.Name, org.Id);

        return Created((string?)null, new LocationListItem(
            location.Id, location.OrganizationId, org.Name, location.Name, location.Slug, location.Address,
            location.IsActive, location.Timezone, location.Currency, location.Locale, location.CreatedAt, 0, 0));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLocationRequest request)
    {
        var location = await _db.Restaurants.Include(x => x.Organization).FirstOrDefaultAsync(x => x.Id == id);
        if (location is null) return NotFound(new { message = "Location not found." });

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0) return BadRequest(new { message = "Name cannot be empty." });
            location.Name = name;
        }
        if (request.Address is not null)
            location.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        if (request.Timezone is not null && request.Timezone.Trim().Length > 0)
            location.Timezone = request.Timezone.Trim();
        if (request.Currency is not null && request.Currency.Trim().Length > 0)
            location.Currency = request.Currency.Trim();
        if (request.Locale is not null && request.Locale.Trim().Length > 0)
            location.Locale = request.Locale.Trim();

        await _db.SaveChangesAsync();

        return Ok(new LocationListItem(
            location.Id, location.OrganizationId, location.Organization!.Name, location.Name, location.Slug,
            location.Address, location.IsActive, location.Timezone, location.Currency, location.Locale,
            location.CreatedAt, 0, 0));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] UpdateLocationStatusRequest request)
    {
        var location = await _db.Restaurants.FindAsync(id);
        if (location is null) return NotFound(new { message = "Location not found." });

        location.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Set location {Id} active={Active}", location.Id, location.IsActive);

        return Ok(new { location.Id, location.IsActive });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool force = false)
    {
        var location = await _db.Restaurants.FindAsync(id);
        if (location is null) return NotFound(new { message = "Location not found." });

        if (!force)
            return Conflict(new
            {
                message = "Deleting a location also removes its menu, tables and order history. " +
                          "Deactivate it instead, or pass ?force=true to permanently delete.",
            });

        try
        {
            var memberships = await _db.Memberships.Where(m => m.RestaurantId == id).ToListAsync();
            _db.Memberships.RemoveRange(memberships);
            _db.Restaurants.Remove(location);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Hard delete of location {Id} blocked by related data", id);
            return Conflict(new
            {
                message = "This location still has related data (orders/menu) that prevents deletion. Deactivate it instead.",
            });
        }

        _logger.LogInformation("Deleted location {Id} ({Name})", location.Id, location.Name);
        return NoContent();
    }
}

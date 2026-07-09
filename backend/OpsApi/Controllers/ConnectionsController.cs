using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpsApi.Auth;
using OpsApi.Data;
using OpsApi.DTO.Requests;
using OpsApi.DTO.Responses;
using OpsApi.Models;
using OpsApi.Providers;
using OpsApi.Providers.Abstractions;
using OpsApi.Security;
using OpsApi.Services;

namespace OpsApi.Controllers;

[ApiController]
[Authorize]
[Route("api/ops/connections")]
public sealed class ConnectionsController : OpsControllerBase
{
    private readonly OpsDbContext _db;
    private readonly ProviderRegistry _registry;
    private readonly ITokenProtector _protector;
    private readonly AuditLogger _audit;

    public ConnectionsController(OpsDbContext db, ProviderRegistry registry, ITokenProtector protector, AuditLogger audit)
    {
        _db = db;
        _registry = registry;
        _protector = protector;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var conns = await _db.Connections.OrderBy(c => c.Name).ToListAsync();
        return Ok(conns.Select(MapDto));
    }

    /// <summary>Fleet overview — every server/connection at a glance (multi-server dashboard).</summary>
    [HttpGet("fleet")]
    public async Task<IActionResult> Fleet()
    {
        var conns = await _db.Connections.OrderBy(c => c.Role).ThenBy(c => c.Name).ToListAsync();
        var items = conns.Select(c => new FleetItemDto(
            c.Id, c.Provider, c.Name, c.Role, c.Status, c.StatusDetail, c.AccountLabel, c.LastCheckedAt)).ToList();
        var active = items.Count(i => i.Status == ConnectionStatus.Active);
        return Ok(new FleetOverviewDto(items.Count, active, items.Count - active, items));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var conn = await _db.Connections.FindAsync(id);
        return conn is null ? NotFound(new { message = "Connection not found." }) : Ok(MapDto(conn));
    }

    /// <summary>Easy connect: validate the credential live, encrypt it, auto-discover the account.</summary>
    [HttpPost]
    public Task<IActionResult> Create([FromBody] CreateConnectionRequest req) => Guarded(async () =>
    {
        if (!_registry.TryGet(req.Provider, out var provider))
            return BadRequest(new { message = $"Unknown provider '{req.Provider}'." });
        if (string.IsNullOrWhiteSpace(req.Token))
            return BadRequest(new { message = "A token/credential is required." });

        var metadata = req.Metadata ?? new Dictionary<string, string?>();
        var result = await provider.ValidateAsync(new ProviderCredentials(req.Token, req.TokenKind), metadata, HttpContext.RequestAborted);
        if (!result.Success)
        {
            await _audit.RecordAsync(User, "connection.create", "failure", req.Provider,
                summary: $"Validation failed: {result.Detail}");
            return BadRequest(new { message = result.Detail ?? "The credential was rejected by the provider." });
        }

        var conn = new Connection
        {
            Provider = req.Provider,
            Name = string.IsNullOrWhiteSpace(req.Name) ? (result.AccountLabel ?? req.Provider) : req.Name.Trim(),
            Role = string.IsNullOrWhiteSpace(req.Role) ? null : req.Role.Trim(),
            TokenCipher = _protector.Protect(req.Token),
            TokenKind = req.TokenKind,
            AccountLabel = result.AccountLabel,
            MetadataJson = metadata.Count > 0 ? JsonSerializer.Serialize(metadata) : null,
            CapabilitiesJson = JsonSerializer.Serialize(result.Capabilities),
            Status = ConnectionStatus.Active,
            StatusDetail = result.Detail,
            LastCheckedAt = DateTime.UtcNow,
            CreatedByAdminId = User.OpsAdminId() ?? Guid.Empty,
        };

        _db.Connections.Add(conn);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(User, "connection.create", "success", req.Provider, conn.Id,
            "connection", conn.Id.ToString(), $"Connected {result.AccountLabel ?? req.Provider}");

        return Created((string?)null, MapDto(conn));
    });

    /// <summary>Re-validate a stored connection and refresh its status.</summary>
    [HttpPost("{id:guid}/test")]
    public Task<IActionResult> Test(Guid id) => Guarded(async () =>
    {
        var conn = await _db.Connections.FindAsync(id);
        if (conn is null) return NotFound(new { message = "Connection not found." });
        if (!_registry.TryGet(conn.Provider, out var provider))
            return BadRequest(new { message = "Unknown provider." });

        var token = _protector.Unprotect(conn.TokenCipher);
        var metadata = ConnectionService.ParseMetadata(conn.MetadataJson);
        var result = await provider.ValidateAsync(new ProviderCredentials(token, conn.TokenKind), metadata, HttpContext.RequestAborted);

        conn.Status = result.Success ? ConnectionStatus.Active : ConnectionStatus.Invalid;
        conn.StatusDetail = result.Detail;
        conn.LastCheckedAt = DateTime.UtcNow;
        if (result.AccountLabel is not null) conn.AccountLabel = result.AccountLabel;
        await _db.SaveChangesAsync();

        await _audit.RecordAsync(User, "connection.test", result.Success ? "success" : "failure",
            conn.Provider, conn.Id, "connection", conn.Id.ToString(), result.Detail);
        return Ok(new ConnectionTestResult(result.Success, result.AccountLabel, result.Detail));
    });

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateConnectionRequest req)
    {
        var conn = await _db.Connections.FindAsync(id);
        if (conn is null) return NotFound(new { message = "Connection not found." });

        if (req.Name is not null) conn.Name = req.Name.Trim();
        if (req.Role is not null) conn.Role = string.IsNullOrWhiteSpace(req.Role) ? null : req.Role.Trim();
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(User, "connection.update", "success", conn.Provider, conn.Id,
            "connection", conn.Id.ToString(), $"Updated {conn.Name}");
        return Ok(MapDto(conn));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var conn = await _db.Connections.FindAsync(id);
        if (conn is null) return NotFound(new { message = "Connection not found." });

        _db.Connections.Remove(conn);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync(User, "connection.delete", "success", conn.Provider, conn.Id,
            "connection", conn.Id.ToString(), $"Removed {conn.Name}");
        return NoContent();
    }

    private ConnectionDto MapDto(Connection c)
    {
        var caps = _registry.TryGet(c.Provider, out var p) ? p.Capabilities : new ProviderCapabilities();
        return new ConnectionDto(c.Id, c.Provider, c.Name, c.Role, c.AccountLabel, c.Status, c.StatusDetail,
            c.CreatedAt, c.LastCheckedAt, caps, ConnectionService.ParseMetadata(c.MetadataJson));
    }
}

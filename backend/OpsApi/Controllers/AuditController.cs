using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpsApi.Data;
using OpsApi.DTO.Responses;
using OpsApi.Services;

namespace OpsApi.Controllers;

/// <summary>
/// Two audit surfaces: our own trail of every console action (`/api/ops/audit`), and the provider's
/// own audit feed where supported (`/api/ops/connections/{id}/provider-audit`).
/// </summary>
[ApiController]
[Authorize]
[Route("api/ops")]
public sealed class AuditController : OpsControllerBase
{
    private readonly OpsDbContext _db;
    private readonly ConnectionService _connections;

    public AuditController(OpsDbContext db, ConnectionService connections)
    {
        _db = db;
        _connections = connections;
    }

    [HttpGet("audit")]
    public async Task<IActionResult> OwnAudit([FromQuery] Guid? connectionId, [FromQuery] int limit = 100)
    {
        var q = _db.AuditLog.AsQueryable();
        if (connectionId is not null) q = q.Where(a => a.ConnectionId == connectionId);

        var rows = await q.OrderByDescending(a => a.CreatedAt).Take(Math.Clamp(limit, 1, 500)).ToListAsync();
        return Ok(rows.Select(a => new AuditEntryDto(
            a.Id, a.ActorEmail, a.Action, a.Provider, a.ConnectionId,
            a.TargetType, a.TargetId, a.Summary, a.Result, a.CreatedAt)));
    }

    [HttpGet("connections/{connectionId:guid}/provider-audit")]
    public Task<IActionResult> ProviderAudit(Guid connectionId, [FromQuery] string? workspaceId, [FromQuery] int first = 50) =>
        Guarded(async () =>
        {
            var (conn, provider, ctx) = await _connections.ResolveAsync(connectionId, HttpContext.RequestAborted);
            if (!provider.Capabilities.ProviderAudit)
                return Ok(Array.Empty<object>()); // empty; UI gates on Capabilities.ProviderAudit

            var entries = await provider.GetAuditLogsAsync(ctx, workspaceId, Math.Clamp(first, 1, 200), HttpContext.RequestAborted);
            return Ok(entries);
        });
}

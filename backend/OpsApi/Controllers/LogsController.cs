using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpsApi.Data;
using OpsApi.DTO.Responses;
using OpsApi.Models;
using OpsApi.Providers.Abstractions;
using OpsApi.Services;

namespace OpsApi.Controllers;

/// <summary>
/// Log access: fetch build/deploy/http logs live (with the provider's filter syntax passed through),
/// optionally archiving them, plus query the local archive that survives the provider's short retention.
/// </summary>
[ApiController]
[Authorize]
[Route("api/ops/connections/{connectionId:guid}/logs")]
public sealed class LogsController : OpsControllerBase
{
    private readonly ConnectionService _connections;
    private readonly OpsDbContext _db;

    public LogsController(ConnectionService connections, OpsDbContext db)
    {
        _connections = connections;
        _db = db;
    }

    [HttpGet]
    public Task<IActionResult> Fetch(
        Guid connectionId,
        [FromQuery] string logType = "deploy",
        [FromQuery] string? deploymentId = null,
        [FromQuery] string? projectId = null,
        [FromQuery] string? serviceId = null,
        [FromQuery] string? environmentId = null,
        [FromQuery] string? filter = null,
        [FromQuery] int limit = 200,
        [FromQuery] bool archive = false) =>
        Guarded(async () =>
        {
            var (conn, provider, ctx) = await _connections.ResolveAsync(connectionId, HttpContext.RequestAborted);
            var query = new LogQuery(logType, deploymentId, projectId, serviceId, environmentId, filter, Math.Clamp(limit, 1, 1000));
            var entries = await provider.GetLogsAsync(ctx, query, HttpContext.RequestAborted);

            if (archive && entries.Count > 0)
            {
                foreach (var e in entries)
                    _db.LogArchive.Add(new LogArchiveEntry
                    {
                        ConnectionId = conn.Id,
                        Provider = conn.Provider,
                        ProjectId = projectId,
                        ServiceId = serviceId,
                        EnvironmentId = environmentId,
                        DeploymentId = deploymentId,
                        LogType = logType,
                        Severity = e.Severity,
                        Message = e.Message,
                        AttributesJson = e.Attributes is null ? null : System.Text.Json.JsonSerializer.Serialize(e.Attributes),
                        Timestamp = e.Timestamp,
                    });
                await _db.SaveChangesAsync();
            }

            return Ok(entries.Select(e => new LogEntryDto(e.Timestamp, e.Message, e.Severity, e.LogType, e.Attributes)));
        });

    /// <summary>Query the local archive (history that outlives the provider's retention window).</summary>
    [HttpGet("archive")]
    public async Task<IActionResult> Archive(
        Guid connectionId,
        [FromQuery] string? deploymentId = null,
        [FromQuery] string? logType = null,
        [FromQuery] int limit = 500)
    {
        var q = _db.LogArchive.Where(l => l.ConnectionId == connectionId);
        if (!string.IsNullOrEmpty(deploymentId)) q = q.Where(l => l.DeploymentId == deploymentId);
        if (!string.IsNullOrEmpty(logType)) q = q.Where(l => l.LogType == logType);

        var rows = await q.OrderByDescending(l => l.Timestamp).Take(Math.Clamp(limit, 1, 2000)).ToListAsync();
        return Ok(rows.Select(l => new LogEntryDto(l.Timestamp, l.Message, l.Severity, l.LogType, null)));
    }
}

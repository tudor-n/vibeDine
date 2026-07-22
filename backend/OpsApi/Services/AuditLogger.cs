using System.Security.Claims;
using System.Text.Json;
using OpsApi.Auth;
using OpsApi.Data;
using OpsApi.Models;

namespace OpsApi.Services;

/// <summary>
/// Writes to our own audit trail (`ops_audit_log`) — every action taken through the console,
/// success or failure. This is the compliance record and is independent of any provider's own feed.
/// </summary>
public sealed class AuditLogger
{
    private readonly OpsDbContext _db;

    public AuditLogger(OpsDbContext db) => _db = db;

    public async Task RecordAsync(
        ClaimsPrincipal actor,
        string action,
        string result = "success",
        string? provider = null,
        Guid? connectionId = null,
        string? targetType = null,
        string? targetId = null,
        string? summary = null,
        object? detail = null,
        CancellationToken ct = default)
    {
        _db.AuditLog.Add(new AuditEntry
        {
            ActorAdminId = actor.OpsAdminId(),
            ActorEmail = actor.Email(),
            Action = action,
            Result = result,
            Provider = provider,
            ConnectionId = connectionId,
            TargetType = targetType,
            TargetId = targetId,
            Summary = summary,
            DetailJson = detail is null ? null : JsonSerializer.Serialize(detail),
        });
        await _db.SaveChangesAsync(ct);
    }
}

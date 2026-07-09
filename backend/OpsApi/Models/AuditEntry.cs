namespace OpsApi.Models;

/// <summary>
/// Our own audit trail: every action taken through the console (who, what, when, result).
/// Distinct from a provider's own audit feed. HQ-owned table `ops_audit_log`.
/// </summary>
public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ActorAdminId { get; set; }
    public string? ActorEmail { get; set; }

    /// <summary>Action verb, e.g. "connection.create", "deployment.redeploy", "variables.upsert".</summary>
    public string Action { get; set; } = string.Empty;

    public string? Provider { get; set; }
    public Guid? ConnectionId { get; set; }

    /// <summary>Target type ("deployment", "service", "variable") and its provider id.</summary>
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }

    /// <summary>Short human summary shown in the audit view.</summary>
    public string? Summary { get; set; }

    /// <summary>Request context as JSON (sanitised — never contains secrets).</summary>
    public string? DetailJson { get; set; }

    /// <summary>"success" | "failure".</summary>
    public string Result { get; set; } = "success";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

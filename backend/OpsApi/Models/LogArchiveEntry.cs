namespace OpsApi.Models;

/// <summary>Log stream kinds (mirror the provider's distinct log types).</summary>
public static class LogType
{
    public const string Build = "build";
    public const string Deploy = "deploy";
    public const string Http = "http";
}

/// <summary>
/// A log line ingested from a provider and persisted locally so history survives past the
/// provider's short retention window (Railway keeps only 7 days on Hobby). Table `ops_log_archive`.
/// </summary>
public sealed class LogArchiveEntry
{
    public long Id { get; set; } // bigserial

    public Guid ConnectionId { get; set; }
    public string Provider { get; set; } = string.Empty;

    public string? ProjectId { get; set; }
    public string? ServiceId { get; set; }
    public string? EnvironmentId { get; set; }
    public string? DeploymentId { get; set; }

    public string LogType { get; set; } = OpsApi.Models.LogType.Deploy;
    public string? Severity { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>Structured attributes / extra fields as JSON.</summary>
    public string? AttributesJson { get; set; }

    public DateTime Timestamp { get; set; }
    public DateTime IngestedAt { get; set; } = DateTime.UtcNow;
}

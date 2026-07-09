namespace OpsApi.Providers.Abstractions;

/// <summary>Decrypted credentials handed to a provider for a single operation. Server-side only.</summary>
public sealed record ProviderCredentials(string Token, string? TokenKind = null);

/// <summary>Everything a provider needs to act on behalf of a stored connection.</summary>
public sealed record ProviderContext(
    Guid ConnectionId,
    string Provider,
    ProviderCredentials Credentials,
    IReadOnlyDictionary<string, string?> Metadata);

/// <summary>
/// What a provider can do. Serialized into the connection and returned to the UI so the client
/// renders only supported features. Adding a provider means declaring its own capability set.
/// </summary>
public sealed record ProviderCapabilities
{
    public bool Projects { get; init; }
    public bool Deployments { get; init; }
    public bool TriggerDeploy { get; init; }
    public bool BuildLogs { get; init; }
    public bool DeployLogs { get; init; }
    public bool HttpLogs { get; init; }
    public bool LiveTail { get; init; }
    public bool Variables { get; init; }
    public bool ProviderAudit { get; init; }
    public bool Metrics { get; init; }
    public bool Redeploy { get; init; }
    public bool Restart { get; init; }
    public bool Rollback { get; init; }
    public bool Stop { get; init; }
    public bool Remove { get; init; }

    /// <summary>Service/process overview (the Railway-dashboard-style canvas).</summary>
    public bool Overview { get; init; }

    /// <summary>Credential flavours the connect wizard should offer (e.g. account/workspace/project).</summary>
    public string[] TokenKinds { get; init; } = [];
}

public sealed record ConnectionValidationResult(
    bool Success,
    string? AccountLabel,
    string? Detail,
    ProviderCapabilities Capabilities);

public sealed record ProjectSummary(string Id, string Name, string? Description = null);

public sealed record ServiceSummary(string Id, string Name);

public sealed record EnvironmentSummary(string Id, string Name);

public sealed record ProjectDetail(
    string Id,
    string Name,
    string? Description,
    IReadOnlyList<ServiceSummary> Services,
    IReadOnlyList<EnvironmentSummary> Environments);

public sealed record DeploymentSummary(
    string Id,
    string Status,
    DateTime? CreatedAt,
    string? Url = null,
    string? StaticUrl = null,
    bool CanRedeploy = false,
    bool CanRollback = false);

public sealed record LogEntry(
    DateTime Timestamp,
    string Message,
    string? Severity,
    string LogType,
    IReadOnlyDictionary<string, object?>? Attributes = null);

/// <summary>Provider-agnostic log query. Not all fields apply to every log type/provider.</summary>
public sealed record LogQuery(
    string LogType,
    string? DeploymentId = null,
    string? ProjectId = null,
    string? ServiceId = null,
    string? EnvironmentId = null,
    string? Filter = null,
    int Limit = 200,
    DateTime? StartDate = null,
    DateTime? EndDate = null);

public sealed record VariableEntry(string Key, string Value);

public sealed record ProviderAuditEntry(DateTime Timestamp, string? Actor, string Action, string? Detail);

public sealed record MetricPoint(DateTime Timestamp, string Metric, double Value);

/// <summary>One row of the process/service overview — a service plus its latest deployment state.</summary>
public sealed record ServiceOverviewItem(
    string ServiceId,
    string ServiceName,
    string? Status,
    string? LatestDeploymentId,
    DateTime? LatestCreatedAt,
    string? Url);

public enum DeploymentAction
{
    Redeploy,
    Restart,
    Rollback,
    Stop,
    Cancel,
    Remove,
}

public sealed record DeploymentActionResult(bool Success, string? Status = null, string? Detail = null);

/// <summary>
/// Raised when a provider call fails in a known way (invalid creds, rate limit, upstream error).
/// Controllers translate <see cref="StatusCode"/> into an HTTP response.
/// </summary>
public sealed class ProviderException : Exception
{
    public int StatusCode { get; }

    public ProviderException(string message, int statusCode = 502) : base(message)
        => StatusCode = statusCode;
}

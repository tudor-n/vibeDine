namespace OpsApi.Providers.Abstractions;

/// <summary>
/// The seam that makes the ops console modular. Every infrastructure backend (Railway now,
/// a Hetzner/SSH VPS later) implements this one interface; <see cref="Capabilities"/> tells the UI
/// what a given provider supports. Unsupported operations throw <see cref="NotSupportedException"/>
/// or return empty — the UI never offers them because it reads Capabilities first.
/// </summary>
public interface IInfraProvider
{
    /// <summary>Stable key (matches <c>ProviderKind</c>), used by the registry to resolve the provider.</summary>
    string Key { get; }

    ProviderCapabilities Capabilities { get; }

    /// <summary>Validate credentials and discover the account label. Called by the connect wizard.</summary>
    Task<ConnectionValidationResult> ValidateAsync(
        ProviderCredentials credentials,
        IReadOnlyDictionary<string, string?> metadata,
        CancellationToken ct);

    Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(ProviderContext ctx, CancellationToken ct);

    Task<ProjectDetail?> GetProjectAsync(ProviderContext ctx, string projectId, CancellationToken ct);

    Task<IReadOnlyList<DeploymentSummary>> ListDeploymentsAsync(
        ProviderContext ctx, string projectId, string? serviceId, string? environmentId, int first, CancellationToken ct);

    Task<DeploymentSummary?> GetDeploymentAsync(ProviderContext ctx, string deploymentId, CancellationToken ct);

    Task<DeploymentActionResult> ExecuteDeploymentActionAsync(
        ProviderContext ctx, string deploymentId, DeploymentAction action, CancellationToken ct);

    Task<DeploymentActionResult> TriggerServiceDeployAsync(
        ProviderContext ctx, string serviceId, string environmentId, CancellationToken ct);

    Task<IReadOnlyList<LogEntry>> GetLogsAsync(ProviderContext ctx, LogQuery query, CancellationToken ct);

    Task<IReadOnlyList<VariableEntry>> GetVariablesAsync(
        ProviderContext ctx, string projectId, string environmentId, string? serviceId, CancellationToken ct);

    Task UpsertVariablesAsync(
        ProviderContext ctx, string projectId, string environmentId, string? serviceId,
        IReadOnlyDictionary<string, string> variables, bool replace, bool skipDeploys, CancellationToken ct);

    Task<IReadOnlyList<ProviderAuditEntry>> GetAuditLogsAsync(
        ProviderContext ctx, string? workspaceId, int first, CancellationToken ct);

    /// <summary>Every service in a project + its latest deployment state (the dashboard canvas).</summary>
    Task<IReadOnlyList<ServiceOverviewItem>> GetServiceOverviewAsync(
        ProviderContext ctx, string projectId, string? environmentId, CancellationToken ct);

    /// <summary>Performance metrics (CPU/RAM/network) for a service over the last <paramref name="windowMinutes"/>.</summary>
    Task<IReadOnlyList<MetricPoint>> GetMetricsAsync(
        ProviderContext ctx, string serviceId, string environmentId, int windowMinutes, CancellationToken ct);

    /// <summary>
    /// Server-level metrics: aggregate (sum) across all services of a project — or the whole
    /// connection when <paramref name="projectId"/> is null — so the fleet is viewed per server,
    /// not per process. <paramref name="maxServices"/> caps the fan-out to stay under rate limits.
    /// </summary>
    Task<IReadOnlyList<MetricPoint>> GetAggregateMetricsAsync(
        ProviderContext ctx, string? projectId, string? environmentId, int windowMinutes, int maxServices, CancellationToken ct);
}

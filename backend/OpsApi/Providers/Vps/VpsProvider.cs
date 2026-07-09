using System.Net.Sockets;
using OpsApi.Providers.Abstractions;

namespace OpsApi.Providers.Vps;

/// <summary>
/// Forward-looking provider for a self-hosted VPS (Hetzner et al.), reached over SSH. It shares the
/// exact <see cref="IInfraProvider"/> contract with Railway, so the console, controllers and UI need
/// no changes when it goes live — that is the whole point of the modular design.
///
/// Implemented today: <see cref="ValidateAsync"/> does a real TCP reachability check so a server can
/// be registered and monitored in the fleet view immediately. The remaining operations are mapped to
/// their eventual SSH commands in comments and throw a clear "not implemented" error until built.
/// Because <see cref="Capabilities"/> advertises what is actually available, the UI won't offer the
/// unbuilt features.
/// </summary>
public sealed class VpsProvider : IInfraProvider
{
    public string Key => "vps";

    public ProviderCapabilities Capabilities { get; } = new()
    {
        // Only reachability is live today; flip these on as each SSH command is implemented.
        Projects = false,
        Deployments = false,
        TriggerDeploy = false,   // → `docker compose up -d` / `systemctl restart <unit>`
        BuildLogs = false,
        DeployLogs = false,      // → `journalctl -u <unit> -n <limit>`
        HttpLogs = false,        // → parse nginx/caddy access logs
        LiveTail = false,        // → `journalctl -f` streamed over SignalR
        Variables = false,       // → read/write an env file
        ProviderAudit = false,   // → `journalctl _COMM=sudo` / auth.log
        Metrics = false,         // → parse `/proc`, `docker stats`
        Overview = false,        // → `systemctl list-units` / `docker ps`
        Redeploy = false,
        Restart = false,         // → `systemctl restart <unit>`
        Stop = false,            // → `systemctl stop <unit>`
        Rollback = false,
        Remove = false,
        TokenKinds = ["ssh-key", "password"],
    };

    public async Task<ConnectionValidationResult> ValidateAsync(
        ProviderCredentials credentials, IReadOnlyDictionary<string, string?> metadata, CancellationToken ct)
    {
        var host = metadata.GetValueOrDefault("host");
        if (string.IsNullOrWhiteSpace(host))
            return new(false, null, "A host is required for a VPS connection.", Capabilities);

        var port = int.TryParse(metadata.GetValueOrDefault("port"), out var p) ? p : 22;
        var user = metadata.GetValueOrDefault("user") ?? "root";

        try
        {
            using var tcp = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await tcp.ConnectAsync(host, port, timeout.Token);
            return new(true, $"{user}@{host}:{port}", $"SSH port {port} is reachable.", Capabilities);
        }
        catch (Exception ex)
        {
            return new(false, $"{user}@{host}:{port}", $"Could not reach {host}:{port} — {ex.Message}", Capabilities);
        }
    }

    private static Task<T> NotYet<T>(string feature) =>
        throw new ProviderException($"VPS provider: {feature} is not implemented yet.", 501);

    public Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(ProviderContext ctx, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProjectSummary>>([]);

    public Task<ProjectDetail?> GetProjectAsync(ProviderContext ctx, string projectId, CancellationToken ct) =>
        Task.FromResult<ProjectDetail?>(null);

    public Task<IReadOnlyList<DeploymentSummary>> ListDeploymentsAsync(
        ProviderContext ctx, string projectId, string? serviceId, string? environmentId, int first, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DeploymentSummary>>([]);

    public Task<DeploymentSummary?> GetDeploymentAsync(ProviderContext ctx, string deploymentId, CancellationToken ct) =>
        Task.FromResult<DeploymentSummary?>(null);

    public Task<DeploymentActionResult> ExecuteDeploymentActionAsync(
        ProviderContext ctx, string deploymentId, DeploymentAction action, CancellationToken ct) =>
        NotYet<DeploymentActionResult>($"deployment {action}");

    public Task<DeploymentActionResult> TriggerServiceDeployAsync(
        ProviderContext ctx, string serviceId, string environmentId, CancellationToken ct) =>
        NotYet<DeploymentActionResult>("trigger deploy");

    public Task<IReadOnlyList<LogEntry>> GetLogsAsync(ProviderContext ctx, LogQuery query, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LogEntry>>([]);

    public Task<IReadOnlyList<VariableEntry>> GetVariablesAsync(
        ProviderContext ctx, string projectId, string environmentId, string? serviceId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<VariableEntry>>([]);

    public Task UpsertVariablesAsync(
        ProviderContext ctx, string projectId, string environmentId, string? serviceId,
        IReadOnlyDictionary<string, string> variables, bool replace, bool skipDeploys, CancellationToken ct) =>
        NotYet<object>("variable upsert");

    public Task<IReadOnlyList<ProviderAuditEntry>> GetAuditLogsAsync(
        ProviderContext ctx, string? workspaceId, int first, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProviderAuditEntry>>([]);

    public Task<IReadOnlyList<ServiceOverviewItem>> GetServiceOverviewAsync(
        ProviderContext ctx, string projectId, string? environmentId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ServiceOverviewItem>>([]);

    public Task<IReadOnlyList<MetricPoint>> GetMetricsAsync(
        ProviderContext ctx, string serviceId, string environmentId, int windowMinutes, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MetricPoint>>([]);

    public Task<IReadOnlyList<MetricPoint>> GetAggregateMetricsAsync(
        ProviderContext ctx, string? projectId, string? environmentId, int windowMinutes, int maxServices, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MetricPoint>>([]);
}

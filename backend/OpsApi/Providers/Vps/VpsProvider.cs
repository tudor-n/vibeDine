using System.Net;
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
        if (port is < 1 or > 65535)
            return new(false, null, "Port must be between 1 and 65535.", Capabilities);

        var user = metadata.GetValueOrDefault("user") ?? "root";
        var label = $"{user}@{host}:{port}";

        // The host/port come straight from the client, so this reachability probe is a server-side
        // request primitive. Resolve every address the host maps to and reject loopback, link-local /
        // cloud-metadata (169.254.0.0/16), and private ranges so it can't be used to scan internal
        // networks (SSRF). Resolving ourselves and connecting to the vetted IPs also closes the
        // DNS-rebinding gap where a re-resolve could land on an internal address.
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return new(false, label, "Host could not be resolved.", Capabilities);
        }

        if (addresses.Length == 0 || addresses.Any(IsDisallowedAddress))
            return new(false, label, "Host is not an allowed target.", Capabilities);

        try
        {
            using var tcp = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await tcp.ConnectAsync(addresses, port, timeout.Token);
            return new(true, label, $"SSH port {port} is reachable.", Capabilities);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Generic result only — never echo the raw connect/timeout error detail back to the client.
            return new(false, label, $"Could not reach {host}:{port}.", Capabilities);
        }
    }

    /// <summary>
    /// True for addresses that must never be probed from the server: loopback, unspecified, multicast,
    /// IPv4 link-local/metadata (169.254/16), CGNAT (100.64/10), the RFC1918 private ranges, and their
    /// IPv6 equivalents (link/site-local, unique-local fc00::/7).
    /// </summary>
    private static bool IsDisallowedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return true;
            // Unique-local addresses fc00::/7.
            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        var b = address.GetAddressBytes(); // IPv4 → 4 bytes
        return b[0] switch
        {
            0 => true,                                  // 0.0.0.0/8 "this network"
            10 => true,                                 // 10.0.0.0/8 private
            127 => true,                                // 127.0.0.0/8 loopback
            100 when b[1] is >= 64 and <= 127 => true,  // 100.64.0.0/10 CGNAT
            169 when b[1] == 254 => true,               // 169.254.0.0/16 link-local + cloud metadata
            172 when b[1] is >= 16 and <= 31 => true,   // 172.16.0.0/12 private
            192 when b[1] == 168 => true,               // 192.168.0.0/16 private
            >= 224 => true,                             // 224.0.0.0/4 multicast + 240/4 reserved
            _ => false,
        };
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

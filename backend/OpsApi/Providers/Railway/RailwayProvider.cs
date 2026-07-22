using System.Globalization;
using System.Text.Json;
using OpsApi.Providers.Abstractions;

namespace OpsApi.Providers.Railway;

/// <summary>
/// Railway implementation of <see cref="IInfraProvider"/> over the public GraphQL API.
/// The token is passed in per call (via <see cref="ProviderContext"/>) and never leaves the server.
/// </summary>
public sealed class RailwayProvider : IInfraProvider
{
    private readonly RailwayGraphQLClient _client;

    public RailwayProvider(RailwayGraphQLClient client) => _client = client;

    public string Key => "railway";

    public ProviderCapabilities Capabilities { get; } = new()
    {
        Projects = true,
        Deployments = true,
        TriggerDeploy = true,
        BuildLogs = true,
        DeployLogs = true,
        HttpLogs = true,
        LiveTail = true,
        Variables = true,
        ProviderAudit = false, // auditLogs signature is [DE CONFIRMAT] — needs introspection first
        Metrics = true,        // best-effort metrics query; surfaces Railway's error if field names differ
        Overview = true,
        Redeploy = true,
        Restart = true,
        Rollback = true,
        Stop = true,
        Remove = true,
        TokenKinds = ["account", "workspace", "project"],
    };

    public async Task<ConnectionValidationResult> ValidateAsync(
        ProviderCredentials credentials, IReadOnlyDictionary<string, string?> metadata, CancellationToken ct)
    {
        var kind = (credentials.TokenKind ?? "account").ToLowerInvariant();
        try
        {
            switch (kind)
            {
                case "workspace":
                {
                    var wsId = metadata.GetValueOrDefault("workspaceId");
                    if (string.IsNullOrWhiteSpace(wsId))
                        return new(false, null, "A workspaceId is required for a workspace token.", Capabilities);
                    var data = await _client.QueryAsync(RailwayQueries.WorkspaceById, new { workspaceId = wsId }, credentials, ct);
                    var name = data.GetProperty("workspace").GetProperty("name").GetString();
                    return new(true, name, "Workspace token validated.", Capabilities);
                }
                case "project":
                {
                    var data = await _client.QueryAsync(RailwayQueries.ProjectToken, null, credentials, ct);
                    var pt = data.GetProperty("projectToken");
                    var label = $"project {pt.GetProperty("projectId").GetString()}";
                    return new(true, label, "Project token validated.", Capabilities);
                }
                default:
                {
                    var data = await _client.QueryAsync(RailwayQueries.Me, null, credentials, ct);
                    var me = data.GetProperty("me");
                    var label = me.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString()
                        : me.GetProperty("email").GetString();
                    return new(true, label, "Account token validated.", Capabilities);
                }
            }
        }
        catch (ProviderException ex)
        {
            return new(false, null, ex.Message, Capabilities);
        }
    }

    public async Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(ProviderContext ctx, CancellationToken ct)
    {
        var data = await _client.QueryAsync(RailwayQueries.Projects, null, ctx.Credentials, ct);
        var list = new List<ProjectSummary>();
        foreach (var node in Edges(data, "projects"))
            list.Add(new ProjectSummary(Str(node, "id")!, Str(node, "name") ?? "(unnamed)", Str(node, "description")));
        return list;
    }

    public async Task<ProjectDetail?> GetProjectAsync(ProviderContext ctx, string projectId, CancellationToken ct)
    {
        var data = await _client.QueryAsync(RailwayQueries.Project, new { id = projectId }, ctx.Credentials, ct);
        if (!data.TryGetProperty("project", out var p) || p.ValueKind == JsonValueKind.Null) return null;

        var services = new List<ServiceSummary>();
        foreach (var s in EdgesOf(p, "services"))
            services.Add(new ServiceSummary(Str(s, "id")!, Str(s, "name") ?? "(unnamed)"));

        var envs = new List<EnvironmentSummary>();
        foreach (var e in EdgesOf(p, "environments"))
            envs.Add(new EnvironmentSummary(Str(e, "id")!, Str(e, "name") ?? "(unnamed)"));

        return new ProjectDetail(Str(p, "id")!, Str(p, "name") ?? "(unnamed)", Str(p, "description"), services, envs);
    }

    public async Task<IReadOnlyList<DeploymentSummary>> ListDeploymentsAsync(
        ProviderContext ctx, string projectId, string? serviceId, string? environmentId, int first, CancellationToken ct)
    {
        var input = new Dictionary<string, object?> { ["projectId"] = projectId };
        if (!string.IsNullOrEmpty(serviceId)) input["serviceId"] = serviceId;
        if (!string.IsNullOrEmpty(environmentId)) input["environmentId"] = environmentId;

        var data = await _client.QueryAsync(RailwayQueries.Deployments, new { input, first }, ctx.Credentials, ct);
        var list = new List<DeploymentSummary>();
        foreach (var node in Edges(data, "deployments"))
            list.Add(new DeploymentSummary(
                Str(node, "id")!, Str(node, "status") ?? "UNKNOWN", Date(node, "createdAt"),
                Str(node, "url"), Str(node, "staticUrl")));
        return list;
    }

    public async Task<DeploymentSummary?> GetDeploymentAsync(ProviderContext ctx, string deploymentId, CancellationToken ct)
    {
        var data = await _client.QueryAsync(RailwayQueries.Deployment, new { id = deploymentId }, ctx.Credentials, ct);
        if (!data.TryGetProperty("deployment", out var d) || d.ValueKind == JsonValueKind.Null) return null;
        return new DeploymentSummary(
            Str(d, "id")!, Str(d, "status") ?? "UNKNOWN", Date(d, "createdAt"),
            Str(d, "url"), Str(d, "staticUrl"), Bool(d, "canRedeploy"), Bool(d, "canRollback"));
    }

    public async Task<DeploymentActionResult> ExecuteDeploymentActionAsync(
        ProviderContext ctx, string deploymentId, DeploymentAction action, CancellationToken ct)
    {
        var query = action switch
        {
            DeploymentAction.Redeploy => RailwayQueries.DeploymentRedeploy,
            DeploymentAction.Rollback => RailwayQueries.DeploymentRollback,
            DeploymentAction.Restart => RailwayQueries.DeploymentRestart,
            DeploymentAction.Stop => RailwayQueries.DeploymentStop,
            DeploymentAction.Cancel => RailwayQueries.DeploymentCancel,
            DeploymentAction.Remove => RailwayQueries.DeploymentRemove,
            _ => throw new NotSupportedException($"Unknown deployment action {action}"),
        };

        var data = await _client.QueryAsync(query, new { id = deploymentId }, ctx.Credentials, ct);

        // Redeploy/rollback return an object with a status; the rest return a scalar boolean.
        var field = action is DeploymentAction.Redeploy ? "deploymentRedeploy"
            : action is DeploymentAction.Rollback ? "deploymentRollback" : null;
        if (field is not null && data.TryGetProperty(field, out var obj) && obj.ValueKind == JsonValueKind.Object)
            return new DeploymentActionResult(true, Str(obj, "status"));
        return new DeploymentActionResult(true);
    }

    public async Task<DeploymentActionResult> TriggerServiceDeployAsync(
        ProviderContext ctx, string serviceId, string environmentId, CancellationToken ct)
    {
        await _client.QueryAsync(RailwayQueries.ServiceInstanceDeploy,
            new { serviceId, environmentId }, ctx.Credentials, ct);
        return new DeploymentActionResult(true);
    }

    public async Task<IReadOnlyList<LogEntry>> GetLogsAsync(ProviderContext ctx, LogQuery query, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(query.DeploymentId))
            throw new ProviderException("A deploymentId is required to fetch Railway logs.", 400);

        return query.LogType switch
        {
            Models.LogType.Build => await FetchLogs(ctx, RailwayQueries.BuildLogs,
                new { deploymentId = query.DeploymentId, limit = query.Limit }, "buildLogs", Models.LogType.Build, ct),
            Models.LogType.Http => await FetchHttpLogs(ctx, query, ct),
            _ => await FetchLogs(ctx, RailwayQueries.DeploymentLogs,
                new { deploymentId = query.DeploymentId, limit = query.Limit, filter = query.Filter },
                "deploymentLogs", Models.LogType.Deploy, ct),
        };
    }

    private async Task<IReadOnlyList<LogEntry>> FetchLogs(
        ProviderContext ctx, string gql, object variables, string field, string logType, CancellationToken ct)
    {
        var data = await _client.QueryAsync(gql, variables, ctx.Credentials, ct);
        var list = new List<LogEntry>();
        if (data.TryGetProperty(field, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
                list.Add(new LogEntry(Date(e, "timestamp") ?? DateTime.UtcNow, Str(e, "message") ?? "",
                    Str(e, "severity"), logType));
        return list;
    }

    private async Task<IReadOnlyList<LogEntry>> FetchHttpLogs(ProviderContext ctx, LogQuery query, CancellationToken ct)
    {
        var data = await _client.QueryAsync(RailwayQueries.HttpLogs,
            new { deploymentId = query.DeploymentId, limit = query.Limit }, ctx.Credentials, ct);
        var list = new List<LogEntry>();
        if (data.TryGetProperty("httpLogs", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
            {
                var method = Str(e, "method");
                var path = Str(e, "path");
                var status = e.TryGetProperty("httpStatus", out var hs) && hs.ValueKind == JsonValueKind.Number
                    ? hs.GetInt32().ToString() : "";
                var attrs = new Dictionary<string, object?>
                {
                    ["requestId"] = Str(e, "requestId"),
                    ["method"] = method,
                    ["path"] = path,
                    ["httpStatus"] = status,
                    ["totalDuration"] = e.TryGetProperty("totalDuration", out var td) && td.ValueKind == JsonValueKind.Number ? td.GetDouble() : null,
                    ["srcIp"] = Str(e, "srcIp"),
                };
                list.Add(new LogEntry(Date(e, "timestamp") ?? DateTime.UtcNow,
                    $"{method} {path} → {status}", status.StartsWith('5') ? "error" : "info",
                    Models.LogType.Http, attrs));
            }
        return list;
    }

    public async Task<IReadOnlyList<VariableEntry>> GetVariablesAsync(
        ProviderContext ctx, string projectId, string environmentId, string? serviceId, CancellationToken ct)
    {
        var data = await _client.QueryAsync(RailwayQueries.Variables,
            new { projectId, environmentId, serviceId }, ctx.Credentials, ct);
        var list = new List<VariableEntry>();
        if (data.TryGetProperty("variables", out var obj) && obj.ValueKind == JsonValueKind.Object)
            foreach (var prop in obj.EnumerateObject())
                list.Add(new VariableEntry(prop.Name, prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? "" : prop.Value.ToString()));
        return list;
    }

    public async Task UpsertVariablesAsync(
        ProviderContext ctx, string projectId, string environmentId, string? serviceId,
        IReadOnlyDictionary<string, string> variables, bool replace, bool skipDeploys, CancellationToken ct)
    {
        var input = new Dictionary<string, object?>
        {
            ["projectId"] = projectId,
            ["environmentId"] = environmentId,
            ["variables"] = variables,
            ["replace"] = replace,
            ["skipDeploys"] = skipDeploys,
        };
        if (!string.IsNullOrEmpty(serviceId)) input["serviceId"] = serviceId;
        await _client.QueryAsync(RailwayQueries.VariableCollectionUpsert, new { input }, ctx.Credentials, ct);
    }

    // auditLogs signature is [DE CONFIRMAT]; return empty so the UI degrades gracefully
    // (Capabilities.ProviderAudit is false, so the UI won't offer it either).
    public Task<IReadOnlyList<ProviderAuditEntry>> GetAuditLogsAsync(
        ProviderContext ctx, string? workspaceId, int first, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProviderAuditEntry>>([]);

    /// <summary>
    /// Railway-dashboard-style process overview: every service + its latest deployment. Built from the
    /// verified project + deployments queries (one deployments call per service; fine for typical counts).
    /// </summary>
    public async Task<IReadOnlyList<ServiceOverviewItem>> GetServiceOverviewAsync(
        ProviderContext ctx, string projectId, string? environmentId, CancellationToken ct)
    {
        var project = await GetProjectAsync(ctx, projectId, ct);
        if (project is null) return [];

        var items = new List<ServiceOverviewItem>();
        foreach (var svc in project.Services)
        {
            DeploymentSummary? latest = null;
            try
            {
                var deps = await ListDeploymentsAsync(ctx, projectId, svc.Id, environmentId, 1, ct);
                latest = deps.FirstOrDefault();
            }
            catch (ProviderException)
            {
                // A single service failing shouldn't blank the whole overview.
            }

            items.Add(new ServiceOverviewItem(
                svc.Id, svc.Name, latest?.Status, latest?.Id, latest?.CreatedAt, latest?.Url ?? latest?.StaticUrl));
        }
        return items;
    }

    /// <summary>
    /// Performance metrics. Uses the best-effort <see cref="RailwayQueries.Metrics"/> query; if Railway
    /// rejects a field/enum name, the error propagates so the UI shows exactly what to confirm in GraphiQL.
    /// </summary>
    public async Task<IReadOnlyList<MetricPoint>> GetMetricsAsync(
        ProviderContext ctx, string serviceId, string environmentId, int windowMinutes, CancellationToken ct)
    {
        var start = DateTime.UtcNow.AddMinutes(-Math.Clamp(windowMinutes, 5, 60 * 24));
        var variables = new
        {
            startDate = start.ToString("o"),
            measurements = RailwayQueries.MetricMeasurements,
            environmentId = string.IsNullOrEmpty(environmentId) ? null : environmentId,
            serviceId,
            sampleRateSeconds = windowMinutes <= 60 ? 60 : windowMinutes <= 360 ? 300 : 1800,
        };

        var data = await _client.QueryAsync(RailwayQueries.Metrics, variables, ctx.Credentials, ct);
        var points = new List<MetricPoint>();
        if (data.TryGetProperty("metrics", out var series) && series.ValueKind == JsonValueKind.Array)
            foreach (var s in series.EnumerateArray())
            {
                var measurement = Str(s, "measurement") ?? "metric";
                if (!s.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) continue;
                foreach (var v in values.EnumerateArray())
                {
                    var ts = ParseTimestamp(v, "ts");
                    var value = v.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Number ? val.GetDouble() : 0;
                    points.Add(new MetricPoint(ts, measurement, value));
                }
            }
        return points;
    }

    public async Task<IReadOnlyList<MetricPoint>> GetAggregateMetricsAsync(
        ProviderContext ctx, string? projectId, string? environmentId, int windowMinutes, int maxServices, CancellationToken ct)
    {
        maxServices = Math.Clamp(maxServices, 1, 50);

        // Collect the services to sum over: one project, or every project in the account.
        var serviceIds = new List<string>();
        if (!string.IsNullOrEmpty(projectId))
        {
            var project = await GetProjectAsync(ctx, projectId, ct);
            if (project is not null) serviceIds.AddRange(project.Services.Select(s => s.Id));
        }
        else
        {
            foreach (var p in await ListProjectsAsync(ctx, ct))
            {
                if (serviceIds.Count >= maxServices) break;
                var detail = await GetProjectAsync(ctx, p.Id, ct);
                if (detail is not null) serviceIds.AddRange(detail.Services.Select(s => s.Id));
            }
        }
        if (serviceIds.Count > maxServices) serviceIds = serviceIds.Take(maxServices).ToList();

        // Fetch per service and sum values by (measurement, timestamp).
        var totals = new Dictionary<(string Metric, DateTime Ts), double>();
        foreach (var serviceId in serviceIds)
        {
            try
            {
                var points = await GetMetricsAsync(ctx, serviceId, environmentId ?? string.Empty, windowMinutes, ct);
                foreach (var pt in points)
                {
                    var key = (pt.Metric, pt.Timestamp);
                    totals[key] = totals.TryGetValue(key, out var cur) ? cur + pt.Value : pt.Value;
                }
            }
            catch (ProviderException)
            {
                // One failing service must not blank the whole server aggregate.
            }
        }

        return totals
            .Select(kv => new MetricPoint(kv.Key.Ts, kv.Key.Metric, kv.Value))
            .OrderBy(p => p.Metric).ThenBy(p => p.Timestamp)
            .ToList();
    }

    private static DateTime ParseTimestamp(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v)) return DateTime.UtcNow;
        if (v.ValueKind == JsonValueKind.Number)
        {
            var raw = v.GetInt64();
            // Heuristic: > 10^12 is milliseconds, otherwise seconds.
            return raw > 1_000_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(raw).UtcDateTime
                : DateTimeOffset.FromUnixTimeSeconds(raw).UtcDateTime;
        }
        return DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt) ? dt : DateTime.UtcNow;
    }

    // ── JSON helpers ────────────────────────────────────────────────────────────────
    private static IEnumerable<JsonElement> Edges(JsonElement data, string field) =>
        data.TryGetProperty(field, out var f) ? EdgesOf(f, null) : [];

    private static IEnumerable<JsonElement> EdgesOf(JsonElement parent, string? field)
    {
        var container = field is null ? parent : (parent.TryGetProperty(field, out var f) ? f : default);
        if (container.ValueKind != JsonValueKind.Object) yield break;
        if (!container.TryGetProperty("edges", out var edges) || edges.ValueKind != JsonValueKind.Array) yield break;
        foreach (var edge in edges.EnumerateArray())
            if (edge.TryGetProperty("node", out var node)) yield return node;
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True);

    private static DateTime? Date(JsonElement e, string prop)
    {
        if (!e.TryGetProperty(prop, out var v) || v.ValueKind != JsonValueKind.String) return null;
        return DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
    }
}

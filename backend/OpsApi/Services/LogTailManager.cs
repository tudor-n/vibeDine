using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using OpsApi.Data;
using OpsApi.Hubs;
using OpsApi.Models;
using OpsApi.Providers.Abstractions;

namespace OpsApi.Services;

/// <summary>
/// Owns the live-tail background workers. One worker per (connection, deployment, log-type) group,
/// reference-counted by subscriber. Each worker polls the provider, fans new lines out to the
/// SignalR group, and archives them so history outlives the provider's retention window.
/// Registered as a singleton; uses a scope factory to reach scoped services (DbContext).
/// </summary>
public sealed class LogTailManager
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IHubContext<LogStreamHub> _hub;
    private readonly ILogger<LogTailManager> _logger;
    private readonly ConcurrentDictionary<string, Worker> _workers = new();

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    public LogTailManager(IServiceScopeFactory scopes, IHubContext<LogStreamHub> hub, ILogger<LogTailManager> logger)
    {
        _scopes = scopes;
        _hub = hub;
        _logger = logger;
    }

    public static string GroupName(string connectionId, string deploymentId, string logType) =>
        $"tail:{connectionId}:{deploymentId}:{logType}";

    public void Start(Guid connectionId, string deploymentId, string logType, string group)
    {
        _workers.AddOrUpdate(group,
            _ =>
            {
                var worker = new Worker { Refs = 1, Cts = new CancellationTokenSource() };
                worker.Task = RunAsync(connectionId, deploymentId, logType, group, worker.Cts.Token);
                _logger.LogInformation("Started log tail for {Group}", group);
                return worker;
            },
            (_, existing) =>
            {
                existing.Refs++;
                return existing;
            });
    }

    public void Stop(string group)
    {
        if (!_workers.TryGetValue(group, out var worker)) return;
        if (--worker.Refs > 0) return;
        if (_workers.TryRemove(group, out var removed))
        {
            removed.Cts.Cancel();
            _logger.LogInformation("Stopped log tail for {Group}", group);
        }
    }

    private async Task RunAsync(Guid connectionId, string deploymentId, string logType, string group, CancellationToken ct)
    {
        var seen = new HashSet<string>();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var connSvc = scope.ServiceProvider.GetRequiredService<ConnectionService>();
                var db = scope.ServiceProvider.GetRequiredService<OpsDbContext>();

                var (conn, provider, pctx) = await connSvc.ResolveAsync(connectionId, ct);
                var logs = await provider.GetLogsAsync(
                    pctx, new LogQuery(logType, DeploymentId: deploymentId, Limit: 100), ct);

                var fresh = new List<LogEntry>();
                foreach (var entry in logs.OrderBy(l => l.Timestamp))
                {
                    var key = $"{entry.Timestamp.Ticks}|{entry.Message}";
                    if (seen.Add(key)) fresh.Add(entry);
                }

                // Keep the dedup set bounded.
                if (seen.Count > 5000) seen.Clear();

                if (fresh.Count > 0)
                {
                    await _hub.Clients.Group(group).SendAsync("LogBatch", fresh.Select(f => new
                    {
                        timestamp = f.Timestamp,
                        message = f.Message,
                        severity = f.Severity,
                        logType = f.LogType,
                        attributes = f.Attributes,
                    }), ct);

                    ArchiveInto(db, conn, deploymentId, logType, fresh);
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Log tail {Group} poll failed; will retry", group);
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static void ArchiveInto(OpsDbContext db, Connection conn, string deploymentId, string logType, IEnumerable<LogEntry> entries)
    {
        foreach (var e in entries)
            db.LogArchive.Add(new LogArchiveEntry
            {
                ConnectionId = conn.Id,
                Provider = conn.Provider,
                DeploymentId = deploymentId,
                LogType = logType,
                Severity = e.Severity,
                Message = e.Message,
                AttributesJson = e.Attributes is null ? null : System.Text.Json.JsonSerializer.Serialize(e.Attributes),
                Timestamp = e.Timestamp,
            });
    }

    private sealed class Worker
    {
        public int Refs;
        public CancellationTokenSource Cts = new();
        public Task Task = Task.CompletedTask;
    }
}

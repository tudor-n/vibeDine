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
    // All lifecycle state is guarded by _sync so ref-counts can't race across concurrent hub calls.
    private readonly Dictionary<string, Worker> _workers = new();
    // Hub connection id -> groups it subscribed to, so a dropped connection releases its refs.
    private readonly Dictionary<string, HashSet<string>> _connectionGroups = new();
    private readonly object _sync = new();

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    public LogTailManager(IServiceScopeFactory scopes, IHubContext<LogStreamHub> hub, ILogger<LogTailManager> logger)
    {
        _scopes = scopes;
        _hub = hub;
        _logger = logger;
    }

    public static string GroupName(string connectionId, string deploymentId, string logType) =>
        $"tail:{connectionId}:{deploymentId}:{logType}";

    public void Subscribe(string hubConnectionId, Guid connectionId, string deploymentId, string logType, string group)
    {
        lock (_sync)
        {
            if (!_connectionGroups.TryGetValue(hubConnectionId, out var groups))
                groups = _connectionGroups[hubConnectionId] = new HashSet<string>();
            if (!groups.Add(group)) return; // this hub connection already tails this group

            if (_workers.TryGetValue(group, out var worker))
            {
                worker.Refs++;
            }
            else
            {
                worker = new Worker { Refs = 1, Cts = new CancellationTokenSource() };
                worker.Task = RunAsync(connectionId, deploymentId, logType, group, worker.Cts.Token);
                _workers[group] = worker;
                _logger.LogInformation("Started log tail for {Group}", group);
            }
        }
    }

    public void Unsubscribe(string hubConnectionId, string group)
    {
        lock (_sync)
        {
            if (_connectionGroups.TryGetValue(hubConnectionId, out var groups) && groups.Remove(group))
                Release(group);
        }
    }

    /// <summary>Release every tail a dropped hub connection held (called from OnDisconnectedAsync).</summary>
    public void Disconnect(string hubConnectionId)
    {
        lock (_sync)
        {
            if (!_connectionGroups.TryGetValue(hubConnectionId, out var groups)) return;
            foreach (var group in groups) Release(group);
            _connectionGroups.Remove(hubConnectionId);
        }
    }

    // Caller must hold _sync.
    private void Release(string group)
    {
        if (!_workers.TryGetValue(group, out var worker)) return;
        if (--worker.Refs > 0) return;
        _workers.Remove(group);
        worker.Cts.Cancel();
        _logger.LogInformation("Stopped log tail for {Group}", group);
    }

    private async Task RunAsync(Guid connectionId, string deploymentId, string logType, string group, CancellationToken ct)
    {
        var seen = new HashSet<string>();
        var floor = DateTime.MinValue; // high-water mark: lines older than this were already processed

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
                    if (entry.Timestamp < floor) continue; // already processed in an earlier window
                    var key = $"{entry.Timestamp.Ticks}|{entry.Message}";
                    if (seen.Add(key)) fresh.Add(entry);
                    if (entry.Timestamp > floor) floor = entry.Timestamp;
                }

                // Bound the dedup set. Clearing is safe: `floor` stops older lines being re-archived.
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

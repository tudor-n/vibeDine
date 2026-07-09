using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OpsApi.Services;

namespace OpsApi.Hubs;

/// <summary>
/// SignalR hub for live log tailing. Clients call <c>Subscribe</c>; the server keeps a single
/// upstream poller per deployment (see <see cref="LogTailManager"/>) and fans results out to the
/// group — it never opens one upstream stream per browser (doc §12 principle).
///
/// Event to the client: <c>LogBatch</c> (payload: array of camelCase log entries).
/// </summary>
[Authorize]
public sealed class LogStreamHub : Hub
{
    private readonly LogTailManager _tails;

    public LogStreamHub(LogTailManager tails) => _tails = tails;

    public async Task Subscribe(string connectionId, string deploymentId, string logType)
    {
        var group = LogTailManager.GroupName(connectionId, deploymentId, logType);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        if (Guid.TryParse(connectionId, out var cid))
            _tails.Subscribe(Context.ConnectionId, cid, deploymentId, logType, group);
    }

    public async Task Unsubscribe(string connectionId, string deploymentId, string logType)
    {
        var group = LogTailManager.GroupName(connectionId, deploymentId, logType);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        _tails.Unsubscribe(Context.ConnectionId, group);
    }

    // A closed tab / lost connection must release its tails, or the poller runs forever.
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _tails.Disconnect(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

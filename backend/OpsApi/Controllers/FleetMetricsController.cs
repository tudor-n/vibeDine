using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpsApi.Data;
using OpsApi.DTO.Responses;
using OpsApi.Providers.Abstractions;
using OpsApi.Services;

namespace OpsApi.Controllers;

/// <summary>
/// Fleet-wide metrics — aggregated CPU/RAM/network for EVERY server (connection) at once, so you see
/// the whole fleet, not one process at a time. Each server is resolved independently and a failure on
/// one is reported inline rather than failing the whole response.
/// </summary>
[ApiController]
[Authorize]
[Route("api/ops/fleet")]
public sealed class FleetMetricsController : ControllerBase
{
    private readonly OpsDbContext _db;
    private readonly ConnectionService _connections;
    private readonly ILogger<FleetMetricsController> _logger;

    public FleetMetricsController(OpsDbContext db, ConnectionService connections, ILogger<FleetMetricsController> logger)
    {
        _db = db;
        _connections = connections;
        _logger = logger;
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> FleetMetrics([FromQuery] int windowMinutes = 60, [FromQuery] int maxServices = 8)
    {
        var conns = await _db.Connections.OrderBy(c => c.Role).ThenBy(c => c.Name).ToListAsync();
        var servers = new List<ServerMetricSummary>();

        foreach (var c in conns)
        {
            var supported = false;
            string? error = null;
            IReadOnlyList<MetricPoint> points = [];
            try
            {
                var (_, provider, ctx) = await _connections.ResolveAsync(c.Id, HttpContext.RequestAborted);
                supported = provider.Capabilities.Metrics;
                if (supported)
                    points = await provider.GetAggregateMetricsAsync(
                        ctx, null, null, windowMinutes, maxServices, HttpContext.RequestAborted);
                else
                    error = "Metrics not supported by this provider yet.";
            }
            catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
            {
                throw; // client went away — abort the whole request, don't report it as a server error
            }
            catch (Exception ex)
            {
                // Log full detail server-side; return a generic per-server message so internal
                // hostnames / driver error text aren't disclosed to the caller.
                _logger.LogWarning(ex, "Fleet metrics failed for connection {ConnectionId} ({Name}). CorrelationId={CorrelationId}",
                    c.Id, c.Name, HttpContext.TraceIdentifier);
                error = $"Unable to retrieve metrics for this server (ref {HttpContext.TraceIdentifier}).";
            }

            servers.Add(new ServerMetricSummary(c.Id, c.Name, c.Provider, c.Role, supported, error, points));
        }

        return Ok(new FleetMetricsDto(windowMinutes, servers));
    }
}

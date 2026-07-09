using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpsApi.Services;

namespace OpsApi.Controllers;

/// <summary>
/// Provider metrics (CPU/RAM/network). Railway's metrics query is [DE CONFIRMAT] so this returns
/// empty until the query is confirmed via introspection; the UI gates on Capabilities.Metrics.
/// </summary>
[ApiController]
[Authorize]
[Route("api/ops/connections/{connectionId:guid}/metrics")]
public sealed class MetricsController : OpsControllerBase
{
    private readonly ConnectionService _connections;

    public MetricsController(ConnectionService connections) => _connections = connections;

    [HttpGet]
    public Task<IActionResult> Get(
        Guid connectionId, [FromQuery] string serviceId, [FromQuery] string environmentId,
        [FromQuery] int windowMinutes = 60) =>
        Guarded(async () =>
        {
            if (string.IsNullOrWhiteSpace(serviceId))
                return BadRequest(new { message = "serviceId is required." });
            var (conn, provider, ctx) = await _connections.ResolveAsync(connectionId, HttpContext.RequestAborted);
            if (!provider.Capabilities.Metrics)
                return Ok(Array.Empty<object>());
            var points = await provider.GetMetricsAsync(ctx, serviceId, environmentId, windowMinutes, HttpContext.RequestAborted);
            return Ok(points);
        });

    /// <summary>Server-level metrics — summed across all services (a project, or the whole connection).</summary>
    [HttpGet("aggregate")]
    public Task<IActionResult> Aggregate(
        Guid connectionId, [FromQuery] string? projectId, [FromQuery] string? environmentId,
        [FromQuery] int windowMinutes = 60, [FromQuery] int maxServices = 15) =>
        Guarded(async () =>
        {
            var (_, provider, ctx) = await _connections.ResolveAsync(connectionId, HttpContext.RequestAborted);
            if (!provider.Capabilities.Metrics) return Ok(Array.Empty<object>());
            var points = await provider.GetAggregateMetricsAsync(
                ctx, projectId, environmentId, windowMinutes, maxServices, HttpContext.RequestAborted);
            return Ok(points);
        });
}

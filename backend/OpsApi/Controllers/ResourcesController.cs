using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpsApi.DTO.Responses;
using OpsApi.Providers.Abstractions;
using OpsApi.Services;

namespace OpsApi.Controllers;

/// <summary>Read-only browse of a connection's projects / services / environments / deployments.</summary>
[ApiController]
[Authorize]
[Route("api/ops/connections/{connectionId:guid}")]
public sealed class ResourcesController : OpsControllerBase
{
    private readonly ConnectionService _connections;

    public ResourcesController(ConnectionService connections) => _connections = connections;

    [HttpGet("projects")]
    public Task<IActionResult> Projects(Guid connectionId) => Guarded(async () =>
    {
        var (_, provider, ctx) = await _connections.ResolveAsync(connectionId, HttpContext.RequestAborted);
        var projects = await provider.ListProjectsAsync(ctx, HttpContext.RequestAborted);
        return Ok(projects.Select(p => new ProjectDto(p.Id, p.Name, p.Description)));
    });

    [HttpGet("projects/{projectId}")]
    public Task<IActionResult> Project(Guid connectionId, string projectId) => Guarded(async () =>
    {
        var (_, provider, ctx) = await _connections.ResolveAsync(connectionId, HttpContext.RequestAborted);
        var d = await provider.GetProjectAsync(ctx, projectId, HttpContext.RequestAborted);
        if (d is null) return NotFound(new { message = "Project not found." });
        return Ok(new ProjectDetailDto(d.Id, d.Name, d.Description,
            d.Services.Select(s => new ServiceDto(s.Id, s.Name)).ToList(),
            d.Environments.Select(e => new EnvironmentDto(e.Id, e.Name)).ToList()));
    });

    /// <summary>Process/service overview — every service in a project + its latest deployment state.</summary>
    [HttpGet("overview")]
    public Task<IActionResult> Overview(Guid connectionId, [FromQuery] string projectId, [FromQuery] string? environmentId) =>
        Guarded(async () =>
        {
            if (string.IsNullOrWhiteSpace(projectId)) return BadRequest(new { message = "projectId is required." });
            var (_, provider, ctx) = await _connections.ResolveAsync(connectionId, HttpContext.RequestAborted);
            var items = await provider.GetServiceOverviewAsync(ctx, projectId, environmentId, HttpContext.RequestAborted);
            return Ok(items);
        });

    [HttpGet("deployments")]
    public Task<IActionResult> Deployments(
        Guid connectionId, [FromQuery] string projectId,
        [FromQuery] string? serviceId, [FromQuery] string? environmentId, [FromQuery] int first = 10) =>
        Guarded(async () =>
        {
            if (string.IsNullOrWhiteSpace(projectId)) return BadRequest(new { message = "projectId is required." });
            var (_, provider, ctx) = await _connections.ResolveAsync(connectionId, HttpContext.RequestAborted);
            var deps = await provider.ListDeploymentsAsync(
                ctx, projectId, serviceId, environmentId, Math.Clamp(first, 1, 100), HttpContext.RequestAborted);
            return Ok(deps.Select(Map));
        });

    [HttpGet("deployments/{deploymentId}")]
    public Task<IActionResult> Deployment(Guid connectionId, string deploymentId) => Guarded(async () =>
    {
        var (_, provider, ctx) = await _connections.ResolveAsync(connectionId, HttpContext.RequestAborted);
        var dep = await provider.GetDeploymentAsync(ctx, deploymentId, HttpContext.RequestAborted);
        return dep is null ? NotFound(new { message = "Deployment not found." }) : Ok(Map(dep));
    });

    private static DeploymentDto Map(DeploymentSummary d) =>
        new(d.Id, d.Status, d.CreatedAt, d.Url, d.StaticUrl, d.CanRedeploy, d.CanRollback);
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpsApi.DTO.Requests;
using OpsApi.DTO.Responses;
using OpsApi.Services;

namespace OpsApi.Controllers;

/// <summary>Environment variable get/upsert. Destructive replace is flagged and audited.</summary>
[ApiController]
[Authorize]
[Route("api/ops/connections/{connectionId:guid}/variables")]
public sealed class VariablesController : OpsControllerBase
{
    private readonly ConnectionService _connections;
    private readonly AuditLogger _audit;

    public VariablesController(ConnectionService connections, AuditLogger audit)
    {
        _connections = connections;
        _audit = audit;
    }

    [HttpGet]
    public Task<IActionResult> Get(
        Guid connectionId, [FromQuery] string projectId, [FromQuery] string environmentId, [FromQuery] string? serviceId) =>
        Guarded(async () =>
        {
            if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(environmentId))
                return BadRequest(new { message = "projectId and environmentId are required." });

            var (_, provider, ctx) = await _connections.ResolveAsync(connectionId, HttpContext.RequestAborted);
            var vars = await provider.GetVariablesAsync(ctx, projectId, environmentId, serviceId, HttpContext.RequestAborted);
            return Ok(vars.Select(v => new VariableDto(v.Key, v.Value)));
        });

    [HttpPut]
    public Task<IActionResult> Upsert(Guid connectionId, [FromBody] UpsertVariablesRequest req) => Guarded(async () =>
    {
        var (conn, provider, ctx) = await _connections.ResolveAsync(connectionId, HttpContext.RequestAborted);
        if (!provider.Capabilities.Variables)
            return StatusCode(501, new { message = $"{conn.Provider} does not support variables." });

        await provider.UpsertVariablesAsync(ctx, req.ProjectId, req.EnvironmentId, req.ServiceId,
            req.Variables, req.Replace, req.SkipDeploys, HttpContext.RequestAborted);

        await _audit.RecordAsync(User, "variables.upsert", "success", conn.Provider, conn.Id,
            "variables", req.ServiceId ?? req.EnvironmentId,
            $"Upserted {req.Variables.Count} var(s){(req.Replace ? " (REPLACE)" : "")}",
            new { req.ProjectId, req.EnvironmentId, req.ServiceId, keys = req.Variables.Keys, req.Replace });
        return NoContent();
    });
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpsApi.DTO.Requests;
using OpsApi.Providers.Abstractions;
using OpsApi.Services;

namespace OpsApi.Controllers;

/// <summary>
/// Deployment control — redeploy/restart/rollback/stop/cancel/remove + trigger a fresh deploy.
/// Every action is capability-gated and written to the audit trail (success or failure).
/// </summary>
[ApiController]
[Authorize]
[Route("api/ops/connections/{connectionId:guid}")]
public sealed class DeploymentsController : OpsControllerBase
{
    private readonly ConnectionService _connections;
    private readonly AuditLogger _audit;

    public DeploymentsController(ConnectionService connections, AuditLogger audit)
    {
        _connections = connections;
        _audit = audit;
    }

    // NOTE: the route parameter is {op}, NOT {action} — "action" is a reserved MVC route value and
    // makes the route fail to match (404).
    [HttpPost("deployments/{deploymentId}/{op}")]
    public Task<IActionResult> Act(Guid connectionId, string deploymentId, string op) => Guarded(async () =>
    {
        if (!Enum.TryParse<DeploymentAction>(op, ignoreCase: true, out var act))
            return BadRequest(new { message = $"Unknown action '{op}'." });

        var (conn, provider, ctx) = await _connections.ResolveAsync(connectionId, HttpContext.RequestAborted);

        var supported = act switch
        {
            DeploymentAction.Redeploy => provider.Capabilities.Redeploy,
            DeploymentAction.Restart => provider.Capabilities.Restart,
            DeploymentAction.Rollback => provider.Capabilities.Rollback,
            DeploymentAction.Stop => provider.Capabilities.Stop,
            DeploymentAction.Remove => provider.Capabilities.Remove,
            DeploymentAction.Cancel => provider.Capabilities.Deployments,
            _ => false,
        };
        if (!supported) return StatusCode(501, new { message = $"{conn.Provider} does not support {act}." });

        var auditAction = $"deployment.{act.ToString().ToLowerInvariant()}";
        try
        {
            var result = await provider.ExecuteDeploymentActionAsync(ctx, deploymentId, act, HttpContext.RequestAborted);
            await _audit.RecordAsync(User, auditAction, result.Success ? "success" : "failure",
                conn.Provider, conn.Id, "deployment", deploymentId, $"{act} → {result.Status ?? "ok"}");
            return Ok(result);
        }
        catch (ProviderException ex)
        {
            await _audit.RecordAsync(User, auditAction, "failure", conn.Provider, conn.Id,
                "deployment", deploymentId, ex.Message);
            throw;
        }
    });

    [HttpPost("deploy")]
    public Task<IActionResult> TriggerDeploy(Guid connectionId, [FromBody] TriggerDeployRequest req) => Guarded(async () =>
    {
        var (conn, provider, ctx) = await _connections.ResolveAsync(connectionId, HttpContext.RequestAborted);
        if (!provider.Capabilities.TriggerDeploy)
            return StatusCode(501, new { message = $"{conn.Provider} does not support triggering deploys." });

        try
        {
            var result = await provider.TriggerServiceDeployAsync(ctx, req.ServiceId, req.EnvironmentId, HttpContext.RequestAborted);
            await _audit.RecordAsync(User, "deployment.trigger", result.Success ? "success" : "failure",
                conn.Provider, conn.Id, "service", req.ServiceId, "Triggered deploy");
            return Ok(result);
        }
        catch (ProviderException ex)
        {
            await _audit.RecordAsync(User, "deployment.trigger", "failure", conn.Provider, conn.Id,
                "service", req.ServiceId, ex.Message);
            throw;
        }
    });
}

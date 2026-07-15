using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OpsApi.Providers.Abstractions;

namespace OpsApi.Controllers;

/// <summary>Shared base that maps <see cref="ProviderException"/> to a clean HTTP response.</summary>
public abstract class OpsControllerBase : ControllerBase
{
    protected async Task<IActionResult> Guarded(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ProviderException ex)
        {
            // ProviderException carries a curated, client-safe message (raw upstream bodies/exception
            // text are stripped at the provider layer). Still log full detail + a correlation id.
            var logger = HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
            logger.LogWarning(ex, "Provider call failed ({StatusCode}). CorrelationId={CorrelationId}",
                ex.StatusCode, HttpContext.TraceIdentifier);
            return StatusCode(ex.StatusCode, new { message = ex.Message, correlationId = HttpContext.TraceIdentifier });
        }
        catch (NotSupportedException ex)
        {
            // Framework/dev exception text may leak internals — log it, return a generic message.
            var logger = HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
            logger.LogWarning(ex, "Unsupported provider operation. CorrelationId={CorrelationId}", HttpContext.TraceIdentifier);
            return StatusCode(501, new { message = "This operation is not supported.", correlationId = HttpContext.TraceIdentifier });
        }
    }
}

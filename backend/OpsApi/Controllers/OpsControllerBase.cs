using Microsoft.AspNetCore.Mvc;
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
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
        catch (NotSupportedException ex)
        {
            return StatusCode(501, new { message = ex.Message });
        }
    }
}

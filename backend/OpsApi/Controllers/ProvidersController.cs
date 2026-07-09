using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpsApi.DTO.Responses;
using OpsApi.Providers;

namespace OpsApi.Controllers;

/// <summary>Lists the available infrastructure providers and their capabilities (drives the wizard).</summary>
[ApiController]
[Authorize]
[Route("api/ops/providers")]
public sealed class ProvidersController : ControllerBase
{
    private readonly ProviderRegistry _registry;

    public ProvidersController(ProviderRegistry registry) => _registry = registry;

    [HttpGet]
    public IActionResult List() =>
        Ok(_registry.All.Select(p => new ProviderInfoDto(p.Key, p.Capabilities)));
}

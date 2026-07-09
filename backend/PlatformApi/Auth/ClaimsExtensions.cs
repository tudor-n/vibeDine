using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace PlatformApi.Auth;

public static class ClaimsExtensions
{
    /// <summary>The signed-in platform admin's id, taken from the JWT "sub" claim.</summary>
    public static Guid? PlatformAdminId(this ClaimsPrincipal user)
    {
        var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}

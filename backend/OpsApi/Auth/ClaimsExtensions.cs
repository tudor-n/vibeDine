using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace OpsApi.Auth;

public static class ClaimsExtensions
{
    /// <summary>Reads the ops admin id from the "sub" claim (falls back to NameIdentifier).</summary>
    public static Guid? OpsAdminId(this ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public static string? Email(this ClaimsPrincipal user) =>
        user.FindFirstValue(JwtRegisteredClaimNames.Email) ?? user.FindFirstValue(ClaimTypes.Email);
}

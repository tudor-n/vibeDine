using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpsApi.Models;

namespace OpsApi.Auth;

/// <summary>Issues access tokens for ops admins. Mirrors PlatformApi's PlatformJwtService.</summary>
public sealed class OpsJwtService
{
    private readonly JwtOptions _opts;

    public OpsJwtService(IOptions<JwtOptions> opts) => _opts = opts.Value;

    public (string token, DateTime expiresAt) CreateAccessToken(OpsAdmin admin)
    {
        var expires = DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, admin.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, admin.Email),
            new("name", admin.FullName),
            new("role", "ops_admin"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}

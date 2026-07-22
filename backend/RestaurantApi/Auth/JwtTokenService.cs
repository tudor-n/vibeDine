using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RestaurantApi.Models;

namespace RestaurantApi.Auth;

public class JwtTokenService
{
    private readonly JwtOptions _opts;

    public JwtTokenService(IOptions<JwtOptions> opts) => _opts = opts.Value;

    public (string token, DateTime expiresAt) CreateAccessToken(AppUser user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("name", user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
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

    /// <summary>
    /// Creates a refresh token. Returns the entity to persist (holding only the SHA-256
    /// hash of the value) and the one-time plaintext to hand back to the client. The
    /// plaintext is never stored, so a DB/backup leak cannot yield usable credentials.
    /// </summary>
    public (RefreshToken token, string plainText) CreateRefreshToken(Guid userId)
    {
        var plainText = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var token = new RefreshToken
        {
            UserId = userId,
            Token = HashRefreshToken(plainText),
            ExpiresAt = DateTime.UtcNow.AddDays(_opts.RefreshTokenDays),
        };
        return (token, plainText);
    }

    /// <summary>SHA-256 hash (base64) of a refresh token's plaintext — the only form persisted.</summary>
    public static string HashRefreshToken(string plainText) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(plainText)));
}

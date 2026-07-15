namespace RestaurantApi.Models;

/// <summary>Opaque, server-side refresh token. Exchanged at /api/auth/refresh.</summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>SHA-256 hash (base64) of the refresh-token value — never the plaintext.
    /// Lookups hash the inbound value via <c>JwtTokenService.HashRefreshToken</c>.</summary>
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}

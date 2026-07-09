namespace OpsApi.Auth;

/// <summary>Bound from the "Jwt" config section. Mirrors PlatformApi's JwtOptions.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "vibedine-ops";
    public string Audience { get; set; } = "vibedine-ops";
    public int AccessTokenMinutes { get; set; } = 720;
}

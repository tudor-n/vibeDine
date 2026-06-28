namespace PlatformApi.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "vibedine-platform";
    public string Audience { get; set; } = "vibedine-platform";
    public int AccessTokenMinutes { get; set; } = 720;
}

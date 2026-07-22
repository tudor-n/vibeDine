namespace RestaurantApi.Options;

public class CustomerSessionOptions
{
    public const string SectionName = "CustomerSession";

    /// <summary>How long an open customer tab stays usable before demanding a re-scan.</summary>
    public int LinkExpirySeconds { get; set; } = 900;

    /// <summary>Sliding idle window after which a device session is wiped on the next join.</summary>
    public int SessionLifetimeMinutes { get; set; } = 180;
}

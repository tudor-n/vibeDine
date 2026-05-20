namespace RestaurantApi.Services.Email;

public class AppUrlOptions
{
    public const string SectionName = "App";

    /// <summary>
    /// Public base URL of the admin frontend, used to build the email-confirmation link.
    /// Example: "http://localhost:3001" in dev.
    /// </summary>
    public string AdminBaseUrl { get; set; } = "http://localhost:3001";
}

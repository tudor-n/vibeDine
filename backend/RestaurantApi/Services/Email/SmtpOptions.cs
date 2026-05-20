namespace RestaurantApi.Services.Email;

public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseStartTls { get; set; } = false;
    public bool UseSsl { get; set; } = false;
    public string FromAddress { get; set; } = "no-reply@smart-restaurant.local";
    public string FromName { get; set; } = "Smart Restaurant";
}

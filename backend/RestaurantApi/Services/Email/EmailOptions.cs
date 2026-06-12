namespace RestaurantApi.Services.Email;

public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Outbound email transport:
    /// <list type="bullet">
    /// <item>"smtp" (default) — MailKit SMTP via <see cref="SmtpEmailSender"/>, for local dev (e.g. mailpit).</item>
    /// <item>"resend" — Resend HTTP API via <see cref="ResendEmailSender"/>, required on hosts that block
    /// outbound SMTP ports (Railway and most PaaS).</item>
    /// </list>
    /// </summary>
    public string Provider { get; set; } = "smtp";
}

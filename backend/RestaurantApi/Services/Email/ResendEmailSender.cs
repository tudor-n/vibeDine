using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace RestaurantApi.Services.Email;

/// <summary>
/// Sends email through Resend's HTTP API (https://api.resend.com/emails) over port 443.
/// Used instead of SMTP on hosts that block outbound SMTP ports (e.g. Railway).
///
/// Reuses <see cref="SmtpOptions"/>: <c>FromAddress</c>/<c>FromName</c> for the sender, and
/// <c>Password</c> for the Resend API key — Resend uses the very same API key as the SMTP
/// password, so no extra configuration is needed beyond selecting this provider.
/// </summary>
public class ResendEmailSender : IEmailSender
{
    private const string ResendEndpoint = "https://api.resend.com/emails";

    private readonly HttpClient _http;
    private readonly SmtpOptions _opts;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(HttpClient http, IOptions<SmtpOptions> opts, ILogger<ResendEmailSender> logger)
    {
        _http = http;
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default)
    {
        var payload = new
        {
            from = $"{_opts.FromName} <{_opts.FromAddress}>",
            to = new[] { toAddress },
            subject,
            html = htmlBody,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ResendEndpoint)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.Password ?? string.Empty);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Resend API returned {(int)response.StatusCode}: {body}");
        }

        _logger.LogInformation("Sent email to {Address} via Resend (subject: {Subject})", toAddress, subject);
    }
}

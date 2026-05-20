using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace RestaurantApi.Services.Email;

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _opts;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> opts, ILogger<SmtpEmailSender> logger)
    {
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_opts.FromName, _opts.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();

        var socketOptions = _opts.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : _opts.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

        await client.ConnectAsync(_opts.Host, _opts.Port, socketOptions, ct);

        if (!string.IsNullOrWhiteSpace(_opts.Username))
            await client.AuthenticateAsync(_opts.Username, _opts.Password ?? string.Empty, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);

        _logger.LogInformation("Sent email to {Address} (subject: {Subject})", toAddress, subject);
    }
}

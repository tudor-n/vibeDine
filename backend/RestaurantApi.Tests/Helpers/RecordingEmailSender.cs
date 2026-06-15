using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using RestaurantApi.Services.Email;

namespace RestaurantApi.Tests.Helpers;

/// <summary>IEmailSender double that records every send and always succeeds.</summary>
public class RecordingEmailSender : IEmailSender
{
    public record SentEmail(string To, string Subject, string HtmlBody);

    public ConcurrentBag<SentEmail> Sent { get; } = new();

    public Task SendEmailAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default)
    {
        Sent.Add(new SentEmail(toAddress, subject, htmlBody));
        return Task.CompletedTask;
    }
}

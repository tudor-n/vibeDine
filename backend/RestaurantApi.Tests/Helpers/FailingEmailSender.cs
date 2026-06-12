using System;
using System.Threading;
using System.Threading.Tasks;
using RestaurantApi.Services.Email;

namespace RestaurantApi.Tests.Helpers;

/// <summary>IEmailSender double that simulates an SMTP outage.</summary>
public class FailingEmailSender : IEmailSender
{
    public Task SendEmailAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default)
        => throw new InvalidOperationException("simulated SMTP failure");
}

namespace RestaurantApi.Services.Email;

public interface IEmailSender
{
    Task SendEmailAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default);
}

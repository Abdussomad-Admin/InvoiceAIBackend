using System.Net.Http.Json;
using Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Email;

public sealed class BrevoEmailSender(
    HttpClient http, EmailSettings settings, ILogger<BrevoEmailSender> logger) : IEmailSender
{
    private const string Endpoint = "https://api.brevo.com/v3/smtp/email";

    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                sender = new { name = settings.FromName, email = settings.FromAddress },
                to = new[] { new { email = email.To } },
                subject = email.Subject,
                htmlContent = EmailHtml.FromPlainText(email.Body)
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Add("api-key", settings.ApiKey);
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await http.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Email sent with subject {Subject}", email.Subject);
                return;
            }

            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            if (detail.Length > 300) detail = detail[..300];
            logger.LogError(
                "Brevo rejected the email with subject {Subject}: HTTP {Status} {Detail}",
                email.Subject, (int)response.StatusCode, detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email with subject {Subject}", email.Subject);
        }
    }
}
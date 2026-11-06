using System.Net.Http.Json;
using Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Email;

public sealed class BrevoEmailSender(
    HttpClient http, EmailSettings settings, ILogger<BrevoEmailSender> logger) : IEmailSender
{
    private const string Endpoint = "https://api.brevo.com/v3/smtp/email";

    public async Task<bool> SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["sender"] = new { name = settings.FromName, email = settings.FromAddress },
                ["to"] = new[] { new { email = email.To } },
                ["subject"] = email.Subject,
                ["htmlContent"] = EmailHtml.FromPlainText(email.Body)
            };

            if (!string.IsNullOrWhiteSpace(email.ReplyTo))
                payload["replyTo"] = new { email = email.ReplyTo };

            if (email.Attachments is { Count: > 0 } attachments)
                payload["attachment"] = attachments
                    .Select(a => new { name = a.FileName, content = Convert.ToBase64String(a.Content) })
                    .ToArray();

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
                return true;
            }

            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            if (detail.Length > 300) detail = detail[..300];
            logger.LogError(
                "Brevo rejected the email with subject {Subject}: HTTP {Status} {Detail}",
                email.Subject, (int)response.StatusCode, detail);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Not rethrown (see SmtpEmailSender). Never log the body: it can hold secret links.
            logger.LogError(ex, "Failed to send email with subject {Subject}", email.Subject);
            return false;
        }
    }
}
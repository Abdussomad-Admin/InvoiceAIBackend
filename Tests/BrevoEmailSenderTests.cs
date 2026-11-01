using System.Net;
using System.Text.Json;
using Application.Common.Interfaces;
using Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed record CapturedRequest(string Url, string? ApiKey, string Body);

public sealed class StubHttpHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<CapturedRequest> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var apiKey = request.Headers.TryGetValues("api-key", out var values) ? values.FirstOrDefault() : null;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add(new CapturedRequest(request.RequestUri!.ToString(), apiKey, body));
        return respond();
    }
}

public class BrevoEmailSenderTests
{
    private static EmailSettings Settings() => new()
    {
        Provider = "Brevo",
        ApiKey = "xkeysib-test",
        FromAddress = "no-reply@example.com",
        FromName = "Invoice AI"
    };

    private static BrevoEmailSender Create(StubHttpHandler handler)
        => new(new HttpClient(handler), Settings(), NullLogger<BrevoEmailSender>.Instance);

    [Fact]
    public async Task Send_PostsToBrevoWithApiKeyAndClickableLink()
    {
        var handler = new StubHttpHandler(() => new HttpResponseMessage(HttpStatusCode.Created));
        var sender = Create(handler);

        await sender.SendAsync(new OutgoingEmail(
            "ada@example.com", "Verify",
            "Open:\nhttps://app.example.com/verify-email?token=abc123\nThanks"));

        var call = Assert.Single(handler.Calls);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", call.Url);
        Assert.Equal("xkeysib-test", call.ApiKey);

        using var json = JsonDocument.Parse(call.Body);
        var root = json.RootElement;
        Assert.Equal("no-reply@example.com", root.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal("ada@example.com", root.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("Verify", root.GetProperty("subject").GetString());
        Assert.Contains(
            "<a href=\"https://app.example.com/verify-email?token=abc123\">",
            root.GetProperty("htmlContent").GetString());
    }

    [Fact]
    public async Task Send_WhenBrevoRejectsTheRequest_DoesNotThrow()
    {
        var handler = new StubHttpHandler(() => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"message\":\"Key not found\"}")
        });

        var error = await Record.ExceptionAsync(() =>
            Create(handler).SendAsync(new OutgoingEmail("ada@example.com", "Hello", "Body")));

        Assert.Null(error);
    }

    [Fact]
    public async Task Send_WhenTheNetworkFails_DoesNotThrow()
    {
        var handler = new StubHttpHandler(() => throw new HttpRequestException("network down"));

        var error = await Record.ExceptionAsync(() =>
            Create(handler).SendAsync(new OutgoingEmail("ada@example.com", "Hello", "Body")));

        Assert.Null(error);
    }

    [Fact]
    public void Validate_BrevoWithoutApiKey_Throws()
    {
        var settings = new EmailSettings { Provider = "Brevo", FromAddress = "a@example.com" };
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }
}
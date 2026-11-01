using Application.Common.Interfaces;
using Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public class EmailSenderTests
{
    [Fact]
    public async Task SmtpSender_WhenServerIsUnreachable_DoesNotThrow()
    {
        var settings = new EmailSettings
        {
            Provider = "Smtp",
            Host = "127.0.0.1",
            Port = 59999,
            Security = "None",
            FromAddress = "no-reply@example.com",
            TimeoutSeconds = 3
        };
        var sender = new SmtpEmailSender(settings, NullLogger<SmtpEmailSender>.Instance);

        var error = await Record.ExceptionAsync(
            () => sender.SendAsync(new OutgoingEmail("ada@example.com", "Hello", "Body")));

        Assert.Null(error);
    }

    [Fact]
    public void Validate_SmtpWithoutHost_Throws()
    {
        var settings = new EmailSettings { Provider = "Smtp", FromAddress = "a@example.com" };
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_UnknownProvider_Throws()
    {
        var settings = new EmailSettings { Provider = "Carrier-Pigeon" };
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_LogProvider_NeedsNothingElse()
    {
        var settings = new EmailSettings { Provider = "Log" };
        settings.Validate();
    }
}
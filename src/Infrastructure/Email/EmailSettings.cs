using MailKit.Security;

namespace Infrastructure.Email;

public sealed class EmailSettings
{
    public string Provider { get; set; } = "Log";          // Log, Smtp, or Brevo
    public string ApiKey { get; set; } = "";               // Brevo
    public string Host { get; set; } = "";                 // Smtp
    public int Port { get; set; } = 587;
    public string Security { get; set; } = "StartTls";     // StartTls, SslOnConnect, None
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Invoice AI";
    public int TimeoutSeconds { get; set; } = 15;

    public bool UseSmtp => Is("Smtp");
    public bool UseBrevo => Is("Brevo");

    private bool Is(string name) => string.Equals(Provider, name, StringComparison.OrdinalIgnoreCase);

    public SecureSocketOptions SocketOptions => Security.Trim().ToLowerInvariant() switch
    {
        "starttls" => SecureSocketOptions.StartTls,
        "sslonconnect" => SecureSocketOptions.SslOnConnect,
        "none" => SecureSocketOptions.None,
        _ => throw new InvalidOperationException(
            "Email:Security must be StartTls, SslOnConnect, or None.")
    };

    // Called at startup so a wrong setting fails immediately, not on the first email.
    public void Validate()
    {
        if (Is("Log")) return;

        if (UseBrevo)
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
                throw new InvalidOperationException("Email:ApiKey is required when Email:Provider is 'Brevo'.");
            if (string.IsNullOrWhiteSpace(FromAddress))
                throw new InvalidOperationException("Email:FromAddress is required when Email:Provider is 'Brevo'.");
            return;
        }

        if (!UseSmtp)
            throw new InvalidOperationException("Email:Provider must be 'Log', 'Smtp', or 'Brevo'.");

        if (string.IsNullOrWhiteSpace(Host))
            throw new InvalidOperationException("Email:Host is required when Email:Provider is 'Smtp'.");
        if (Port is < 1 or > 65535)
            throw new InvalidOperationException("Email:Port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(FromAddress))
            throw new InvalidOperationException("Email:FromAddress is required when Email:Provider is 'Smtp'.");

        _ = SocketOptions;
    }
}
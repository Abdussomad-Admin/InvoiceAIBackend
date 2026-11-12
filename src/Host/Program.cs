using Application.Assistant;
using Application.Billing;
using Application.Common;
using Application.Common.Interfaces;
using Application.Documents;
using Domain.Organizations;
using Host;
using Host.Background;
using Host.Authorization;
using Infrastructure;
using Infrastructure.Auth;
using Infrastructure.Email;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "Connection string 'Default' is missing. Set ConnectionStrings__Default when deployed.");

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Key must be at least 32 characters. Set the Jwt__Key environment variable when deployed.");
if (!builder.Environment.IsDevelopment() && jwt.Key.StartsWith("local-development", StringComparison.Ordinal))
    throw new InvalidOperationException("The local development JWT key must not be used outside Development.");

var emailSettings = builder.Configuration.GetSection("Email").Get<EmailSettings>() ?? new EmailSettings();

var assistantSettings = builder.Configuration.GetSection("Assistant").Get<AssistantSettings>() ?? new AssistantSettings();

var billingSettings = builder.Configuration.GetSection("Billing").Get<BillingSettings>() ?? new BillingSettings();
var paystackSettings = builder.Configuration.GetSection("Paystack").Get<PaystackSettings>() ?? new PaystackSettings();
builder.Services.AddSingleton(billingSettings);
builder.Services.AddSingleton(paystackSettings);
builder.Services.AddScoped<InvoicePaymentWebhookProcessor>();
builder.Services.AddScoped<IPlanLimitChecker, BillingPlanLimitChecker>();
builder.Services.AddHttpClient("Paystack", client =>
{
    client.BaseAddress = new Uri("https://api.paystack.co/");
    client.Timeout = TimeSpan.FromSeconds(20);
});


assistantSettings.Validate();
builder.Services.AddSingleton(assistantSettings);

var documentSettings = builder.Configuration.GetSection("Documents").Get<DocumentSettings>() ?? new DocumentSettings();
documentSettings.Validate();
builder.Services.AddSingleton(documentSettings);

var reminderSettings = builder.Configuration.GetSection("Reminders").Get<ReminderSettings>() ?? new ReminderSettings();
reminderSettings.PollIntervalSeconds = Math.Clamp(reminderSettings.PollIntervalSeconds, 10, 3600);
reminderSettings.MaxAttempts = Math.Clamp(reminderSettings.MaxAttempts, 1, 20);
reminderSettings.LeaseMinutes = Math.Clamp(reminderSettings.LeaseMinutes, 1, 60);
builder.Services.AddSingleton(reminderSettings);


builder.Services.AddInfrastructure(connectionString, jwt, emailSettings);

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

// CORS: only the origins listed in configuration may call the API from a browser.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? Array.Empty<string>();
var configuredFrontendUrl = builder.Configuration["App:FrontendBaseUrl"];
var publicFrontendOrigin = allowedOrigins.FirstOrDefault(origin =>
    Uri.TryCreate(origin, UriKind.Absolute, out var uri)
    && uri.Scheme == Uri.UriSchemeHttps
    && !uri.IsLoopback);
var configuredFrontendIsAllowed = Uri.TryCreate(configuredFrontendUrl, UriKind.Absolute, out var configuredFrontendUri)
    && allowedOrigins.Any(origin => Uri.TryCreate(origin, UriKind.Absolute, out var allowedUri)
        && string.Equals(allowedUri.Host, configuredFrontendUri.Host, StringComparison.OrdinalIgnoreCase));

// Email and checkout return links must point to the SPA host. If the configured URL
// is stale (for example, set to the API host), use the public origin already allowed by CORS.
var frontendBaseUrl = !string.IsNullOrWhiteSpace(publicFrontendOrigin) && !configuredFrontendIsAllowed
    ? publicFrontendOrigin
    : configuredFrontendUrl ?? "http://localhost:5173";
builder.Services.AddSingleton(new AppLinkSettings(frontendBaseUrl));

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0)
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));

// Rate limit sensitive endpoints: 10 requests per minute per IP address.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));

    options.AddPolicy("assistant", httpContext => RateLimitPartition.GetFixedWindowLimiter(
    httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
    _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = assistantSettings.QuestionsPerHour,
        Window = TimeSpan.FromHours(1)
    }));

    options.AddPolicy("upload", httpContext => RateLimitPartition.GetFixedWindowLimiter(
    httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
    _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));

    options.AddPolicy("extract", httpContext => RateLimitPartition.GetFixedWindowLimiter(
    httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
    _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromHours(1) }));
});

// Register every class ending in "Handler" from the Application project.
foreach (var type in typeof(Application.Auth.RegisterHandler).Assembly.GetTypes()
             .Where(t => t is { IsClass: true, IsAbstract: false }
                 && (t.Name.EndsWith("Handler", StringComparison.Ordinal)
                     || t.Name.EndsWith("Handlers", StringComparison.Ordinal))))
{
    builder.Services.AddScoped(type);
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentOrganization, HttpContextCurrentOrganization>();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddScoped<EmailOutboxProcessor>();
builder.Services.AddHostedService<EmailOutboxWorker>();
builder.Services.AddHostedService<RecurringInvoiceWorker>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

// One policy per permission, named after the permission (for example "ManageCustomers").
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Enum.GetValues<Permission>())
    {
        options.AddPolicy(permission.ToString(), policy =>
            policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
    }
});

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("Database migrations applied.");
}

if (app.Environment.IsProduction() && !emailSettings.UseSmtp && !emailSettings.UseBrevo)
    app.Logger.LogWarning(
        "No real email provider is configured. Emails, including secret links, are only written to the log.");

app.UseExceptionHandler();
app.UseHttpsRedirection();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Invoice AI v1"));
}

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.Run();

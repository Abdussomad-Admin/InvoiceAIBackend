using Application.Auth;
using Application.Common.Interfaces;
using Application.Customers;
using Application.Invoices;
using Application.Organizations;
using Application.Users;
using Infrastructure.Auth;
using Infrastructure.Email;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Audit;
using Application.Payments;
using Application.Reports;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
    this IServiceCollection services, string connectionString, JwtSettings jwtSettings, EmailSettings emailSettings)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(jwtSettings);
        services.AddScoped<AuditTimestampInterceptor>();

        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
            options
                .UseNpgsql(connectionString)
                .AddInterceptors(serviceProvider.GetRequiredService<AuditTimestampInterceptor>()));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();

        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        services.AddScoped<IUserTokenRepository, UserTokenRepository>();
        services.AddSingleton<ISecureTokenService, SecureTokenService>();

        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<SessionIssuer>();

        services.AddScoped<ICustomerRepository, CustomerRepository>();

        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<ITenantWriteBypass>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IInvoiceNumberGenerator, InvoiceNumberGenerator>();
        services.AddScoped<ITransactionRunner>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IReceivablesRepository, ReceivablesRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();

        services.AddScoped<IDashboardRepository, DashboardRepository>();

        emailSettings.Validate();
        services.AddSingleton(emailSettings);

        if (emailSettings.UseBrevo)
        {
            services.AddHttpClient<IEmailSender, BrevoEmailSender>(client =>
                client.Timeout = TimeSpan.FromSeconds(emailSettings.TimeoutSeconds));
        }
        else if (emailSettings.UseSmtp)
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, DevelopmentEmailSender>();
        }

        services.AddScoped<AccountMailer>();

        return services;
    }
}
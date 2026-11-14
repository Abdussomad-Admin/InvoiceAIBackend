using Application.Auth;
using Application.Common.Interfaces;
using Domain.Bills;
using Domain.Customers;
using Domain.Invoices;
using Domain.Organizations;
using Domain.Payments;
using Domain.Suppliers;
using Domain.Users;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Host.Background;

/// <summary>
/// Creates an isolated fictional workspace only when explicitly enabled in Development.
/// It never runs in a deployed environment and never contacts email or payment providers.
/// </summary>
public sealed class DemoScenarioSeeder(
    IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<DemoScenarioSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("DemoScenario:Enabled")) return;

        var email = configuration["DemoScenario:Email"]?.Trim();
        var password = configuration["DemoScenario:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "DemoScenario:Email and DemoScenario:Password are required when demo seeding is enabled.");
        PasswordPolicy.Validate(password);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.Database.CanConnectAsync(cancellationToken))
            throw new InvalidOperationException("Cannot connect to the database required for the demo scenario.");

        if (await db.Users.IgnoreQueryFilters().AnyAsync(user => user.Email == email.ToLowerInvariant(), cancellationToken))
        {
            logger.LogInformation("Fictional demo workspace already exists for {Email}; no records were changed.", email);
            return;
        }

        var user = User.Register(email, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password), "Demo Owner");
        var organization = Organization.Create("Abeokuta Stitch & Style (FICTIONAL DEMO)", "NG", "NGN", "Africa/Lagos");
        organization.UpdateProfile(organization.Name, "owner@stitch-style.example", "+234 800 000 0000", null,
            "12 Adire Lane", "Demo Estate", "Abeokuta", "Ogun State", "110001");
        organization.CompleteOnboarding();
        user.VerifyEmail(TimeProvider.System.GetUtcNow());

        db.Users.Add(user);
        db.Organizations.Add(organization);
        db.Memberships.Add(Membership.Create(user.Id, organization.Id, OrganizationRole.Owner));

        var customers = new[]
        {
            CustomerRecord(organization.Id, "Amina Okafor", "amina@example.invalid", "08000000001"),
            CustomerRecord(organization.Id, "Kemi's Corner Shop", "kemi@example.invalid", "08000000002"),
            CustomerRecord(organization.Id, "Ola Market", "ola@example.invalid", "08000000003"),
            CustomerRecord(organization.Id, "Tola Events", "tola@example.invalid", "08000000004")
        };
        var suppliers = new[]
        {
            SupplierRecord(organization.Id, "Abeokuta Fabric Market"),
            SupplierRecord(organization.Id, "Colour & Thread Depot"),
            SupplierRecord(organization.Id, "Demo Power and Utilities")
        };
        db.Customers.AddRange(customers);
        db.Suppliers.AddRange(suppliers);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(TimeProvider.System.GetUtcNow(),
            TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos")).DateTime);
        var now = TimeProvider.System.GetUtcNow();
        var invoiceSpecs = new[]
        {
            ("DEMO-INV-001", 85000m, today.AddDays(-55), today.AddDays(-25), 85000m, customers[0], "Ankara outfit and alterations"),
            ("DEMO-INV-002", 120000m, today.AddDays(-20), today.AddDays(10), 40000m, customers[1], "Shop uniforms · first instalment"),
            ("DEMO-INV-003", 63000m, today.AddDays(-35), today.AddDays(-5), 0m, customers[2], "Three custom occasion outfits"),
            ("DEMO-INV-004", 95000m, today.AddDays(-2), today.AddDays(28), 0m, customers[3], "Event clothing · due next month")
        };
        var paymentIndex = 1;
        foreach (var spec in invoiceSpecs)
        {
            var invoice = Invoice.CreateDraft(organization.Id, spec.Item6.Id, "NGN", 30,
                "FICTIONAL DEMO RECORD — no customer or payment provider was contacted.");
            invoice.ReplaceLines([new LineInput(spec.Item7, 1m, spec.Item2)]);
            invoice.Issue(spec.Item1, spec.Item3, spec.Item4);
            db.Invoices.Add(invoice);
            if (spec.Item5 <= 0m) continue;

            invoice.ApplyPayment(spec.Item5);
            db.Payments.Add(Payment.Create(organization.Id, invoice.Id, spec.Item6.Id, "NGN", spec.Item5,
                spec.Item3.AddDays(2), PaymentMethod.BankTransfer, $"DEMO-CUST-PAY-{paymentIndex:000}",
                "Fictional sample payment for demonstration only.", user.Id));
            paymentIndex++;
        }

        var billSpecs = new[]
        {
            ("DEMO-BILL-001", 32000m, today.AddDays(-40), today.AddDays(-10), 32000m, suppliers[0], "Lining fabric and zips"),
            ("DEMO-BILL-002", 47000m, today.AddDays(-12), today.AddDays(5), 17000m, suppliers[1], "Thread, buttons and packaging"),
            ("DEMO-BILL-003", 18000m, today.AddDays(-32), today.AddDays(-2), 0m, suppliers[2], "Workshop electricity")
        };
        var billPaymentIndex = 1;
        foreach (var spec in billSpecs)
        {
            var bill = SupplierBill.CreateDraft(organization.Id, spec.Item6.Id, "NGN", spec.Item1,
                spec.Item3, spec.Item4, "FICTIONAL DEMO RECORD — not a real supplier bill.");
            bill.ReplaceLines([new LineInput(spec.Item7, 1m, spec.Item2)]);
            bill.Finalize(now);
            db.SupplierBills.Add(bill);
            if (spec.Item5 <= 0m) continue;

            bill.ApplyPayment(spec.Item5);
            db.BillPayments.Add(BillPayment.Create(organization.Id, bill.Id, spec.Item6.Id, "NGN", spec.Item5,
                spec.Item3.AddDays(2), PaymentMethod.BankTransfer, $"DEMO-SUP-PAY-{billPaymentIndex:000}",
                "Fictional sample payment for demonstration only.", user.Id, "NGN"));
            billPaymentIndex++;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogWarning("Seeded a fictional demo workspace for {Email}. Its sample payments are manual records, not provider-verified transactions.", email);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static Customer CustomerRecord(Guid organizationId, string name, string email, string phone)
    {
        var customer = Customer.Create(organizationId, name);
        customer.Update(name, email, phone, null, "Fictional Demo Address", null,
            "Abeokuta", "Ogun State", "110001", "NG", "Fictional demo customer.");
        return customer;
    }

    private static Supplier SupplierRecord(Guid organizationId, string name)
    {
        var supplier = Supplier.Create(organizationId, name);
        supplier.Update(name, null, "+234 800 000 0100", null, "Demo Supply Road", null,
            "Abeokuta", "Ogun State", "110001", "NG", "Fictional demo supplier.", 30);
        return supplier;
    }
}

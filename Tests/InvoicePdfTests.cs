using System.Text;
using Application.Common.Exceptions;
using Application.Invoices;
using Domain.Invoices;
using Infrastructure.Pdf;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Tests;

internal sealed class CapturingPdfRenderer : IInvoicePdfRenderer
{
    public InvoicePdfModel? Model { get; private set; }

    public byte[] Render(InvoicePdfModel model)
    {
        Model = model;
        return [1, 2, 3];
    }
}

public class PdfFormatTests
{
    [Fact]
    public void Formatting_IsCultureIndependent_WithTwoDecimals()
    {
        Assert.Equal("1,234,567.80", PdfFormat.Money(1234567.8m));
        Assert.Equal("0.00", PdfFormat.Money(0m));
        Assert.Equal("05 Jan 2026", PdfFormat.Date(new DateOnly(2026, 1, 5)));
        Assert.Equal("-", PdfFormat.Date(null));
        Assert.Equal("-", PdfFormat.Percent(0m));
        Assert.Equal("7.5%", PdfFormat.Percent(7.5m));
        Assert.Equal("2.5", PdfFormat.Quantity(2.5m));
    }
}

public class QuestPdfRendererTests
{
    private static InvoicePdfModel Model(
        int lineCount, InvoiceStatus status = InvoiceStatus.Issued, string? voidReason = null)
    {
        var seller = new PdfParty(
            "Acme Trading Ltd", ["1 Marina Road", "Lagos, Lagos 100001", "NG"],
            "billing@acme.com", "+2348012345678", "TIN-12345");
        var buyer = new PdfParty("Zenith Traders", ["5 Broad Street", "Lagos"], "ap@zenith.com", null, null);

        var lines = Enumerable.Range(1, lineCount)
            .Select(i => new PdfLine(
                i, $"Consulting service number {i} with a long description to check that wrapping works",
                2m, 100m, 10m, 7.5m, 193.50m))
            .ToList();
        var total = lines.Sum(l => l.LineTotal);

        return new InvoicePdfModel(
            seller, buyer, status == InvoiceStatus.Draft ? null : "INV-0001", status, "NGN",
            new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 14), "Thank you for your business",
            lines, total, 0m, 0m, total, 0m, total, false, voidReason);
    }

    private static bool LooksLikeAPdf(byte[] bytes)
        => bytes.Length > 1000 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";

    [Theory]
    [InlineData(InvoiceStatus.Draft)]
    [InlineData(InvoiceStatus.Issued)]
    [InlineData(InvoiceStatus.PartiallyPaid)]
    [InlineData(InvoiceStatus.Paid)]
    [InlineData(InvoiceStatus.Void)]
    public void Render_ProducesAValidPdf_ForEveryStatus(InvoiceStatus status)
    {
        var bytes = new QuestPdfInvoiceRenderer().Render(Model(3, status, voidReason: "Raised in error"));

        Assert.True(LooksLikeAPdf(bytes));
    }

    [Fact]
    public void Render_HandlesManyLines_AcrossSeveralPages()
    {
        var renderer = new QuestPdfInvoiceRenderer();

        var small = renderer.Render(Model(3));
        var large = renderer.Render(Model(150));

        Assert.True(LooksLikeAPdf(large));
        Assert.True(large.Length > small.Length);
    }
}

public class InvoicePdfHandlerTests
{
    private static readonly FixedTimeProvider Clock = PaymentTestData.Clock;

    private static GetInvoicePdfHandler Handler(Infrastructure.Persistence.AppDbContext ctx, Seed seed, CapturingPdfRenderer renderer)
        => new(new InvoiceRepository(ctx), new CustomerRepository(ctx), new OrganizationRepository(ctx),
            renderer, new TestCurrentOrganization(seed.OrganizationId), Clock);

    [Fact]
    public async Task PdfModel_CarriesTheSellerBuyerLinesAndTotals()
    {
        var seed = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seed); // 273.02
        await PaymentTestData.RecordAsync(seed, invoice.Id, 100.00m);

        var renderer = new CapturingPdfRenderer();
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var file = await Handler(ctx, seed, renderer).Handle(invoice.Id, CancellationToken.None);

        var organization = await ctx.Organizations.SingleAsync(o => o.Id == seed.OrganizationId);
        var customer = await ctx.Customers.SingleAsync(c => c.Id == seed.CustomerId);
        var model = renderer.Model!;

        Assert.Equal("INV-0001.pdf", file.FileName);
        Assert.Equal(organization.Name, model.Seller.Name);
        Assert.Equal(customer.Name, model.Buyer.Name);
        Assert.Equal("INV-0001", model.Number);
        Assert.Equal(2, model.Lines.Count);
        Assert.Equal(273.02m, model.Total);
        Assert.Equal(100.00m, model.AmountPaid);
        Assert.Equal(173.02m, model.Balance);
        Assert.False(model.IsOverdue);
    }

    [Fact]
    public async Task APdfOfADraft_HasNoNumber_AndADraftFileName()
    {
        var seed = await PaymentTestData.SeedAsync();
        var draft = await PaymentTestData.CreateDraftAsync(seed);

        var renderer = new CapturingPdfRenderer();
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var file = await Handler(ctx, seed, renderer).Handle(draft.Id, CancellationToken.None);

        Assert.StartsWith("invoice-draft-", file.FileName);
        Assert.Null(renderer.Model!.Number);
        Assert.Equal(InvoiceStatus.Draft, renderer.Model.Status);
    }

    [Fact]
    public async Task AnotherOrganization_CannotDownloadTheInvoice()
    {
        var seedA = await PaymentTestData.SeedAsync();
        var seedB = await PaymentTestData.SeedAsync();
        var invoice = await PaymentTestData.IssueInvoiceAsync(seedA);

        await using var ctx = TestDatabase.CreateContext(Clock, seedB.OrganizationId, seedB.UserId);

        await Assert.ThrowsAsync<NotFoundException>(
            () => Handler(ctx, seedB, new CapturingPdfRenderer()).Handle(invoice.Id, CancellationToken.None));
    }
}
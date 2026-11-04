using Domain.Common;
using Domain.Invoices;

namespace Tests;

public class InvoiceTests
{
    private static Invoice NewDraft(int terms = 30)
        => Invoice.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "ngn", terms, null);

    // Two lines: total 273.02, issued 15 Jan 2026, due 14 Feb 2026.
    private static Invoice IssuedInvoice()
    {
        var invoice = NewDraft();
        invoice.ReplaceLines(
        [
            new LineInput("Widget", 3m, 19.99m, 10m, 7.5m),
            new LineInput("Service", 2m, 100m, 0m, 7.5m)
        ]);
        invoice.Issue("INV-0001", new DateOnly(2026, 1, 15));
        return invoice;
    }

    [Fact]
    public void Line_AppliesDiscountThenTax_WithRounding()
    {
        var invoice = NewDraft();
        invoice.ReplaceLines([new LineInput("Widget", 3m, 19.99m, 10m, 7.5m)]);

        var line = Assert.Single(invoice.Lines);
        Assert.Equal(59.97m, line.LineSubtotal);
        Assert.Equal(6.00m, line.DiscountAmount);
        Assert.Equal(4.05m, line.TaxAmount);
        Assert.Equal(58.02m, line.LineTotal);
    }

    [Fact]
    public void Line_RoundsMidpointsAwayFromZero()
    {
        var invoice = NewDraft();
        invoice.ReplaceLines([new LineInput("Item", 1m, 0.125m)]);

        Assert.Equal(0.13m, Assert.Single(invoice.Lines).LineSubtotal);
    }

    [Fact]
    public void Invoice_TotalsAreTheSumOfTheLines()
    {
        var invoice = IssuedInvoice();

        Assert.Equal(259.97m, invoice.Subtotal);
        Assert.Equal(6.00m, invoice.DiscountTotal);
        Assert.Equal(19.05m, invoice.TaxTotal);
        Assert.Equal(273.02m, invoice.Total);
        Assert.Equal(invoice.Subtotal - invoice.DiscountTotal + invoice.TaxTotal, invoice.Total);
        Assert.Equal(invoice.Total, invoice.Balance);
    }

    [Fact]
    public void Lines_WithInvalidValues_Throw()
    {
        var invoice = NewDraft();
        var invalid = new[]
        {
            new LineInput("Item", 0m, 10m),
            new LineInput("Item", -1m, 10m),
            new LineInput("Item", 100_000.01m, 10m),
            new LineInput("Item", 1m, -1m),
            new LineInput("Item", 1m, 1.12345m),
            new LineInput("Item", 1m, 100_000_001m),
            new LineInput("Item", 1m, 10m, 100.01m),
            new LineInput("Item", 1m, 10m, -1m),
            new LineInput("Item", 1m, 10m, 0m, 100.01m),
            new LineInput("Item", 1m, 10m, 0m, -5m),
            new LineInput("", 1m, 10m)
        };

        foreach (var bad in invalid)
            Assert.Throws<DomainException>(() => invoice.ReplaceLines([bad]));
    }

    [Fact]
    public void Invoice_WithMoreThan200Lines_Throws()
    {
        var invoice = NewDraft();

        invoice.ReplaceLines(Enumerable.Range(0, 200).Select(_ => new LineInput("Item", 1m, 1m)));
        Assert.Throws<DomainException>(
            () => invoice.ReplaceLines(Enumerable.Range(0, 201).Select(_ => new LineInput("Item", 1m, 1m))));
    }

    [Fact]
    public void CreateDraft_NormalizesCurrency_AndRejectsBadPaymentTerms()
    {
        Assert.Equal("NGN", NewDraft().CurrencyCode);
        Assert.Throws<DomainException>(() => NewDraft(terms: -1));
        Assert.Throws<DomainException>(() => NewDraft(terms: 366));
    }

    [Fact]
    public void Issue_WithoutLines_Throws()
    {
        var invoice = NewDraft();

        Assert.Throws<DomainException>(() => invoice.Issue("INV-0001", new DateOnly(2026, 1, 15)));
    }

    [Fact]
    public void Issue_SetsDueDateFromPaymentTerms()
    {
        var invoice = IssuedInvoice();

        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal("INV-0001", invoice.Number);
        Assert.Equal(new DateOnly(2026, 2, 14), invoice.DueDate);
    }

    [Fact]
    public void Issue_WithDueDateBeforeIssueDate_Throws()
    {
        var invoice = NewDraft();
        invoice.ReplaceLines([new LineInput("Item", 1m, 10m)]);

        Assert.Throws<DomainException>(
            () => invoice.Issue("INV-0001", new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 14)));
    }

    [Fact]
    public void IssuedInvoice_CannotBeEdited()
    {
        var invoice = IssuedInvoice();

        Assert.Throws<DomainException>(() => invoice.ReplaceLines([new LineInput("Other", 1m, 1m)]));
        Assert.Throws<DomainException>(() => invoice.UpdateDetails(Guid.NewGuid(), 10, null));
    }

    [Fact]
    public void Payments_PartialThenFull_UpdateStatusAndBalance()
    {
        var invoice = IssuedInvoice();

        invoice.ApplyPayment(100.00m);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Equal(173.02m, invoice.Balance);

        invoice.ApplyPayment(173.02m);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0m, invoice.Balance);
        Assert.Equal(invoice.Total, invoice.AmountPaid);
    }

    [Fact]
    public void Payment_ExceedingTheBalance_Throws()
    {
        var invoice = IssuedInvoice();

        Assert.Throws<DomainException>(() => invoice.ApplyPayment(273.03m));
        Assert.Equal(0m, invoice.AmountPaid);
    }

    [Fact]
    public void Payment_WithInvalidAmounts_Throws()
    {
        var invoice = IssuedInvoice();

        Assert.Throws<DomainException>(() => invoice.ApplyPayment(0m));
        Assert.Throws<DomainException>(() => invoice.ApplyPayment(-5m));
        Assert.Throws<DomainException>(() => invoice.ApplyPayment(10.005m));
    }

    [Fact]
    public void Payment_OnADraft_Throws()
    {
        var invoice = NewDraft();
        invoice.ReplaceLines([new LineInput("Item", 1m, 10m)]);

        Assert.Throws<DomainException>(() => invoice.ApplyPayment(5m));
    }

    [Fact]
    public void ReversingPayments_RestoresTheStatus()
    {
        var invoice = IssuedInvoice();
        invoice.ApplyPayment(invoice.Total);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);

        invoice.ReversePayment(73.02m);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Equal(200.00m, invoice.AmountPaid);

        invoice.ReversePayment(200.00m);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal(0m, invoice.AmountPaid);

        Assert.Throws<DomainException>(() => invoice.ReversePayment(1m));
    }

    [Fact]
    public void Void_WorksOnlyForIssuedInvoicesWithoutPayments()
    {
        var now = new DateTimeOffset(2026, 1, 20, 9, 0, 0, TimeSpan.Zero);

        var draft = NewDraft();
        Assert.Throws<DomainException>(() => draft.Void("Mistake", now));

        var withoutReason = IssuedInvoice();
        Assert.Throws<DomainException>(() => withoutReason.Void("  ", now));

        var paid = IssuedInvoice();
        paid.ApplyPayment(50m);
        Assert.Throws<DomainException>(() => paid.Void("Mistake", now));

        var unpaid = IssuedInvoice();
        unpaid.Void("Raised in error", now);
        Assert.Equal(InvoiceStatus.Void, unpaid.Status);
        Assert.Equal("Raised in error", unpaid.VoidReason);
        Assert.Throws<DomainException>(() => unpaid.ApplyPayment(10m));
    }

    [Fact]
    public void Overdue_IsDerivedFromTheDueDateAndStatus()
    {
        var invoice = IssuedInvoice(); // due 14 Feb 2026

        Assert.False(invoice.IsOverdue(new DateOnly(2026, 2, 14)));
        Assert.True(invoice.IsOverdue(new DateOnly(2026, 2, 15)));
        Assert.Equal(1, invoice.DaysOverdue(new DateOnly(2026, 2, 15)));

        invoice.ApplyPayment(invoice.Total);
        Assert.False(invoice.IsOverdue(new DateOnly(2026, 3, 1)));
    }
}
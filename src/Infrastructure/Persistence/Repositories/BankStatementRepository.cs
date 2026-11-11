using Application.Banking;
using Domain.Banking;
using Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class BankStatementRepository(AppDbContext db) : IBankStatementRepository
{
    public Task<bool> FileHashExistsAsync(string fileHash, CancellationToken cancellationToken = default)
        => db.BankStatementBatches.AnyAsync(x => x.FileHash == fileHash, cancellationToken);

    public Task<BankStatementBatch?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
        => db.BankStatementBatches.FirstOrDefaultAsync(x => x.Id == batchId, cancellationToken);

    public async Task<IReadOnlyList<BankStatementLine>> GetLinesAsync(
        Guid batchId, CancellationToken cancellationToken = default)
        => await db.BankStatementLines.AsNoTracking().Where(x => x.StatementBatchId == batchId)
            .OrderBy(x => x.RowNumber).ToListAsync(cancellationToken);

    public Task<BankStatementLine?> GetLineAsync(Guid lineId, CancellationToken cancellationToken = default)
        => db.BankStatementLines.FirstOrDefaultAsync(x => x.Id == lineId, cancellationToken);

    public async Task<bool> LockLineAsync(Guid lineId, CancellationToken cancellationToken = default)
    {
        if (!await db.BankStatementLines.AsNoTracking().AnyAsync(x => x.Id == lineId, cancellationToken)) return false;
        await db.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM "BankStatementLines" WHERE "Id" = {lineId} FOR UPDATE""", cancellationToken);
        return true;
    }

    public async Task<bool> LockPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        if (!await db.Payments.AsNoTracking().AnyAsync(x => x.Id == paymentId, cancellationToken)) return false;
        await db.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM "Payments" WHERE "Id" = {paymentId} FOR UPDATE""", cancellationToken);
        return true;
    }

    public Task<bool> PaymentCanMatchAsync(
        BankStatementLine line, Guid paymentId, CancellationToken cancellationToken = default)
    {
        var dateFrom = line.TransactionDate.AddDays(-3);
        var dateTo = line.TransactionDate.AddDays(3);
        return db.Payments.AnyAsync(p => p.Id == paymentId && p.ReversedAt == null
            && p.Amount == Math.Abs(line.Amount) && p.CurrencyCode == line.CurrencyCode
            && p.PaidOn >= dateFrom && p.PaidOn <= dateTo
            && !db.BankStatementLines.Any(m => m.MatchedPaymentId == p.Id), cancellationToken);
    }

    public async Task<BankStatementSuggestionsDto> GetSuggestionsAsync(
        BankStatementLine line, CancellationToken cancellationToken = default)
    {
        if (line.Amount <= 0)
            return new BankStatementSuggestionsDto([], []);

        var dateFrom = line.TransactionDate.AddDays(-3);
        var dateTo = line.TransactionDate.AddDays(3);
        var paymentRows = await (
            from payment in db.Payments.AsNoTracking()
            join invoice in db.Invoices.AsNoTracking() on payment.InvoiceId equals invoice.Id
            join customer in db.Customers.AsNoTracking() on payment.CustomerId equals customer.Id
            where payment.ReversedAt == null && payment.Amount == line.Amount
                  && payment.CurrencyCode == line.CurrencyCode
                  && payment.PaidOn >= dateFrom && payment.PaidOn <= dateTo
                  && (db.BankStatementLines.Any(m => m.MatchedPaymentId == payment.Id) == false)
            select new { payment.Id, invoice.Number, customer.Name, payment.Amount, payment.CurrencyCode,
                payment.PaidOn, payment.Reference }
        ).ToListAsync(cancellationToken);
        var payments = paymentRows.Select(p => new BankPaymentCandidate(p.Id, p.Number, p.Name,
                p.Amount, p.CurrencyCode, p.PaidOn, p.Reference,
                !string.IsNullOrWhiteSpace(line.Reference) && string.Equals(
                    line.Reference.Trim(), p.Reference?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(p => p.ReferenceMatches)
            .ThenBy(p => Math.Abs(p.PaidOn.DayNumber - line.TransactionDate.DayNumber))
            .Take(5).ToList();

        var invoiceRows = await (
            from invoice in db.Invoices.AsNoTracking()
            join customer in db.Customers.AsNoTracking() on invoice.CustomerId equals customer.Id
            where (invoice.Status == InvoiceStatus.Issued || invoice.Status == InvoiceStatus.PartiallyPaid)
                  && invoice.Total - invoice.AmountPaid == line.Amount
                  && invoice.CurrencyCode == line.CurrencyCode
                  && invoice.IssueDate <= line.TransactionDate
            select new BankInvoiceCandidate(invoice.Id, invoice.Number, customer.Name,
                invoice.Total - invoice.AmountPaid, invoice.CurrencyCode, invoice.DueDate)
        ).OrderBy(x => x.DueDate).Take(5).ToListAsync(cancellationToken);

        return new BankStatementSuggestionsDto(payments, invoiceRows);
    }

    public void Add(BankStatementBatch batch) => db.BankStatementBatches.Add(batch);
}

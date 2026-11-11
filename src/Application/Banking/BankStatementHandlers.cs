using System.Globalization;
using Application.Common.Exceptions;
using Application.Common;
using Application.Common.Interfaces;
using Domain.Banking;
using Domain.Common;

namespace Application.Banking;

public sealed class ImportBankStatementHandler(
    IBankStatementRepository statements, IUnitOfWork unitOfWork,
    ICurrentOrganization currentOrganization, ICurrentUser currentUser)
{
    public async Task<BankStatementImportResult> Handle(string fileName, string fileHash,
        IReadOnlyList<BankStatementImportRow> rows, CancellationToken cancellationToken)
    {
        if (await statements.FileHashExistsAsync(fileHash, cancellationToken))
            throw new ConflictException("This statement file has already been imported.");

        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        var organizationId = currentOrganization.RequireId();
        var batch = BankStatementBatch.Create(organizationId, userId, fileName, fileHash);
        var errors = new List<BankStatementImportError>();

        foreach (var row in rows)
        {
            if (!DateOnly.TryParseExact(row.TransactionDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                errors.Add(new(row.RowNumber, "TransactionDate must be a valid date."));
                continue;
            }
            if (!decimal.TryParse(row.Amount?.Trim(), NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                    CultureInfo.InvariantCulture, out var amount))
            {
                errors.Add(new(row.RowNumber, "Amount must be a number using a period as the decimal separator."));
                continue;
            }

            try
            {
                var line = BankStatementLine.Create(organizationId, batch.Id, row.RowNumber,
                    date, row.Description ?? "", amount, row.CurrencyCode ?? "", row.Reference);
                batch.AddLine(line);
            }
            catch (DomainException ex)
            {
                errors.Add(new(row.RowNumber, ex.Message));
            }
        }

        if (batch.ImportedLineCount == 0)
            return new BankStatementImportResult(null, rows.Count, 0, errors.OrderBy(e => e.RowNumber).ToList());

        statements.Add(batch);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new BankStatementImportResult(batch.Id, rows.Count, batch.ImportedLineCount,
            errors.OrderBy(e => e.RowNumber).ToList());
    }
}

public sealed class GetBankStatementHandler(IBankStatementRepository statements)
{
    public async Task<BankStatementDetailsDto> Handle(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await statements.GetBatchAsync(batchId, cancellationToken)
            ?? throw new NotFoundException("Bank statement not found.");
        var lines = await statements.GetLinesAsync(batchId, cancellationToken);
        return new BankStatementDetailsDto(
            new BankStatementBatchDto(batch.Id, batch.FileName, batch.ImportedLineCount, batch.CreatedAt),
            lines.Select(ToDto).ToList());
    }

    internal static BankStatementLineDto ToDto(BankStatementLine line) => new(
        line.Id, line.RowNumber, line.TransactionDate, line.Description, line.Amount, line.CurrencyCode,
        line.Reference, line.Status, line.MatchedPaymentId, line.ReconciledAt, line.ResolutionNote);
}

public sealed class GetBankStatementSuggestionsHandler(IBankStatementRepository statements)
{
    public async Task<BankStatementSuggestionsDto> Handle(Guid lineId, CancellationToken cancellationToken)
    {
        var line = await statements.GetLineAsync(lineId, cancellationToken)
            ?? throw new NotFoundException("Bank statement line not found.");
        return await statements.GetSuggestionsAsync(line, cancellationToken);
    }
}

public sealed class MatchBankStatementPaymentHandler(
    IBankStatementRepository statements, ITransactionRunner transactions,
    IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<BankStatementLineDto> Handle(Guid lineId, MatchStatementPaymentCommand command,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        BankStatementLine? matchedLine = null;
        await transactions.RunInTransactionAsync(async () =>
        {
            if (!await statements.LockLineAsync(lineId, cancellationToken))
                throw new NotFoundException("Bank statement line not found.");
            var line = await statements.GetLineAsync(lineId, cancellationToken)
                ?? throw new NotFoundException("Bank statement line not found.");
            if (line.Amount <= 0)
                throw new DomainException("Only incoming deposits can be matched to recorded customer payments.");
            if (!await statements.LockPaymentAsync(command.PaymentId, cancellationToken)
                || !await statements.PaymentCanMatchAsync(line, command.PaymentId, cancellationToken))
                throw new ConflictException("The payment does not match this statement line or is already reconciled.");

            line.MatchPayment(command.PaymentId, userId, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
            matchedLine = line;
        }, cancellationToken);

        return GetBankStatementHandler.ToDto(matchedLine!);
    }
}

public sealed class IgnoreBankStatementLineHandler(
    IBankStatementRepository statements, ITransactionRunner transactions,
    IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<BankStatementLineDto> Handle(Guid lineId, IgnoreStatementLineCommand command,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Not signed in.");
        BankStatementLine? line = null;
        await transactions.RunInTransactionAsync(async () =>
        {
            if (!await statements.LockLineAsync(lineId, cancellationToken))
                throw new NotFoundException("Bank statement line not found.");
            line = await statements.GetLineAsync(lineId, cancellationToken)
                ?? throw new NotFoundException("Bank statement line not found.");
            line.Ignore(command.Reason, userId, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
        return GetBankStatementHandler.ToDto(line!);
    }
}

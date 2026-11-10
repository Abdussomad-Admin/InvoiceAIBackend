using System.Globalization;
using System.Text.Json;
using Application.Bills;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Invoices;
using Application.Organizations;
using Application.Suppliers;
using Domain.Bills;
using Domain.Common;
using Domain.Documents;
using Domain.Invoices;
using Domain.Organizations;
using Domain.Suppliers;

namespace Application.Documents;

public sealed class ApproveDocumentHandler(
    IDocumentRepository documents, IBillRepository bills, ISupplierRepository suppliers,
    IOrganizationRepository organizations, ITransactionRunner transactions, IUnitOfWork unitOfWork,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<ApprovalResultDto> Handle(
        Guid documentId, ApproveDocumentCommand command, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var today = InvoiceLookups.Today(organization, clock);

        // Checks that need no database come first.
        if ((command.SupplierId is null) == (command.NewSupplier is null))
            throw new DomainException(
                "Choose an existing supplier, or give the details of a new one. Not both, and not neither.");
        if (command.Lines is not { Count: > 0 })
            throw new DomainException("Add at least one line.");
        BillLookups.EnsureNotInFuture(command.BillDate, today);

        ApprovalResultDto? result = null;

        await transactions.RunInTransactionAsync(async () =>
        {
            // Lock, then load: a second approval of the same document waits here and then sees it approved.
            if (!await documents.LockAsync(documentId, cancellationToken))
                throw new NotFoundException("Document not found.");

            var document = await documents.GetByIdAsync(documentId, cancellationToken)
                ?? throw new NotFoundException("Document not found.");

            document.EnsureCanApprove();
            EnsureCurrencyAcknowledged(document, organization, command);

            Supplier supplier;
            var created = false;

            if (command.SupplierId is { } supplierId)
            {
                supplier = await BillLookups.ActiveSupplierAsync(suppliers, supplierId, cancellationToken);
                await BillLookups.EnsureNumberFreeAsync(bills, supplier.Id, command.Number, Guid.Empty, cancellationToken);
            }
            else
            {
                supplier = await CreateSupplierAsync(organization.Id, command.NewSupplier!, cancellationToken);
                created = true;
            }

            var dueDate = command.DueDate ?? command.BillDate.AddDays(supplier.DefaultPaymentTermsDays);

            var bill = SupplierBill.CreateDraft(
                organization.Id, supplier.Id, organization.BaseCurrencyCode,
                command.Number, command.BillDate, dueDate, command.Notes);

            bill.ReplaceLines(command.Lines.Select(l =>
                new LineInput(l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxRatePercent)));

            if (command.PrintedTotal is { } printed && Math.Abs(bill.Total - printed) > 0.01m)
                throw new DomainException(
                    $"The lines add up to {Money(bill.Total)}, but you entered a printed total of {Money(printed)}. " +
                    "Correct the lines or the total.");

            bills.Add(bill);
            document.Approve(bill.Id, clock.GetUtcNow());

            // The supplier (if new), the bill and the document's link are saved together or not at all.
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var duplicates = await bills.FindPossibleDuplicatesAsync(
                supplier.Id, bill.Total, bill.BillDate, bill.Id, cancellationToken);

            result = new ApprovalResultDto(bill.ToDto(supplier.Name, today, duplicates), created, document.Id);
        }, cancellationToken);

        return result!;
    }

    private async Task<Supplier> CreateSupplierAsync(
        Guid organizationId, NewSupplierInput input, CancellationToken cancellationToken)
    {
        // A new supplier that matches an existing one would create a duplicate record, so it is refused.
        var candidates = await suppliers.ListForMatchingAsync(cancellationToken);
        var (_, strongMatchId) = SupplierMatcher.Match(input.Name, input.TaxIdentificationNumber, candidates);

        if (strongMatchId is { } existingId)
        {
            var name = candidates.First(c => c.Id == existingId).Name;
            throw new ConflictException($"A supplier called '{name}' already exists. Choose it instead of creating a new one.");
        }

        var supplier = Supplier.Create(organizationId, input.Name);
        supplier.Update(
            input.Name, input.Email, input.Phone, input.TaxIdentificationNumber,
            null, null, null, null, null, null, null, input.DefaultPaymentTermsDays ?? 30);

        suppliers.Add(supplier);
        return supplier;
    }

    // The stored extraction is only consulted for this one check. No values are taken from it.
    private static void EnsureCurrencyAcknowledged(
        SourceDocument document, Organization organization, ApproveDocumentCommand command)
    {
        if (document.ExtractedData is not { } json) return;

        var extracted = JsonSerializer.Deserialize<ExtractedBill>(json, ExtractDocumentHandler.StoredJson);

        if (extracted?.CurrencyCode is { } currency &&
            currency != organization.BaseCurrencyCode && !command.ConfirmCurrencyIsBase)
            throw new DomainException(
                $"The document appears to be in {currency}, but bills are recorded in {organization.BaseCurrencyCode}. " +
                $"If the amounts you entered are already in {organization.BaseCurrencyCode}, send confirmCurrencyIsBase as true.");
    }

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}

public sealed class GetDocumentByBillHandler(IDocumentRepository documents)
{
    public async Task<DocumentDto> Handle(Guid billId, CancellationToken cancellationToken)
    {
        var document = await documents.GetByBillIdAsync(billId, cancellationToken)
            ?? throw new NotFoundException("No uploaded document is linked to this bill. It was entered by hand.");

        return document.ToDto();
    }
}
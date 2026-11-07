using Application.Common;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Invoices;
using Application.Organizations;
using Application.Suppliers;
using Domain.Bills;
using Domain.Common;
using Domain.Invoices;
using Domain.Suppliers;

namespace Application.Bills;

internal static class BillLookups
{
    public static async Task<Supplier> ActiveSupplierAsync(
        ISupplierRepository suppliers, Guid id, CancellationToken cancellationToken)
    {
        var supplier = await suppliers.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Supplier not found.");
        if (supplier.IsArchived) throw new DomainException("This supplier is archived.");
        return supplier;
    }

    public static async Task<string> SupplierNameAsync(
        ISupplierRepository suppliers, Guid id, CancellationToken cancellationToken)
        => (await suppliers.GetByIdAsync(id, cancellationToken))?.Name ?? "(unknown supplier)";

    public static IEnumerable<LineInput> ToLineInputs(BillInput input)
        => (input.Lines ?? Array.Empty<BillLineInput>()).Select(l =>
            new LineInput(l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxRatePercent));

    public static void EnsureNotInFuture(DateOnly billDate, DateOnly today)
    {
        if (billDate > today)
            throw new DomainException("The bill date cannot be in the future.");
    }

    public static async Task EnsureNumberFreeAsync(
        IBillRepository bills, Guid supplierId, string number, Guid excludeBillId, CancellationToken cancellationToken)
    {
        var key = SupplierBill.KeyFor(number); // also rejects a blank number
        if (await bills.NumberInUseAsync(supplierId, key, excludeBillId, cancellationToken))
            throw new ConflictException($"A bill numbered '{number.Trim()}' already exists for this supplier.");
    }
}

internal static class BillMapping
{
    public static BillDto ToDto(
        this SupplierBill b, string supplierName, DateOnly today, IReadOnlyList<PossibleDuplicateDto> duplicates) => new(
        b.Id, b.Number, b.SupplierId, supplierName, b.CurrencyCode, b.Status, b.BillDate, b.DueDate, b.Notes,
        b.Subtotal, b.DiscountTotal, b.TaxTotal, b.Total, b.AmountPaid, b.Balance,
        b.IsOverdue(today), b.DaysOverdue(today), b.VoidReason,
        b.Lines.OrderBy(l => l.Position).Select(l => new BillLineDto(
            l.Position, l.Description, l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxRatePercent,
            l.LineSubtotal, l.DiscountAmount, l.TaxAmount, l.LineTotal)).ToList(),
        duplicates);
}

public sealed class CreateBillHandler(
    IBillRepository bills, ISupplierRepository suppliers, IOrganizationRepository organizations,
    IUnitOfWork unitOfWork, ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<BillDto> Handle(BillInput input, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var supplier = await BillLookups.ActiveSupplierAsync(suppliers, input.SupplierId, cancellationToken);
        var today = InvoiceLookups.Today(organization, clock);

        BillLookups.EnsureNotInFuture(input.BillDate, today);
        var dueDate = input.DueDate ?? input.BillDate.AddDays(supplier.DefaultPaymentTermsDays);

        await BillLookups.EnsureNumberFreeAsync(bills, supplier.Id, input.Number, Guid.Empty, cancellationToken);

        var bill = SupplierBill.CreateDraft(
            organization.Id, supplier.Id, organization.BaseCurrencyCode,
            input.Number, input.BillDate, dueDate, input.Notes);
        bill.ReplaceLines(BillLookups.ToLineInputs(input));

        bills.Add(bill);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var duplicates = await bills.FindPossibleDuplicatesAsync(
            supplier.Id, bill.Total, bill.BillDate, bill.Id, cancellationToken);
        return bill.ToDto(supplier.Name, today, duplicates);
    }
}

public sealed class UpdateBillHandler(
    IBillRepository bills, ISupplierRepository suppliers, IOrganizationRepository organizations,
    IUnitOfWork unitOfWork, ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<BillDto> Handle(Guid id, BillInput input, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var bill = await bills.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Bill not found.");
        var supplier = await BillLookups.ActiveSupplierAsync(suppliers, input.SupplierId, cancellationToken);
        var today = InvoiceLookups.Today(organization, clock);

        BillLookups.EnsureNotInFuture(input.BillDate, today);
        var dueDate = input.DueDate ?? input.BillDate.AddDays(supplier.DefaultPaymentTermsDays);

        // The entity refuses first if the bill is no longer a draft.
        bill.UpdateDetails(supplier.Id, input.Number, input.BillDate, dueDate, input.Notes);
        await BillLookups.EnsureNumberFreeAsync(bills, supplier.Id, input.Number, bill.Id, cancellationToken);
        bill.ReplaceLines(BillLookups.ToLineInputs(input));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var duplicates = await bills.FindPossibleDuplicatesAsync(
            supplier.Id, bill.Total, bill.BillDate, bill.Id, cancellationToken);
        return bill.ToDto(supplier.Name, today, duplicates);
    }
}

public sealed class FinalizeBillHandler(
    IBillRepository bills, ISupplierRepository suppliers, IOrganizationRepository organizations,
    IUnitOfWork unitOfWork, ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<BillDto> Handle(Guid id, FinalizeBillCommand command, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var bill = await bills.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Bill not found.");

        await BillLookups.EnsureNumberFreeAsync(bills, bill.SupplierId, bill.Number, bill.Id, cancellationToken);

        var duplicates = await bills.FindPossibleDuplicatesAsync(
            bill.SupplierId, bill.Total, bill.BillDate, bill.Id, cancellationToken);

        if (duplicates.Count > 0 && !command.ConfirmPossibleDuplicates)
            throw new ConflictException(
                "This bill may duplicate: " + string.Join(", ", duplicates.Select(d => d.Number)) +
                ". If it is a genuine separate bill, send confirmPossibleDuplicates as true.");

        bill.Finalize(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var name = await BillLookups.SupplierNameAsync(suppliers, bill.SupplierId, cancellationToken);
        return bill.ToDto(name, InvoiceLookups.Today(organization, clock), []);
    }
}

public sealed class VoidBillHandler(
    IBillRepository bills, ISupplierRepository suppliers, IOrganizationRepository organizations,
    IUnitOfWork unitOfWork, ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<BillDto> Handle(Guid id, VoidBillCommand command, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var bill = await bills.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Bill not found.");

        bill.Void(command.Reason, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var name = await BillLookups.SupplierNameAsync(suppliers, bill.SupplierId, cancellationToken);
        return bill.ToDto(name, InvoiceLookups.Today(organization, clock), []);
    }
}

public sealed class GetBillHandler(
    IBillRepository bills, ISupplierRepository suppliers, IOrganizationRepository organizations,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<BillDto> Handle(Guid id, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var bill = await bills.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Bill not found.");

        // Possible duplicates matter during review, so they are only computed for drafts.
        IReadOnlyList<PossibleDuplicateDto> duplicates = bill.Status == BillStatus.Draft
            ? await bills.FindPossibleDuplicatesAsync(
                bill.SupplierId, bill.Total, bill.BillDate, bill.Id, cancellationToken)
            : [];

        var name = await BillLookups.SupplierNameAsync(suppliers, bill.SupplierId, cancellationToken);
        return bill.ToDto(name, InvoiceLookups.Today(organization, clock), duplicates);
    }
}

public sealed class ListBillsHandler(
    IBillRepository bills, IOrganizationRepository organizations,
    ICurrentOrganization currentOrganization, TimeProvider clock)
{
    public async Task<PagedResult<BillSummaryDto>> Handle(ListBillsQuery query, CancellationToken cancellationToken)
    {
        var organization = await InvoiceLookups.OrganizationAsync(organizations, currentOrganization, cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, total) = await bills.SearchAsync(
            new BillSearch(query.Search, query.Status, query.SupplierId, query.Overdue, query.DueBy),
            InvoiceLookups.Today(organization, clock), (page - 1) * pageSize, pageSize, cancellationToken);

        return new PagedResult<BillSummaryDto>(items, total, page, pageSize);
    }
}
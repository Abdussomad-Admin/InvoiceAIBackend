using Application.Bills;

namespace Application.Documents;

public sealed record NewSupplierInput(
    string Name, string? TaxIdentificationNumber, string? Email, string? Phone, int? DefaultPaymentTermsDays);

public sealed record ApproveDocumentCommand(
    Guid? SupplierId, NewSupplierInput? NewSupplier, string Number, DateOnly BillDate, DateOnly? DueDate,
    string? Notes, IReadOnlyList<BillLineInput> Lines, decimal? PrintedTotal, bool ConfirmCurrencyIsBase);

public sealed record ApprovalResultDto(BillDto Bill, bool SupplierCreated, Guid DocumentId);
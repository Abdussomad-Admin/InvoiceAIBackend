using Domain.Bills;
using Domain.Payments;

namespace Application.Bills;

public sealed record RecordBillPaymentCommand(
    decimal Amount, DateOnly? PaidOn, PaymentMethod Method, string? Reference, string? Notes);

public sealed record ReverseBillPaymentCommand(string Reason);

public sealed record ListBillPaymentsQuery(
    Guid? BillId, Guid? SupplierId, DateOnly? From, DateOnly? To, bool IncludeReversed, int Page, int PageSize);

public sealed record BillPaymentSearch(
    Guid? BillId, Guid? SupplierId, DateOnly? From, DateOnly? To, bool IncludeReversed);

public sealed record BillPaymentDto(
    Guid Id, Guid BillId, string BillNumber, Guid SupplierId, string SupplierName,
    decimal Amount, string CurrencyCode, DateOnly PaidOn, PaymentMethod Method,
    string? Reference, string? Notes, Guid RecordedByUserId, DateTimeOffset CreatedAt,
    bool IsReversed, DateTimeOffset? ReversedAt, string? ReversalReason);

public interface IBillPaymentRepository
{
    Task<BillPayment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Guid?> GetBillIdAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<bool> ReferenceInUseAsync(Guid billId, string reference, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<BillPaymentDto> Items, int Total)> SearchAsync(
        BillPaymentSearch filter, int skip, int take, CancellationToken cancellationToken = default);

    void Add(BillPayment payment);
}
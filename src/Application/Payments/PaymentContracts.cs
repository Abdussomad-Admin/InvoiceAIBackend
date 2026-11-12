using Domain.Payments;

namespace Application.Payments;

public sealed record RecordPaymentCommand(
    decimal Amount, DateOnly? PaidOn, PaymentMethod Method, string? Reference, string? Notes,
    decimal? ExchangeRateToBase = null);

public sealed record ReversePaymentCommand(string Reason);

public sealed record ListPaymentsQuery(
    Guid? InvoiceId, Guid? CustomerId, DateOnly? From, DateOnly? To, bool IncludeReversed, int Page, int PageSize);

public sealed record PaymentSearch(
    Guid? InvoiceId, Guid? CustomerId, DateOnly? From, DateOnly? To, bool IncludeReversed);

public sealed record PaymentDto(
    Guid Id, Guid InvoiceId, string? InvoiceNumber, Guid CustomerId, string CustomerName,
    decimal Amount, string CurrencyCode, decimal ExchangeRateToBase, DateOnly PaidOn, PaymentMethod Method,
    string? Reference, string? Notes, Guid RecordedByUserId, DateTimeOffset CreatedAt,
    bool IsReversed, DateTimeOffset? ReversedAt, string? ReversalReason);

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Guid?> GetInvoiceIdAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<bool> ReferenceInUseAsync(Guid invoiceId, string reference, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<PaymentDto> Items, int Total)> SearchAsync(
        PaymentSearch filter, int skip, int take, CancellationToken cancellationToken = default);

    void Add(Payment payment);
}

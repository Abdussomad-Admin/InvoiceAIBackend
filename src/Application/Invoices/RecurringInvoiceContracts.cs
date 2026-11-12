using Domain.Invoices;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Application.Customers;
using Application.Organizations;
using Domain.Common;
using Application.Common;

namespace Application.Invoices;

public sealed record CreateRecurringInvoiceCommand(Guid CustomerId, string Description, string CurrencyCode,
    int PaymentTermsDays, RecurrenceFrequency Frequency, DateOnly FirstRunOn, IReadOnlyList<InvoiceLineInput> Lines,
    decimal? ExchangeRateToBase = null);
public sealed record RecurringInvoiceDto(Guid Id, Guid CustomerId, string CustomerName, string Description,
    string CurrencyCode, string BaseCurrencyCode, decimal ExchangeRateToBase,
    RecurrenceFrequency Frequency, DateOnly NextRunOn, int PaymentTermsDays,
    bool IsActive, DateTimeOffset? LastGeneratedAt);

public interface IRecurringInvoiceRepository
{
    Task<IReadOnlyList<RecurringInvoiceDto>> ListAsync(CancellationToken ct);
    Task<RecurringInvoiceSchedule?> GetAsync(Guid id, CancellationToken ct);
    void Add(RecurringInvoiceSchedule schedule);
    Task<IReadOnlyList<RecurringInvoiceSchedule>> DueAsync(DateOnly today, CancellationToken ct);
}

public sealed class RecurringInvoiceHandlers(IRecurringInvoiceRepository schedules,
    IOrganizationRepository organizations, ICustomerRepository customers, ICurrentOrganization current,
    IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<RecurringInvoiceDto>> List(CancellationToken ct)
    {
        _ = current.RequireId();
        return await schedules.ListAsync(ct);
    }
    public async Task<RecurringInvoiceDto> Create(CreateRecurringInvoiceCommand command, CancellationToken ct)
    {
        var organization = await organizations.GetByIdAsync(current.RequireId(), ct)
            ?? throw new NotFoundException("Workspace not found.");
        var customer = await customers.GetByIdAsync(command.CustomerId, ct)
            ?? throw new NotFoundException("Customer not found.");
        if (customer.IsArchived) throw new DomainException("This customer is archived.");
        var currencyCode = command.CurrencyCode.Trim().ToUpperInvariant();
        if (currencyCode != organization.BaseCurrencyCode && command.ExchangeRateToBase is null)
            throw new DomainException($"Enter the {currencyCode} to {organization.BaseCurrencyCode} exchange rate.");
        var schedule = RecurringInvoiceSchedule.Create(organization.Id, customer.Id, command.Description,
            currencyCode, organization.BaseCurrencyCode, command.ExchangeRateToBase ?? 1m,
            command.Lines.Select(l => new LineInput(l.Description,l.Quantity,l.UnitPrice,l.DiscountPercent,l.TaxRatePercent)).ToList(),
            command.Frequency, command.FirstRunOn, command.PaymentTermsDays);
        schedules.Add(schedule);
        await unitOfWork.SaveChangesAsync(ct);
        return new(schedule.Id, customer.Id, customer.Name, schedule.Description, schedule.CurrencyCode,
            schedule.BaseCurrencyCode, schedule.ExchangeRateToBase,
            schedule.Frequency, schedule.NextRunOn, schedule.PaymentTermsDays, schedule.IsActive, schedule.LastGeneratedAt);
    }
    public async Task<RecurringInvoiceDto> SetActive(Guid id, bool active, CancellationToken ct)
    {
        var schedule = await schedules.GetAsync(id, ct) ?? throw new NotFoundException("Recurring invoice schedule not found.");
        if (active) schedule.Resume(); else schedule.Pause();
        await unitOfWork.SaveChangesAsync(ct);
        return (await schedules.ListAsync(ct)).First(x => x.Id == id);
    }
}

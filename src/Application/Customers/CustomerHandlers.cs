using Application.Common;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Domain.Customers;

namespace Application.Customers;

public sealed record CustomerInput(
    string Name, string? Email, string? Phone, string? TaxIdentificationNumber,
    string? AddressLine1, string? AddressLine2, string? City, string? Region,
    string? PostalCode, string? CountryCode, string? Notes);

public sealed record CustomerDto(
    Guid Id, string Name, string? Email, string? Phone, string? TaxIdentificationNumber,
    string? AddressLine1, string? AddressLine2, string? City, string? Region,
    string? PostalCode, string? CountryCode, string? Notes, bool IsArchived, DateTimeOffset CreatedAt);

public sealed record ListCustomersQuery(string? Search, bool IncludeArchived, int Page, int PageSize);

internal static class CustomerMapping
{
    public static void Apply(this Customer c, CustomerInput i) => c.Update(
        i.Name, i.Email, i.Phone, i.TaxIdentificationNumber, i.AddressLine1, i.AddressLine2,
        i.City, i.Region, i.PostalCode, i.CountryCode, i.Notes);

    public static CustomerDto ToDto(this Customer c) => new(
        c.Id, c.Name, c.Email, c.Phone, c.TaxIdentificationNumber, c.AddressLine1, c.AddressLine2,
        c.City, c.Region, c.PostalCode, c.CountryCode, c.Notes, c.IsArchived, c.CreatedAt);
}

public sealed class CreateCustomerHandler(
    ICustomerRepository customers, IUnitOfWork unitOfWork, ICurrentOrganization currentOrganization)
{
    public async Task<CustomerDto> Handle(CustomerInput input, CancellationToken cancellationToken)
    {
        var customer = Customer.Create(currentOrganization.RequireId(), input.Name);
        customer.Apply(input);
        customers.Add(customer);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return customer.ToDto();
    }
}

public sealed class UpdateCustomerHandler(ICustomerRepository customers, IUnitOfWork unitOfWork)
{
    public async Task<CustomerDto> Handle(Guid id, CustomerInput input, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Customer not found.");
        customer.Apply(input);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return customer.ToDto();
    }
}

public sealed class ArchiveCustomerHandler(ICustomerRepository customers, IUnitOfWork unitOfWork)
{
    public async Task Handle(Guid id, bool archived, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Customer not found.");
        if (archived) customer.Archive(); else customer.Restore();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class GetCustomerHandler(ICustomerRepository customers)
{
    public async Task<CustomerDto> Handle(Guid id, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Customer not found.");
        return customer.ToDto();
    }
}

public sealed class ListCustomersHandler(ICustomerRepository customers)
{
    public async Task<PagedResult<CustomerDto>> Handle(ListCustomersQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, total) = await customers.SearchAsync(
            query.Search, query.IncludeArchived, (page - 1) * pageSize, pageSize, cancellationToken);

        return new PagedResult<CustomerDto>(items.Select(c => c.ToDto()).ToList(), total, page, pageSize);
    }
}
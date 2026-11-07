using Application.Common;
using Application.Common.Exceptions;
using Application.Common.Interfaces;
using Domain.Suppliers;

namespace Application.Suppliers;

public sealed record SupplierInput(
    string Name, string? Email, string? Phone, string? TaxIdentificationNumber,
    string? AddressLine1, string? AddressLine2, string? City, string? Region,
    string? PostalCode, string? CountryCode, string? Notes, int? DefaultPaymentTermsDays);

public sealed record SupplierDto(
    Guid Id, string Name, string? Email, string? Phone, string? TaxIdentificationNumber,
    string? AddressLine1, string? AddressLine2, string? City, string? Region,
    string? PostalCode, string? CountryCode, string? Notes, int DefaultPaymentTermsDays,
    bool IsArchived, DateTimeOffset CreatedAt);

public sealed record ListSuppliersQuery(string? Search, bool IncludeArchived, int Page, int PageSize);

public interface ISupplierRepository
{
    Task<Supplier?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Supplier> Items, int Total)> SearchAsync(
        string? search, bool includeArchived, int skip, int take, CancellationToken cancellationToken = default);

    void Add(Supplier supplier);
}

internal static class SupplierMapping
{
    public static void Apply(this Supplier s, SupplierInput i) => s.Update(
        i.Name, i.Email, i.Phone, i.TaxIdentificationNumber, i.AddressLine1, i.AddressLine2,
        i.City, i.Region, i.PostalCode, i.CountryCode, i.Notes,
        i.DefaultPaymentTermsDays ?? s.DefaultPaymentTermsDays);

    public static SupplierDto ToDto(this Supplier s) => new(
        s.Id, s.Name, s.Email, s.Phone, s.TaxIdentificationNumber, s.AddressLine1, s.AddressLine2,
        s.City, s.Region, s.PostalCode, s.CountryCode, s.Notes, s.DefaultPaymentTermsDays,
        s.IsArchived, s.CreatedAt);
}

public sealed class CreateSupplierHandler(
    ISupplierRepository suppliers, IUnitOfWork unitOfWork, ICurrentOrganization currentOrganization)
{
    public async Task<SupplierDto> Handle(SupplierInput input, CancellationToken cancellationToken)
    {
        var supplier = Supplier.Create(currentOrganization.RequireId(), input.Name);
        supplier.Apply(input);
        suppliers.Add(supplier);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return supplier.ToDto();
    }
}

public sealed class UpdateSupplierHandler(ISupplierRepository suppliers, IUnitOfWork unitOfWork)
{
    public async Task<SupplierDto> Handle(Guid id, SupplierInput input, CancellationToken cancellationToken)
    {
        var supplier = await suppliers.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Supplier not found.");
        supplier.Apply(input);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return supplier.ToDto();
    }
}

public sealed class ArchiveSupplierHandler(ISupplierRepository suppliers, IUnitOfWork unitOfWork)
{
    public async Task Handle(Guid id, bool archived, CancellationToken cancellationToken)
    {
        var supplier = await suppliers.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Supplier not found.");
        if (archived) supplier.Archive(); else supplier.Restore();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class GetSupplierHandler(ISupplierRepository suppliers)
{
    public async Task<SupplierDto> Handle(Guid id, CancellationToken cancellationToken)
    {
        var supplier = await suppliers.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Supplier not found.");
        return supplier.ToDto();
    }
}

public sealed class ListSuppliersHandler(ISupplierRepository suppliers)
{
    public async Task<PagedResult<SupplierDto>> Handle(ListSuppliersQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, total) = await suppliers.SearchAsync(
            query.Search, query.IncludeArchived, (page - 1) * pageSize, pageSize, cancellationToken);

        return new PagedResult<SupplierDto>(items.Select(s => s.ToDto()).ToList(), total, page, pageSize);
    }
}
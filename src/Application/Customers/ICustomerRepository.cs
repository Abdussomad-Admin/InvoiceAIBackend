using Domain.Customers;

namespace Application.Customers;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Customer> Items, int Total)> SearchAsync(
        string? search, bool includeArchived, int skip, int take, CancellationToken cancellationToken = default);

    void Add(Customer customer);
}
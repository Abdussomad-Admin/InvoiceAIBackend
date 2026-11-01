using Domain.Users;

namespace Application.Users;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    void Add(User user);
}
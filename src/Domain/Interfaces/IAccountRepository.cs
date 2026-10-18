using Domain.Entities;

namespace Domain.Interfaces;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Account account);

    void Remove(Account account);
}

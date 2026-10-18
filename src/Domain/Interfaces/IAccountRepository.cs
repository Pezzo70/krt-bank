using Domain.Entities;
using Domain.ValueObjects;

namespace Domain.Interfaces;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, bool cacheable = true, CancellationToken cancellationToken = default);

    Task<bool> ExistsByCpfAsync(Cpf cpf, CancellationToken cancellationToken = default);

    void Add(Account account);

    void Remove(Account account);
}

using Domain.Entities;

namespace Domain.Interfaces;

public interface IContaRepository
{
    Task<Conta?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Conta conta);

    void Remove(Conta conta);
}

using Domain.Entities;

namespace Domain.Interfaces;

public interface IAccountQueries
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

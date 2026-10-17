using Domain.Models;

namespace Domain.Interfaces;

public interface IContaQueries
{
    Task<ContaConsulta?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

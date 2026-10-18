using Domain.Entities;

namespace Domain.Interfaces;

public interface IOutboxRepository
{
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default);
}

using FluentResults;

namespace Domain.Interfaces;

public interface IOutboxService
{
    Task<Result<int>> ProcessPendingAsync(CancellationToken cancellationToken = default);
}

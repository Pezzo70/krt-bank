using FluentResults;

namespace Domain.Interfaces;

public interface IUnitOfWork
{
    Task<Result> CommitAsync(CancellationToken cancellationToken = default);
}

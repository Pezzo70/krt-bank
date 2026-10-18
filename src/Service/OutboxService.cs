using Domain.Interfaces;
using FluentResults;

namespace Service;

public sealed class OutboxService(IOutboxRepository repository, IUnitOfWork unitOfWork) : IOutboxService
{
    public async Task<Result<int>> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        var messages = await repository.GetPendingAsync(100, cancellationToken);
        if (messages.Count == 0)
            return Result.Ok(0);

        var publishedAt = DateTimeOffset.UtcNow;
        foreach (var message in messages)
            message.PublishedAt = publishedAt;

        var commit = await unitOfWork.CommitAsync(cancellationToken);
        return commit.IsFailed ? Result.Fail<int>(commit.Errors) : Result.Ok(messages.Count);
    }
}

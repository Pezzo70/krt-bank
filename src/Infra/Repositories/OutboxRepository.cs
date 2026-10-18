using Domain.Entities;
using Domain.Interfaces;
using Infra.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infra.Repositories;

public sealed class OutboxRepository(BankDbContext context) : IOutboxRepository
{
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize, CancellationToken cancellationToken = default) =>
        await context.OutboxMessages
            .Where(message => message.PublishedAt == null)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.InternalId)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
}

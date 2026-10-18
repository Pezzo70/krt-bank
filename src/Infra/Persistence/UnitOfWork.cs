using Domain.Entities;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infra.Persistence;

public sealed class UnitOfWork(BankDbContext context, IOutboxMessageFactory messageFactory) : IUnitOfWork
{
    private bool _failed;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_failed)
        {
            throw new InvalidOperationException("The operation failed. Discard this Unit of Work and its context.");
        }

        try
        {
            context.ChangeTracker.DetectChanges();
            var entries = context.ChangeTracker.Entries<Entity>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToList();

            foreach (var entry in entries)
            {
                if (entry.State == EntityState.Deleted)
                {
                    // Direct context removal also respects soft deletion.
                    if (!entry.Property(x => x.IsActive).OriginalValue && !entry.Entity.IsActive)
                    {
                        entry.State = EntityState.Unchanged;
                        continue;
                    }
                    entry.State = EntityState.Modified;
                    entry.Entity.SetActive(false);
                }

                var operation = entry.State == EntityState.Added ? "created" :
                    entry.Property(x => x.IsActive).OriginalValue && !entry.Entity.IsActive
                        ? "deleted" : "updated";

                if (entry.State == EntityState.Modified)
                {
                    entry.Property(x => x.UpdatedAt).CurrentValue = DateTimeOffset.UtcNow;
                }

                // Outbox persistence must not produce another Outbox notification.
                if (entry.Entity is OutboxMessage) continue;

                context.OutboxMessages.Add(
                    messageFactory.Create(entry.Entity, entry.Metadata.ClrType, operation));
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            _failed = true;
            throw;
        }
    }
}

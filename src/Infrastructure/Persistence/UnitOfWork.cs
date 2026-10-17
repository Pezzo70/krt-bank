using Domain.Entities;
using Domain.Interfaces;
using Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class UnitOfWork(BankDbContext context, OutboxMessageFactory messageFactory) : IUnitOfWork
{
    private bool _failed;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_failed)
        {
            throw new InvalidOperationException("A operação falhou. Descarte esta Unit of Work e seu contexto.");
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
                    // Uma remoção direta do contexto também respeita a exclusão lógica.
                    if (!entry.Property(x => x.IsActive).OriginalValue && !entry.Entity.IsActive)
                    {
                        entry.State = EntityState.Unchanged;
                        continue;
                    }
                    entry.State = EntityState.Modified;
                    entry.Entity.SetActive(false);
                }

                var operation = entry.State == EntityState.Added ? "criada" :
                    entry.Property(x => x.IsActive).OriginalValue && !entry.Entity.IsActive
                        ? "excluida" : "atualizada";

                if (entry.State == EntityState.Modified)
                {
                    entry.Property(x => x.UpdatedAt).CurrentValue = DateTimeOffset.UtcNow;
                }

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

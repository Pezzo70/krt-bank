using Domain.Entities;
using Domain.Errors;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using FluentResults;
using Npgsql;

namespace Infra.Persistence;

public sealed class UnitOfWork(BankDbContext context) : IUnitOfWork
{
    private bool _failed;

    public async Task<Result> CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_failed)
        {
            throw new InvalidOperationException("The operation failed. Discard this Unit of Work and its context.");
        }

        try
        {
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

                if (entry.Entity is Account account)
                    context.OutboxMessages.Add(new OutboxMessage(account, operation));
            }

            await context.SaveChangesAsync(cancellationToken);
            return Result.Ok();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_accounts_cpf"
        })
        {
            _failed = true;
            return Result.Fail(new ConflictError("CPF is already registered."));
        }
        catch
        {
            _failed = true;
            throw;
        }
    }
}

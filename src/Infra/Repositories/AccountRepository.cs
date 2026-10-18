using Domain.Entities;
using Domain.Interfaces;
using Domain.ValueObjects;
using Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infra.Repositories;

public sealed class AccountRepository(
    BankDbContext context, ReadySetDbContext readySetContext, ILogger<AccountRepository> logger) : IAccountRepository
{
    public async Task<Account?> GetByIdAsync(
        Guid id, bool cacheable = true, CancellationToken cancellationToken = default)
    {
        var accounts = cacheable ? readySetContext.Accounts.AsNoTracking() : context.Accounts;

        try
        {
            return (await accounts.Where(account => account.Id == id).ToListAsync(cancellationToken))
                .SingleOrDefault();
        }
        catch (Exception exception) when (cacheable && !cancellationToken.IsCancellationRequested &&
            exception is NpgsqlException { IsTransient: true } or TimeoutException or
                InvalidOperationException { InnerException: NpgsqlException { IsTransient: true } or TimeoutException })
        {
            logger.LogWarning("ReadySet is unavailable ({ErrorType}); falling back to PostgreSQL.",
                exception.GetType().Name);
            return await context.Accounts.AsNoTracking()
                .SingleOrDefaultAsync(account => account.Id == id, cancellationToken);
        }
    }

    public Task<bool> ExistsByCpfAsync(Cpf cpf, CancellationToken cancellationToken = default) =>
        context.Accounts.IgnoreQueryFilters().AsNoTracking().AnyAsync(
            account => account.Cpf == cpf,
            cancellationToken);

    public void Add(Account account) => context.Accounts.Add(account);

    public void Remove(Account account) => account.SetActive(false);
}

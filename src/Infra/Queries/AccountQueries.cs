using Domain.Interfaces;
using Domain.Entities;
using Infra.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infra.Queries;

public sealed class AccountQueries(
    ReadySetDbContext readySetContext,
    BankDbContext postgresContext,
    ILogger<AccountQueries> logger) : IAccountQueries
{
    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ReadAsync(readySetContext.Accounts, id, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
            exception is NpgsqlException { IsTransient: true } or TimeoutException or
                InvalidOperationException { InnerException: NpgsqlException { IsTransient: true } or TimeoutException })
        {
            logger.LogWarning("ReadySet is unavailable ({ErrorType}); falling back to PostgreSQL.",
                exception.GetType().Name);
            return await ReadAsync(postgresContext.Accounts, id, cancellationToken);
        }
    }

    private static async Task<Account?> ReadAsync(
        IQueryable<Account> accounts, Guid id, CancellationToken cancellationToken)
    {
        // Id is unique: the query returns at most one row. Keeping the expression
        // intact lets the cache definition use exactly the same EF-generated SQL.
        var results = await AccountReadQuery.ById(accounts, id).ToListAsync(cancellationToken);
        return results.SingleOrDefault();
    }
}

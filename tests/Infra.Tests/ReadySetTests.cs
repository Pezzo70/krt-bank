using Domain.Interfaces;
using Infra.Persistence;
using Infra.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Infra.Tests;

[Collection("Infra")]
public sealed class ReadySetTests(InfraFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Read_ShouldBeServedFromDeepCache()
    {
        await using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var account = PersistenceTests.NewAccount();
        scope.ServiceProvider.GetRequiredService<IAccountRepository>().Add(account);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        var queries = scope.ServiceProvider.GetRequiredService<IAccountQueries>();
        await InfraFixture.EventuallyAsync(async () => (await queries.GetByIdAsync(account.Id))?.Name == account.Name);

        // Disable the origin to prove the EF reader receives results from ReadySet.
        var unavailableOrigin = new NpgsqlConnectionStringBuilder(fixture.PostgresConnectionString)
        {
            Host = "127.0.0.1", Port = 1, Timeout = 1
        }.ConnectionString;
        await using var sources = new DatabaseConnections(unavailableOrigin, fixture.ReadySetConnectionString);
        await using var cache = new ReadySetDbContext(new DbContextOptionsBuilder<ReadySetDbContext>()
            .UseNpgsql(sources.ReadySet).Options);
        await using var origin = new BankDbContext(new DbContextOptionsBuilder<BankDbContext>()
            .UseNpgsql(sources.Postgres).Options);
        var reader = new AccountQueries(cache, origin, NullLogger<AccountQueries>.Instance);
        var cached = await reader.GetByIdAsync(account.Id);
        Assert.NotNull(cached);
        Assert.Equal(account.Id, cached.Id);
        Assert.Equal(account.InternalId, cached.InternalId);
        Assert.Equal(account.Cpf, cached.Cpf);
        Assert.Empty(cache.ChangeTracker.Entries());

        var result = await fixture.ReadySetSqlAsync(
            fixture.AccountCache.Query.Replace("$1", $"'{account.Id:D}'"), "EXPLAIN LAST STATEMENT");
        Assert.Contains($"readyset({fixture.AccountCache.Name})|ok", result);
    }

    [Fact]
    public async Task Replication_ShouldUpdateNameAndHideDeletedAccount()
    {
        await using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var account = PersistenceTests.NewAccount();
        var repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var queries = scope.ServiceProvider.GetRequiredService<IAccountQueries>();
        repository.Add(account);
        await unit.CommitAsync();
        await InfraFixture.EventuallyAsync(async () => await queries.GetByIdAsync(account.Id) is not null);

        account.Name = "Name updated in PostgreSQL";
        await unit.CommitAsync();
        await InfraFixture.EventuallyAsync(async () => (await queries.GetByIdAsync(account.Id))?.Name == account.Name);

        repository.Remove(account);
        await unit.CommitAsync();
        await InfraFixture.EventuallyAsync(async () => await queries.GetByIdAsync(account.Id) is null);
    }

    [Fact]
    public async Task CacheOutage_ShouldFallBackToPostgres()
    {
        var unavailable = new NpgsqlConnectionStringBuilder(fixture.PostgresConnectionString)
        {
            Host = "127.0.0.1", Port = 1, Timeout = 1
        }.ConnectionString;
        await using var provider = fixture.CreateProvider(unavailable);
        await using var scope = provider.CreateAsyncScope();
        var account = PersistenceTests.NewAccount();
        scope.ServiceProvider.GetRequiredService<IAccountRepository>().Add(account);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();

        var result = await scope.ServiceProvider.GetRequiredService<IAccountQueries>().GetByIdAsync(account.Id);
        Assert.NotNull(result);
        Assert.Equal(account.Name, result.Name);
        Assert.Equal(account.Cpf, result.Cpf);
        Assert.NotSame(account, result);
        Assert.Equal(EntityState.Detached,
            scope.ServiceProvider.GetRequiredService<BankDbContext>().Entry(result).State);
    }

    [Fact]
    public async Task MissingCachedAccount_ShouldNotFallBackToOrigin()
    {
        await fixture.ReadySetSqlAsync("ALTER READYSET STOP REPLICATION");

        try
        {
            await using var provider = fixture.CreateProvider();
            await using var scope = provider.CreateAsyncScope();
            var account = PersistenceTests.NewAccount();
            scope.ServiceProvider.GetRequiredService<IAccountRepository>().Add(account);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IAccountRepository>().GetByIdAsync(account.Id));
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IAccountQueries>().GetByIdAsync(account.Id));
        }
        finally
        {
            await fixture.ReadySetSqlAsync("ALTER READYSET START REPLICATION");
        }
    }
}

using Domain.Interfaces;
using Domain.ValueObjects;
using Infra.Persistence;
using Infra.Repositories;
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
        var queries = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        await InfraFixture.EventuallyAsync(async () => (await queries.GetByIdAsync(account.Id))?.Name == account.Name);

        // Disable the application fallback connection so this read must go through ReadySet.
        var unavailableOrigin = new NpgsqlConnectionStringBuilder(fixture.PostgresConnectionString)
        {
            Host = "127.0.0.1", Port = 1, Timeout = 1
        }.ConnectionString;
        await using var cache = new ReadySetDbContext(new DbContextOptionsBuilder<ReadySetDbContext>()
            .UseNpgsql(fixture.ReadySetConnectionString).Options);
        await using var origin = new BankDbContext(new DbContextOptionsBuilder<BankDbContext>()
            .UseNpgsql(unavailableOrigin).Options);
        var reader = new AccountRepository(origin, cache, NullLogger<AccountRepository>.Instance);
        var cached = await reader.GetByIdAsync(account.Id);
        Assert.NotNull(cached);
        Assert.Equal(account.Id, cached.Id);
        Assert.Equal(account.InternalId, cached.InternalId);
        Assert.Equal(account.Cpf, cached.Cpf);
        Assert.Empty(cache.ChangeTracker.Entries());

        var caches = await fixture.ReadySetSqlAsync("SHOW CACHES;");
        Assert.Contains("|deep, autoparam on|", caches);
    }

    [Fact]
    public async Task Replication_ShouldUpdateNameAndHideDeletedAccount()
    {
        await using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var account = PersistenceTests.NewAccount();
        var repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var queries = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        repository.Add(account);
        await unit.CommitAsync();
        await InfraFixture.EventuallyAsync(async () => await queries.GetByIdAsync(account.Id) is not null);

        account.Name = AccountName.Create("Name updated in PostgreSQL").Value;
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

        var result = await scope.ServiceProvider.GetRequiredService<IAccountRepository>().GetByIdAsync(account.Id);
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
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IAccountRepository>().GetByIdAsync(account.Id, cacheable: false));
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IAccountRepository>().GetByIdAsync(account.Id));

            var updated = await scope.ServiceProvider.GetRequiredService<IAccountService>()
                .UpdateAsync(account.Id, "Updated while replication is stopped", account.Cpf.Value);
            Assert.True(updated.IsSuccess);
            await using var verification = fixture.CreateContext();
            Assert.Equal("Updated while replication is stopped",
                (await verification.Accounts.SingleAsync()).Name.Value);
        }
        finally
        {
            await fixture.ReadySetSqlAsync("ALTER READYSET START REPLICATION");
        }
    }
}

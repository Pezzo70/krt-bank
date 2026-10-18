using Domain.Entities;
using Domain.Interfaces;
using Infra.Persistence;
using Infra.Queries;
using Infra.ReadySet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Infra.Tests;

[Collection("Infra")]
public sealed class ReadySetInitializationTests(InfraFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Startup_ShouldReuseExistingCacheAndVersionChangedQueriesWithoutDroppingCaches()
    {
        var configuration = fixture.CreateConfiguration();
        var client = new RecordingAdminClient(new PsqlReadySetAdminClient(configuration,
            new ReadySetCacheOptions { PsqlPath = configuration["ReadySet:PsqlPath"]! }));
        using (var unchanged = CreateHost(configuration, client))
        {
            await unchanged.StartAsync();
            await unchanged.StopAsync();
        }
        Assert.DoesNotContain(client.Commands, sql => sql.StartsWith("CREATE") || sql.StartsWith("DROP"));

        string changedName;
        using (var changed = CreateHost(configuration, client, changedQuery: true))
        {
            await changed.StartAsync();
            await using var scope = changed.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<BankDbContext>();
            changedName = changed.Services.GetServices<ReadySetCacheDefinition>().Last().CreateCommand(context).Name;
            await changed.StopAsync();
        }
        Assert.NotEqual(fixture.AccountCache.Name, changedName);
        Assert.Single(client.Commands, sql => sql.StartsWith("CREATE"));
        var caches = await fixture.ReadySetSqlAsync("SHOW CACHES;");
        Assert.Contains($"|{fixture.AccountCache.Name}|", caches);
        Assert.Contains($"|{changedName}|", caches);

        client.Commands.Clear();
        using var restarted = CreateHost(configuration, client, changedQuery: true);
        await restarted.StartAsync();
        await restarted.StopAsync();
        Assert.DoesNotContain(client.Commands, sql => sql.StartsWith("CREATE") || sql.StartsWith("DROP"));
    }

    [Fact]
    public async Task ConcurrentStartup_ShouldRegisterTheSameCacheVersionOnce()
    {
        var configuration = fixture.CreateConfiguration();
        void Register(IServiceCollection services) => services.AddReadySetCache("parallel_accounts",
            (BankDbContext context, Guid id) => AccountReadQuery.ById(context.Accounts, id)
                .Select(account => new { account.Id, account.CreatedAt }));

        using var first = CreateHost(configuration, configure: Register);
        using var second = CreateHost(configuration, configure: Register);
        await Task.WhenAll(first.StartAsync(), second.StartAsync());
        await using var context = fixture.CreateContext();
        var name = first.Services.GetServices<ReadySetCacheDefinition>().Last().CreateCommand(context).Name;
        var caches = await fixture.ReadySetSqlAsync("SHOW CACHES;");
        Assert.Single(caches.Split('\n'), line => line.Contains($"|{name}|"));
        await Task.WhenAll(first.StopAsync(), second.StopAsync());
    }

    [Fact]
    public async Task Startup_ShouldAllowPostgresReadsWhenReadySetRemainsUnavailable()
    {
        var unavailable = new NpgsqlConnectionStringBuilder(fixture.ReadySetConnectionString)
        {
            Host = "127.0.0.1", Port = 1, Timeout = 1
        }.ConnectionString;
        var configuration = fixture.CreateConfiguration(unavailable);
        configuration["ReadySet:StartupTimeoutSeconds"] = "1";
        using var host = CreateHost(configuration);
        await host.StartAsync();

        await using var scope = host.Services.CreateAsyncScope();
        var account = PersistenceTests.NewAccount();
        scope.ServiceProvider.GetRequiredService<IAccountRepository>().Add(account);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        var result = await scope.ServiceProvider.GetRequiredService<IAccountQueries>().GetByIdAsync(account.Id);
        Assert.NotNull(result);
        Assert.Equal(account.Id, result.Id);
        await host.StopAsync();
    }

    [Fact]
    public async Task Startup_ShouldHonorCancellationWhileWaitingForReadySet()
    {
        var unavailable = new NpgsqlConnectionStringBuilder(fixture.ReadySetConnectionString)
        {
            Host = "127.0.0.1", Port = 1, Timeout = 1
        }.ConnectionString;
        using var host = CreateHost(fixture.CreateConfiguration(unavailable));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.StartAsync(cancellation.Token));
    }

    private IHost CreateHost(IConfiguration configuration,
        IReadySetAdminClient? client = null, bool changedQuery = false, Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddInfra(builder.Configuration);
        builder.Services.Replace(ServiceDescriptor.Singleton(
            fixture.GetConnections(configuration.GetConnectionString("ReadySet"))));
        if (client is not null)
            builder.Services.Replace(ServiceDescriptor.Singleton(client));
        if (changedQuery)
            builder.Services.AddReadySetCache("accounts_by_id", (BankDbContext context, Guid id) =>
                AccountReadQuery.ById(context.Accounts, id).Select(account => new { account.Id, account.Name }));
        configure?.Invoke(builder.Services);
        return builder.Build();
    }

    private sealed class RecordingAdminClient(IReadySetAdminClient inner) : IReadySetAdminClient
    {
        public List<string> Commands { get; } = [];

        public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return inner.ExecuteAsync(command, cancellationToken);
        }
    }
}

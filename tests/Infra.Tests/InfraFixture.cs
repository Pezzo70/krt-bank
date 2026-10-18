using Application.Configuration;
using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Infra.Persistence;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Infra.Tests;

[CollectionDefinition("Infra")]
public sealed class InfraCollection : ICollectionFixture<InfraFixture> { }

public sealed class InfraFixture : IAsyncLifetime
{
    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly PostgreSqlContainer _postgres;
    private readonly IContainer _readySet;
    private readonly Dictionary<string, DatabaseConnections> _connections = new();

    public InfraFixture()
    {
        _postgres = new PostgreSqlBuilder("postgres:16")
            .WithDatabase("krt_bank_tests")
            .WithUsername("postgres")
            .WithPassword("krt_bank_tests")
            .WithEnvironment("PGPASSWORD", "krt_bank_tests")
            .WithNetwork(_network)
            .WithNetworkAliases("postgres")
            .WithCommand("-c", "wal_level=logical", "-c", "timezone=UTC")
            .Build();

        _readySet = new ContainerBuilder("readysettech/readyset:stable-260924")
            .WithNetwork(_network)
            .WithNetworkAliases("readyset")
            .WithEnvironment("UPSTREAM_DB_URL", "postgres://postgres:krt_bank_tests@postgres:5432/krt_bank_tests")
            .WithEnvironment("REPLICATION_TABLES", "public.accounts")
            .WithEnvironment("CACHE_MODE", "deep")
            .WithEnvironment("QUERY_CACHING", "in-request-path")
            .WithEnvironment("LISTEN_ADDRESS", "0.0.0.0:5433")
            .WithEnvironment("DEPLOYMENT", "krt-bank-tests")
            .WithEnvironment("STORAGE_DIR", "/data/cache")
            .WithEnvironment("TZ", "UTC")
            .WithPortBinding(5433, true)
            .Build();
    }

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public string ReadySetConnectionString => new NpgsqlConnectionStringBuilder(PostgresConnectionString)
    {
        Host = _readySet.Hostname,
        Port = _readySet.GetMappedPublicPort(5433),
        Timezone = "UTC",
        Timeout = 2,
        CommandTimeout = 10
    }.ConnectionString;

    public BankDbContext CreateContext() => new(new DbContextOptionsBuilder<BankDbContext>()
        .UseNpgsql(PostgresConnectionString).Options);

    public IConfiguration CreateConfiguration(string? readySetConnectionString = null)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = PostgresConnectionString,
            ["ConnectionStrings:ReadySet"] = readySetConnectionString ?? ReadySetConnectionString
        }).Build();
    }

    public ServiceProvider CreateProvider(string? readySetConnectionString = null)
    {
        var services = new ServiceCollection().AddApplication(CreateConfiguration(readySetConnectionString));
        services.Replace(ServiceDescriptor.Singleton(GetConnections(readySetConnectionString)));
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public DatabaseConnections GetConnections(string? readySetConnectionString = null)
    {
        var readySet = readySetConnectionString ?? ReadySetConnectionString;
        if (!_connections.TryGetValue(readySet, out var connections))
        {
            connections = new DatabaseConnections(PostgresConnectionString, readySet);
            _connections.Add(readySet, connections);
        }
        return connections;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _network.CreateAsync();
            await _postgres.StartAsync();
            await using (var context = CreateContext())
            {
                await context.Database.MigrateAsync();
            }

            await _readySet.StartAsync();
            await EventuallyAsync(async () =>
            {
                try
                {
                    return (await ReadySetSqlAsync("SHOW READYSET STATUS;"))
                        .Contains("Replication Status|Running");
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }, TimeSpan.FromSeconds(120));

            if (!string.IsNullOrWhiteSpace(await ReadySetSqlAsync("SHOW CACHES;")))
                throw new InvalidOperationException("The fresh ReadySet container already has caches.");

            await using var provider = CreateProvider();
            await using var scope = provider.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            await repository.GetByIdAsync(Guid.Empty);
            if (!(await ReadySetSqlAsync("SHOW CACHES;")).Contains("|deep, autoparam on|"))
                throw new InvalidOperationException("ReadySet did not automatically create the deep cache.");
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.OutboxMessages.IgnoreQueryFilters().ExecuteDeleteAsync();
        await context.Accounts.IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    // ReadySet administrative commands use the psql simple protocol.
    // Application queries use Entity Framework Core and parameters.
    private Task<ExecResult> ReadySetCommandAsync(params string[] statements)
    {
        var arguments = new List<string>
        {
            "psql", "-X", "-A", "-t", "-F", "|", "-v", "ON_ERROR_STOP=1",
            "-h", "readyset", "-p", "5433", "-U", "postgres", "-d", "krt_bank_tests"
        };
        foreach (var statement in statements)
        {
            arguments.Add("-c");
            arguments.Add(statement);
        }
        return _postgres.ExecAsync(arguments);
    }

    public async Task<string> ReadySetSqlAsync(params string[] statements)
    {
        var result = await ReadySetCommandAsync(statements);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"ReadySet administrative command failed: {result.Stderr}");
        return result.Stdout;
    }

    public async Task DisposeAsync()
    {
        foreach (var connections in _connections.Values) await connections.DisposeAsync();
        await _readySet.DisposeAsync();
        await _postgres.DisposeAsync();
        await _network.DisposeAsync();
    }

    public static async Task EventuallyAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < (timeout ?? TimeSpan.FromSeconds(30)))
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("The integration condition was not met within the timeout.");
    }
}

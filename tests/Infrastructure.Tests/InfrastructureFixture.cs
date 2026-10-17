using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Infrastructure.Tests;

[CollectionDefinition("Infrastructure")]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture> { }

public sealed class InfrastructureFixture : IAsyncLifetime
{
    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly PostgreSqlContainer _postgres;
    private readonly IContainer _readySet;

    public InfrastructureFixture()
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
            .WithEnvironment("REPLICATION_TABLES", "public.contas")
            .WithEnvironment("CACHE_MODE", "deep")
            .WithEnvironment("QUERY_CACHING", "explicit")
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

    public ServiceProvider CreateProvider(string? readySetConnectionString = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = PostgresConnectionString,
            ["ConnectionStrings:ReadySet"] = readySetConnectionString ?? ReadySetConnectionString
        }).Build();

        return new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
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
                var result = await ReadySetCommandAsync("SHOW READYSET STATUS");
                return result.ExitCode == 0 && result.Stdout.Contains("Replication Status|Running");
            }, TimeSpan.FromMinutes(2));

            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "readyset-cache.sql"));
            await ReadySetSqlAsync(sql);
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
        await context.Database.ExecuteSqlRawAsync("DELETE FROM outbox_messages; DELETE FROM contas;");
    }

    // Os comandos administrativos do ReadySet usam o protocolo simples do psql.
    // As consultas da aplicação continuam usando Npgsql e parâmetros.
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
            throw new InvalidOperationException($"Comando administrativo ReadySet falhou: {result.Stderr}");
        return result.Stdout;
    }

    public async Task DisposeAsync()
    {
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
        throw new TimeoutException("A condição de integração não foi atendida dentro do prazo.");
    }
}

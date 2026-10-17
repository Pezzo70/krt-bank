using Domain.Interfaces;
using Infrastructure.Persistence;
using Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Infrastructure.Tests;

[Collection("Infrastructure")]
public sealed class ReadySetTests(InfrastructureFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Consulta_DeveSerAtendidaPeloDeepCache()
    {
        await using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var conta = PersistenceTests.NewConta();
        scope.ServiceProvider.GetRequiredService<IContaRepository>().Add(conta);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        var queries = scope.ServiceProvider.GetRequiredService<IContaQueries>();
        await InfrastructureFixture.EventuallyAsync(async () => (await queries.GetByIdAsync(conta.Id))?.Nome == conta.Nome);

        // O fallback fica indisponível para provar que o leitor .NET recebe o resultado do ReadySet.
        var unavailableOrigin = new NpgsqlConnectionStringBuilder(fixture.PostgresConnectionString)
        {
            Host = "127.0.0.1", Port = 1, Timeout = 1
        }.ConnectionString;
        await using var sources = new DatabaseConnections(unavailableOrigin, fixture.ReadySetConnectionString);
        var reader = new ContaQueries(sources, NullLogger<ContaQueries>.Instance);
        Assert.Equal(conta.Id, (await reader.GetByIdAsync(conta.Id))!.Id);

        var result = await fixture.ReadySetSqlAsync(
            ContaQueries.GetByIdSql.Replace("$1", $"'{conta.Id:D}'"), "EXPLAIN LAST STATEMENT");
        Assert.Contains("readyset(contas_por_id)|ok", result);
    }

    [Fact]
    public async Task Replicacao_DeveAtualizarNomeEOcultarContaExcluida()
    {
        await using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var conta = PersistenceTests.NewConta();
        var repository = scope.ServiceProvider.GetRequiredService<IContaRepository>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var queries = scope.ServiceProvider.GetRequiredService<IContaQueries>();
        repository.Add(conta);
        await unit.CommitAsync();
        await InfrastructureFixture.EventuallyAsync(async () => await queries.GetByIdAsync(conta.Id) is not null);

        conta.Nome = "Nome atualizado no PostgreSQL";
        await unit.CommitAsync();
        await InfrastructureFixture.EventuallyAsync(async () => (await queries.GetByIdAsync(conta.Id))?.Nome == conta.Nome);

        repository.Remove(conta);
        await unit.CommitAsync();
        await InfrastructureFixture.EventuallyAsync(async () => await queries.GetByIdAsync(conta.Id) is null);
    }

    [Fact]
    public async Task Indisponibilidade_DoCacheDeveConsultarPostgres()
    {
        var unavailable = new NpgsqlConnectionStringBuilder(fixture.PostgresConnectionString)
        {
            Host = "127.0.0.1", Port = 1, Timeout = 1
        }.ConnectionString;
        await using var provider = fixture.CreateProvider(unavailable);
        await using var scope = provider.CreateAsyncScope();
        var conta = PersistenceTests.NewConta();
        scope.ServiceProvider.GetRequiredService<IContaRepository>().Add(conta);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();

        var result = await scope.ServiceProvider.GetRequiredService<IContaQueries>().GetByIdAsync(conta.Id);
        Assert.NotNull(result);
        Assert.Equal(conta.Nome, result.Nome);
        Assert.Equal(conta.Cpf.Value, result.Cpf);
    }

    [Fact]
    public async Task ResultadoNulo_NaoDeveDispararFallbackParaOrigem()
    {
        await fixture.ReadySetSqlAsync("ALTER READYSET STOP REPLICATION");

        try
        {
            await using var provider = fixture.CreateProvider();
            await using var scope = provider.CreateAsyncScope();
            var conta = PersistenceTests.NewConta();
            scope.ServiceProvider.GetRequiredService<IContaRepository>().Add(conta);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IContaRepository>().GetByIdAsync(conta.Id));
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IContaQueries>().GetByIdAsync(conta.Id));
        }
        finally
        {
            await fixture.ReadySetSqlAsync("ALTER READYSET START REPLICATION");
        }
    }
}

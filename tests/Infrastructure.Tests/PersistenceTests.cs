using System.Text.Json;
using Domain.Entities;
using Domain.Interfaces;
using Domain.ValueObjects;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Infrastructure.Tests;

[Collection("Infrastructure")]
public sealed class PersistenceTests(InfrastructureFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Inclusao_DevePersistirIdentidadesCpfDatasEOutbox()
    {
        await using var provider = fixture.CreateProvider();
        var conta = NewConta();
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IContaRepository>();
            repository.Add(conta);
            Assert.Equal(0, conta.InternalId);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        await using var context = fixture.CreateContext();
        var salva = await context.Contas.SingleAsync();
        Assert.True(salva.InternalId > 0);
        Assert.Equal(conta.Id, salva.Id);
        Assert.Equal(conta.InternalId, salva.InternalId);
        Assert.Equal(conta.Cpf, salva.Cpf);
        Assert.Equal(conta.CreatedAt.ToUnixTimeMilliseconds(), salva.CreatedAt.ToUnixTimeMilliseconds());
        Assert.Equal(TimeSpan.Zero, salva.CreatedAt.Offset);
        Assert.Null(salva.UpdatedAt);

        var message = await context.OutboxMessages.SingleAsync();
        Assert.Equal("conta.criada", message.Type);
        Assert.Equal(conta.Id, message.EntityId);
        Assert.Null(message.PublishedAt);
        using var json = JsonDocument.Parse(message.Payload);
        Assert.Equal(conta.Id, json.RootElement.GetProperty("contaId").GetGuid());
        Assert.Equal(conta.Cpf.Value, json.RootElement.GetProperty("cpf").GetString());
        Assert.False(json.RootElement.TryGetProperty("internalId", out _));
    }

    [Fact]
    public async Task Atualizacao_DeveRegistrarMudancaSemDuplicarEventosEmCommitSemAlteracao()
    {
        await using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IContaRepository>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var conta = NewConta();
        repository.Add(conta);
        await unit.CommitAsync();

        conta.Nome = "Conta atualizada";
        await unit.CommitAsync();
        var updatedAt = conta.UpdatedAt;
        Assert.NotNull(updatedAt);
        await unit.CommitAsync();

        await using var context = fixture.CreateContext();
        Assert.Equal(2, await context.OutboxMessages.CountAsync());
        var salva = await context.Contas.SingleAsync();
        Assert.Equal("Conta atualizada", salva.Nome);
        Assert.Equal(updatedAt!.Value.ToUnixTimeMilliseconds(), salva.UpdatedAt!.Value.ToUnixTimeMilliseconds());
        Assert.Equal(conta.CreatedAt.ToUnixTimeMilliseconds(), salva.CreatedAt.ToUnixTimeMilliseconds());
        Assert.Equal(1, await context.OutboxMessages.CountAsync(x => x.Type == "conta.atualizada"));
    }

    [Fact]
    public async Task ExclusaoLogica_DeveOcultarContaManterCpfReservadoEEmitirUmaNotificacao()
    {
        await using var provider = fixture.CreateProvider();
        var conta = NewConta();
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IContaRepository>();
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            repository.Add(conta);
            await unit.CommitAsync();
            repository.Remove(conta);
            await unit.CommitAsync();
            repository.Remove(conta);
            await unit.CommitAsync();
            Assert.Null(await repository.GetByIdAsync(conta.Id));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IContaRepository>().Add(NewConta());
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        }

        await using var context = fixture.CreateContext();
        Assert.Equal(0, await context.Contas.CountAsync());
        Assert.False((await context.Contas.IgnoreQueryFilters().SingleAsync()).IsActive);
        Assert.Equal(2, await context.OutboxMessages.CountAsync());
        Assert.Equal(1, await context.OutboxMessages.CountAsync(x => x.Type == "conta.excluida"));
    }

    [Fact]
    public async Task Falha_DeveReverterContaEOutboxEExigirNovoContexto()
    {
        await using var provider = fixture.CreateProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IContaRepository>();
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            repository.Add(NewConta());
            await unit.CommitAsync();
            repository.Add(NewConta("08782758000"));
            repository.Add(NewConta());
            await Assert.ThrowsAsync<DbUpdateException>(() => unit.CommitAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => unit.CommitAsync());
        }

        await using var context = fixture.CreateContext();
        Assert.Equal(1, await context.Contas.CountAsync());
        Assert.Equal(1, await context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task RemocaoDiretaDoContexto_DeveRespeitarExclusaoLogica()
    {
        await using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankDbContext>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var conta = NewConta();
        context.Contas.Add(conta);
        await unit.CommitAsync();
        conta.Nome = "Nome no encerramento";
        context.Contas.Remove(conta);
        await unit.CommitAsync();

        await using var verification = fixture.CreateContext();
        var salva = await verification.Contas.IgnoreQueryFilters().SingleAsync();
        Assert.False(salva.IsActive);
        Assert.Equal("Nome no encerramento", salva.Nome);
        Assert.Equal(1, await verification.OutboxMessages.CountAsync(x => x.Type == "conta.excluida"));
    }

    [Fact]
    public async Task Commit_DevePersistirVariasContasComUmaMensagemPorConta()
    {
        await using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IContaRepository>();
        var contas = new[] { NewConta(), NewConta("08782758000"), NewConta("62564939074") };
        foreach (var conta in contas) repository.Add(conta);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();

        await using var context = fixture.CreateContext();
        Assert.Equal(3, await context.Contas.CountAsync());
        Assert.Equal(3, await context.OutboxMessages.CountAsync());
        Assert.Equal(3, (await context.Contas.Select(x => x.InternalId).ToListAsync()).Distinct().Count());
    }

    internal static Conta NewConta(string cpf = "28784142090") => new()
    {
        Nome = "Conta de integração",
        Cpf = Cpf.Create(cpf).Value
    };
}

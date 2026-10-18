using System.Text.Json;
using Domain.Entities;
using Domain.Errors;
using Domain.Interfaces;
using Domain.ValueObjects;
using Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.Tests;

[Collection("Infra")]
public sealed class PersistenceTests(InfraFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Insert_ShouldPersistIdentifiersCpfTimestampsAndOutbox()
    {
        await using var provider = fixture.CreateProvider();
        var account = NewAccount();
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            repository.Add(account);
            Assert.Equal(0, account.InternalId);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
        }

        await using var context = fixture.CreateContext();
        var persisted = await context.Accounts.SingleAsync();
        Assert.True(persisted.InternalId > 0);
        Assert.Equal(account.Id, persisted.Id);
        Assert.Equal(account.InternalId, persisted.InternalId);
        Assert.Equal(account.Cpf, persisted.Cpf);
        Assert.Equal(account.CreatedAt.ToUnixTimeMilliseconds(), persisted.CreatedAt.ToUnixTimeMilliseconds());
        Assert.Equal(TimeSpan.Zero, persisted.CreatedAt.Offset);
        Assert.Null(persisted.UpdatedAt);

        var message = await context.OutboxMessages.SingleAsync();
        Assert.Equal("account.created", message.Type);
        Assert.Equal(account.Id, message.EntityId);
        Assert.Null(message.PublishedAt);
        using var json = JsonDocument.Parse(message.Payload);
        Assert.Equal(account.Id, json.RootElement.GetProperty("accountId").GetGuid());
        Assert.Equal(account.Name.Value, json.RootElement.GetProperty("name").GetString());
        Assert.Equal(account.Cpf.Value, json.RootElement.GetProperty("cpf").GetString());
        Assert.True(json.RootElement.GetProperty("isActive").GetBoolean());
        Assert.Equal(account.CreatedAt, json.RootElement.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("updatedAt").ValueKind);
        Assert.False(json.RootElement.TryGetProperty("internalId", out _));
    }

    [Fact]
    public async Task Update_ShouldRecordChangesWithoutDuplicatingUnchangedCommits()
    {
        await using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var account = NewAccount();
        repository.Add(account);
        await unit.CommitAsync();

        account.Name = AccountName.Create("Account updated").Value;
        await unit.CommitAsync();
        var updatedAt = account.UpdatedAt;
        Assert.NotNull(updatedAt);
        await unit.CommitAsync();

        await using var context = fixture.CreateContext();
        Assert.Equal(2, await context.OutboxMessages.CountAsync());
        var persisted = await context.Accounts.SingleAsync();
        Assert.Equal("Account updated", persisted.Name.Value);
        Assert.Equal(updatedAt!.Value.ToUnixTimeMilliseconds(), persisted.UpdatedAt!.Value.ToUnixTimeMilliseconds());
        Assert.Equal(account.CreatedAt.ToUnixTimeMilliseconds(), persisted.CreatedAt.ToUnixTimeMilliseconds());
        Assert.Equal(1, await context.OutboxMessages.CountAsync(x => x.Type == "account.updated"));
    }

    [Fact]
    public async Task SoftDelete_ShouldHideAccountReserveCpfAndNotifyOnce()
    {
        await using var provider = fixture.CreateProvider();
        var account = NewAccount();
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            repository.Add(account);
            await unit.CommitAsync();
            repository.Remove(account);
            Assert.Null(account.UpdatedAt);
            await unit.CommitAsync();
            var deletedAt = account.UpdatedAt;
            Assert.NotNull(deletedAt);
            repository.Remove(account);
            await unit.CommitAsync();
            Assert.Equal(deletedAt, account.UpdatedAt);
            Assert.Null(await repository.GetByIdAsync(account.Id, cacheable: false));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IAccountRepository>().Add(NewAccount());
            var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();
            Assert.True(result.IsFailed);
            Assert.IsType<ConflictError>(Assert.Single(result.Errors));
        }

        await using var context = fixture.CreateContext();
        Assert.Equal(0, await context.Accounts.CountAsync());
        Assert.False((await context.Accounts.IgnoreQueryFilters().SingleAsync()).IsActive);
        Assert.Equal(2, await context.OutboxMessages.CountAsync());
        Assert.Equal(1, await context.OutboxMessages.CountAsync(x => x.Type == "account.deleted"));
    }

    [Fact]
    public async Task Failure_ShouldRollBackAccountAndOutboxAndRejectContextReuse()
    {
        await using var provider = fixture.CreateProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            repository.Add(NewAccount());
            await unit.CommitAsync();
            repository.Add(NewAccount("08782758000"));
            repository.Add(NewAccount());
            var failed = await unit.CommitAsync();
            Assert.True(failed.IsFailed);
            Assert.IsType<ConflictError>(Assert.Single(failed.Errors));
            await Assert.ThrowsAsync<InvalidOperationException>(() => unit.CommitAsync());
        }

        await using var context = fixture.CreateContext();
        Assert.Equal(1, await context.Accounts.CountAsync());
        Assert.Equal(1, await context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task ContextRemoval_ShouldUseSoftDelete()
    {
        await using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankDbContext>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var account = NewAccount();
        context.Accounts.Add(account);
        await unit.CommitAsync();
        account.Name = AccountName.Create("Name at closure").Value;
        context.Accounts.Remove(account);
        await unit.CommitAsync();

        await using var verification = fixture.CreateContext();
        var persisted = await verification.Accounts.IgnoreQueryFilters().SingleAsync();
        Assert.False(persisted.IsActive);
        Assert.Equal("Name at closure", persisted.Name.Value);
        Assert.Equal(1, await verification.OutboxMessages.CountAsync(x => x.Type == "account.deleted"));
    }

    [Fact]
    public async Task OutboxChanges_ShouldPersistEntityStateWithoutProducingMoreMessages()
    {
        await using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankDbContext>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var account = NewAccount();
        scope.ServiceProvider.GetRequiredService<IAccountRepository>().Add(account);
        await unit.CommitAsync();

        var message = await context.OutboxMessages.SingleAsync();
        Assert.True(message.InternalId > 0);
        Assert.NotEqual(Guid.Empty, message.Id);
        message.PublishedAt = DateTimeOffset.UtcNow;
        await unit.CommitAsync();

        await using (var verification = fixture.CreateContext())
        {
            var persisted = await verification.OutboxMessages.SingleAsync();
            Assert.Equal(message.Id, persisted.Id);
            Assert.NotNull(persisted.PublishedAt);
            Assert.NotNull(persisted.UpdatedAt);
        }

        context.OutboxMessages.Remove(message);
        await unit.CommitAsync();
        await using var deletedVerification = fixture.CreateContext();
        Assert.Empty(await deletedVerification.OutboxMessages.ToListAsync());
        Assert.False((await deletedVerification.OutboxMessages.IgnoreQueryFilters().SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Commit_ShouldPersistMultipleAccountsWithOneMessageEach()
    {
        await using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var accounts = new[] { NewAccount(), NewAccount("08782758000"), NewAccount("62564939074") };
        foreach (var account in accounts) repository.Add(account);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync();

        await using var context = fixture.CreateContext();
        Assert.Equal(3, await context.Accounts.CountAsync());
        Assert.Equal(3, await context.OutboxMessages.CountAsync());
        Assert.Equal(3, (await context.Accounts.Select(x => x.InternalId).ToListAsync()).Distinct().Count());
    }

    internal static Account NewAccount(string cpf = "28784142090") => new()
    {
        Name = AccountName.Create("Integration account").Value,
        Cpf = Cpf.Create(cpf).Value
    };
}

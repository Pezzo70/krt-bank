using Domain.Entities;
using Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace Infra.Tests;

public sealed class MigrationTests
{
    [Fact]
    public async Task Rename_ShouldPreserveExistingAccountsOutboxAndIdentitySequence()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<BankDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using var context = new BankDbContext(options);
        var migrator = context.GetService<IMigrator>();
        const string initialMigration = "20261017224819_InitialPersistence";
        await migrator.MigrateAsync(initialMigration);

        var account = PersistenceTests.NewAccount();
        var message = new OutboxMessage
        {
            EntityId = account.Id,
            Type = "conta.criada",
            Payload = "{\"nome\":\"Historical payload\"}"
        };
        var published = new OutboxMessage
        {
            EntityId = account.Id,
            Type = "conta.atualizada",
            Payload = "{}",
            PublishedAt = DateTimeOffset.UtcNow
        };
        await using (var legacy = new LegacyBankDbContext(new DbContextOptionsBuilder<LegacyBankDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options))
        {
            legacy.Add(account);
            legacy.Add(message);
            legacy.Add(published);
            await legacy.SaveChangesAsync();
        }

        await context.Database.MigrateAsync();
        var persisted = await context.Accounts.SingleAsync();
        Assert.Equal(account.Id, persisted.Id);
        Assert.Equal(account.InternalId, persisted.InternalId);
        Assert.Equal(account.Name, persisted.Name);
        Assert.Equal(account.Cpf, persisted.Cpf);
        Assert.Equal(account.CreatedAt.ToUnixTimeMilliseconds(), persisted.CreatedAt.ToUnixTimeMilliseconds());
        var historical = await context.OutboxMessages.SingleAsync(x => x.Id == message.Id);
        Assert.Equal(message.Id, historical.Id);
        Assert.Equal(message.CreatedAt.ToUnixTimeMilliseconds(), historical.CreatedAt.ToUnixTimeMilliseconds());
        Assert.True(historical.InternalId > 0);
        Assert.True(historical.IsActive);
        Assert.Null(historical.PublishedAt);
        Assert.NotNull((await context.OutboxMessages.SingleAsync(x => x.Id == published.Id)).PublishedAt);
        Assert.Equal(2, await context.OutboxMessages.Select(x => x.InternalId).Distinct().CountAsync());
        Assert.Equal(message.Type, historical.Type);
        using var payload = System.Text.Json.JsonDocument.Parse(historical.Payload);
        Assert.Equal("Historical payload", payload.RootElement.GetProperty("nome").GetString());

        var next = PersistenceTests.NewAccount("08782758000");
        context.Accounts.Add(next);
        await context.SaveChangesAsync();
        Assert.True(next.InternalId > account.InternalId);

        // Rolling back must preserve the same records as well.
        await migrator.MigrateAsync(initialMigration);
        await using var rolledBack = new LegacyBankDbContext(new DbContextOptionsBuilder<LegacyBankDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options);
        Assert.Equal(2, await rolledBack.Set<Account>().CountAsync());
        Assert.Equal(account.Name, (await rolledBack.Set<Account>().SingleAsync(x => x.Id == account.Id)).Name);
        Assert.Equal(2, await rolledBack.Set<OutboxMessage>().CountAsync());
        Assert.Equal(message.Id, (await rolledBack.Set<OutboxMessage>().SingleAsync(x => x.Id == message.Id)).Id);
    }

    // Map the historical schema only to seed and verify an actual database upgrade.
    private sealed class LegacyBankDbContext(DbContextOptions<LegacyBankDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            BankDbContext.ConfigureModel(modelBuilder);
            modelBuilder.Entity<Account>().ToTable("contas");
            modelBuilder.Entity<Account>().Property(x => x.Name).HasColumnName("nome");
            var outbox = modelBuilder.Entity<OutboxMessage>();
            outbox.HasKey(x => x.Id);
            outbox.Ignore(x => x.InternalId);
            outbox.Ignore(x => x.IsActive);
            outbox.Ignore(x => x.UpdatedAt);
            outbox.HasQueryFilter(x => true);
            outbox.Property(x => x.Id).HasColumnName("event_id");
            outbox.Property(x => x.CreatedAt).HasColumnName("occurred_at");
        }
    }
}

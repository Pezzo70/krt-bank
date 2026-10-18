using Application.Configuration;
using Domain.Entities;
using Domain.Interfaces;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Infra.Tests;

[Collection("Infra")]
public sealed class OutboxWorkerTests(InfraFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Worker_ShouldSimulatePendingEventsWithoutReprocessingPublishedOrInactiveMessages()
    {
        var account = PersistenceTests.NewAccount();
        var publishedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var published = new OutboxMessage(account, "created") { PublishedAt = publishedAt };
        var inactive = new OutboxMessage(account, "created");
        inactive.SetActive(false);

        await using (var provider = fixture.CreateProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            repository.Add(account);
            Assert.True((await unit.CommitAsync()).IsSuccess);
            account.Name = AccountName.Create("Updated account").Value;
            Assert.True((await unit.CommitAsync()).IsSuccess);
            repository.Remove(account);
            Assert.True((await unit.CommitAsync()).IsSuccess);
        }

        await using (var context = fixture.CreateContext())
        {
            context.OutboxMessages.AddRange(published, inactive);
            await context.SaveChangesAsync();
        }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddConfiguration(fixture.CreateConfiguration());
        builder.Services.AddApplication(builder.Configuration);
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            await InfraFixture.EventuallyAsync(async () =>
            {
                await using var context = fixture.CreateContext();
                return !await context.OutboxMessages.AnyAsync(message => message.PublishedAt == null);
            });

            await using var verification = fixture.CreateContext();
            var messages = await verification.OutboxMessages.IgnoreQueryFilters().ToListAsync();
            Assert.Equal(5, messages.Count);
            var processed = messages.Where(message => message.Id != published.Id && message.Id != inactive.Id).ToArray();
            Assert.Equal(new[] { "account.created", "account.deleted", "account.updated" },
                processed.Select(message => message.Type).OrderBy(type => type).ToArray());
            Assert.All(processed, message =>
            {
                Assert.NotNull(message.PublishedAt);
                Assert.NotNull(message.UpdatedAt);
            });
            var existing = Assert.Single(messages, message => message.Id == published.Id);
            Assert.Equal(publishedAt.ToUnixTimeMilliseconds(), existing.PublishedAt!.Value.ToUnixTimeMilliseconds());
            Assert.Null(existing.UpdatedAt);
            Assert.Null(Assert.Single(messages, message => message.Id == inactive.Id).PublishedAt);
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await host.StopAsync(timeout.Token);
        }
    }
}

using Domain.Entities;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.Tests;

[Collection("Infra")]
public sealed class OutboxServiceTests(InfraFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ProcessPending_ShouldLimitBatchesAndSkipProcessedMessages()
    {
        var account = PersistenceTests.NewAccount();
        await using (var context = fixture.CreateContext())
        {
            context.OutboxMessages.AddRange(Enumerable.Range(0, 101)
                .Select(_ => new OutboxMessage(account, "created")));
            await context.SaveChangesAsync();
        }

        await using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IOutboxService>();

        var first = await service.ProcessPendingAsync();
        Assert.True(first.IsSuccess);
        Assert.Equal(100, first.Value);
        await using (var context = fixture.CreateContext())
        {
            Assert.Equal(1, await context.OutboxMessages.CountAsync(message => message.PublishedAt == null));
        }

        var second = await service.ProcessPendingAsync();
        Assert.True(second.IsSuccess);
        Assert.Equal(1, second.Value);
        var empty = await service.ProcessPendingAsync();
        Assert.True(empty.IsSuccess);
        Assert.Equal(0, empty.Value);

        await using var verification = fixture.CreateContext();
        var messages = await verification.OutboxMessages.ToListAsync();
        Assert.Equal(101, messages.Count);
        Assert.All(messages, message =>
        {
            Assert.NotNull(message.PublishedAt);
            Assert.NotNull(message.UpdatedAt);
        });
    }
}

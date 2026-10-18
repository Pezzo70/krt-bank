using Infra.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infra.Tests;

[Collection("Infra")]
public sealed class ReadySetAutomaticCachingTests(InfraFixture fixture)
{
    [Fact]
    public async Task NewEfQuery_ShouldCreateDeepCacheAndReuseItForOtherIds()
    {
        var existingCaches = await fixture.ReadySetSqlAsync("SHOW CACHES;");
        var existingIds = CacheIds(existingCaches);

        await using var provider = fixture.CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ReadySetDbContext>();
        var id = Guid.Empty;
        await context.Accounts.AsNoTracking().Where(account => account.Id == id)
            .Select(account => new { account.Id, account.Name }).ToListAsync();

        var caches = await fixture.ReadySetSqlAsync("SHOW CACHES;");
        Assert.Contains("|deep, autoparam on|", caches);
        var cacheId = Assert.Single(CacheIds(caches).Except(existingIds));

        id = Guid.NewGuid();
        await context.Accounts.AsNoTracking().Where(account => account.Id == id)
            .Select(account => new { account.Id, account.Name }).ToListAsync();
        var reused = await fixture.ReadySetSqlAsync("SHOW CACHES;");
        Assert.Equal(cacheId, Assert.Single(CacheIds(reused).Except(existingIds)));
        Assert.Equal(existingIds.Length + 1, CacheIds(reused).Length);
        Assert.Matches($@"{cacheId}\|{cacheId}\|[\s\S]*?\|deep, autoparam on\|2(?:\r?\n|$)", reused);
    }

    private static string[] CacheIds(string output) => output.Split('\n')
        .Where(line => line.StartsWith("q_"))
        .Select(line => line.Split('|')[0]).ToArray();
}

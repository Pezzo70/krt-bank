using Infra.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infra.ReadySet;

public sealed class ReadySetCacheInitializer(
    IServiceScopeFactory scopeFactory,
    IEnumerable<ReadySetCacheDefinition> definitions,
    IReadySetAdminClient client,
    ReadySetCacheOptions options,
    ILogger<ReadySetCacheInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.StartupTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = linked.Token;
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BankDbContext>();
        var commands = definitions.Select(definition => definition.CreateCommand(context)).ToArray();

        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var status = await client.ExecuteAsync("SHOW READYSET STATUS;", token);
                    if (status.Split('\n').Any(line => line.Trim() == "Replication Status|Running"))
                    {
                        foreach (var command in commands)
                        {
                            if (await ExistsAsync(command.Name, token)) continue;
                            try
                            {
                                await client.ExecuteAsync(command.CreateSql, token);
                            }
                            catch (ReadySetAdminException)
                            {
                                // Another instance may have created the same version concurrently.
                                if (!await ExistsAsync(command.Name, token)) throw;
                            }
                            logger.LogInformation("ReadySet cache {CacheName} is registered.", command.Name);
                        }
                        logger.LogInformation("ReadySet cache initialization completed.");
                        return;
                    }
                }
                catch (ReadySetAdminException)
                {
                    logger.LogDebug("ReadySet is not ready for cache registration; retrying.");
                }
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            logger.LogWarning("ReadySet cache registration timed out. Reads may use PostgreSQL until caches are registered on a later startup.");
        }
    }

    private async Task<bool> ExistsAsync(string name, CancellationToken token)
    {
        var caches = await client.ExecuteAsync("SHOW CACHES;", token);
        return caches.Split('\n').Any(line => line.Split('|') is { Length: > 1 } columns && columns[1] == name);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

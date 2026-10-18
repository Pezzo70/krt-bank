using Domain.Interfaces;

namespace Application.Workers;

public sealed class OutboxWorker(IServiceScopeFactory scopeFactory, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IOutboxService>()
                    .ProcessPendingAsync(stoppingToken);
                if (result.IsFailed)
                    logger.LogError("Could not save simulated Outbox publication; pending messages will be retried.");
                else if (result.Value > 0)
                    logger.LogInformation("Simulated publication of {MessageCount} Outbox messages.", result.Value);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox simulation failed; pending messages will be retried.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

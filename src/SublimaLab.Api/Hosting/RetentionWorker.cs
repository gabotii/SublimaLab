using SublimaLab.Application.Uploads;

namespace SublimaLab.Api.Hosting;

/// <summary>Runs <see cref="RetentionService"/> shortly after startup and then once a day.</summary>
internal sealed class RetentionWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Retention:Enabled", true))
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<RetentionService>().RunAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Retention run failed; it will be retried tomorrow");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}

using Microsoft.Extensions.Options;
using Nunam.Core.Jobs;

namespace Nunam.AIWorker.Workers
{
    /// <summary>
    /// Hourly purge of finished jobs for every machine. It runs here because the AI worker is the one
    /// service that is always on.
    /// </summary>
    public class JobCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<AIWorkerOptions> options,
        ILogger<JobCleanupWorker> logger) : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
                    var purged = await queue.PurgeDoneAsync(TimeSpan.FromDays(options.Value.KeepDoneJobsDays), stoppingToken);
                    if (purged > 0) logger.LogInformation("Purged {Count} finished job(s)", purged);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not purge finished jobs; will retry in an hour");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false) is true);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.API.Agents;
using Nunam.Core.Data;
using Nunam.Core.Jobs;

namespace Nunam.API.Services
{
    /// <summary>Hourly clean-up of finished jobs, old indexer logs and spent enrollment codes; the tables only grow otherwise.</summary>
    public class MaintenanceWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<AgentOptions> options,
        ILogger<MaintenanceWorker> logger) : BackgroundService
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
                    var db = scope.ServiceProvider.GetRequiredService<NunamDbContext>();
                    var now = DateTimeOffset.UtcNow;

                    var jobs = await new JobQueue(db).PurgeDoneAsync(TimeSpan.FromDays(options.Value.KeepDoneJobsDays), stoppingToken);
                    var logCutoff = now - TimeSpan.FromDays(options.Value.KeepLogsDays);
                    var logs = await db.IndexerLogs.Where(l => l.At < logCutoff).ExecuteDeleteAsync(stoppingToken);
                    var codeCutoff = now - TimeSpan.FromDays(1);
                    await db.MachineEnrollments.Where(e => e.ExpiresAt < codeCutoff).ExecuteDeleteAsync(stoppingToken);

                    if (jobs > 0 || logs > 0) logger.LogInformation("Purged {Jobs} finished job(s) and {Logs} old log entries", jobs, logs);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Clean-up failed; will retry in an hour");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
    }
}

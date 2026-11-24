using Nunam.Core.Management;
using Nunam.Indexer.Server;

namespace Nunam.Indexer.Management
{
    /// <summary>
    /// Runs the indexing workers (<see cref="IndexerHost"/>) and starts them again when they ask for a
    /// restart, when one of them stops, or when they fail to start (the server is down). With remote
    /// management on, the settings from the UI are read before every start: a scan started on the
    /// local watch paths alone would drop the files of the remote ones from the index.
    /// </summary>
    public sealed class IndexerSupervisor(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        IndexerControl control,
        IndexerServer server,
        ILogger<IndexerSupervisor> logger) : BackgroundService
    {
        private static readonly TimeSpan MinRetry = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan MaxRetry = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var remoteManagement = configuration.GetValue<bool>($"{IndexerOptions.SectionName}:{nameof(IndexerOptions.RemoteManagement)}");
            var retry = MinRetry;

            while (!stoppingToken.IsCancellationRequested)
            {
                IHost host;
                try
                {
                    var (remote, revision) = remoteManagement ? await ReadRemoteSettingsAsync(stoppingToken) : (null, null);
                    control.BeginRun(remote, revision);
                    host = IndexerHost.Build(configuration, loggerFactory, control, server, remote);
                    await StartAsync(host, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "The indexer could not start; trying again in {Seconds} s", retry.TotalSeconds);
                    await DelayAsync(retry, stoppingToken);
                    retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, MaxRetry.Ticks));
                    continue;
                }

                retry = MinRetry;
                string? reason = null;
                using (host)
                {
                    // A worker that throws stops its host; that counts as a request to start again.
                    using var stopped = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping
                        .Register(() => control.RequestRestart("a worker stopped"));
                    try
                    {
                        reason = await control.WaitForRestartAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                    }

                    using var timeout = new CancellationTokenSource(StopTimeout);
                    await host.StopAsync(timeout.Token);
                }

                if (reason is not null)
                {
                    logger.LogInformation("Restarting the indexer: {Reason}", reason);
                    // A worker that dies at once would otherwise restart in a tight loop.
                    await DelayAsync(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
        }

        private static async Task StartAsync(IHost host, CancellationToken cancellationToken)
        {
            try
            {
                await host.StartAsync(cancellationToken);
            }
            catch
            {
                using var timeout = new CancellationTokenSource(StopTimeout);
                await host.StopAsync(timeout.Token);
                host.Dispose();
                throw;
            }
        }

        private async Task<(RemoteIndexerSettings?, int?)> ReadRemoteSettingsAsync(CancellationToken cancellationToken)
        {
            var (settings, revision) = await server.GetSettingsAsync(cancellationToken);
            if (settings is null) return (null, revision);
            if (settings.Validate() is { Count: > 0 } problems)
            {
                logger.LogWarning("Ignoring the settings from the UI (revision {Revision}): {Problems}",
                    revision, string.Join(" ", problems.Values.SelectMany(p => p)));
                return (null, revision);
            }
            return (settings, revision);
        }

        private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}

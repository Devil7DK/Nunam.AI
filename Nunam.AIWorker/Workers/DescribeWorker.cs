using System.ClientModel;
using System.IO;
using Microsoft.Extensions.Options;
using Nunam.Core.AI;
using Nunam.Core.Agents;
using Nunam.Core.Entities;
using Nunam.Core.Files;
using Nunam.Core.Jobs;

namespace Nunam.AIWorker.Workers
{
    /// <summary>
    /// Consumes <see cref="IndexJobKind.Describe"/> jobs with the describe providers set up in the web UI.
    /// Each provider gets as many lanes as its concurrency, and a lane claims one job at a time, only
    /// while no higher-priority provider is available, or, for a provider that takes overflow, while all
    /// of those are busy. A provider that can't be reached or refuses its key backs off and hands its job
    /// back, so another provider can take it; one that answers with nothing passes the job straight on.
    /// The file's Embed job is queued with each result, so the vector includes what the model saw.
    /// </summary>
    public class DescribeWorker(WorkerServer server, IOptions<AIWorkerOptions> options, ILogger<DescribeWorker> logger)
        : BackgroundService
    {
        private readonly AIWorkerOptions _options = options.Value;
        private readonly Lock _gate = new();
        private readonly Dictionary<Guid, Provider> _providers = [];

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("{Worker} started", nameof(DescribeWorker));
            var nextStaleCheck = DateTimeOffset.MinValue;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= nextStaleCheck)
                {
                    nextStaleCheck = DateTimeOffset.UtcNow.AddMinutes(5);
                    await RecoverStaleAsync(stoppingToken);
                }
                await RefreshAsync(stoppingToken);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _options.ProviderRefreshSeconds)), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task RecoverStaleAsync(CancellationToken cancellationToken)
        {
            try
            {
                var (requeued, failed) = await server.RecoverAsync(IndexJobKind.Describe, _options.StaleJobMinutes, _options.MaxAttempts, cancellationToken);
                if (requeued > 0) logger.LogWarning("Requeued {Count} stale Describe job(s)", requeued);
                if (failed > 0) logger.LogError("Gave up on {Count} Describe job(s) that stopped the worker {Max} times", failed, _options.MaxAttempts);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not recover stale Describe jobs; will try again");
            }
        }

        /// <summary>Picks up providers added, changed, disabled or out of budget since the last look.</summary>
        private async Task RefreshAsync(CancellationToken stoppingToken)
        {
            List<WorkerDescribeProvider> settings;
            try
            {
                settings = await server.DescribeProvidersAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not fetch the describe providers; keeping the current ones");
                return;
            }

            lock (_gate)
            {
                var seen = new HashSet<Guid>();
                foreach (var s in settings)
                {
                    seen.Add(s.Id);
                    if (_providers.TryGetValue(s.Id, out var current) && current.Settings with { BudgetRemainingUsd = null } == s with { BudgetRemainingUsd = null })
                    {
                        // Spend counted by the server now includes what this worker recorded.
                        current.Settings = s;
                        current.UnreportedSpend = 0;
                    }
                    else
                    {
                        if (current is not null) current.Retired = true;
                        var provider = new Provider(s, new ImageDescriber(s.Kind, s.BaseUrl, s.ApiKey, s.Model, s.Prompt,
                            s.MaxOutputTokens, s.DisableReasoning, TimeSpan.FromSeconds(_options.DescribeTimeoutSeconds)));
                        _providers[s.Id] = provider;
                        logger.LogInformation("Describing with {Name} ({Model}): priority {Priority}, {Concurrency} at a time{Overflow}",
                            s.Name, s.Model, s.Priority, s.Concurrency, s.TakesOverflow ? ", takes overflow" : "");
                    }
                }
                foreach (var gone in _providers.Keys.Where(id => !seen.Contains(id)).ToList())
                {
                    logger.LogInformation("Stopped describing with {Name}: disabled, removed or out of budget", _providers[gone].Settings.Name);
                    _providers[gone].Retired = true;
                    _providers.Remove(gone);
                }
                foreach (var provider in _providers.Values)
                {
                    while (provider.Lanes < provider.Settings.Concurrency)
                    {
                        var lane = provider.Lanes++;
                        _ = Task.Run(() => RunLaneAsync(provider, lane, stoppingToken), CancellationToken.None);
                    }
                }
            }
        }

        private async Task RunLaneAsync(Provider provider, int lane, CancellationToken stoppingToken)
        {
            var outbox = new ResultOutbox(server, logger);
            try
            {
                while (!stoppingToken.IsCancellationRequested && !provider.Retired && lane < provider.Settings.Concurrency)
                {
                    if (!await outbox.SendAsync(stoppingToken))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(_options.MinBackoffSeconds), stoppingToken);
                        continue;
                    }
                    if (!MayWork(provider))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                        continue;
                    }

                    Interlocked.Increment(ref provider.InFlight);
                    try
                    {
                        List<WorkerJob> jobs;
                        try
                        {
                            jobs = await server.ClaimAsync(IndexJobKind.Describe, 1, 0, stoppingToken);
                        }
                        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                        {
                            logger.LogWarning(ex, "Could not claim Describe jobs; will retry");
                            await Task.Delay(TimeSpan.FromSeconds(_options.MinBackoffSeconds), stoppingToken);
                            continue;
                        }
                        if (jobs.Count == 0)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(_options.IdlePollSeconds), stoppingToken);
                            continue;
                        }

                        foreach (var job in jobs) outbox.Add(await DescribeAsync(provider, job, stoppingToken));
                        // Not cancellable: the work is done and its result must land.
                        await outbox.SendAsync(CancellationToken.None);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref provider.InFlight);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            finally
            {
                await outbox.SendAsync(CancellationToken.None);
                lock (_gate)
                {
                    provider.Lanes--;
                    if (provider.Retired && provider.Lanes == 0) provider.Describer.Dispose();
                }
            }
        }

        /// <summary>Whether this provider's turn has come: no available provider ranks above it, or they're all busy and it takes overflow.</summary>
        private bool MayWork(Provider provider)
        {
            lock (_gate)
            {
                var now = DateTimeOffset.UtcNow;
                if (!provider.IsAvailable(now)) return false;
                var higher = _providers.Values
                    .Where(p => p != provider && p.Settings.Priority < provider.Settings.Priority && p.IsAvailable(now))
                    .ToList();
                return higher.Count == 0 || (provider.Settings.TakesOverflow && higher.All(p => p.IsBusy));
            }
        }

        private async Task<WorkerJobResult> DescribeAsync(Provider provider, WorkerJob job, CancellationToken stoppingToken)
        {
            if (job.PreviewImage is not { Length: > 0 } preview)
            {
                return Result(job, JobOutcome.Failed, "File has no preview image to describe.", queueEmbed: true);
            }

            var mediaType = MediaTypes.Detect(preview, job.Extension ?? Path.GetExtension(job.FileName));
            var empty = new List<DescribeUsageReport>();
            var tried = new HashSet<Provider>();
            var current = provider;
            while (true)
            {
                var settings = current.Settings;
                var handedOver = current != provider;
                var attempt = current;
                tried.Add(current);
                // A lane's own provider is already counted; one it hands over to is not.
                if (handedOver) Interlocked.Increment(ref attempt.InFlight);
                try
                {
                    var outcome = await current.Describer.DescribeAsync(preview, mediaType, stoppingToken);
                    lock (_gate)
                    {
                        current.UnreportedSpend += Cost(settings, outcome.InputTokens, outcome.OutputTokens);
                        current.Backoff = TimeSpan.Zero;
                    }
                    await ReportAsync(current, null);
                    return new WorkerJobResult(job.Id, JobOutcome.Done, null, outcome.Text, null, QueueEmbed: true,
                        new DescribeUsageReport(settings.Id, outcome.InputTokens, outcome.OutputTokens), empty.Count > 0 ? empty : null);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return Result(job, JobOutcome.Returned, emptyAttempts: empty);
                }
                catch (Exception ex) when (IsProviderProblem(ex))
                {
                    TimeSpan wait;
                    lock (_gate)
                    {
                        current.Backoff = current.Backoff == TimeSpan.Zero
                            ? TimeSpan.FromSeconds(_options.MinBackoffSeconds)
                            : TimeSpan.FromSeconds(Math.Min(current.Backoff.TotalSeconds * 2, _options.MaxBackoffSeconds));
                        current.BackoffUntil = DateTimeOffset.UtcNow + current.Backoff;
                        wait = current.Backoff;
                    }
                    logger.LogWarning(ex, "{Name} is unavailable; handing the job back and pausing it for {Seconds} s", settings.Name, wait.TotalSeconds);
                    await ReportAsync(current, ex.Message);
                    return Result(job, JobOutcome.Returned, emptyAttempts: empty);
                }
                catch (EmptyDescriptionException ex) when (NextProvider(tried) is { } next)
                {
                    lock (_gate) current.UnreportedSpend += Cost(settings, ex.InputTokens, ex.OutputTokens);
                    empty.Add(new DescribeUsageReport(settings.Id, ex.InputTokens, ex.OutputTokens));
                    logger.LogWarning("{Name} wrote nothing for {Path} ({Reason}); handing it to {Next}",
                        settings.Name, job.Path, ex.Message, next.Settings.Name);
                    current = next;
                }
                catch (Exception ex)
                {
                    var usage = ex is EmptyDescriptionException e
                        ? new DescribeUsageReport(settings.Id, e.InputTokens, e.OutputTokens)
                        : new DescribeUsageReport(settings.Id, 0, 0);
                    lock (_gate) current.UnreportedSpend += Cost(settings, usage.InputTokens, usage.OutputTokens);
                    if (job.Attempts < _options.MaxAttempts)
                    {
                        logger.LogWarning(ex, "{Name} could not describe {Path} (attempt {Attempt} of {Max}); will retry",
                            settings.Name, job.Path, job.Attempts, _options.MaxAttempts);
                        return Result(job, JobOutcome.Retry, usage: usage, emptyAttempts: empty);
                    }
                    // The model kept failing on this image: embed the file without a description.
                    logger.LogError(ex, "Describe job {JobId} for {Path} failed", job.Id, job.Path);
                    return Result(job, JobOutcome.Failed, ex.Message, queueEmbed: true, usage, empty);
                }
                finally
                {
                    if (handedOver) Interlocked.Decrement(ref attempt.InFlight);
                }
            }
        }

        /// <summary>
        /// The best-ranked available provider not yet tried on this job, busy or not, and whether or not
        /// it takes overflow: the job is already claimed, and handing it back would only send it to the
        /// provider that just failed.
        /// </summary>
        private Provider? NextProvider(HashSet<Provider> tried)
        {
            lock (_gate)
            {
                var now = DateTimeOffset.UtcNow;
                return _providers.Values
                    .Where(p => !tried.Contains(p) && p.IsAvailable(now))
                    .OrderBy(p => p.Settings.Priority)
                    .FirstOrDefault();
            }
        }

        private static decimal Cost(WorkerDescribeProvider settings, long inputTokens, long outputTokens) =>
            (inputTokens * settings.InputPricePerMillion + outputTokens * settings.OutputPricePerMillion) / 1_000_000m;

        /// <summary>Tells the server about a provider's failure, or its recovery, once per change.</summary>
        private async Task ReportAsync(Provider provider, string? error)
        {
            lock (_gate)
            {
                if (provider.ReportedError == error) return;
                provider.ReportedError = error;
            }
            try
            {
                await server.ReportProviderStatusAsync(provider.Settings.Id, error, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not report the status of {Name}", provider.Settings.Name);
            }
        }

        /// <summary>
        /// The provider, not the image, is at fault: unreachable, a key it refuses, a model it doesn't
        /// have, rate limiting, or a server error.
        /// </summary>
        private static bool IsProviderProblem(Exception? ex) => ex switch
        {
            null => false,
            ClientResultException c => c.Status is 0 or 401 or 402 or 403 or 404 or 408 or 429 or >= 500,
            HttpRequestException { StatusCode: null } => true,
            HttpRequestException h => (int)h.StatusCode! is 401 or 402 or 403 or 404 or 408 or 429 or >= 500,
            _ => IsProviderProblem(ex.InnerException),
        };

        private static WorkerJobResult Result(WorkerJob job, JobOutcome outcome, string? error = null, bool queueEmbed = false,
            DescribeUsageReport? usage = null, List<DescribeUsageReport>? emptyAttempts = null) =>
            new(job.Id, outcome, error is { Length: > 2000 } ? error[..2000] : error, null, null, queueEmbed, usage,
                emptyAttempts is { Count: > 0 } ? emptyAttempts : null);

        private sealed class Provider(WorkerDescribeProvider settings, ImageDescriber describer)
        {
            public WorkerDescribeProvider Settings { get; set; } = settings;
            public ImageDescriber Describer { get; } = describer;
            public int InFlight;
            public int Lanes { get; set; }
            public bool Retired { get; set; }
            public TimeSpan Backoff { get; set; }
            public DateTimeOffset BackoffUntil { get; set; }
            public decimal UnreportedSpend { get; set; }
            public string? ReportedError { get; set; }

            public bool IsBusy => InFlight >= Settings.Concurrency;

            public bool IsAvailable(DateTimeOffset now) =>
                !Retired && now >= BackoffUntil
                && (Settings.BudgetRemainingUsd is not { } remaining || remaining - UnreportedSpend > 0);
        }
    }
}

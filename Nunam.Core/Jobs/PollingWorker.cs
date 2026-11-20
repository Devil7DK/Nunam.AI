using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nunam.Core.Jobs
{
    /// <summary>Outcome of one poll, which drives the loop's pacing.</summary>
    public enum PollResult
    {
        /// <summary>Jobs were processed — poll again immediately.</summary>
        DidWork,

        /// <summary>Queue was empty — wait the idle interval.</summary>
        NoWork,

        /// <summary>A transient failure (AI backend or DB unreachable) — back off (exponential) and retry; jobs stay <c>Pending</c>.</summary>
        Backoff,
    }

    /// <summary>
    /// Base for queue-polling background workers. Polls immediately while there is work, sleeps a
    /// fixed interval when the queue is empty, and applies exponential backoff (capped) on transient
    /// failures — so an offline GPU machine or a briefly-down database never produces <c>Failed</c>
    /// rows, just patient retries.
    /// </summary>
    public abstract class PollingWorker(ILogger logger) : BackgroundService
    {
        protected abstract string Name { get; }
        protected abstract TimeSpan IdleInterval { get; }
        protected abstract TimeSpan MinBackoff { get; }
        protected abstract TimeSpan MaxBackoff { get; }

        /// <summary>How often <see cref="RecoverStaleAsync"/> runs.</summary>
        protected virtual TimeSpan StaleCheckInterval => TimeSpan.FromMinutes(5);

        /// <summary>Claim and process one batch. Must not throw for normal job failures (mark the job <c>Failed</c> instead).</summary>
        protected abstract Task<PollResult> PollAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Returns jobs abandoned in <c>Processing</c> (by a crash or a kill) to the queue. Runs before
        /// the first poll and then every <see cref="StaleCheckInterval"/>; failures are logged and retried
        /// on the next check rather than stopping the worker.
        /// </summary>
        protected virtual Task RecoverStaleAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("{Worker} started", Name);
            var backoff = MinBackoff;
            var nextStaleCheck = DateTimeOffset.MinValue;

            while (!stoppingToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= nextStaleCheck)
                {
                    nextStaleCheck = DateTimeOffset.UtcNow + StaleCheckInterval;
                    try
                    {
                        await RecoverStaleAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "{Worker} could not requeue stale jobs; will retry", Name);
                    }
                }

                PollResult result;
                try
                {
                    result = await PollAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Anything that escaped PollAsync is treated as transient so claimed work
                    // isn't hammered; the worker backs off rather than spinning on errors.
                    logger.LogError(ex, "{Worker} poll threw; backing off", Name);
                    result = PollResult.Backoff;
                }

                TimeSpan delay;
                switch (result)
                {
                    case PollResult.DidWork:
                        backoff = MinBackoff;
                        continue; // re-poll immediately — there may be more queued
                    case PollResult.NoWork:
                        backoff = MinBackoff;
                        delay = IdleInterval;
                        break;
                    default: // Backoff
                        delay = backoff;
                        backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
                        break;
                }

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            logger.LogInformation("{Worker} stopping", Name);
        }

        /// <summary>
        /// What a failed backend call says about the job. Only a backend that gave no usable answer at
        /// all is an outage; a server error or timeout on one input counts against that job, or a single
        /// bad file would be retried at the head of the queue forever and block every machine behind it.
        /// </summary>
        protected static BackendFailure Classify(Exception ex, CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested && ex is OperationCanceledException) return BackendFailure.Shutdown;
            return Inspect(ex);

            static BackendFailure Inspect(Exception? e) => e switch
            {
                null => BackendFailure.Rejected,
                HttpRequestException { StatusCode: null } => BackendFailure.Outage,
                HttpRequestException { StatusCode: HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.GatewayTimeout or HttpStatusCode.TooManyRequests } => BackendFailure.Outage,
                HttpRequestException => BackendFailure.JobFailed,
                // HttpClient.Timeout surfaces as a TaskCanceledException wrapping a TimeoutException.
                TaskCanceledException { InnerException: TimeoutException } => BackendFailure.JobFailed,
                TimeoutException => BackendFailure.JobFailed,
                _ => Inspect(e.InnerException),
            };
        }

        protected static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max];
    }

    public enum BackendFailure
    {
        /// <summary>No usable answer from the backend; the job is not to blame.</summary>
        Outage,

        /// <summary>The worker is stopping; the job is not to blame.</summary>
        Shutdown,

        /// <summary>A server error or timeout on this input: retry it a few times, then fail it.</summary>
        JobFailed,

        /// <summary>The backend refused this input: fail it now.</summary>
        Rejected,
    }
}

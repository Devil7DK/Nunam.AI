using System.Threading.Channels;
using Nunam.Core.Agents;
using Nunam.Indexer.Server;

namespace Nunam.Indexer.Management
{
    /// <summary>
    /// Collects the indexer's own log entries (Information and up) and everything else at Warning and
    /// up, for <see cref="ServerLogShipper"/> to send. The buffer drops the oldest entries when the
    /// server has been out of reach for long.
    /// </summary>
    public sealed class ServerLoggerProvider : ILoggerProvider
    {
        private const int MaxMessageLength = 4000;
        private const int MaxExceptionLength = 8000;

        internal Channel<LogRecord> Buffer { get; } = Channel.CreateBounded<LogRecord>(
            new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

        public ILogger CreateLogger(string categoryName) => new ServerLogger(categoryName, this);

        public void Dispose() { }

        private sealed class ServerLogger(string category, ServerLoggerProvider provider) : ILogger
        {
            // The shipper's own failures would otherwise queue more entries for the server it can't reach.
            private readonly LogLevel _minimum =
                category.StartsWith(typeof(ServerLogShipper).FullName!, StringComparison.Ordinal) ? LogLevel.None
                : category.StartsWith("Nunam.", StringComparison.Ordinal) ? LogLevel.Information
                : LogLevel.Warning;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimum && logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                provider.Buffer.Writer.TryWrite(new LogRecord(
                    DateTimeOffset.UtcNow, logLevel, category,
                    Truncate(formatter(state, exception), MaxMessageLength),
                    exception is null ? null : Truncate(exception.ToString(), MaxExceptionLength)));
            }

            private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
        }
    }

    /// <summary>Sends buffered log entries to the server every few seconds.</summary>
    public sealed class ServerLogShipper(ServerLoggerProvider provider, IndexerServer server, ILogger<ServerLogShipper> logger)
        : BackgroundService
    {
        private const int BatchSize = 500;
        private readonly List<LogRecord> _batch = [];

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken)) await ShipAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }

            // What's left when the service stops, such as the reason it stopped.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await ShipAsync(timeout.Token);
        }

        private async Task ShipAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                while (_batch.Count < BatchSize && provider.Buffer.Reader.TryRead(out var record)) _batch.Add(record);
                if (_batch.Count == 0) return;

                try
                {
                    await server.StoreLogsAsync(_batch, cancellationToken);
                    _batch.Clear();
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
                {
                    // Kept for the next try; the buffer behind it absorbs what arrives meanwhile.
                    logger.LogDebug(ex, "Could not send {Count} log entries; will retry", _batch.Count);
                    return;
                }
            }
        }
    }
}

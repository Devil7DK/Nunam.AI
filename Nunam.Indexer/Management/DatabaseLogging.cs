using System.Threading.Channels;
using Npgsql;

namespace Nunam.Indexer.Management
{
    /// <summary>
    /// Collects the indexer's own log entries (Information and up) and everything else at Warning and
    /// up, for <see cref="DatabaseLogShipper"/> to store. The buffer drops the oldest entries when the
    /// database has been out of reach for long.
    /// </summary>
    public sealed class DatabaseLoggerProvider : ILoggerProvider
    {
        private const int MaxMessageLength = 4000;
        private const int MaxExceptionLength = 8000;

        internal Channel<LogRecord> Buffer { get; } = Channel.CreateBounded<LogRecord>(
            new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

        public ILogger CreateLogger(string categoryName) => new DatabaseLogger(categoryName, this);

        public void Dispose() { }

        internal sealed record LogRecord(DateTimeOffset At, LogLevel Level, string Category, string Message, string? Exception);

        private sealed class DatabaseLogger(string category, DatabaseLoggerProvider provider) : ILogger
        {
            // The shipper's own failures would otherwise queue more entries for the database it can't reach.
            private readonly LogLevel _minimum =
                category.StartsWith(typeof(DatabaseLogShipper).FullName!, StringComparison.Ordinal) ? LogLevel.None
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

    /// <summary>Writes buffered log entries to <c>indexer_logs</c> every few seconds, once the machine is known.</summary>
    public sealed class DatabaseLogShipper(
        DatabaseLoggerProvider provider, IndexerControl control, NpgsqlDataSource dataSource, ILogger<DatabaseLogShipper> logger)
        : BackgroundService
    {
        private const int BatchSize = 500;
        private readonly List<DatabaseLoggerProvider.LogRecord> _batch = [];

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
            await ShipAsync(CancellationToken.None);
        }

        private async Task ShipAsync(CancellationToken cancellationToken)
        {
            if (control.MachineId is not { } machineId) return;

            while (true)
            {
                while (_batch.Count < BatchSize && provider.Buffer.Reader.TryRead(out var record)) _batch.Add(record);
                if (_batch.Count == 0) return;

                try
                {
                    await using var batch = dataSource.CreateBatch();
                    foreach (var record in _batch)
                    {
                        batch.BatchCommands.Add(new NpgsqlBatchCommand(
                            "INSERT INTO indexer_logs (id, machine_id, at, level, category, message, exception) VALUES ($1, $2, $3, $4, $5, $6, $7)")
                        {
                            Parameters =
                            {
                                new() { Value = Guid.NewGuid() },
                                new() { Value = machineId },
                                new() { Value = record.At },
                                new() { Value = record.Level.ToString() },
                                new() { Value = record.Category },
                                new() { Value = record.Message },
                                new() { Value = (object?)record.Exception ?? DBNull.Value },
                            },
                        });
                    }
                    await batch.ExecuteNonQueryAsync(cancellationToken);
                    _batch.Clear();
                }
                catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
                {
                    // Kept for the next try; the buffer behind it absorbs what arrives meanwhile.
                    logger.LogDebug(ex, "Could not store {Count} log entries; will retry", _batch.Count);
                    return;
                }
            }
        }
    }
}

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Nunam.Core.Data;
using Nunam.Core.Entities;
using Nunam.Core.Management;
using Nunam.Indexer.Indexing;

namespace Nunam.Indexer.Management
{
    /// <summary>
    /// Reports this indexer's status for the UI, carries out commands sent from it, and restarts the
    /// workers when the settings from the UI change. Commands and remote settings are ignored unless
    /// this machine's own settings turn on <see cref="IndexerOptions.RemoteManagement"/>.
    /// </summary>
    public class ManagementWorker(
        IServiceScopeFactory scopeFactory,
        MachineContext machine,
        IndexerControl control,
        IConfiguration configuration,
        IOptions<IndexerOptions> options,
        ILogger<ManagementWorker> logger) : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan LogRetention = TimeSpan.FromDays(7);

        private readonly IndexerOptions _options = options.Value;
        private DateTimeOffset _nextReport = DateTimeOffset.MinValue;
        private DateTimeOffset _nextLogPurge = DateTimeOffset.MinValue;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<NunamDbContext>();

                    if (await RunCommandsAsync(db, stoppingToken)) _nextReport = DateTimeOffset.MinValue;
                    if (_options.RemoteManagement) await CheckSettingsAsync(db, stoppingToken);

                    if (DateTimeOffset.UtcNow >= _nextReport)
                    {
                        await ReportAsync(db, stoppingToken);
                        _nextReport = DateTimeOffset.UtcNow + ReportInterval;
                    }

                    if (DateTimeOffset.UtcNow >= _nextLogPurge)
                    {
                        var cutoff = DateTimeOffset.UtcNow - LogRetention;
                        await db.IndexerLogs.Where(l => l.MachineId == machine.MachineId && l.At < cutoff).ExecuteDeleteAsync(stoppingToken);
                        _nextLogPurge = DateTimeOffset.UtcNow + TimeSpan.FromHours(1);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is NpgsqlException or DbUpdateException or TimeoutException)
                {
                    logger.LogDebug(ex, "Could not reach the database for status and commands; will retry");
                }

                try
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>Returns whether any command ran, so the next report shows its effect at once.</summary>
        private async Task<bool> RunCommandsAsync(NunamDbContext db, CancellationToken cancellationToken)
        {
            var commands = await db.IndexerCommands.AsTracking()
                .Where(c => c.MachineId == machine.MachineId && c.Status == IndexerCommandStatus.Pending)
                .OrderBy(c => c.Created)
                .ToListAsync(cancellationToken);

            foreach (var command in commands)
            {
                if (!_options.RemoteManagement)
                {
                    Complete(command, IndexerCommandStatus.Rejected, "Remote management is off in this machine's settings.");
                    await db.SaveChangesAsync(cancellationToken);
                    continue;
                }

                logger.LogInformation("Running {Command} requested by {User}", command.Kind, command.RequestedBy);
                var restart = false;
                switch (command.Kind)
                {
                    case IndexerCommandKind.Pause:
                        control.Paused = true;
                        Complete(command, IndexerCommandStatus.Done, "Parsing paused. Changes are still queued.");
                        break;
                    case IndexerCommandKind.Resume:
                        control.Paused = false;
                        Complete(command, IndexerCommandStatus.Done, "Parsing resumed.");
                        break;
                    case IndexerCommandKind.RetryFailed:
                        var queued = await db.IndexJobs
                            .Where(j => j.MachineId == machine.MachineId && j.Kind == IndexJobKind.Parse && j.Status == IndexJobStatus.Failed)
                            .ExecuteUpdateAsync(j => j
                                .SetProperty(x => x.Status, IndexJobStatus.Pending)
                                .SetProperty(x => x.Attempts, 0)
                                .SetProperty(x => x.Error, (string?)null)
                                .SetProperty(x => x.LastModified, DateTimeOffset.UtcNow), cancellationToken);
                        Complete(command, IndexerCommandStatus.Done, $"Queued {queued} failed file(s) again.");
                        break;
                    case IndexerCommandKind.Restart:
                        Complete(command, IndexerCommandStatus.Done, "Restarted; every watch path is scanned again.");
                        restart = true;
                        break;
                    default:
                        Complete(command, IndexerCommandStatus.Failed, "This indexer doesn't know that command.");
                        break;
                }
                await db.SaveChangesAsync(cancellationToken);

                if (restart)
                {
                    control.RequestRestart($"requested by {command.RequestedBy}");
                    return true;
                }
            }
            return commands.Count > 0;
        }

        private static void Complete(IndexerCommand command, IndexerCommandStatus status, string result)
        {
            command.Status = status;
            command.Result = result;
            command.CompletedAt = DateTimeOffset.UtcNow;
        }

        private async Task CheckSettingsAsync(NunamDbContext db, CancellationToken cancellationToken)
        {
            var revision = await db.IndexerSettings
                .Where(s => s.MachineId == machine.MachineId)
                .Select(s => (int?)s.Revision)
                .FirstOrDefaultAsync(cancellationToken);
            if (revision != control.AppliedRevision) control.RequestRestart($"settings changed in the UI (revision {revision})");
        }

        private async Task ReportAsync(NunamDbContext db, CancellationToken cancellationToken)
        {
            var status = await db.IndexerStatus.AsTracking().FirstOrDefaultAsync(s => s.MachineId == machine.MachineId, cancellationToken);
            if (status is null)
            {
                status = new IndexerStatus { MachineId = machine.MachineId };
                db.IndexerStatus.Add(status);
            }

            status.ReportedAt = DateTimeOffset.UtcNow;
            status.ProcessStartedAt = control.ProcessStartedAt;
            status.StartedAt = control.StartedAt;
            status.Version = Version;
            status.OperatingSystem = RuntimeInformation.OSDescription;
            status.ServiceAccount = OperatingSystem.IsWindows() ? $@"{Environment.UserDomainName}\{Environment.UserName}" : Environment.UserName;
            status.State = control.Paused ? IndexerState.Paused
                : control.ScanStartedAt is not null && control.ScanFinishedAt is null ? IndexerState.Scanning
                : IndexerState.Running;
            status.RemoteManagement = _options.RemoteManagement;
            status.AppliedRevision = control.AppliedRevision;
            status.ScanStartedAt = control.ScanStartedAt;
            status.ScanFinishedAt = control.ScanFinishedAt;
            status.Config = JsonSerializer.Serialize(ConfigReport(), RemoteIndexerSettings.Json);
            status.WatchPaths = JsonSerializer.Serialize(WatchPathReports(), RemoteIndexerSettings.Json);

            await db.SaveChangesAsync(cancellationToken);
        }

        private static string Version =>
            typeof(ManagementWorker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? "unknown";

        private IEnumerable<WatchPathReport> WatchPathReports() =>
            _options.WatchPaths.Select(path => Directory.Exists(path)
                ? new WatchPathReport(path, true, null)
                : new WatchPathReport(path, false, "Doesn't exist, or the service can't open it."));

        /// <summary>Every option in effect and where it came from; secrets are reduced to whether they're set.</summary>
        private List<ConfigEntry> ConfigReport()
        {
            var defaults = new IndexerOptions();
            var remote = control.Remote?.OverriddenOptions() ?? [];
            var entries = new List<ConfigEntry>();

            foreach (var property in typeof(IndexerOptions).GetProperties().Where(p => p.CanWrite))
            {
                var value = property.GetValue(_options);
                var source = remote.Contains(property.Name) ? ConfigSource.Remote
                    : Format(value) == Format(property.GetValue(defaults)) ? ConfigSource.Default
                    : ConfigSource.Local;
                entries.Add(new ConfigEntry($"{IndexerOptions.SectionName}:{property.Name}", Format(value), source));
            }

            var connection = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Postgres"));
            entries.Add(new ConfigEntry("ConnectionStrings:Postgres",
                $"Host={connection.Host};Port={connection.Port};Database={connection.Database};Username={connection.Username};SSL Mode={connection.SslMode}",
                ConfigSource.Local));
            return entries;
        }

        private static string? Format(object? value) => value switch
        {
            null => null,
            string[] items => string.Join('\n', items),
            IDictionary<string, string[]> lists => string.Join('\n', lists.OrderBy(l => l.Key).Select(l => $"{l.Key}: {string.Join(", ", l.Value)}")),
            IDictionary<string, CommandOptions> commands => string.Join('\n', commands.OrderBy(c => c.Key).Select(c =>
                $"{c.Key}: {c.Value.Path} {string.Join(' ', c.Value.EffectiveArguments)} ({c.Value.Use}{(c.Value.Extensions.Length > 0 ? ", " + string.Join(' ', c.Value.Extensions) : "")})")),
            bool b => b ? "true" : "false",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}

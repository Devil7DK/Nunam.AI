using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using Nunam.Core.Agents;
using Nunam.Core.Entities;
using Nunam.Core.Management;
using Nunam.Indexer.Server;

namespace Nunam.Indexer.Management
{
    /// <summary>
    /// Reports this indexer's status for the UI, carries out commands sent from it, and restarts the
    /// workers when the settings from the UI change. Commands and remote settings are ignored unless
    /// this machine's own settings turn on <see cref="IndexerOptions.RemoteManagement"/>.
    /// </summary>
    public class ManagementWorker(
        IndexerServer server,
        IndexerControl control,
        IConfiguration configuration,
        IOptions<IndexerOptions> options,
        ILogger<ManagementWorker> logger) : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(30);

        private readonly IndexerOptions _options = options.Value;
        private DateTimeOffset _nextReport = DateTimeOffset.MinValue;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var poll = await server.PollAsync(stoppingToken);
                    if (await RunCommandsAsync(poll.Commands, stoppingToken)) _nextReport = DateTimeOffset.MinValue;
                    if (_options.RemoteManagement && poll.SettingsRevision != control.AppliedRevision)
                    {
                        control.RequestRestart($"settings changed in the UI (revision {poll.SettingsRevision})");
                    }

                    if (DateTimeOffset.UtcNow >= _nextReport)
                    {
                        await server.ReportStatusAsync(Report(), stoppingToken);
                        _nextReport = DateTimeOffset.UtcNow + ReportInterval;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    logger.LogDebug(ex, "Could not reach the server for status and commands; will retry");
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
        private async Task<bool> RunCommandsAsync(List<PendingCommand> commands, CancellationToken cancellationToken)
        {
            foreach (var command in commands)
            {
                if (!_options.RemoteManagement)
                {
                    await server.CompleteCommandAsync(command.Id,
                        new CommandResult(IndexerCommandStatus.Rejected, "Remote management is off in this machine's settings."), cancellationToken);
                    continue;
                }

                logger.LogInformation("Running {Command} requested by {User}", command.Kind, command.RequestedBy);
                var restart = false;
                CommandResult result;
                switch (command.Kind)
                {
                    case IndexerCommandKind.Pause:
                        control.Paused = true;
                        result = new(IndexerCommandStatus.Done, "Parsing paused. Changes are still queued.");
                        break;
                    case IndexerCommandKind.Resume:
                        control.Paused = false;
                        result = new(IndexerCommandStatus.Done, "Parsing resumed.");
                        break;
                    case IndexerCommandKind.RetryFailed:
                        var queued = await server.RetryFailedAsync(cancellationToken);
                        result = new(IndexerCommandStatus.Done, $"Queued {queued} failed file(s) again.");
                        break;
                    case IndexerCommandKind.Restart:
                        result = new(IndexerCommandStatus.Done, "Restarted; every watch path is scanned again.");
                        restart = true;
                        break;
                    default:
                        result = new(IndexerCommandStatus.Failed, "This indexer doesn't know that command.");
                        break;
                }
                await server.CompleteCommandAsync(command.Id, result, cancellationToken);

                if (restart)
                {
                    control.RequestRestart($"requested by {command.RequestedBy}");
                    return true;
                }
            }
            return commands.Count > 0;
        }

        private IndexerStatusReport Report() => new(
            control.ProcessStartedAt,
            control.StartedAt,
            Version,
            RuntimeInformation.OSDescription,
            OperatingSystem.IsWindows() ? $@"{Environment.UserDomainName}\{Environment.UserName}" : Environment.UserName,
            control.Paused ? IndexerState.Paused
                : control.ScanStartedAt is not null && control.ScanFinishedAt is null ? IndexerState.Scanning
                : IndexerState.Running,
            _options.RemoteManagement,
            control.AppliedRevision,
            control.ScanStartedAt,
            control.ScanFinishedAt,
            ConfigReport(),
            [.. WatchPathReports()]);

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

            var server = configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>() ?? new ServerOptions();
            entries.Add(new ConfigEntry($"{ServerOptions.SectionName}:Url", server.Url, ConfigSource.Local));
            entries.Add(new ConfigEntry($"{ServerOptions.SectionName}:CertificateSha256", server.CertificateSha256, ConfigSource.Local));
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

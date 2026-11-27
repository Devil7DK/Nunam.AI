using Microsoft.Extensions.Logging;
using Nunam.Core.Entities;
using Nunam.Core.Management;

namespace Nunam.API.Contracts
{
    /// <summary>One row of the machines page.</summary>
    public record IndexerListItem(
        Guid MachineId,
        string Name,
        string Hostname,
        int FileCount,
        bool Online,
        IndexerState? State,
        DateTimeOffset? ReportedAt,
        string? Version,
        bool RemoteManagement,
        int Queued,
        int Failed,
        bool CanManage);

    public record IndexerDetails(
        Guid MachineId,
        string Name,
        string Hostname,
        int FileCount,
        DateTimeOffset? LastIndexedAt,
        bool CanManage,
        IndexerStatusInfo? Status,
        IReadOnlyList<JobCount> Jobs,
        IReadOnlyList<JobRate> Rates,
        IndexerSettingsInfo? Settings,
        IReadOnlyList<IndexerCommandInfo>? Commands);

    /// <param name="Config">Only for those who can manage the machine.</param>
    public record IndexerStatusInfo(
        bool Online,
        DateTimeOffset ReportedAt,
        DateTimeOffset ProcessStartedAt,
        DateTimeOffset StartedAt,
        string Version,
        string OperatingSystem,
        string? ServiceAccount,
        IndexerState State,
        bool RemoteManagement,
        int? AppliedRevision,
        DateTimeOffset? ScanStartedAt,
        DateTimeOffset? ScanFinishedAt,
        IReadOnlyList<WatchPathReport> WatchPaths,
        IReadOnlyList<ConfigEntry>? Config);

    /// <summary>How fast a stage has been settling jobs (done or failed) lately.</summary>
    public record JobRate(IndexJobKind Kind, double PerSecond);

    public record IndexerSettingsInfo(RemoteIndexerSettings Settings, int Revision, string UpdatedBy, DateTimeOffset UpdatedAt);

    public record IndexerCommandInfo(
        Guid Id, IndexerCommandKind Kind, IndexerCommandStatus Status, string RequestedBy,
        DateTimeOffset RequestedAt, DateTimeOffset? CompletedAt, string? Result);

    public record IndexerLogInfo(Guid Id, DateTimeOffset At, LogLevel Level, string Category, string Message, string? Exception);

    public record SendIndexerCommandRequest(string? Kind);
}

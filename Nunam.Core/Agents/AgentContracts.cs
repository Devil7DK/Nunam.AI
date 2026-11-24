using Microsoft.Extensions.Logging;
using Nunam.Core.Entities;
using Nunam.Core.Management;

namespace Nunam.Core.Agents
{
    public record EnrollRequest(string Code, string Hostname, string? Name, string OperatingSystem);

    public record EnrollResponse(Guid MachineId, string Name, string Token);

    /// <param name="Name">The name the machine's own settings give it; null keeps the one it has.</param>
    public record IndexerHelloRequest(string Hostname, string? Name);

    public record IndexerHelloResponse(Guid MachineId, string Name);

    public record IndexerSettingsResponse(RemoteIndexerSettings? Settings, int? Revision);

    public record CountResponse(int Count);

    public record ObservedFile(string Path, long SizeBytes, DateTimeOffset ModifiedAt);

    public record ScanBatchRequest(List<ObservedFile> Files);

    public record IndexedPathPage(List<string> Paths, string? Next);

    public record EnqueueParseRequest(List<string> Paths);

    /// <param name="Separator">The machine's directory separator: each path also removes everything under it.</param>
    public record RemoveFilesRequest(List<string> Paths, char Separator);

    public record ClaimRequest(int BatchSize);

    public record ClaimedParseJob(Guid Id, string Path, int Attempts);

    public record RecoverRequest(int StaleMinutes, int MaxAttempts);

    /// <summary>What becomes of a claimed job.</summary>
    public enum JobOutcome
    {
        Done,
        Failed,
        /// <summary>Back to the queue, the attempt counted.</summary>
        Retry,
        /// <summary>Back to the queue, the attempt not counted: the job wasn't to blame.</summary>
        Returned,
    }

    public record ParseResult(JobOutcome Outcome, IndexJobStage Stage, string? Error, ParsedFile? File);

    /// <param name="Metadata">JSON object, or null.</param>
    /// <param name="NeedsDescribe">Whether the vision model should describe <paramref name="PreviewImage"/>.</param>
    public record ParsedFile(
        string FileName, string Extension, long SizeBytes, FileType FileType,
        DateTimeOffset CreatedAt, DateTimeOffset ModifiedAt,
        string? Content, string? Metadata, byte[]? PreviewImage, bool NeedsDescribe);

    public record PendingCommand(Guid Id, IndexerCommandKind Kind, string RequestedBy);

    public record IndexerPollResponse(List<PendingCommand> Commands, int? SettingsRevision);

    public record CommandResult(IndexerCommandStatus Status, string Result);

    public record IndexerStatusReport(
        DateTimeOffset ProcessStartedAt, DateTimeOffset StartedAt, string Version, string OperatingSystem,
        string ServiceAccount, IndexerState State, bool RemoteManagement, int? AppliedRevision,
        DateTimeOffset? ScanStartedAt, DateTimeOffset? ScanFinishedAt,
        List<ConfigEntry> Config, List<WatchPathReport> WatchPaths);

    public record LogRecord(DateTimeOffset At, LogLevel Level, string Category, string Message, string? Exception);

    /// <param name="ContentCharLimit">Content is cut to this many characters before it is sent.</param>
    public record WorkerClaimRequest(IndexJobKind Kind, int BatchSize, int ContentCharLimit);

    public record WorkerJob(
        Guid Id, int Attempts, string Path, string FileName, string? Extension,
        string? Description, string? Content, byte[]? PreviewImage);

    /// <param name="QueueEmbed">Queue the file's Embed job, which a Describe job does once it is settled.</param>
    public record WorkerJobResult(Guid Id, JobOutcome Outcome, string? Error, string? Description, float[]? Embedding, bool QueueEmbed);

    public record WorkerRecoverRequest(IndexJobKind Kind, int StaleMinutes, int MaxAttempts);
}

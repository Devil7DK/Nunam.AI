using System.Text.Json;
using Nunam.Core.Entities;

namespace Nunam.API.Contracts
{
    public record FileDetails(
        Guid Id,
        string FileName,
        string Path,
        string Machine,
        FileType FileType,
        string? Extension,
        long? SizeBytes,
        DateTimeOffset? CreatedAt,
        DateTimeOffset? ModifiedAt,
        DateTimeOffset IndexedAt,
        string? Description,
        JsonElement? Metadata,
        string? Content,
        int ContentLength,
        bool ContentTruncated,
        bool HasPreview,
        bool HasEmbedding);

    public record MachineSummary(
        Guid Id,
        string Name,
        string Hostname,
        DateTimeOffset? LastSeen,
        int FileCount,
        DateTimeOffset? LastIndexedAt,
        bool CanShare);

    public record IndexStatus(
        int TotalFiles,
        IReadOnlyDictionary<FileType, int> FilesByType,
        int Described,
        int Embedded,
        DateTimeOffset? LastIndexedAt,
        IReadOnlyList<JobCount> Jobs,
        IReadOnlyList<FailedJob> RecentFailures,
        IReadOnlyList<MachineSummary> Machines);

    public record JobCount(IndexJobKind Kind, IndexJobStatus Status, int Count);

    public record FailedJob(Guid Id, IndexJobKind Kind, string Machine, string Path, IndexJobStage Stage, string? Error, DateTimeOffset At);
}

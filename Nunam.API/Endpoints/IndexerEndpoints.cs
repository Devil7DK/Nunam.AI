using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Nunam.API.Auth;
using Nunam.API.Contracts;
using Nunam.Core.Data;
using Nunam.Core.Entities;
using Nunam.Core.Management;

namespace Nunam.API.Endpoints
{
    /// <summary>
    /// The machines page: each indexer's status for everyone who can see the machine; its settings,
    /// logs and commands for admins and the machine's owners. Indexers pick up commands and settings
    /// from the database, and only when their own settings allow remote management.
    /// </summary>
    public static class IndexerEndpoints
    {
        // Indexers report every 30 s; three missed reports means it's off or can't reach the database.
        private static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(90);
        private const int RecentCommandCount = 20;
        private const int MaxLogPage = 500;

        public static IEndpointRouteBuilder MapIndexerEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/indexers", ListAsync);
            var indexer = app.MapGroup("/api/machines/{machineId:guid}/indexer");
            indexer.MapGet("/", GetAsync);
            indexer.MapGet("/logs", GetLogsAsync);
            indexer.MapPost("/commands", SendCommandAsync);
            indexer.MapPut("/settings", SaveSettingsAsync);
            return app;
        }

        private static async Task<List<IndexerListItem>> ListAsync(
            ClaimsPrincipal user, MachineScope scope, NunamDbContext db, CancellationToken cancellationToken)
        {
            var ids = await scope.VisibleMachineIdsAsync(user, cancellationToken);
            var isAdmin = user.IsAdmin();
            var userId = user.UserId();
            var online = DateTimeOffset.UtcNow - OnlineWindow;

            var rows = await db.Machines
                .Where(m => ids == null || ids.Contains(m.Id))
                .OrderBy(m => m.Name)
                .Select(m => new
                {
                    m.Id,
                    m.Name,
                    m.Hostname,
                    FileCount = m.Files.Count(),
                    Status = db.IndexerStatus.Where(s => s.MachineId == m.Id)
                        .Select(s => new { s.State, s.ReportedAt, s.Version, s.RemoteManagement })
                        .FirstOrDefault(),
                    Queued = m.Jobs.Count(j => j.Kind == IndexJobKind.Parse && (j.Status == IndexJobStatus.Pending || j.Status == IndexJobStatus.Processing)),
                    Failed = m.Jobs.Count(j => j.Status == IndexJobStatus.Failed),
                    CanManage = isAdmin || m.Access.Any(a => a.UserId == userId && a.Role == MachineAccessRole.Owner),
                })
                .ToListAsync(cancellationToken);

            return [.. rows.Select(r => new IndexerListItem(
                r.Id, r.Name, r.Hostname, r.FileCount,
                r.Status is not null && r.Status.ReportedAt >= online,
                r.Status?.State, r.Status?.ReportedAt, r.Status?.Version, r.Status?.RemoteManagement ?? false,
                r.Queued, r.Failed, r.CanManage))];
        }

        private static async Task<Results<Ok<IndexerDetails>, NotFound>> GetAsync(
            Guid machineId, ClaimsPrincipal user, MachineScope scope, NunamDbContext db, CancellationToken cancellationToken)
        {
            if (!await CanSeeAsync(user, scope, machineId, cancellationToken)) return TypedResults.NotFound();
            var machine = await db.Machines
                .Where(m => m.Id == machineId)
                .Select(m => new { m.Name, m.Hostname, FileCount = m.Files.Count(), LastIndexedAt = m.Files.Max(f => (DateTimeOffset?)f.IndexedAt) })
                .FirstOrDefaultAsync(cancellationToken);
            if (machine is null) return TypedResults.NotFound();

            var canManage = await scope.CanManageAsync(user, machineId, cancellationToken);
            var status = await db.IndexerStatus.FirstOrDefaultAsync(s => s.MachineId == machineId, cancellationToken);
            var jobs = await db.IndexJobs
                .Where(j => j.MachineId == machineId)
                .GroupBy(j => new { j.Kind, j.Status })
                .Select(g => new JobCount(g.Key.Kind, g.Key.Status, g.Count()))
                .ToListAsync(cancellationToken);

            IndexerSettingsInfo? settings = null;
            List<IndexerCommandInfo>? commands = null;
            if (canManage)
            {
                settings = await db.IndexerSettings
                    .Where(s => s.MachineId == machineId)
                    .Select(s => new { s.Settings, s.Revision, s.UpdatedBy, s.UpdatedAt })
                    .ToListAsync(cancellationToken) is [var row]
                    ? new IndexerSettingsInfo(RemoteIndexerSettings.FromJson(row.Settings), row.Revision, row.UpdatedBy, row.UpdatedAt)
                    : null;
                commands = await db.IndexerCommands
                    .Where(c => c.MachineId == machineId)
                    .OrderByDescending(c => c.Created)
                    .Take(RecentCommandCount)
                    .Select(c => new IndexerCommandInfo(c.Id, c.Kind, c.Status, c.RequestedBy, c.Created, c.CompletedAt, c.Result))
                    .ToListAsync(cancellationToken);
            }

            return TypedResults.Ok(new IndexerDetails(
                machineId, machine.Name, machine.Hostname, machine.FileCount, machine.LastIndexedAt, canManage,
                status is null ? null : ToInfo(status, canManage),
                [.. jobs.OrderBy(j => j.Kind).ThenBy(j => j.Status)],
                settings, commands));
        }

        private static async Task<Results<Ok<List<IndexerLogInfo>>, NotFound, ValidationProblem>> GetLogsAsync(
            Guid machineId, ClaimsPrincipal user, MachineScope scope, NunamDbContext db,
            string? level, DateTimeOffset? before, int? limit, CancellationToken cancellationToken)
        {
            if (!await CanManageVisibleAsync(user, scope, machineId, cancellationToken)) return TypedResults.NotFound();

            var minimum = LogLevel.Information;
            if (level is not null && !EnumNames.TryParse(level, out minimum))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["level"] = ["Use information, warning or error."] });
            }
            var levels = Enum.GetValues<LogLevel>().Where(l => l >= minimum && l != LogLevel.None).ToArray();
            var take = Math.Clamp(limit ?? 200, 1, MaxLogPage);

            return TypedResults.Ok(await db.IndexerLogs
                .Where(l => l.MachineId == machineId && levels.Contains(l.Level) && (before == null || l.At < before))
                .OrderByDescending(l => l.At)
                .Take(take)
                .Select(l => new IndexerLogInfo(l.Id, l.At, l.Level, l.Category, l.Message, l.Exception))
                .ToListAsync(cancellationToken));
        }

        private static async Task<Results<Accepted<IndexerCommandInfo>, NotFound, ValidationProblem, ProblemHttpResult>> SendCommandAsync(
            Guid machineId, SendIndexerCommandRequest request, ClaimsPrincipal user, MachineScope scope, NunamDbContext db,
            ILogger<MachineScope> logger, CancellationToken cancellationToken)
        {
            if (!await CanManageVisibleAsync(user, scope, machineId, cancellationToken)) return TypedResults.NotFound();
            if (!EnumNames.TryParse<IndexerCommandKind>(request.Kind, out var kind))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Use restart, pause, resume or retryFailed."] });
            }
            if (await RemoteManagementOffAsync(db, machineId, cancellationToken) is { } off) return off;

            var command = new IndexerCommand { MachineId = machineId, Kind = kind, RequestedBy = user.Identity!.Name! };
            db.IndexerCommands.Add(command);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("{User} sent {Command} to the indexer of machine {MachineId}", user.Identity.Name, kind, machineId);
            return TypedResults.Accepted((string?)null,
                new IndexerCommandInfo(command.Id, command.Kind, command.Status, command.RequestedBy, command.Created, null, null));
        }

        private static async Task<Results<Ok<IndexerSettingsInfo>, NotFound, ValidationProblem, ProblemHttpResult>> SaveSettingsAsync(
            Guid machineId, RemoteIndexerSettings request, ClaimsPrincipal user, MachineScope scope, NunamDbContext db,
            ILogger<MachineScope> logger, CancellationToken cancellationToken)
        {
            if (!await CanManageVisibleAsync(user, scope, machineId, cancellationToken)) return TypedResults.NotFound();

            var settings = Normalize(request);
            if (settings.Validate() is { Count: > 0 } errors) return TypedResults.ValidationProblem(errors);
            if (await RemoteManagementOffAsync(db, machineId, cancellationToken) is { } off) return off;

            var row = await db.IndexerSettings.AsTracking().FirstOrDefaultAsync(s => s.MachineId == machineId, cancellationToken);
            if (row is null)
            {
                row = new IndexerSettings { MachineId = machineId };
                db.IndexerSettings.Add(row);
            }
            row.Settings = settings.ToJson();
            row.Revision++;
            row.UpdatedBy = user.Identity!.Name!;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("{User} changed the indexer settings of machine {MachineId} (revision {Revision})",
                user.Identity.Name, machineId, row.Revision);
            return TypedResults.Ok(new IndexerSettingsInfo(settings, row.Revision, row.UpdatedBy, row.UpdatedAt));
        }

        /// <summary>Trims text and drops blank list entries, which a form produces for an empty line.</summary>
        private static RemoteIndexerSettings Normalize(RemoteIndexerSettings s) => s with
        {
            MachineName = string.IsNullOrWhiteSpace(s.MachineName) ? null : s.MachineName.Trim(),
            WatchPaths = s.WatchPaths?.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToArray(),
            ExcludePatterns = s.ExcludePatterns?.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToArray(),
            OcrLanguage = string.IsNullOrWhiteSpace(s.OcrLanguage) ? null : s.OcrLanguage.Trim(),
        };

        private static async Task<ProblemHttpResult?> RemoteManagementOffAsync(NunamDbContext db, Guid machineId, CancellationToken cancellationToken) =>
            await db.IndexerStatus.AnyAsync(s => s.MachineId == machineId && s.RemoteManagement, cancellationToken)
                ? null
                : TypedResults.Problem(
                    "This machine doesn't allow remote management. Set Indexer:RemoteManagement to true in its own settings.",
                    statusCode: StatusCodes.Status409Conflict);

        private static async Task<bool> CanSeeAsync(ClaimsPrincipal user, MachineScope scope, Guid machineId, CancellationToken cancellationToken) =>
            await scope.VisibleMachineIdsAsync(user, cancellationToken) is not { } ids || ids.Contains(machineId);

        // Not 403: a machine the user can't see is one they shouldn't learn exists, and one they can
        // see but not manage gets the same answer, so the two can't be told apart.
        private static Task<bool> CanManageVisibleAsync(ClaimsPrincipal user, MachineScope scope, Guid machineId, CancellationToken cancellationToken) =>
            scope.CanManageAsync(user, machineId, cancellationToken);

        private static IndexerStatusInfo ToInfo(IndexerStatus s, bool canManage) => new(
            DateTimeOffset.UtcNow - s.ReportedAt <= OnlineWindow,
            s.ReportedAt, s.ProcessStartedAt, s.StartedAt, s.Version, s.OperatingSystem,
            canManage ? s.ServiceAccount : null,
            s.State, s.RemoteManagement, s.AppliedRevision, s.ScanStartedAt, s.ScanFinishedAt,
            JsonSerializer.Deserialize<List<WatchPathReport>>(s.WatchPaths, RemoteIndexerSettings.Json) ?? [],
            canManage ? JsonSerializer.Deserialize<List<ConfigEntry>>(s.Config, RemoteIndexerSettings.Json) : null);
    }
}

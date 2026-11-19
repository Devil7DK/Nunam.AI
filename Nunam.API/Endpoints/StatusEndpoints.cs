using Microsoft.EntityFrameworkCore;
using Nunam.API.Contracts;
using Nunam.Core.Data;
using Nunam.Core.Entities;

namespace Nunam.API.Endpoints
{
    public static class StatusEndpoints
    {
        private const int RecentFailureCount = 20;

        public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/status", GetStatusAsync).WithName("GetStatus");
            app.MapGet("/api/machines", GetMachinesAsync).WithName("GetMachines");
            app.MapGet("/healthz", async (NunamDbContext db, CancellationToken ct) =>
                await db.Database.CanConnectAsync(ct) ? Results.Ok("ok") : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
                .ExcludeFromDescription();
            return app;
        }

        private static async Task<IndexStatus> GetStatusAsync(NunamDbContext db, CancellationToken cancellationToken)
        {
            var byType = await db.Files
                .GroupBy(f => f.FileType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Type, g => g.Count, cancellationToken);

            var described = await db.Files.CountAsync(f => f.Description != null, cancellationToken);
            var embedded = await db.Files.CountAsync(f => f.Embedding != null, cancellationToken);
            var lastIndexedAt = await db.Files.MaxAsync(f => (DateTimeOffset?)f.IndexedAt, cancellationToken);

            var jobs = await db.IndexJobs
                .GroupBy(j => new { j.Kind, j.Status })
                .Select(g => new JobCount(g.Key.Kind, g.Key.Status, g.Count()))
                .ToListAsync(cancellationToken);

            var failures = await db.IndexJobs
                .Where(j => j.Status == IndexJobStatus.Failed)
                .OrderByDescending(j => j.LastModified)
                .Take(RecentFailureCount)
                .Select(j => new FailedJob(j.Id, j.Kind, j.Machine.Name, j.Path, j.Stage, j.Error, j.LastModified))
                .ToListAsync(cancellationToken);

            return new IndexStatus(
                byType.Values.Sum(), byType, described, embedded, lastIndexedAt,
                [.. jobs.OrderBy(j => j.Kind).ThenBy(j => j.Status)], failures,
                await GetMachinesAsync(db, cancellationToken));
        }

        private static async Task<List<MachineSummary>> GetMachinesAsync(NunamDbContext db, CancellationToken cancellationToken) =>
            await db.Machines
                .OrderBy(m => m.Name)
                .Select(m => new MachineSummary(
                    m.Id, m.Name, m.Hostname, m.LastSeen,
                    m.Files.Count(),
                    m.Files.Max(f => (DateTimeOffset?)f.IndexedAt)))
                .ToListAsync(cancellationToken);
    }
}

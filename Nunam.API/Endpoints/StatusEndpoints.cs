using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nunam.API.Auth;
using Nunam.API.Contracts;
using Nunam.Core.Data;
using Nunam.Core.Entities;

namespace Nunam.API.Endpoints
{
    /// <summary>Index progress, counted over the machines the user can see.</summary>
    public static class StatusEndpoints
    {
        private const int RecentFailureCount = 20;

        public static IEndpointRouteBuilder MapStatusEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/status", GetStatusAsync).WithName("GetStatus");
            app.MapGet("/api/machines", GetMachinesAsync).WithName("GetMachines");
            app.MapGet("/healthz", async (NunamDbContext db, CancellationToken ct) =>
                await db.Database.CanConnectAsync(ct) ? Results.Ok("ok") : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
                .AllowAnonymous()
                .ExcludeFromDescription();
            return app;
        }

        private static async Task<IndexStatus> GetStatusAsync(
            ClaimsPrincipal user, MachineScope scope, NunamDbContext db, CancellationToken cancellationToken)
        {
            var ids = await scope.VisibleMachineIdsAsync(user, cancellationToken);
            var files = db.Files.Where(f => ids == null || ids.Contains(f.MachineId));
            var jobs = db.IndexJobs.Where(j => ids == null || ids.Contains(j.MachineId));

            var byType = await files
                .GroupBy(f => f.FileType)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Type, g => g.Count, cancellationToken);

            var described = await files.CountAsync(f => f.Description != null, cancellationToken);
            var embedded = await files.CountAsync(f => f.Embedding != null, cancellationToken);
            var lastIndexedAt = await files.MaxAsync(f => (DateTimeOffset?)f.IndexedAt, cancellationToken);

            var jobCounts = await jobs
                .GroupBy(j => new { j.Kind, j.Status })
                .Select(g => new JobCount(g.Key.Kind, g.Key.Status, g.Count()))
                .ToListAsync(cancellationToken);

            var failures = await jobs
                .Where(j => j.Status == IndexJobStatus.Failed)
                .OrderByDescending(j => j.LastModified)
                .Take(RecentFailureCount)
                .Select(j => new FailedJob(j.Id, j.Kind, j.Machine.Name, j.Path, j.Stage, j.Error, j.LastModified))
                .ToListAsync(cancellationToken);

            return new IndexStatus(
                byType.Values.Sum(), byType, described, embedded, lastIndexedAt,
                [.. jobCounts.OrderBy(j => j.Kind).ThenBy(j => j.Status)], failures,
                await GetMachinesAsync(user, scope, db, cancellationToken));
        }

        private static async Task<List<MachineSummary>> GetMachinesAsync(
            ClaimsPrincipal user, MachineScope scope, NunamDbContext db, CancellationToken cancellationToken)
        {
            var ids = await scope.VisibleMachineIdsAsync(user, cancellationToken);
            var isAdmin = user.IsAdmin();
            var userId = user.UserId();

            return await db.Machines
                .Where(m => ids == null || ids.Contains(m.Id))
                .OrderBy(m => m.Name)
                .Select(m => new MachineSummary(
                    m.Id, m.Name, m.Hostname, m.LastSeen,
                    m.Files.Count(),
                    m.Files.Max(f => (DateTimeOffset?)f.IndexedAt),
                    isAdmin || m.Access.Any(a => a.UserId == userId && a.Role == MachineAccessRole.Owner)))
                .ToListAsync(cancellationToken);
        }
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Nunam.API.Auth;
using Nunam.API.Contracts;
using Nunam.Core.Data;
using Nunam.Core.Entities;

namespace Nunam.API.Endpoints
{
    /// <summary>
    /// Who can see a machine. Admins and the machine's owners manage it; nobody but an admin changes
    /// their own entry, so an owner can't lock themselves out by accident.
    /// </summary>
    public static class MachineAccessEndpoints
    {
        public static IEndpointRouteBuilder MapMachineAccessEndpoints(this IEndpointRouteBuilder app)
        {
            var access = app.MapGroup("/api/machines/{machineId:guid}/access");
            access.MapGet("/", ListAsync);
            access.MapPut("/{userId:guid}", SetAsync);
            access.MapDelete("/{userId:guid}", RemoveAsync);
            return app;
        }

        private static async Task<Results<Ok<List<MachineAccessEntry>>, NotFound>> ListAsync(
            Guid machineId, ClaimsPrincipal user, MachineScope scope, NunamDbContext db, CancellationToken cancellationToken)
        {
            // Not 403: a machine the user can't share is one they shouldn't learn exists.
            if (!await scope.CanShareAsync(user, machineId, cancellationToken)) return TypedResults.NotFound();

            return TypedResults.Ok(await db.MachineAccess
                .Where(a => a.MachineId == machineId)
                .OrderBy(a => a.User.DisplayName)
                .Select(a => new MachineAccessEntry(a.UserId, a.User.UserName, a.User.DisplayName, a.Role))
                .ToListAsync(cancellationToken));
        }

        private static async Task<Results<NoContent, NotFound, ValidationProblem>> SetAsync(
            Guid machineId, Guid userId, SetMachineAccessRequest request, ClaimsPrincipal user, MachineScope scope,
            NunamDbContext db, ILogger<MachineScope> logger, CancellationToken cancellationToken)
        {
            if (!await scope.CanShareAsync(user, machineId, cancellationToken)) return TypedResults.NotFound();

            if (!EnumNames.TryParse<MachineAccessRole>(request.Role, out var role))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Use owner or member."] });
            }

            if (userId == user.UserId() && !user.IsAdmin()) return OwnEntry();
            if (!await db.Machines.AnyAsync(m => m.Id == machineId, cancellationToken)
                || !await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            {
                return TypedResults.NotFound();
            }

            var entry = await db.MachineAccess.AsTracking()
                .FirstOrDefaultAsync(a => a.MachineId == machineId && a.UserId == userId, cancellationToken);
            if (entry is null) db.MachineAccess.Add(new MachineAccess { MachineId = machineId, UserId = userId, Role = role });
            else entry.Role = role;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("{By} gave user {UserId} {Role} access to machine {MachineId}",
                user.Identity!.Name, userId, role, machineId);
            return TypedResults.NoContent();
        }

        private static async Task<Results<NoContent, NotFound, ValidationProblem>> RemoveAsync(
            Guid machineId, Guid userId, ClaimsPrincipal user, MachineScope scope, NunamDbContext db,
            ILogger<MachineScope> logger, CancellationToken cancellationToken)
        {
            if (!await scope.CanShareAsync(user, machineId, cancellationToken)) return TypedResults.NotFound();
            if (userId == user.UserId() && !user.IsAdmin()) return OwnEntry();

            var removed = await db.MachineAccess
                .Where(a => a.MachineId == machineId && a.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            if (removed == 0) return TypedResults.NotFound();

            logger.LogInformation("{By} removed user {UserId} from machine {MachineId}", user.Identity!.Name, userId, machineId);
            return TypedResults.NoContent();
        }

        private static ValidationProblem OwnEntry() =>
            TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["userId"] = ["You can't change your own access. Ask another owner or an admin."],
            });
    }
}

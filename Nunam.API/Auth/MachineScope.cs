using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nunam.Core.Data;
using Nunam.Core.Entities;

namespace Nunam.API.Auth
{
    /// <summary>Which machines' files a signed-in user may see and share.</summary>
    public class MachineScope(NunamDbContext db)
    {
        /// <summary>The machines <paramref name="user"/> can see, or <c>null</c> for all of them (admins).</summary>
        public async Task<Guid[]?> VisibleMachineIdsAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
        {
            if (user.IsAdmin()) return null;

            var userId = user.UserId();
            return await db.MachineAccess
                .Where(a => a.UserId == userId)
                .Select(a => a.MachineId)
                .ToArrayAsync(cancellationToken);
        }

        /// <summary>Admins and the machine's owners share it, and read and manage its indexer.</summary>
        public Task<bool> CanManageAsync(ClaimsPrincipal user, Guid machineId, CancellationToken cancellationToken) =>
            CanShareAsync(user, machineId, cancellationToken);

        public async Task<bool> CanShareAsync(ClaimsPrincipal user, Guid machineId, CancellationToken cancellationToken)
        {
            if (user.IsAdmin()) return true;

            var userId = user.UserId();
            return await db.MachineAccess.AnyAsync(
                a => a.UserId == userId && a.MachineId == machineId && a.Role == MachineAccessRole.Owner,
                cancellationToken);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nunam.Core.Entities;

namespace Nunam.Core.Data
{
    public sealed class TouchEntityInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            TouchModifiedEntities(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            TouchModifiedEntities(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private static void TouchModifiedEntities(DbContext? context)
        {
            if (context is null) return;

            var now = DateTimeOffset.UtcNow;
            foreach (var entry in context.ChangeTracker.Entries<EntityBase>())
            {
                if (entry.State == EntityState.Modified)
                {
                    entry.Property(nameof(EntityBase.LastModified)).CurrentValue = now;
                }
            }
        }
    }
}

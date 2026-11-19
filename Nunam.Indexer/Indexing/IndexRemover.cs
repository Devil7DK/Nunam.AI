using Microsoft.EntityFrameworkCore;
using Nunam.Core.Data;
using Nunam.Core.Entities;

namespace Nunam.Indexer.Indexing
{
    /// <summary>Drops index entries for files that are gone from disk.</summary>
    public static class IndexRemover
    {
        /// <summary>
        /// Removes the entry for <paramref name="path"/> and, since a delete event doesn't say whether
        /// it was a file or a folder, every entry under it. Jobs a worker is processing are left alone:
        /// deleting them under the worker would fail its whole batch, and it fails them itself once it
        /// finds the file row gone. Returns the number of files removed.
        /// </summary>
        public static async Task<int> RemoveAsync(
            NunamDbContext db, Guid machineId, string path, CancellationToken cancellationToken)
        {
            var folderPrefix = path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            await db.IndexJobs
                .Where(j => j.MachineId == machineId
                    && j.Status != IndexJobStatus.Processing
                    && (j.Path == path || j.Path.StartsWith(folderPrefix)))
                .ExecuteDeleteAsync(cancellationToken);

            return await db.Files
                .Where(f => f.MachineId == machineId && (f.Path == path || f.Path.StartsWith(folderPrefix)))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}

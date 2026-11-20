using Microsoft.Extensions.Options;

namespace Nunam.Indexer.Indexing
{
    /// <summary>
    /// Which files this indexer may read: under a configured watch root, reached without passing
    /// through a symlink or junction, and not excluded. Queue rows come from a database every indexer
    /// writes to, so a path is checked here before it is opened, not trusted because it was queued.
    /// </summary>
    public class IndexScope(IOptions<IndexerOptions> options, PathFilter pathFilter)
    {
        private static readonly StringComparison PathComparison =
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private readonly string[] _roots = [.. options.Value.WatchPaths
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)))];

        /// <summary>Whether <paramref name="path"/> may be indexed; <paramref name="reason"/> says why not.</summary>
        public bool Allows(string path, out string reason)
        {
            var full = Path.GetFullPath(path);
            var root = _roots.FirstOrDefault(r => full.StartsWith(r + Path.DirectorySeparatorChar, PathComparison));
            if (root is null)
            {
                reason = "Path is outside this indexer's watch paths.";
                return false;
            }

            if (pathFilter.IsExcluded(full))
            {
                reason = "Path matches an exclude pattern.";
                return false;
            }

            // The root itself may be a link the owner chose; anything below it may not, or a file author
            // could point the indexer at files outside the root that the service account can read.
            for (var current = full; current.Length > root.Length; current = Path.GetDirectoryName(current)!)
            {
                FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if (IsLink(entry))
                {
                    reason = "Path passes through a symbolic link or junction.";
                    return false;
                }
            }

            reason = "";
            return true;
        }

        /// <summary>Symlinks, junctions and other reparse points.</summary>
        public static bool IsLink(FileSystemInfo entry) =>
            entry.Exists && (entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint));
    }
}

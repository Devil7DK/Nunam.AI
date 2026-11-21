namespace Nunam.Indexer.Indexing
{
    /// <summary>
    /// The folders to put a <see cref="FileSystemWatcher"/> on for one watch root. A recursive watcher
    /// can't skip anything: on Linux it adds an inotify watch to every folder below (all of <c>/proc</c>
    /// for a root of <c>/</c>), and on Windows it reports every write under <c>C:\Windows</c>. So the
    /// path down to each excluded system folder is watched folder by folder, and everything beside it
    /// recursively. A folder created later directly in one of those is queued once but not watched
    /// until the indexer restarts.
    /// </summary>
    public static class WatchPlan
    {
        public static List<(string Folder, bool Recursive)> For(string root, PathFilter filter)
        {
            root = PathRules.Normalize(root);
            var skipped = filter.SystemPaths
                .Concat(filter.SkipsSystemFolders ? SystemFolders.HiddenHomeFoldersUnder(root) : [])
                .Where(path => PathRules.IsUnder(path, root) && Directory.Exists(path) && filter.IsExcluded(path))
                .ToList();

            var plan = new List<(string, bool)>();
            Add(root);
            return plan;

            void Add(string folder)
            {
                if (!skipped.Any(path => PathRules.IsUnder(path, folder)))
                {
                    // A watcher on a folder the service can't read reports an error instead of nothing.
                    if (CanRead(folder)) plan.Add((folder, true));
                    return;
                }

                plan.Add((folder, false));
                string[] subfolders;
                try
                {
                    subfolders = Directory.GetDirectories(folder);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return;
                }

                foreach (var sub in subfolders.Select(PathRules.Normalize))
                {
                    if (skipped.Any(path => string.Equals(path, sub, PathRules.Comparison))) continue;
                    if (IndexScope.IsLink(new DirectoryInfo(sub))) continue;
                    Add(sub);
                }
            }
        }

        private static bool CanRead(string folder)
        {
            try
            {
                using var entries = Directory.EnumerateFileSystemEntries(folder).GetEnumerator();
                entries.MoveNext();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}

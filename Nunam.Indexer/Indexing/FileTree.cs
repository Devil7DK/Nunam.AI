namespace Nunam.Indexer.Indexing
{
    /// <summary>Walks a directory tree, skipping excluded paths and directories it can't read.</summary>
    public class FileTree(PathFilter pathFilter, ILogger<FileTree> logger)
    {
        /// <summary>Depth-first, lazily, so a huge tree is never held in memory.</summary>
        public IEnumerable<string> EnumerateFiles(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                if (pathFilter.IsExcluded(dir + Path.DirectorySeparatorChar)) continue;

                foreach (var sub in Read(() => Directory.GetDirectories(dir), dir)) stack.Push(sub);

                foreach (var file in Read(() => Directory.GetFiles(dir), dir))
                {
                    if (!pathFilter.IsExcluded(file)) yield return file;
                }
            }
        }

        private string[] Read(Func<string[]> list, string dir)
        {
            try
            {
                return list();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Cannot read {Dir}; skipping", dir);
                return [];
            }
        }
    }
}

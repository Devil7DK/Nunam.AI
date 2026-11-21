using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Nunam.Indexer.Indexing
{
    /// <summary>
    /// Decides whether a path should be skipped, based on the configured exclude patterns and, unless
    /// turned off, <see cref="SystemFolders"/>. A pattern containing <c>*</c>/<c>?</c> is a filename glob;
    /// otherwise it's a case-insensitive substring match (e.g. <c>/node_modules/</c>) against the part of
    /// the path below its watch root, so a root chosen inside such a folder is still indexed. Separators
    /// are normalized to <c>/</c> on both sides, so one config works on Windows and Linux.
    /// </summary>
    public class PathFilter
    {
        private readonly string[] _roots;
        private readonly List<string> _substrings = [];
        private readonly List<Regex> _globs = [];

        public PathFilter(IOptions<IndexerOptions> options)
        {
            var o = options.Value;
            _roots = [.. o.WatchPaths.Where(r => !string.IsNullOrWhiteSpace(r)).Select(PathRules.Normalize)];

            var patterns = o.SkipSystemFolders ? o.ExcludePatterns.Concat(SystemFolders.Patterns) : o.ExcludePatterns;
            foreach (var pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;

                if (pattern.Contains('*') || pattern.Contains('?'))
                {
                    var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                    _globs.Add(new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.Compiled));
                }
                else
                {
                    _substrings.Add(Normalize(pattern));
                }
            }

            SkipsSystemFolders = o.SkipSystemFolders;
            SystemPaths = o.SkipSystemFolders ? [.. _roots.SelectMany(SystemFolders.Under).Distinct()] : [];
        }

        public bool SkipsSystemFolders { get; }

        /// <summary>System folders and files inside some watch root, absolute.</summary>
        public IReadOnlyList<string> SystemPaths { get; }

        public bool IsExcluded(string fullPath)
        {
            var path = PathRules.Normalize(fullPath);
            var root = PathRules.RootOf(path, _roots);

            // Only applies where the system folder lies inside the path's own root: a root nested in it was chosen on purpose.
            foreach (var system in SystemPaths)
            {
                if (PathRules.IsSameOrUnder(path, system) && AppliesIn(system, root)) return true;
            }

            if (SkipsSystemFolders && SystemFolders.HiddenHomeFolderOf(path) is { } hidden && AppliesIn(hidden, root)) return true;

            var below = Normalize(root is null ? fullPath : fullPath[root.TrimEnd('/', '\\').Length..]);
            foreach (var sub in _substrings)
            {
                if (below.Contains(sub, StringComparison.OrdinalIgnoreCase)) return true;
            }

            if (_globs.Count > 0)
            {
                var name = Path.GetFileName(fullPath);
                foreach (var glob in _globs)
                {
                    if (glob.IsMatch(name)) return true;
                }
            }

            return false;
        }

        private static bool AppliesIn(string systemPath, string? root) => root is null || PathRules.IsUnder(systemPath, root);

        private static string Normalize(string path) => path.Replace('\\', '/');
    }
}

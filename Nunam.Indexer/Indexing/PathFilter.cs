using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Nunam.Indexer.Indexing
{
    /// <summary>
    /// Decides whether a path should be skipped, based on the configured exclude patterns. A pattern containing <c>*</c>/<c>?</c> is a filename glob;
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

            foreach (var pattern in o.ExcludePatterns)
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
        }

        public bool IsExcluded(string fullPath)
        {
            var path = PathRules.Normalize(fullPath);
            var root = PathRules.RootOf(path, _roots);

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

        private static string Normalize(string path) => path.Replace('\\', '/');
    }
}

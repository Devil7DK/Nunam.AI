using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Nunam.Indexer.Indexing
{
    /// <summary>
    /// Decides whether a path should be skipped, based on the configured exclude patterns. A pattern
    /// containing <c>*</c>/<c>?</c> is treated as a filename glob; otherwise it's a case-insensitive
    /// substring match against the full path (e.g. <c>/node_modules/</c>). Separators are normalized
    /// to <c>/</c> on both sides, so one config works on Windows and Linux.
    /// </summary>
    public class PathFilter
    {
        private readonly List<string> _substrings = [];
        private readonly List<Regex> _globs = [];

        public PathFilter(IOptions<IndexerOptions> options)
        {
            foreach (var pattern in options.Value.ExcludePatterns)
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
            var normalized = Normalize(fullPath);
            foreach (var sub in _substrings)
            {
                if (normalized.Contains(sub, StringComparison.OrdinalIgnoreCase)) return true;
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

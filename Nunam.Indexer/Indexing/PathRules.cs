namespace Nunam.Indexer.Indexing
{
    /// <summary>Path comparisons that also hold for watch roots that are a whole drive (<c>/</c>, <c>C:\</c>).</summary>
    public static class PathRules
    {
        public static readonly StringComparison Comparison =
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>Absolute, without a trailing separator except on a drive root, which can't lose it.</summary>
        public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        /// <summary>Whether <paramref name="path"/> is strictly inside <paramref name="folder"/>; both normalized.</summary>
        public static bool IsUnder(string path, string folder) =>
            path.Length > folder.Length && path.StartsWith(WithSeparator(folder), Comparison);

        public static bool IsSameOrUnder(string path, string folder) =>
            string.Equals(path, folder, Comparison) || IsUnder(path, folder);

        /// <summary>The innermost root holding <paramref name="path"/>, so a root nested in another keeps its own rules.</summary>
        public static string? RootOf(string path, IEnumerable<string> roots) =>
            roots.Where(r => IsSameOrUnder(path, r)).MaxBy(r => r.Length);

        private static string WithSeparator(string folder) =>
            Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
    }
}

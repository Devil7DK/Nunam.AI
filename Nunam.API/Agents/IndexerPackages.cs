using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Nunam.API.Agents
{
    /// <summary>The indexer packages in <see cref="AgentOptions.PackageFolder"/>, as the build scripts name them.</summary>
    public partial class IndexerPackages(IOptions<AgentOptions> options)
    {
        public static readonly string[] Kinds = ["msi", "deb", "rpm"];

        [GeneratedRegex(@"^nunam-indexer-(?<v>\d+(\.\d+){1,3})-(?<a>x64|arm64)\.msi$")]
        private static partial Regex Msi();

        [GeneratedRegex(@"^nunam-indexer_(?<v>\d+(\.\d+){1,3})_(?<a>amd64|arm64)\.deb$")]
        private static partial Regex Deb();

        [GeneratedRegex(@"^nunam-indexer-(?<v>\d+(\.\d+){1,3})-\d+\.(?<a>x86_64|aarch64)\.rpm$")]
        private static partial Regex Rpm();

        /// <summary>The newest package of <paramref name="kind"/> for <paramref name="arch"/> (<c>x64</c> or <c>arm64</c>).</summary>
        public FileInfo? Find(string kind, string arch)
        {
            if (options.Value.PackageFolder is not { Length: > 0 } folder || !Directory.Exists(folder)) return null;
            var (pattern, archName) = (kind, arch) switch
            {
                ("msi", "x64") => (Msi(), "x64"),
                ("msi", "arm64") => (Msi(), "arm64"),
                ("deb", "x64") => (Deb(), "amd64"),
                ("deb", "arm64") => (Deb(), "arm64"),
                ("rpm", "x64") => (Rpm(), "x86_64"),
                ("rpm", "arm64") => (Rpm(), "aarch64"),
                _ => (null, null),
            };
            if (pattern is null) return null;

            return new DirectoryInfo(folder).EnumerateFiles()
                .Select(f => (File: f, Match: pattern.Match(f.Name)))
                .Where(x => x.Match.Success && x.Match.Groups["a"].Value == archName)
                .OrderByDescending(x => Version.Parse(x.Match.Groups["v"].Value))
                .Select(x => x.File)
                .FirstOrDefault();
        }

        public List<string> Available() => [.. Kinds.Where(k => Find(k, "x64") is not null)];
    }
}

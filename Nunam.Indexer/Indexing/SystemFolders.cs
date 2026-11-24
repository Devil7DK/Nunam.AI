namespace Nunam.Indexer.Indexing
{
    /// <summary>
    /// Folders that hold the operating system, installed programs, package caches and app data: nothing
    /// anyone searches for, much of it unreadable to the service, and churning constantly. Skipped when
    /// a watch root contains them, so watching <c>/</c> or <c>C:\</c> indexes only people's files; a
    /// watch root inside one of them is indexed as configured.
    /// </summary>
    public static class SystemFolders
    {
        // Also on a Windows drive mounted elsewhere: AppData of any profile, and each profile's registry hives.
        // ~$ files are the owner files Office keeps beside an open document, not documents themselves.
        private static readonly string[] AnyOs =
        [
            "/.git/", "/node_modules/", "/bin/", "/obj/", "/.vs/", "/$RECYCLE.BIN/", "/System Volume Information/", "*.tmp", "~$*",
            "/AppData/", "ntuser.*", "/.cache/", "/.nuget/packages/", "/.npm/", "/.cargo/registry/", "/.rustup/", "/.m2/repository/",
            "/.gradle/caches/", "/.dotnet/", "/.vscode/extensions/", "/.vscode-server/", "/__pycache__/",
            "/.venv/", "/site-packages/", "/.Trash/", "/.Trash-", "/.Spotlight-V100/", "/.fseventsd/",
        ];

        private static readonly string[] UnixAbsolute =
        [
            "/proc", "/sys", "/dev", "/run", "/tmp", "/boot", "/efi", "/lost+found", "/snap", "/nix", "/sysroot",
            "/usr", "/bin", "/sbin", "/lib", "/lib32", "/lib64", "/libx32", "/opt", "/etc",
            "/var", "/afs", "/swapfile", "/swap.img",
        ];

        private static readonly string[] WindowsVolumeRelative =
        [
            "Windows", "Windows.old", "Program Files", "Program Files (x86)", "ProgramData", "Recovery", "PerfLogs",
            "$Recycle.Bin", "System Volume Information", "Config.Msi", "MSOCache", "OneDriveTemp", "$WinREAgent",
            "$Windows.~BT", "$Windows.~WS", "$SysReset", "$GetCurrent", "Users/Default",
            "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log", "DumpStack.log.tmp",
        ];

        private static readonly string[] WindowsLocations = ["SystemRoot", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData"];

        /// <summary>Patterns in <see cref="IndexerOptions.ExcludePatterns"/> syntax that apply anywhere.</summary>
        public static IEnumerable<string> Patterns => AnyOs;

        /// <summary>System folders and files strictly inside <paramref name="root"/> (normalized).</summary>
        public static IEnumerable<string> Under(string root) =>
            Candidates(root)
                .Select(PathRules.Normalize)
                .Where(path => PathRules.IsUnder(path, root))
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        /// <summary>
        /// The hidden folder in a home folder that holds <paramref name="path"/> (normalized), if any:
        /// <c>~/.config</c>, <c>~/.local</c>, <c>C:\Users\me\AppData</c> or <c>C:\Users\me\.gradle</c>
        /// hold app data by convention.
        /// </summary>
        public static string? HiddenHomeFolderOf(string path)
        {
            foreach (var (parent, depth) in HomeParents())
            {
                if (!PathRules.IsUnder(path, parent)) continue;
                var parts = path[(parent.TrimEnd(Path.DirectorySeparatorChar).Length + 1)..].Split(Path.DirectorySeparatorChar);
                if (parts.Length > depth && IsAppData(parts[depth]))
                {
                    return Path.Combine([parent, .. parts[..(depth + 1)]]);
                }
            }
            return null;
        }

        /// <summary>The hidden home folders that exist now inside <paramref name="root"/>.</summary>
        public static IEnumerable<string> HiddenHomeFoldersUnder(string root)
        {
            foreach (var (parent, depth) in HomeParents())
            {
                var homes = depth == 0 ? [parent] : Subfolders(parent);
                foreach (var hidden in homes.SelectMany(Subfolders).Where(f => IsAppData(Path.GetFileName(f))))
                {
                    if (PathRules.IsUnder(hidden, root)) yield return hidden;
                }
            }
        }

        private static bool IsAppData(string name) =>
            name.StartsWith('.') || (OperatingSystem.IsWindows() && name.Equals("AppData", StringComparison.OrdinalIgnoreCase));

        // Depth 1: homes are the folders inside the parent; depth 0: the parent is itself a home.
        private static IEnumerable<(string Parent, int Depth)> HomeParents()
        {
            if (!OperatingSystem.IsWindows()) return [("/home", 1), ("/root", 0)];
            var drive = Environment.GetEnvironmentVariable("SystemDrive");
            return string.IsNullOrEmpty(drive) ? [] : [(PathRules.Normalize(drive + @"\Users"), 1)];
        }

        private static string[] Subfolders(string folder)
        {
            try
            {
                return Directory.GetDirectories(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        private static IEnumerable<string> Candidates(string root)
        {
            if (!OperatingSystem.IsWindows()) return UnixAbsolute.Concat(MountedWindowsVolumes(root).SelectMany(OnVolume));

            var volume = Path.GetPathRoot(root);
            var onVolume = string.IsNullOrEmpty(volume) ? [] : OnVolume(volume);
            var located = WindowsLocations
                .Select(Environment.GetEnvironmentVariable)
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(path => path!);
            return onVolume.Concat(located);
        }

        private static IEnumerable<string> OnVolume(string volume) => WindowsVolumeRelative.Select(name => Path.Combine(volume, name));

        // A Windows system drive mounted on Linux or macOS: the root itself or the folder above it that holds Windows/System32.
        private static IEnumerable<string> MountedWindowsVolumes(string root)
        {
            for (var folder = root; !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
            {
                if (Directory.Exists(Path.Combine(folder, "Windows", "System32"))) yield return folder;
            }
        }
    }
}

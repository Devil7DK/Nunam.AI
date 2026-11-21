using System.Reflection;
using System.Runtime.Loader;
using Nunam.Indexer.Abstractions;

namespace Nunam.Indexer.Providers
{
    /// <summary>
    /// Finds parser, OCR and PDF renderer types in plugin assemblies: <c>&lt;folder&gt;/&lt;plugin&gt;/&lt;plugin&gt;.dll</c>,
    /// each with its own dependencies beside it. Each plugin gets its own load context, sharing only
    /// the contracts and the framework with the indexer, so plugins can use different versions of a
    /// library. Assemblies are loaded once per process; the indexer's restarts reuse them.
    /// </summary>
    public static class PluginLoader
    {
        private static readonly Lock Gate = new();
        private static readonly Dictionary<string, IReadOnlyList<Type>> Loaded = new(StringComparer.Ordinal);

        public static IReadOnlyList<Type> ProviderTypes(string folder, ILogger logger)
        {
            lock (Gate)
            {
                if (Loaded.TryGetValue(folder, out var cached)) return cached;

                var types = new List<Type>();
                if (Directory.Exists(folder))
                {
                    foreach (var directory in Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal))
                    {
                        var path = Path.Combine(directory, Path.GetFileName(directory) + ".dll");
                        if (!File.Exists(path))
                        {
                            logger.LogWarning("Plugin folder {Folder} has no {Dll}; skipped", directory, Path.GetFileName(path));
                            continue;
                        }

                        try
                        {
                            var assembly = new PluginLoadContext(path).LoadFromAssemblyPath(path);
                            var found = assembly.GetExportedTypes().Where(IsProvider).ToList();
                            logger.LogInformation("Plugin {Plugin} {Version}: {Types}", assembly.GetName().Name,
                                assembly.GetName().Version, found.Count == 0 ? "no providers" : string.Join(", ", found.Select(t => t.Name)));
                            types.AddRange(found);
                        }
                        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException
                                                   or ReflectionTypeLoadException or TypeLoadException)
                        {
                            logger.LogError(ex, "Could not load plugin {Path}", path);
                        }
                    }
                }

                Loaded[folder] = types;
                return types;
            }
        }

        private static bool IsProvider(Type type) =>
            type is { IsClass: true, IsAbstract: false }
            && (typeof(IFileParser).IsAssignableFrom(type) || typeof(IOcrEngine).IsAssignableFrom(type)
                || typeof(IPdfRenderer).IsAssignableFrom(type));

        private sealed class PluginLoadContext(string path) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(path))
        {
            private readonly AssemblyDependencyResolver _resolver = new(path);

            protected override Assembly? Load(AssemblyName name)
            {
                // The indexer's own copy of the contracts, whatever version the plugin was built against,
                // so types like IFileParser are the same on both sides.
                var contracts = typeof(IFileParser).Assembly;
                if (name.Name == contracts.GetName().Name) return contracts;

                // Anything else the indexer already has (logging, configuration, the framework) comes
                // from the indexer too.
                if (Default.Assemblies.Any(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), name))) return null;

                var resolved = _resolver.ResolveAssemblyToPath(name);
                return resolved is null ? null : LoadFromAssemblyPath(resolved);
            }

            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                var resolved = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
                return resolved is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(resolved);
            }
        }
    }
}

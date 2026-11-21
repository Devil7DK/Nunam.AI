namespace Nunam.Core.Management
{
    /// <summary>One setting in effect on an indexer, and where its value came from.</summary>
    public record ConfigEntry(string Key, string? Value, ConfigSource Source);

    public enum ConfigSource
    {
        Default,

        /// <summary>The machine's own settings file or environment.</summary>
        Local,

        /// <summary>Set from the UI (<see cref="RemoteIndexerSettings"/>).</summary>
        Remote,
    }

    /// <summary>Whether the indexer can reach one of its watch paths.</summary>
    public record WatchPathReport(string Path, bool Available, string? Problem);
}

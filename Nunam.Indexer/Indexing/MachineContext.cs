namespace Nunam.Indexer.Indexing
{
    /// <summary>
    /// Singleton that holds the resolved identity of the machine this indexer runs on. Populated
    /// once by <see cref="MachineInitializer"/> at startup before any worker does real work.
    /// </summary>
    public class MachineContext
    {
        private Guid _machineId;

        /// <summary>The <c>machines</c> row id for this host. Throws if read before initialization.</summary>
        public Guid MachineId =>
            _machineId == Guid.Empty
                ? throw new InvalidOperationException("MachineContext accessed before MachineInitializer ran.")
                : _machineId;

        public string Name { get; private set; } = string.Empty;
        public string Hostname { get; private set; } = string.Empty;

        public void Set(Guid machineId, string name, string hostname)
        {
            _machineId = machineId;
            Name = name;
            Hostname = hostname;
        }
    }
}

using Microsoft.Extensions.Options;
using Nunam.Core.Agents;
using Nunam.Indexer.Management;
using Nunam.Indexer.Server;

namespace Nunam.Indexer.Indexing
{
    /// <summary>
    /// Tells the server this machine's hostname and name, and stamps <see cref="MachineContext"/>.
    /// Registered as the first hosted service so its awaited <see cref="StartAsync"/> completes before
    /// the scan/watch/parse workers begin.
    /// </summary>
    public class MachineInitializer(
        IndexerServer server,
        MachineContext context,
        IndexerControl control,
        IOptions<IndexerOptions> options,
        ILogger<MachineInitializer> logger) : IHostedService
    {
        private readonly IndexerOptions _options = options.Value;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var hostname = Environment.MachineName;
            var name = string.IsNullOrWhiteSpace(_options.MachineName) ? null : _options.MachineName.Trim();
            var machine = await server.HelloAsync(new IndexerHelloRequest(hostname, name), cancellationToken);

            context.Set(machine.MachineId, machine.Name, hostname);
            control.MachineId = machine.MachineId;
            logger.LogInformation("Signed in to the server as machine {Name} ({MachineId})", machine.Name, machine.MachineId);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

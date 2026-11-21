using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.Core.Data;
using Nunam.Core.Entities;
using Nunam.Indexer.Management;

namespace Nunam.Indexer.Indexing
{
    /// <summary>
    /// Upserts this host's <see cref="Machine"/> row and stamps <see cref="MachineContext"/> at
    /// startup. Registered as the first hosted service so its awaited <see cref="StartAsync"/>
    /// completes — and <c>MachineContext</c> is populated — before the scan/watch/parse workers begin.
    /// </summary>
    public class MachineInitializer(
        IServiceScopeFactory scopeFactory,
        MachineContext context,
        IndexerControl control,
        IOptions<IndexerOptions> options,
        ILogger<MachineInitializer> logger) : IHostedService
    {
        private readonly IndexerOptions _options = options.Value;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var hostname = Environment.MachineName;
            var name = string.IsNullOrWhiteSpace(_options.MachineName) ? hostname : _options.MachineName!;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NunamDbContext>();

            var machine = await db.Machines.FirstOrDefaultAsync(m => m.Hostname == hostname, cancellationToken);
            if (machine is null)
            {
                machine = new Machine { Name = name, Hostname = hostname, LastSeen = DateTimeOffset.UtcNow };
                db.Machines.Add(machine);
                logger.LogInformation("Registered new machine {Name} ({Hostname})", name, hostname);
            }
            else
            {
                machine.Name = name;
                machine.LastSeen = DateTimeOffset.UtcNow;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            {
                // Row-level security hides machines registered by other logins, so the lookup above missed it.
                logger.LogCritical("Machine {Hostname} is already registered under another database login. "
                    + "Use that machine's login, or have an admin delete the old machine entry", hostname);
                throw;
            }

            context.Set(machine.Id, machine.Name, hostname);
            control.MachineId = machine.Id;

            logger.LogInformation("Machine identity resolved: {MachineId}", machine.Id);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

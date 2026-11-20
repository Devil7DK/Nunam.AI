using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nunam.Core.Data;

namespace Nunam.API.Auth
{
    /// <summary>
    /// The one-time code that creates the first admin. It is only ever shown in the API's log, so
    /// creating the first account takes access to the server, not just the first visit to the UI.
    /// </summary>
    public class SetupCode
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public string Value { get; } = Generate();

        public bool Matches(string? candidate)
        {
            if (candidate is null) return false;
            var normalized = candidate.Replace("-", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(normalized), Encoding.ASCII.GetBytes(Value.Replace("-", string.Empty)));
        }

        /// <summary>16 characters from a 32-letter alphabet: 80 bits, grouped for reading off a log.</summary>
        private static string Generate()
        {
            var chars = RandomNumberGenerator.GetItems<char>(Alphabet, 16);
            return string.Join('-', Enumerable.Range(0, 4).Select(i => new string(chars, i * 4, 4)));
        }
    }

    /// <summary>Logs the setup code while no account exists. Waits for the database, which may start later.</summary>
    public class SetupCodeAnnouncer(IServiceScopeFactory scopeFactory, SetupCode code, ILogger<SetupCodeAnnouncer> logger)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var delay = TimeSpan.FromSeconds(2);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<NunamDbContext>();
                    if (!await db.Users.AnyAsync(stoppingToken))
                    {
                        logger.LogWarning("No accounts yet. Open the web UI and enter setup code {SetupCode} to create the first admin", code.Value);
                    }
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Database not ready; checking for accounts again in {Delay}", delay);
                    await Task.Delay(delay, stoppingToken);
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
                }
            }
        }
    }
}

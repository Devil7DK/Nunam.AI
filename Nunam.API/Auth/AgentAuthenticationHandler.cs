using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.API.Agents;
using Nunam.Core.Data;

namespace Nunam.API.Auth
{
    /// <summary>
    /// Signs an indexer in from its machine token, or the AI worker from its token, sent as
    /// <c>Authorization: Bearer</c>. Never reads cookies, so a browser can't be steered into these endpoints.
    /// </summary>
    public class AgentAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        NunamDbContext db,
        IOptions<AgentOptions> agentOptions) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
    {
        public const string SchemeName = "NunamAgent";
        public const string IndexerRole = "Indexer";
        public const string WorkerRole = "AIWorker";
        public const string MachineIdClaim = "nunam:machine_id";

        private static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(1);

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return AuthenticateResult.NoResult();
            var token = header["Bearer ".Length..].Trim();
            if (token.Length is 0 or > 128) return AuthenticateResult.Fail("Malformed token.");

            if (agentOptions.Value.WorkerToken is { Length: > 0 } workerToken
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(workerToken)))
            {
                return Success([new Claim(ClaimTypes.Name, "aiworker"), new Claim(ClaimTypes.Role, WorkerRole)]);
            }

            var hash = SessionTokens.Hash(token);
            var machine = await db.Machines
                .Where(m => m.TokenHash == hash)
                .Select(m => new { m.Id, m.Name, m.LastSeen })
                .FirstOrDefaultAsync(Context.RequestAborted);
            if (machine is null) return AuthenticateResult.Fail("Unknown machine token.");

            var now = DateTimeOffset.UtcNow;
            if (machine.LastSeen is null || now - machine.LastSeen > LastSeenResolution)
            {
                await db.Machines.Where(m => m.Id == machine.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(m => m.LastSeen, now), Context.RequestAborted);
            }

            return Success(
            [
                new Claim(ClaimTypes.Name, machine.Name),
                new Claim(ClaimTypes.Role, IndexerRole),
                new Claim(MachineIdClaim, machine.Id.ToString()),
            ]);
        }

        private static AuthenticateResult Success(Claim[] claims) =>
            AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }
}

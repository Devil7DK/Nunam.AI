using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.Core.Data;

namespace Nunam.API.Auth
{
    /// <summary>Signs a request in from the session cookie, against <c>user_sessions</c>.</summary>
    public class SessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        NunamDbContext db,
        IOptions<AuthOptions> authOptions) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
    {
        public const string SchemeName = "NunamSession";

        /// <summary>Saves a write per request; idle expiry is measured in days.</summary>
        private static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(5);

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Cookies.TryGetValue(SessionTokens.CookieName, out var token)
                || string.IsNullOrEmpty(token) || token.Length > 128)
            {
                return AuthenticateResult.NoResult();
            }

            var hash = SessionTokens.Hash(token);
            var now = DateTimeOffset.UtcNow;
            var idleSince = now - TimeSpan.FromDays(authOptions.Value.SessionIdleDays);

            var session = await db.UserSessions
                .Where(s => s.TokenHash == hash && s.ExpiresAt > now && s.LastSeen > idleSince && !s.User.Disabled)
                .Select(s => new
                {
                    s.Id, s.LastSeen, s.UserId, s.User.UserName, s.User.DisplayName, s.User.Role, s.User.MustChangePassword,
                })
                .FirstOrDefaultAsync(Context.RequestAborted);

            if (session is null) return AuthenticateResult.Fail("Unknown or expired session.");

            if (now - session.LastSeen > LastSeenResolution)
            {
                await db.UserSessions
                    .Where(s => s.Id == session.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastSeen, now), Context.RequestAborted);
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, session.UserId.ToString()),
                    new Claim(ClaimTypes.Name, session.UserName),
                    new Claim(CurrentUser.DisplayNameClaim, session.DisplayName),
                    new Claim(ClaimTypes.Role, session.Role.ToString()),
                    new Claim(CurrentUser.SessionIdClaim, session.Id.ToString()),
                    new Claim(CurrentUser.MustChangePasswordClaim, session.MustChangePassword ? "true" : "false"),
                ],
                SchemeName);

            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
        }

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

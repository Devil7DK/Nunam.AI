using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.Core.Data;
using Nunam.Core.Entities;

namespace Nunam.API.Auth
{
    public class SessionService(NunamDbContext db, IOptions<AuthOptions> options)
    {
        private readonly AuthOptions _options = options.Value;

        /// <summary>Starts a session for <paramref name="user"/>, saving pending changes, and sets its cookie.</summary>
        public async Task SignInAsync(HttpContext http, User user, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var token = SessionTokens.Create();
            var expiresAt = now + TimeSpan.FromDays(_options.SessionMaxDays);

            await PurgeExpiredAsync(user.Id, now, cancellationToken);

            db.UserSessions.Add(new UserSession
            {
                UserId = user.Id,
                TokenHash = SessionTokens.Hash(token),
                LastSeen = now,
                ExpiresAt = expiresAt,
                UserAgent = Truncate(http.Request.Headers.UserAgent.ToString(), 300),
                IpAddress = http.Connection.RemoteIpAddress?.ToString(),
            });
            await db.SaveChangesAsync(cancellationToken);

            http.Response.Cookies.Append(SessionTokens.CookieName, token, CookieOptions(http, expiresAt));
        }

        public void ClearCookie(HttpContext http) =>
            http.Response.Cookies.Delete(SessionTokens.CookieName, CookieOptions(http, null));

        public Task<int> EndAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
            db.UserSessions
                .Where(s => s.Id == sessionId && s.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);

        public Task<int> EndAllAsync(Guid userId, Guid? except, CancellationToken cancellationToken) =>
            db.UserSessions
                .Where(s => s.UserId == userId && s.Id != except)
                .ExecuteDeleteAsync(cancellationToken);

        private Task<int> PurgeExpiredAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var idleSince = now - TimeSpan.FromDays(_options.SessionIdleDays);
            return db.UserSessions
                .Where(s => s.UserId == userId && (s.ExpiresAt <= now || s.LastSeen <= idleSince))
                .ExecuteDeleteAsync(cancellationToken);
        }

        /// <summary>
        /// <c>Secure</c> follows the request, so it holds behind an HTTPS proxy and plain-HTTP LAN
        /// access still works. <c>SameSite=Strict</c> keeps other sites' pages from sending it.
        /// </summary>
        private static CookieOptions CookieOptions(HttpContext http, DateTimeOffset? expires) => new()
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = http.Request.IsHttps,
            Path = "/",
            Expires = expires,
            IsEssential = true,
        };

        private static string? Truncate(string value, int max) =>
            string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
    }
}

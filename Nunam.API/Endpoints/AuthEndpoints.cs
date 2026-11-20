using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.API.Auth;
using Nunam.API.Contracts;
using Nunam.Core.Data;
using Nunam.Core.Entities;

namespace Nunam.API.Endpoints
{
    public static class AuthEndpoints
    {
        private const string SignInFailed = "Wrong user name or password.";

        public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
        {
            var auth = app.MapGroup("/api/auth");
            auth.MapGet("/state", GetStateAsync).AllowAnonymous();
            auth.MapPost("/setup", SetupAsync).AllowAnonymous().RequireRateLimiting(Policies.PasswordAttempts);
            auth.MapPost("/sign-in", SignInAsync).AllowAnonymous().RequireRateLimiting(Policies.PasswordAttempts);
            auth.MapPost("/sign-out", SignOutAsync).RequireAuthorization(Policies.SignedIn);
            auth.MapPost("/password", ChangePasswordAsync)
                .RequireAuthorization(Policies.SignedIn)
                .RequireRateLimiting(Policies.PasswordAttempts);

            auth.MapGet("/sessions", ListSessionsAsync);
            auth.MapDelete("/sessions/{id:guid}", EndSessionAsync);
            auth.MapPost("/sessions/end-others", EndOtherSessionsAsync);
            return app;
        }

        private static async Task<AuthState> GetStateAsync(ClaimsPrincipal user, NunamDbContext db, CancellationToken cancellationToken)
        {
            var setupRequired = !await db.Users.AnyAsync(cancellationToken);
            return new AuthState(setupRequired, user.Identity?.IsAuthenticated == true ? ToInfo(user) : null);
        }

        private static async Task<Results<Ok<UserInfo>, ValidationProblem, ProblemHttpResult>> SetupAsync(
            SetupRequest request, HttpContext http, NunamDbContext db, SetupCode setupCode, Passwords passwords,
            SessionService sessions, ILogger<SetupCode> logger, CancellationToken cancellationToken)
        {
            if (!setupCode.Matches(request.Code))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["That setup code is wrong. It is in the API's log."] });
            }

            var errors = ValidateNewAccount(request.UserName, request.DisplayName, request.Password, passwords,
                out var userName, out var displayName, passwordField: "password");
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // Two setups racing with the code both see an empty table; the lock lets only the first through.
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext('nunam:setup'))", cancellationToken);
            if (await db.Users.AnyAsync(cancellationToken))
            {
                return TypedResults.Problem("Setup is already done. Sign in instead.", statusCode: StatusCodes.Status409Conflict);
            }

            var admin = new User
            {
                UserName = userName!,
                DisplayName = displayName!,
                PasswordHash = passwords.Hash(request.Password!),
                Role = UserRole.Admin,
                LastSignIn = DateTimeOffset.UtcNow,
            };
            db.Users.Add(admin);
            await sessions.SignInAsync(http, admin, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogWarning("Setup done: created admin {UserName}", admin.UserName);
            return TypedResults.Ok(ToInfo(admin));
        }

        private static async Task<Results<Ok<UserInfo>, ProblemHttpResult>> SignInAsync(
            SignInRequest request, HttpContext http, NunamDbContext db, Passwords passwords, SessionService sessions,
            IOptions<AuthOptions> options, ILogger<SessionService> logger, CancellationToken cancellationToken)
        {
            var password = request.Password ?? string.Empty;
            var userName = AccountRules.NormalizeUserName(request.UserName);
            var user = userName is null || password.Length > Passwords.MaxLength
                ? null
                : await db.Users.AsTracking().FirstOrDefaultAsync(u => u.UserName == userName, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            if (user?.LockedUntil > now)
            {
                passwords.Verify(null, password, out _);
                return Failed();
            }

            if (!passwords.Verify(user, password, out var needsRehash))
            {
                if (user is not null)
                {
                    user.FailedSignIns++;
                    if (user.FailedSignIns >= options.Value.MaxFailedSignIns)
                    {
                        user.LockedUntil = now.AddMinutes(options.Value.LockoutMinutes);
                        user.FailedSignIns = 0;
                        logger.LogWarning("Locked {UserName} for {Minutes} minutes after repeated failed sign-ins from {Address}",
                            user.UserName, options.Value.LockoutMinutes, http.Connection.RemoteIpAddress);
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }
                return Failed();
            }

            if (user!.Disabled) return Failed();

            user.FailedSignIns = 0;
            user.LockedUntil = null;
            user.LastSignIn = now;
            if (needsRehash) user.PasswordHash = passwords.Hash(password);

            await sessions.SignInAsync(http, user, cancellationToken);
            return TypedResults.Ok(ToInfo(user));

            static ProblemHttpResult Failed() =>
                TypedResults.Problem(SignInFailed, statusCode: StatusCodes.Status401Unauthorized);
        }

        private static async Task<NoContent> SignOutAsync(
            ClaimsPrincipal user, HttpContext http, SessionService sessions, bool? everywhere, CancellationToken cancellationToken)
        {
            if (everywhere == true) await sessions.EndAllAsync(user.UserId(), except: null, cancellationToken);
            else await sessions.EndAsync(user.UserId(), user.SessionId(), cancellationToken);

            sessions.ClearCookie(http);
            return TypedResults.NoContent();
        }

        private static async Task<Results<Ok<UserInfo>, ValidationProblem>> ChangePasswordAsync(
            ChangePasswordRequest request, ClaimsPrincipal principal, NunamDbContext db, Passwords passwords,
            SessionService sessions, CancellationToken cancellationToken)
        {
            var userId = principal.UserId();
            var user = await db.Users.AsTracking().FirstAsync(u => u.Id == userId, cancellationToken);

            if (!passwords.Verify(user, request.CurrentPassword ?? string.Empty, out _))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["currentPassword"] = ["That isn't your current password."] });
            }

            var problem = passwords.Problem(request.NewPassword)
                ?? (request.NewPassword == request.CurrentPassword ? "Choose a password different from the current one." : null);
            if (problem is not null)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = [problem] });
            }

            user.PasswordHash = passwords.Hash(request.NewPassword!);
            user.MustChangePassword = false;
            await db.SaveChangesAsync(cancellationToken);
            // Whoever knew the old password may hold a session; only the one changing it keeps theirs.
            await sessions.EndAllAsync(userId, except: principal.SessionId(), cancellationToken);

            return TypedResults.Ok(ToInfo(user));
        }

        private static async Task<List<SessionInfo>> ListSessionsAsync(
            ClaimsPrincipal user, NunamDbContext db, CancellationToken cancellationToken)
        {
            var userId = user.UserId();
            var current = user.SessionId();
            return await db.UserSessions
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.LastSeen)
                .Select(s => new SessionInfo(s.Id, s.Created, s.LastSeen, s.UserAgent, s.IpAddress, s.Id == current))
                .ToListAsync(cancellationToken);
        }

        private static async Task<Results<NoContent, NotFound>> EndSessionAsync(
            Guid id, ClaimsPrincipal user, SessionService sessions, CancellationToken cancellationToken) =>
            await sessions.EndAsync(user.UserId(), id, cancellationToken) > 0 ? TypedResults.NoContent() : TypedResults.NotFound();

        private static async Task<NoContent> EndOtherSessionsAsync(
            ClaimsPrincipal user, SessionService sessions, CancellationToken cancellationToken)
        {
            await sessions.EndAllAsync(user.UserId(), except: user.SessionId(), cancellationToken);
            return TypedResults.NoContent();
        }

        /// <summary>Checks the fields of a new account; outputs the normalized names when valid.</summary>
        internal static Dictionary<string, string[]> ValidateNewAccount(
            string? rawUserName, string? rawDisplayName, string? password, Passwords passwords,
            out string? userName, out string? displayName, string passwordField)
        {
            var errors = new Dictionary<string, string[]>();
            userName = AccountRules.NormalizeUserName(rawUserName);
            displayName = null;

            if (userName is null) errors["userName"] = [AccountRules.UserNameRule];
            else
            {
                displayName = AccountRules.NormalizeDisplayName(rawDisplayName, userName);
                if (displayName is null) errors["displayName"] = [$"Use at most {AccountRules.MaxDisplayNameLength} characters."];
            }

            if (passwords.Problem(password) is { } problem) errors[passwordField] = [problem];
            return errors;
        }

        internal static UserInfo ToInfo(User user) =>
            new(user.Id, user.UserName, user.DisplayName, user.Role, user.MustChangePassword);

        private static UserInfo ToInfo(ClaimsPrincipal user) => new(
            user.UserId(),
            user.FindFirstValue(ClaimTypes.Name)!,
            user.FindFirstValue(CurrentUser.DisplayNameClaim)!,
            user.IsAdmin() ? UserRole.Admin : UserRole.Member,
            user.MustChangePassword());
    }
}

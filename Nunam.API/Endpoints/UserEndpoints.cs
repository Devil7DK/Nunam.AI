using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Nunam.API.Auth;
using Nunam.API.Contracts;
using Nunam.Core.Data;
using Nunam.Core.Entities;

namespace Nunam.API.Endpoints
{
    public static class UserEndpoints
    {
        public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/users/directory", GetDirectoryAsync);

            var users = app.MapGroup("/api/users").RequireAuthorization(Policies.Admin);
            users.MapGet("/", ListAsync);
            users.MapPost("/", CreateAsync);
            users.MapPatch("/{id:guid}", UpdateAsync);
            users.MapPost("/{id:guid}/password", ResetPasswordAsync);
            users.MapDelete("/{id:guid}", DeleteAsync);
            return app;
        }

        private static async Task<List<UserDirectoryEntry>> GetDirectoryAsync(NunamDbContext db, CancellationToken cancellationToken) =>
            await db.Users
                .Where(u => !u.Disabled)
                .OrderBy(u => u.DisplayName)
                .Select(u => new UserDirectoryEntry(u.Id, u.UserName, u.DisplayName))
                .ToListAsync(cancellationToken);

        private static async Task<List<UserSummary>> ListAsync(NunamDbContext db, CancellationToken cancellationToken) =>
            await db.Users
                .OrderBy(u => u.UserName)
                .Select(u => ToSummary(u))
                .ToListAsync(cancellationToken);

        private static async Task<Results<Created<UserSummary>, ValidationProblem>> CreateAsync(
            CreateUserRequest request, NunamDbContext db, Passwords passwords, ILogger<SessionService> logger,
            ClaimsPrincipal admin, CancellationToken cancellationToken)
        {
            var errors = AuthEndpoints.ValidateNewAccount(request.UserName, request.DisplayName, request.TemporaryPassword,
                passwords, out var userName, out var displayName, passwordField: "temporaryPassword");

            var role = UserRole.Member;
            if (request.Role is not null && !EnumNames.TryParse(request.Role, out role))
            {
                errors["role"] = ["Use admin or member."];
            }

            if (userName is not null && await db.Users.AnyAsync(u => u.UserName == userName, cancellationToken))
            {
                errors["userName"] = ["That user name is taken."];
            }

            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

            var user = new User
            {
                UserName = userName!,
                DisplayName = displayName!,
                PasswordHash = passwords.Hash(request.TemporaryPassword!),
                Role = role,
                MustChangePassword = true,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("{Admin} created {Role} account {UserName}", admin.Identity!.Name, role, user.UserName);
            return TypedResults.Created($"/api/users/{user.Id}", ToSummary(user));
        }

        private static async Task<Results<Ok<UserSummary>, NotFound, ValidationProblem>> UpdateAsync(
            Guid id, UpdateUserRequest request, NunamDbContext db, SessionService sessions, ClaimsPrincipal admin,
            ILogger<SessionService> logger, CancellationToken cancellationToken)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await LockAdminChangesAsync(db, cancellationToken);
            var user = await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null) return TypedResults.NotFound();

            var errors = new Dictionary<string, string[]>();
            var isSelf = id == admin.UserId();

            if (request.DisplayName is not null)
            {
                var displayName = AccountRules.NormalizeDisplayName(request.DisplayName, user.UserName);
                if (displayName is null) errors["displayName"] = [$"Use at most {AccountRules.MaxDisplayNameLength} characters."];
                else user.DisplayName = displayName;
            }

            if (request.Role is not null)
            {
                if (!EnumNames.TryParse<UserRole>(request.Role, out var role)) errors["role"] = ["Use admin or member."];
                else if (role != user.Role && isSelf) errors["role"] = ["You can't change your own role."];
                else user.Role = role;
            }

            if (request.Disabled is { } disabled && disabled != user.Disabled)
            {
                if (isSelf) errors["disabled"] = ["You can't disable your own account."];
                else user.Disabled = disabled;
            }

            if (errors.Count == 0 && await WouldLeaveNoAdminAsync(db, user, cancellationToken))
            {
                errors["role"] = ["At least one enabled admin must remain."];
            }

            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (user.Disabled) await sessions.EndAllAsync(user.Id, except: null, cancellationToken);

            logger.LogInformation("{Admin} updated account {UserName} (role {Role}, disabled {Disabled})",
                admin.Identity!.Name, user.UserName, user.Role, user.Disabled);
            return TypedResults.Ok(ToSummary(user));
        }

        private static async Task<Results<NoContent, NotFound, ValidationProblem>> ResetPasswordAsync(
            Guid id, ResetPasswordRequest request, NunamDbContext db, Passwords passwords, SessionService sessions,
            ClaimsPrincipal admin, ILogger<SessionService> logger, CancellationToken cancellationToken)
        {
            if (id == admin.UserId())
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["temporaryPassword"] = ["Change your own password from your account menu."],
                });
            }

            var user = await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null) return TypedResults.NotFound();

            if (passwords.Problem(request.TemporaryPassword) is { } problem)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["temporaryPassword"] = [problem] });
            }

            user.PasswordHash = passwords.Hash(request.TemporaryPassword!);
            user.MustChangePassword = true;
            user.FailedSignIns = 0;
            user.LockedUntil = null;
            await db.SaveChangesAsync(cancellationToken);
            await sessions.EndAllAsync(user.Id, except: null, cancellationToken);

            logger.LogInformation("{Admin} reset the password of {UserName}", admin.Identity!.Name, user.UserName);
            return TypedResults.NoContent();
        }

        private static async Task<Results<NoContent, NotFound, ValidationProblem>> DeleteAsync(
            Guid id, NunamDbContext db, ClaimsPrincipal admin, ILogger<SessionService> logger, CancellationToken cancellationToken)
        {
            if (id == admin.UserId())
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["id"] = ["You can't delete your own account."] });
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await LockAdminChangesAsync(db, cancellationToken);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (user is null) return TypedResults.NotFound();

            if (user.Role == UserRole.Admin && !user.Disabled && !await AnotherAdminAsync(db, user.Id, cancellationToken))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["id"] = ["At least one enabled admin must remain."] });
            }

            await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("{Admin} deleted account {UserName}", admin.Identity!.Name, user.UserName);
            return TypedResults.NoContent();
        }

        /// <summary>
        /// Serializes changes that can remove an admin: two admins demoting or deleting each other at once
        /// would each see the other still enabled and together leave none.
        /// </summary>
        private static Task LockAdminChangesAsync(NunamDbContext db, CancellationToken cancellationToken) =>
            db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext('nunam:admins'))", cancellationToken);

        /// <summary>Whether saving <paramref name="user"/> as it now stands would leave no enabled admin.</summary>
        private static async Task<bool> WouldLeaveNoAdminAsync(NunamDbContext db, User user, CancellationToken cancellationToken)
        {
            if (user.Role == UserRole.Admin && !user.Disabled) return false;
            return !await AnotherAdminAsync(db, user.Id, cancellationToken);
        }

        private static Task<bool> AnotherAdminAsync(NunamDbContext db, Guid userId, CancellationToken cancellationToken) =>
            db.Users.AnyAsync(u => u.Id != userId && u.Role == UserRole.Admin && !u.Disabled, cancellationToken);

        private static UserSummary ToSummary(User u) =>
            new(u.Id, u.UserName, u.DisplayName, u.Role, u.Disabled, u.MustChangePassword, u.LastSignIn, u.Created);
    }
}

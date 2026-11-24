using Microsoft.AspNetCore.Authorization;
using Nunam.Core.Entities;

namespace Nunam.API.Auth
{
    public static class Policies
    {
        /// <summary>Signed in, even with a password still to change. Only for the endpoints that change it.</summary>
        public const string SignedIn = "SignedIn";

        public const string Admin = "Admin";

        /// <summary>A machine's indexer, signed in with its machine token.</summary>
        public const string Indexer = "Indexer";

        public const string AIWorker = "AIWorker";

        /// <summary>Rate-limiter policy for endpoints that take a password or setup code.</summary>
        public const string PasswordAttempts = "PasswordAttempts";

        /// <summary>Applied to every endpoint that doesn't say otherwise.</summary>
        public static AuthorizationPolicy Active { get; } = new AuthorizationPolicyBuilder(SessionAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => !ctx.User.MustChangePassword())
            .Build();

        public static void Add(AuthorizationOptions options)
        {
            options.DefaultPolicy = Active;
            options.FallbackPolicy = Active;
            options.AddPolicy(SignedIn, p => p
                .AddAuthenticationSchemes(SessionAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser());
            options.AddPolicy(Admin, p => p
                .Combine(Active)
                .RequireRole(nameof(UserRole.Admin)));
            options.AddPolicy(Indexer, p => p
                .AddAuthenticationSchemes(AgentAuthenticationHandler.SchemeName)
                .RequireRole(AgentAuthenticationHandler.IndexerRole));
            options.AddPolicy(AIWorker, p => p
                .AddAuthenticationSchemes(AgentAuthenticationHandler.SchemeName)
                .RequireRole(AgentAuthenticationHandler.WorkerRole));
        }
    }
}

namespace Nunam.API.Auth
{
    public class AuthOptions
    {
        public const string SectionName = "Auth";

        /// <summary>A session unused for this long ends.</summary>
        public int SessionIdleDays { get; set; } = 14;

        /// <summary>A session ends this long after sign-in, however active it stays.</summary>
        public int SessionMaxDays { get; set; } = 90;

        public int MaxFailedSignIns { get; set; } = 5;
        public int LockoutMinutes { get; set; } = 15;

        /// <summary>Sign-in and setup attempts allowed per client address per minute.</summary>
        public int SignInAttemptsPerMinute { get; set; } = 10;

        public int MinPasswordLength { get; set; } = 10;

        /// <summary>
        /// Honour <c>X-Forwarded-For</c>/<c>-Proto</c> from any sender. Only safe when nothing but the
        /// reverse proxy can reach the API, as in the Compose stack; otherwise a client could pick its
        /// own address and dodge the sign-in rate limit.
        /// </summary>
        public bool TrustForwardedHeaders { get; set; }
    }
}

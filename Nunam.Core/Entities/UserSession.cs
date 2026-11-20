namespace Nunam.Core.Entities
{
    public class UserSession : EntityBase
    {
        public Guid UserId { get; set; }
        public User User { get; set; } = default!;

        /// <summary>SHA-256 of the cookie's token, so a copy of the database can't be used to sign in.</summary>
        public byte[] TokenHash { get; set; } = default!;

        public DateTimeOffset LastSeen { get; set; }

        /// <summary>Hard limit, however active the session stays.</summary>
        public DateTimeOffset ExpiresAt { get; set; }

        public string? UserAgent { get; set; }
        public string? IpAddress { get; set; }
    }
}

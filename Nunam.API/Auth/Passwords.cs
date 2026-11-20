using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Nunam.Core.Entities;

namespace Nunam.API.Auth
{
    /// <summary>PBKDF2 hashing via ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/>.</summary>
    public class Passwords(IOptions<AuthOptions> options)
    {
        /// <summary>Hashing cost grows with length; nobody types more than this.</summary>
        public const int MaxLength = 256;

        private static readonly PasswordHasher<User> Hasher = new();

        /// <summary>Checked against when the user name is unknown, so both failures take as long.</summary>
        private static readonly string DummyHash = Hasher.HashPassword(null!, "not-a-real-password");

        private readonly int _minLength = options.Value.MinPasswordLength;

        public string Hash(string password) => Hasher.HashPassword(null!, password);

        /// <summary>Verifies <paramref name="password"/>; a <c>null</c> user is checked against a dummy hash.</summary>
        public bool Verify(User? user, string password, out bool needsRehash)
        {
            var result = Hasher.VerifyHashedPassword(null!, user?.PasswordHash ?? DummyHash, password);
            needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
            return user is not null && result != PasswordVerificationResult.Failed;
        }

        /// <summary>Why <paramref name="password"/> can't be used, or <c>null</c> when it can.</summary>
        public string? Problem(string? password) => password switch
        {
            null or "" => "Enter a password.",
            { Length: > MaxLength } => $"Use at most {MaxLength} characters.",
            _ when password.Length < _minLength => $"Use at least {_minLength} characters.",
            _ when string.IsNullOrWhiteSpace(password) => "A password can't be only spaces.",
            _ => null,
        };
    }
}

using System.Text.RegularExpressions;
using Nunam.Core.Entities;

namespace Nunam.API.Contracts
{
    public record AuthState(bool SetupRequired, UserInfo? User);

    public record UserInfo(Guid Id, string UserName, string DisplayName, UserRole Role, bool MustChangePassword);

    public record SetupRequest(string? Code, string? UserName, string? DisplayName, string? Password);

    public record SignInRequest(string? UserName, string? Password);

    public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    public record SessionInfo(Guid Id, DateTimeOffset Created, DateTimeOffset LastSeen, string? UserAgent, string? IpAddress, bool Current);

    public record UserSummary(
        Guid Id,
        string UserName,
        string DisplayName,
        UserRole Role,
        bool Disabled,
        bool MustChangePassword,
        DateTimeOffset? LastSignIn,
        DateTimeOffset Created);

    /// <summary>What anyone signed in may see of other accounts, to pick people to share a machine with.</summary>
    public record UserDirectoryEntry(Guid Id, string UserName, string DisplayName);

    public record CreateUserRequest(string? UserName, string? DisplayName, string? Role, string? TemporaryPassword);

    public record UpdateUserRequest(string? DisplayName, string? Role, bool? Disabled);

    public record ResetPasswordRequest(string? TemporaryPassword);

    public record MachineAccessEntry(Guid UserId, string UserName, string DisplayName, MachineAccessRole Role);

    public record SetMachineAccessRequest(string? Role);

    public static partial class AccountRules
    {
        public const int MaxDisplayNameLength = 64;

        /// <summary>The stored form of a user name, or <c>null</c> when it isn't a valid one.</summary>
        public static string? NormalizeUserName(string? userName)
        {
            var normalized = userName?.Trim().ToLowerInvariant();
            return normalized is not null && UserNamePattern().IsMatch(normalized) ? normalized : null;
        }

        public const string UserNameRule = "Use 3–32 letters, digits, dots, dashes or underscores, starting with a letter or digit.";

        public static string? NormalizeDisplayName(string? displayName, string fallback)
        {
            var trimmed = displayName?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return fallback;
            return trimmed.Length <= MaxDisplayNameLength ? trimmed : null;
        }

        [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,31}$")]
        private static partial Regex UserNamePattern();
    }
}

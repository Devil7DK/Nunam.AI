using System.Security.Claims;
using Nunam.Core.Entities;

namespace Nunam.API.Auth
{
    public static class CurrentUser
    {
        public const string SessionIdClaim = "nunam:sid";
        public const string DisplayNameClaim = "nunam:display_name";
        public const string MustChangePasswordClaim = "nunam:must_change_password";

        public static Guid UserId(this ClaimsPrincipal user) =>
            Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public static Guid SessionId(this ClaimsPrincipal user) =>
            Guid.Parse(user.FindFirstValue(SessionIdClaim)!);

        public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(nameof(UserRole.Admin));

        public static bool MustChangePassword(this ClaimsPrincipal user) =>
            user.FindFirstValue(MustChangePasswordClaim) == "true";
    }
}

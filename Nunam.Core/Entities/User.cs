namespace Nunam.Core.Entities
{
    public class User : EntityBase
    {
        /// <summary>Sign-in name, stored lower-case so lookups ignore case.</summary>
        public string UserName { get; set; } = default!;
        public string DisplayName { get; set; } = default!;
        public string PasswordHash { get; set; } = default!;
        public UserRole Role { get; set; }

        /// <summary>Set when an admin creates the account or resets its password.</summary>
        public bool MustChangePassword { get; set; }
        public bool Disabled { get; set; }

        public int FailedSignIns { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
        public DateTimeOffset? LastSignIn { get; set; }

        public ICollection<UserSession> Sessions { get; set; } = [];
        public ICollection<MachineAccess> MachineAccess { get; set; } = [];
    }
}

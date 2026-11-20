namespace Nunam.Core.Entities
{
    public enum UserRole
    {
        /// <summary>Sees the machines shared with them.</summary>
        Member,

        /// <summary>Sees every machine, manages accounts and assigns machine owners.</summary>
        Admin,
    }
}

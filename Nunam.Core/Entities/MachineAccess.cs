namespace Nunam.Core.Entities
{
    /// <summary>A user's access to one machine's files.</summary>
    public class MachineAccess : EntityBase
    {
        public Guid MachineId { get; set; }
        public Machine Machine { get; set; } = default!;

        public Guid UserId { get; set; }
        public User User { get; set; } = default!;

        public MachineAccessRole Role { get; set; }
    }

    public enum MachineAccessRole
    {
        /// <summary>Searches the machine's files.</summary>
        Member,

        /// <summary>Also shares the machine with others and changes their access.</summary>
        Owner,
    }
}

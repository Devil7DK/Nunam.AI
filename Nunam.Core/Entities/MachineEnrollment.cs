namespace Nunam.Core.Entities
{
    /// <summary>
    /// A one-time code an admin creates in the UI. An indexer trades it for its machine token, which
    /// adds a machine, or replaces the token of <see cref="MachineId"/> when set.
    /// </summary>
    public class MachineEnrollment : EntityBase
    {
        public byte[] CodeHash { get; set; } = default!;

        /// <summary>The machine to reconnect; null adds a new one.</summary>
        public Guid? MachineId { get; set; }
        public Machine? Machine { get; set; }

        /// <summary>Name for a new machine; the hostname when null.</summary>
        public string? Name { get; set; }

        public string CreatedBy { get; set; } = default!;
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset? UsedAt { get; set; }

        /// <summary>The machine the code enrolled, for the UI to follow.</summary>
        public Guid? EnrolledMachineId { get; set; }
    }
}

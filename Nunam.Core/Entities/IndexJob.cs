namespace Nunam.Core.Entities
{
    public class IndexJob : EntityBase
    {
        public Guid MachineId { get; set; }
        public Machine Machine { get; set; } = default!;

        public string Path { get; set; } = default!;
        public IndexJobKind Kind { get; set; } = IndexJobKind.Parse;
        public IndexJobStatus Status { get; set; } = IndexJobStatus.Pending;
        public IndexJobStage Stage { get; set; } = IndexJobStage.None;
        public string? Error { get; set; }
    }
}

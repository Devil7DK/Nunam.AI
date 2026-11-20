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

        /// <summary>How many times the job has been claimed; a worker that dies on it can't retry it forever.</summary>
        public int Attempts { get; set; }
    }
}

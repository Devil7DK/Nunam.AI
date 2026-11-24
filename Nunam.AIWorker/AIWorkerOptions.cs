namespace Nunam.AIWorker
{
    /// <summary>
    /// Tuning for the AI-side queue workers. Batch sizes follow the plan's per-kind strategy
    /// (Describe = one heavy image per call; Embed = many short texts per call).
    /// </summary>
    public class AIWorkerOptions
    {
        public const string SectionName = "AIWorker";

        /// <summary>Describe jobs claimed per poll. LLaVA-style models process one image at a time, so 1 is the norm.</summary>
        public int DescribeBatchSize { get; set; } = 1;

        /// <summary>Embed jobs claimed per poll. Embeddings batch well — many inputs go in a single request.</summary>
        public int EmbedBatchSize { get; set; } = 16;

        /// <summary>Max characters of <c>Content</c> folded into the embedding input (rough token budget).</summary>
        public int ContentCharLimit { get; set; } = 2000;

        /// <summary>Delay between polls when the queue is empty.</summary>
        public int IdlePollSeconds { get; set; } = 5;

        /// <summary>First backoff delay after the AI backend is found unreachable.</summary>
        public int MinBackoffSeconds { get; set; } = 5;

        /// <summary>Backoff ceiling — the GPU machine may be off for a long time; keep retrying patiently.</summary>
        public int MaxBackoffSeconds { get; set; } = 120;

        /// <summary>
        /// A job left in <c>Processing</c> longer than this is assumed abandoned (worker crashed or was
        /// killed) and is returned to the queue. Keep it well above the slowest describe call.
        /// </summary>
        public int StaleJobMinutes { get; set; } = 30;

        /// <summary>Claims per job before a job that keeps stopping the worker is failed.</summary>
        public int MaxAttempts { get; set; } = 3;
    }
}

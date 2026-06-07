using Pgvector;

namespace Nunam.Core.AI
{
    /// <summary>
    /// Domain-facing AI operations Nunam needs: text embeddings (for semantic search)
    /// and image description (for the Describe pipeline stage). Backed by
    /// <c>Microsoft.Extensions.AI</c>, so the concrete provider (Ollama / LM Studio /
    /// any OpenAI-compatible server) is selected at startup and invisible here.
    /// </summary>
    public interface IAIInferenceService
    {
        /// <summary>Embed a single text into a pgvector-ready vector.</summary>
        Task<Vector> EmbedAsync(string text, CancellationToken cancellationToken = default);

        /// <summary>
        /// Embed many texts in one request. Order of results matches the input.
        /// Used by the batched Embed worker.
        /// </summary>
        Task<IReadOnlyList<Vector>> EmbedAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken = default);

        /// <summary>
        /// Describe an image with the vision model. <paramref name="mediaType"/> is the
        /// MIME type (e.g. <c>image/png</c>). Pass <paramref name="prompt"/> to override
        /// the configured default.
        /// </summary>
        Task<string> DescribeImageAsync(
            ReadOnlyMemory<byte> image,
            string mediaType,
            string? prompt = null,
            CancellationToken cancellationToken = default);
    }
}

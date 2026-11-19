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
        /// <summary>Embed a search query, with the configured query prefix.</summary>
        Task<Vector> EmbedQueryAsync(string query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Embed many indexed texts in one request, with the configured document prefix. Order of
        /// results matches the input.
        /// </summary>
        Task<IReadOnlyList<Vector>> EmbedDocumentsAsync(
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

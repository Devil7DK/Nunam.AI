using Pgvector;

namespace Nunam.Core.AI
{
    /// <summary>
    /// Text embeddings for semantic search, from the one embedding model every vector comes from.
    /// Images are described by <see cref="ImageDescriber"/>, per provider. Backed by
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
    }
}

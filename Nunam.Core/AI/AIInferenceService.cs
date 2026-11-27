using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Pgvector;

namespace Nunam.Core.AI
{
    /// <summary>
    /// Thin wrapper over the <c>Microsoft.Extensions.AI</c> <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>,
    /// with the embedding model passed per request from <see cref="AIOptions"/>.
    /// </summary>
    public class AIInferenceService(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<AIOptions> options) : IAIInferenceService
    {
        private readonly AIOptions _options = options.Value;

        public async Task<Vector> EmbedQueryAsync(string query, CancellationToken cancellationToken = default)
        {
            var vectors = await EmbedAsync([_options.EmbeddingQueryPrefix + query], cancellationToken);
            return vectors[0];
        }

        public Task<IReadOnlyList<Vector>> EmbedDocumentsAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            texts.Count == 0
                ? Task.FromResult<IReadOnlyList<Vector>>([])
                : EmbedAsync([.. texts.Select(t => _options.EmbeddingDocumentPrefix + t)], cancellationToken);

        private async Task<IReadOnlyList<Vector>> EmbedAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            var embeddings = await embeddingGenerator.GenerateAsync(
                texts,
                new EmbeddingGenerationOptions { ModelId = _options.EmbeddingModel },
                cancellationToken);

            return [.. embeddings.Select(e => new Vector(e.Vector))];
        }
    }
}

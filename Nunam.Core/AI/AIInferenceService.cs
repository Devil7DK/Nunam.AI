using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Pgvector;

namespace Nunam.Core.AI
{
    /// <summary>
    /// Thin wrapper over the <c>Microsoft.Extensions.AI</c> <see cref="IChatClient"/> and
    /// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> abstractions. Model ids are
    /// passed per-request from <see cref="AIOptions"/>, so a single underlying client can
    /// serve both chat and embeddings regardless of provider.
    /// </summary>
    public class AIInferenceService(
        IChatClient chatClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<AIOptions> options) : IAIInferenceService
    {
        private readonly AIOptions _options = options.Value;

        public async Task<Vector> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            var embeddings = await embeddingGenerator.GenerateAsync(
                [text],
                new EmbeddingGenerationOptions { ModelId = _options.EmbeddingModel },
                cancellationToken);

            return new Vector(embeddings[0].Vector);
        }

        public async Task<IReadOnlyList<Vector>> EmbedAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            if (texts.Count == 0)
            {
                return [];
            }

            var embeddings = await embeddingGenerator.GenerateAsync(
                texts,
                new EmbeddingGenerationOptions { ModelId = _options.EmbeddingModel },
                cancellationToken);

            return [.. embeddings.Select(e => new Vector(e.Vector))];
        }

        public async Task<string> DescribeImageAsync(
            ReadOnlyMemory<byte> image,
            string mediaType,
            string? prompt = null,
            CancellationToken cancellationToken = default)
        {
            var message = new ChatMessage(ChatRole.User,
            [
                new TextContent(prompt ?? _options.DescribePrompt),
                new DataContent(image, mediaType),
            ]);

            var response = await chatClient.GetResponseAsync(
                [message],
                new ChatOptions
                {
                    ModelId = _options.ChatModel,
                    MaxOutputTokens = _options.MaxOutputTokens,
                },
                cancellationToken);

            return response.Text;
        }
    }
}

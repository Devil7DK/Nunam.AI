using System.ClientModel;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace Nunam.Core.AI
{
    /// <summary>What a describe call returned, with the tokens it was billed for.</summary>
    public record DescribeOutcome(string Text, long InputTokens, long OutputTokens);

    /// <summary>
    /// Describes images with one provider's vision model. Anthropic, Gemini and OpenRouter are all
    /// reached through their OpenAI-compatible endpoints.
    /// </summary>
    public sealed class ImageDescriber : IDisposable
    {
        private readonly IChatClient _client;
        private readonly string _model;
        private readonly string _prompt;
        private readonly int _maxOutputTokens;
        private readonly bool _disableReasoning;

        public ImageDescriber(AIProvider kind, string baseUrl, string? apiKey, string model, string? prompt,
            int maxOutputTokens, bool disableReasoning, TimeSpan timeout)
        {
            _model = model;
            _prompt = string.IsNullOrWhiteSpace(prompt) ? DescribePrompts.Default : prompt;
            _maxOutputTokens = maxOutputTokens;
            _disableReasoning = disableReasoning;
            _client = kind switch
            {
                AIProvider.OpenAICompatible => new OpenAIClient(
                    // Local OpenAI-compatible servers ignore the key, but the SDK requires one.
                    new ApiKeyCredential(string.IsNullOrWhiteSpace(apiKey) ? "not-needed" : apiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(baseUrl), NetworkTimeout = timeout })
                    .GetChatClient(model).AsIChatClient(),
                _ => new OllamaApiClient(new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = timeout }, model),
            };
        }

        public async Task<DescribeOutcome> DescribeAsync(ReadOnlyMemory<byte> image, string mediaType, CancellationToken cancellationToken)
        {
            var message = new ChatMessage(ChatRole.User, [new TextContent(_prompt), new DataContent(image, mediaType)]);
            var response = await _client.GetResponseAsync([message], new ChatOptions
            {
                ModelId = _model,
                MaxOutputTokens = _maxOutputTokens,
                Reasoning = _disableReasoning ? new ReasoningOptions { Effort = ReasoningEffort.None } : null,
            }, cancellationToken);

            var text = response.Text.Trim();
            if (text.Length == 0)
            {
                throw new EmptyDescriptionException(
                    $"{_model} returned no text (finish reason: {response.FinishReason?.ToString() ?? "none"}).");
            }
            return new DescribeOutcome(text, response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);
        }

        public void Dispose() => _client.Dispose();
    }

    /// <summary>The model answered but wrote nothing, typically after spending its whole output on reasoning.</summary>
    public class EmptyDescriptionException(string message) : Exception(message);
}

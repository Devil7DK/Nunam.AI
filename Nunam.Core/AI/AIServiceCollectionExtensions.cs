using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;
using OpenAI;

namespace Nunam.Core.AI
{
    public static class AIServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="IAIInferenceService"/> plus the underlying
        /// <see cref="IChatClient"/> and <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>,
        /// choosing the backend from the bound <see cref="AIOptions.Provider"/>. Switching
        /// between Ollama and an OpenAI-compatible server (LM Studio) is config-only.
        /// </summary>
        public static IServiceCollection AddNunamAI(
            this IServiceCollection services, IConfiguration configuration)
        {
            var section = configuration.GetSection(AIOptions.SectionName);
            services.Configure<AIOptions>(section);
            var options = section.Get<AIOptions>() ?? new AIOptions();

            switch (options.Provider)
            {
                case AIProvider.OpenAICompatible:
                    // LM Studio (and other OpenAI-compatible servers) ignore the key but the
                    // SDK requires a non-empty credential.
                    var openAI = new OpenAIClient(
                        new ApiKeyCredential(
                            string.IsNullOrWhiteSpace(options.ApiKey) ? "not-needed" : options.ApiKey),
                        new OpenAIClientOptions { Endpoint = new Uri(options.BaseUrl) });

                    services.AddChatClient(
                        openAI.GetChatClient(options.ChatModel).AsIChatClient());
                    services.AddEmbeddingGenerator(
                        openAI.GetEmbeddingClient(options.EmbeddingModel).AsIEmbeddingGenerator());
                    break;

                case AIProvider.Ollama:
                default:
                    // One client serves both roles; the model is selected per request.
                    var ollama = new OllamaApiClient(new Uri(options.BaseUrl));
                    services.AddChatClient(ollama);
                    services.AddEmbeddingGenerator(ollama);
                    break;
            }

            services.AddSingleton<IAIInferenceService, AIInferenceService>();
            return services;
        }
    }
}

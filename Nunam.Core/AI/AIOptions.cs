namespace Nunam.Core.AI
{
    /// <summary>
    /// Which inference backend to talk to. Both expose models for chat (vision)
    /// and embeddings; they differ only in wire protocol and default port.
    /// </summary>
    public enum AIProvider
    {
        /// <summary>Ollama native API (OllamaSharp). Default port 11434.</summary>
        Ollama,

        /// <summary>
        /// Any OpenAI-compatible <c>/v1</c> server — LM Studio (port 1234),
        /// or Ollama's own <c>/v1</c> shim. Uses the OpenAI SDK with a custom endpoint.
        /// </summary>
        OpenAICompatible,
    }

    /// <summary>
    /// Binds the <c>AI</c> configuration section. Provider-neutral: the same shape
    /// drives both the Ollama and OpenAI-compatible (LM Studio) backends.
    /// </summary>
    public class AIOptions
    {
        public const string SectionName = "AI";

        public AIProvider Provider { get; set; } = AIProvider.Ollama;

        /// <summary>
        /// Base address of the server. Ollama: <c>http://gaming-pc.lan:11434</c>.
        /// LM Studio / OpenAI-compatible: <c>http://gaming-pc.lan:1234/v1</c>.
        /// </summary>
        public string BaseUrl { get; set; } = "http://localhost:11434";

        /// <summary>Ignored by Ollama and LM Studio, kept for real OpenAI-compatible gateways.</summary>
        public string? ApiKey { get; set; }

        /// <summary>Vision model used to describe images and rendered pages.</summary>
        public string ChatModel { get; set; } = "gemma4:12b-it-qat";

        /// <summary>
        /// Turns off "thinking" for reasoning-capable vision models: a description needs no reasoning,
        /// and the hidden reasoning tokens count against <see cref="MaxOutputTokens"/>. Disable for a
        /// server that rejects the reasoning parameter.
        /// </summary>
        public bool DisableReasoning { get; set; } = true;

        /// <summary>
        /// Upper bound on tokens generated per description. Sized generously because
        /// reasoning-style vision models spend part of the budget "thinking" before
        /// emitting any text — too low a cap returns empty/truncated descriptions.
        /// </summary>
        public int MaxOutputTokens { get; set; } = 1024;

        /// <summary>
        /// Embedding model. bge-m3 is multilingual, so a Tamil query finds an English document and the
        /// other way round.
        /// </summary>
        public string EmbeddingModel { get; set; } = "bge-m3";

        /// <summary>Embedding dimensionality; must match the <c>vector(N)</c> column. bge-m3 = 1024.</summary>
        public int EmbeddingDimensions { get; set; } = 1024;

        /// <summary>
        /// Prepended to indexed text before embedding, for models trained with task prefixes
        /// (nomic-embed-text: <c>search_document: </c>); bge-m3 uses none. Changing this invalidates
        /// every stored vector: re-embed all files.
        /// </summary>
        public string EmbeddingDocumentPrefix { get; set; } = "";

        /// <summary>Prepended to search queries (nomic-embed-text: <c>search_query: </c>). Pairs with <see cref="EmbeddingDocumentPrefix"/>.</summary>
        public string EmbeddingQueryPrefix { get; set; } = "";

        /// <summary>Default instruction sent alongside an image when no per-call prompt is given.</summary>
        public string DescribePrompt { get; set; } =
            "Describe this image in detail for a search index. Mention any visible text, " +
            "objects, people, scene, and notable attributes. Be concise but thorough.";
    }
}

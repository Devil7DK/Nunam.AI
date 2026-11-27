using Nunam.Core.AI;
using Nunam.Core.Entities;

namespace Nunam.API.Contracts
{
    /// <summary>The AI page: the embedding model (set in the server's environment) and the describe providers.</summary>
    /// <param name="SecretsConfigured">Whether API keys can be stored: the server has its secrets key.</param>
    public record AISettings(
        EmbeddingSettings Embedding, bool SecretsConfigured, string DefaultPrompt, string ShortPrompt,
        List<DescribeProviderSummary> Providers, DescribeQueue Queue, List<DescribedByCount> DescribedBy);

    public record EmbeddingSettings(AIProvider Provider, string BaseUrl, string Model, int Dimensions);

    /// <param name="Status">ok, disabled, budget (spent for this period) or error (the last request failed).</param>
    public record DescribeProviderSummary(
        Guid Id, string Name, AIProvider Kind, string BaseUrl, bool HasApiKey, string Model, string? Prompt,
        int MaxOutputTokens, bool DisableReasoning, int Priority, int Concurrency, bool TakesOverflow, bool Enabled,
        decimal InputPricePerMillion, decimal OutputPricePerMillion, decimal? BudgetUsd, BudgetPeriod BudgetPeriod,
        decimal SpentThisPeriod, decimal SpentTotal, int RequestsToday, int FailuresToday,
        DateTimeOffset? LastUsedAt, string? LastError, DateTimeOffset? LastErrorAt, string Status);

    public record DescribeQueue(int Pending, int Processing, int Done, int Failed);

    public record DescribedByCount(string? Model, string? Provider, int Files);

    /// <param name="ApiKey"><c>null</c> keeps the stored key; an empty string removes it.</param>
    /// <param name="Prompt"><c>null</c> or empty for the default prompt.</param>
    public record DescribeProviderRequest(
        string Name, string Kind, string BaseUrl, string? ApiKey, string Model, string? Prompt,
        int MaxOutputTokens, bool DisableReasoning, int Priority, int Concurrency, bool TakesOverflow, bool Enabled,
        decimal InputPricePerMillion, decimal OutputPricePerMillion, decimal? BudgetUsd, string BudgetPeriod);

    /// <summary>One image described with a provider, picked at random from the index, and what it cost.</summary>
    public record DescribeTestResult(
        string FileName, string Path, string? Text, string? Error, long InputTokens, long OutputTokens, decimal CostUsd, double Seconds);
}

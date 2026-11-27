using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.Core.AI;
using Nunam.Core.Data;
using Nunam.Core.Entities;

namespace Nunam.API.Services
{
    /// <summary>What a provider has spent, for its budget and for the AI settings page.</summary>
    public record ProviderSpend(decimal ThisPeriod, decimal Total, int RequestsToday, int FailuresToday);

    public static class DescribeProviders
    {
        public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

        public static async Task<Dictionary<Guid, ProviderSpend>> SpendAsync(NunamDbContext db, CancellationToken cancellationToken)
        {
            var today = Today;
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            var rows = await db.DescribeUsage
                .GroupBy(u => u.ProviderId)
                .Select(g => new
                {
                    ProviderId = g.Key,
                    Month = g.Where(u => u.Day >= monthStart).Sum(u => u.CostUsd),
                    Total = g.Sum(u => u.CostUsd),
                    RequestsToday = g.Where(u => u.Day == today).Sum(u => u.Requests),
                    FailuresToday = g.Where(u => u.Day == today).Sum(u => u.Failures),
                })
                .ToListAsync(cancellationToken);
            var periods = await db.DescribeProviders.ToDictionaryAsync(p => p.Id, p => p.BudgetPeriod, cancellationToken);
            return rows.ToDictionary(r => r.ProviderId, r => new ProviderSpend(
                periods.GetValueOrDefault(r.ProviderId) == BudgetPeriod.Total ? r.Total : r.Month,
                r.Total, r.RequestsToday, r.FailuresToday));
        }

        /// <summary><c>null</c> for a provider without a budget.</summary>
        public static decimal? Remaining(DescribeProvider provider, ProviderSpend? spend) =>
            provider.BudgetUsd is { } budget ? Math.Max(0, budget - (spend?.ThisPeriod ?? 0)) : null;

        public static decimal Cost(DescribeProvider provider, long inputTokens, long outputTokens) =>
            (inputTokens * provider.InputPricePerMillion + outputTokens * provider.OutputPricePerMillion) / 1_000_000m;

        /// <summary>Adds one request to the provider's usage for today.</summary>
        public static Task RecordAsync(NunamDbContext db, DescribeProvider provider, long inputTokens, long outputTokens,
            bool failed, CancellationToken cancellationToken) =>
            db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO describe_usage (provider_id, day, requests, failures, input_tokens, output_tokens, cost_usd)
                VALUES ({provider.Id}, {Today}, 1, {(failed ? 1 : 0)}, {inputTokens}, {outputTokens}, {Cost(provider, inputTokens, outputTokens)})
                ON CONFLICT (provider_id, day) DO UPDATE SET
                    requests = describe_usage.requests + 1,
                    failures = describe_usage.failures + excluded.failures,
                    input_tokens = describe_usage.input_tokens + excluded.input_tokens,
                    output_tokens = describe_usage.output_tokens + excluded.output_tokens,
                    cost_usd = describe_usage.cost_usd + excluded.cost_usd
                """, cancellationToken);
    }

    /// <summary>
    /// On a server without describe providers, creates one from the <c>AI</c> settings, so the model
    /// that was describing images keeps doing so, and credits it with the descriptions it already wrote.
    /// </summary>
    public sealed class DescribeProviderSeeder(IServiceScopeFactory scopes, IOptions<AIOptions> ai, ILogger<DescribeProviderSeeder> logger)
        : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NunamDbContext>();
            if (await db.DescribeProviders.AnyAsync(cancellationToken)) return;

            var options = ai.Value;
            var provider = new DescribeProvider
            {
                Name = options.Provider == AIProvider.Ollama ? "Local Ollama" : "Local model",
                Kind = options.Provider,
                BaseUrl = options.BaseUrl,
                Model = options.ChatModel,
                Prompt = options.DescribePrompt == DescribePrompts.Default ? null : options.DescribePrompt,
                MaxOutputTokens = options.MaxOutputTokens,
                DisableReasoning = options.DisableReasoning,
            };
            db.DescribeProviders.Add(provider);
            await db.SaveChangesAsync(cancellationToken);

            var credited = await db.Files
                .Where(f => f.Description != null && f.DescriptionModel == null)
                .ExecuteUpdateAsync(f => f
                    .SetProperty(x => x.DescriptionModel, provider.Model)
                    .SetProperty(x => x.DescriptionProvider, provider.Name), cancellationToken);
            logger.LogInformation("Created describe provider {Name} ({Model}) from the AI settings; credited it with {Count} description(s)",
                provider.Name, provider.Model, credited);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

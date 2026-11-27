using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.API.Auth;
using Nunam.API.Contracts;
using Nunam.API.Services;
using Nunam.Core.AI;
using Nunam.Core.Data;
using Nunam.Core.Entities;
using Nunam.Core.Files;

namespace Nunam.API.Endpoints
{
    /// <summary>The AI settings page: describe providers, their spend, and the embedding model in use.</summary>
    public static class AIEndpoints
    {
        public static IEndpointRouteBuilder MapAIEndpoints(this IEndpointRouteBuilder app)
        {
            var ai = app.MapGroup("/api/ai").RequireAuthorization(Policies.Admin);
            ai.MapGet("/", GetAsync);
            ai.MapPost("/providers", CreateAsync);
            ai.MapPut("/providers/{id:guid}", UpdateAsync);
            ai.MapDelete("/providers/{id:guid}", DeleteAsync);
            ai.MapPost("/providers/{id:guid}/test", TestAsync);
            return app;
        }

        private static async Task<AISettings> GetAsync(
            NunamDbContext db, IOptions<AIOptions> ai, SecretProtector secrets, CancellationToken cancellationToken)
        {
            var spend = await DescribeProviders.SpendAsync(db, cancellationToken);
            var providers = await db.DescribeProviders.OrderBy(p => p.Priority).ThenBy(p => p.Name).ToListAsync(cancellationToken);

            var jobs = await db.IndexJobs
                .Where(j => j.Kind == IndexJobKind.Describe)
                .GroupBy(j => j.Status)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

            var describedBy = await db.Files
                .Where(f => f.Description != null)
                .GroupBy(f => new { f.DescriptionModel, f.DescriptionProvider })
                .Select(g => new DescribedByCount(g.Key.DescriptionModel, g.Key.DescriptionProvider, g.Count()))
                .ToListAsync(cancellationToken);

            var options = ai.Value;
            return new AISettings(
                new EmbeddingSettings(options.Provider, options.BaseUrl, options.EmbeddingModel, options.EmbeddingDimensions),
                secrets.IsConfigured, DescribePrompts.Default, DescribePrompts.Short,
                [.. providers.Select(p => Summarize(p, spend.GetValueOrDefault(p.Id)))],
                new DescribeQueue(jobs.GetValueOrDefault(IndexJobStatus.Pending), jobs.GetValueOrDefault(IndexJobStatus.Processing),
                    jobs.GetValueOrDefault(IndexJobStatus.Done), jobs.GetValueOrDefault(IndexJobStatus.Failed)),
                [.. describedBy.OrderByDescending(d => d.Files)]);
        }

        private static DescribeProviderSummary Summarize(DescribeProvider p, ProviderSpend? spend)
        {
            var status = !p.Enabled ? "disabled"
                : DescribeProviders.Remaining(p, spend) is 0 ? "budget"
                : p.LastErrorAt is { } failed && (p.LastUsedAt is null || failed > p.LastUsedAt) ? "error"
                : "ok";
            return new DescribeProviderSummary(
                p.Id, p.Name, p.Kind, p.BaseUrl, p.ApiKeyProtected is not null, p.Model, p.Prompt,
                p.MaxOutputTokens, p.DisableReasoning, p.Priority, p.Concurrency, p.TakesOverflow, p.Enabled,
                p.InputPricePerMillion, p.OutputPricePerMillion, p.BudgetUsd, p.BudgetPeriod,
                spend?.ThisPeriod ?? 0, spend?.Total ?? 0, spend?.RequestsToday ?? 0, spend?.FailuresToday ?? 0,
                p.LastUsedAt, p.LastError, p.LastErrorAt, status);
        }

        private static async Task<Results<Created<DescribeProviderSummary>, ValidationProblem>> CreateAsync(
            DescribeProviderRequest request, NunamDbContext db, SecretProtector secrets, CancellationToken cancellationToken)
        {
            var provider = new DescribeProvider();
            var errors = await ApplyAsync(provider, request, db, secrets, cancellationToken);
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

            db.DescribeProviders.Add(provider);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Created($"/api/ai/providers/{provider.Id}", Summarize(provider, null));
        }

        private static async Task<Results<Ok<DescribeProviderSummary>, NotFound, ValidationProblem>> UpdateAsync(
            Guid id, DescribeProviderRequest request, NunamDbContext db, SecretProtector secrets, CancellationToken cancellationToken)
        {
            var provider = await db.DescribeProviders.AsTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (provider is null) return TypedResults.NotFound();

            var errors = await ApplyAsync(provider, request, db, secrets, cancellationToken);
            if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

            await db.SaveChangesAsync(cancellationToken);
            var spend = await DescribeProviders.SpendAsync(db, cancellationToken);
            return TypedResults.Ok(Summarize(provider, spend.GetValueOrDefault(provider.Id)));
        }

        private static async Task<Results<NoContent, NotFound>> DeleteAsync(Guid id, NunamDbContext db, CancellationToken cancellationToken)
        {
            var deleted = await db.DescribeProviders.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);
            return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
        }

        /// <summary>
        /// Describes one random indexed image with the provider as saved, through the API rather than the
        /// AI worker, and counts it toward the provider's spend.
        /// </summary>
        private static async Task<Results<Ok<DescribeTestResult>, NotFound, ValidationProblem>> TestAsync(
            Guid id, NunamDbContext db, SecretProtector secrets, CancellationToken cancellationToken)
        {
            var provider = await db.DescribeProviders.AsTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (provider is null) return TypedResults.NotFound();

            var sample = await db.Files
                .Where(f => f.PreviewImage != null)
                .OrderBy(_ => EF.Functions.Random())
                .Select(f => new { f.FileName, f.Path, f.Extension, f.PreviewImage })
                .FirstOrDefaultAsync(cancellationToken);
            if (sample is null)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["provider"] = ["No indexed file has a preview to describe yet."] });
            }

            string? apiKey = null;
            if (provider.ApiKeyProtected is { } stored && (apiKey = secrets.Unprotect(stored)) is null)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["apiKey"] = ["The stored API key can't be decrypted with this server's secrets key. Enter it again."],
                });
            }

            var watch = Stopwatch.StartNew();
            using var describer = new ImageDescriber(provider.Kind, provider.BaseUrl, apiKey, provider.Model, provider.Prompt,
                provider.MaxOutputTokens, provider.DisableReasoning, TimeSpan.FromSeconds(90));
            try
            {
                var outcome = await describer.DescribeAsync(sample.PreviewImage!,
                    MediaTypes.Detect(sample.PreviewImage!, sample.Extension ?? Path.GetExtension(sample.FileName)), cancellationToken);
                await DescribeProviders.RecordAsync(db, provider, outcome.InputTokens, outcome.OutputTokens, failed: false, cancellationToken);
                provider.LastUsedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return TypedResults.Ok(new DescribeTestResult(sample.FileName, sample.Path, outcome.Text, null,
                    outcome.InputTokens, outcome.OutputTokens, DescribeProviders.Cost(provider, outcome.InputTokens, outcome.OutputTokens),
                    watch.Elapsed.TotalSeconds));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                provider.LastError = Truncate(ex.Message, 1000);
                provider.LastErrorAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
                return TypedResults.Ok(new DescribeTestResult(sample.FileName, sample.Path, null, ex.Message, 0, 0, 0, watch.Elapsed.TotalSeconds));
            }
        }

        private static async Task<Dictionary<string, string[]>> ApplyAsync(
            DescribeProvider provider, DescribeProviderRequest request, NunamDbContext db, SecretProtector secrets,
            CancellationToken cancellationToken)
        {
            var errors = new Dictionary<string, string[]>();
            var name = request.Name?.Trim() ?? "";
            if (name.Length is 0 or > 100) errors["name"] = ["Use 1 to 100 characters."];
            else if (await db.DescribeProviders.AnyAsync(p => p.Name == name && p.Id != provider.Id, cancellationToken))
            {
                errors["name"] = ["Another provider has that name."];
            }

            if (!EnumNames.TryParse<AIProvider>(request.Kind, out var kind)) errors["kind"] = ["Use ollama or openAICompatible."];

            var baseUrl = request.BaseUrl?.Trim() ?? "";
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                errors["baseUrl"] = ["Use an http or https address."];
            }

            var model = request.Model?.Trim() ?? "";
            if (model.Length is 0 or > 200) errors["model"] = ["Use 1 to 200 characters."];
            if (request.Prompt is { Length: > 4000 }) errors["prompt"] = ["Use at most 4000 characters."];
            if (request.MaxOutputTokens is < 16 or > 16384) errors["maxOutputTokens"] = ["Use 16 to 16384."];
            if (request.Priority is < 1 or > 100) errors["priority"] = ["Use 1 to 100."];
            if (request.Concurrency is < 1 or > 32) errors["concurrency"] = ["Use 1 to 32."];
            if (request.InputPricePerMillion is < 0 or > 1000) errors["inputPricePerMillion"] = ["Use 0 to 1000."];
            if (request.OutputPricePerMillion is < 0 or > 1000) errors["outputPricePerMillion"] = ["Use 0 to 1000."];
            if (request.BudgetUsd is <= 0 or > 1_000_000) errors["budgetUsd"] = ["Use more than 0, or leave it empty for no budget."];
            if (!EnumNames.TryParse<BudgetPeriod>(request.BudgetPeriod, out var period)) errors["budgetPeriod"] = ["Use total or monthly."];

            if (request.ApiKey is { Length: > 0 } && !secrets.IsConfigured)
            {
                errors["apiKey"] = [$"The server has no secrets key to store API keys with. Set NUNAM_SECRETS_KEY in its .env (openssl rand -base64 32) and restart it."];
            }
            else if (request.ApiKey is { Length: > 1000 })
            {
                errors["apiKey"] = ["Use at most 1000 characters."];
            }

            if (errors.Count > 0) return errors;

            provider.Name = name;
            provider.Kind = kind;
            provider.BaseUrl = baseUrl;
            provider.Model = model;
            provider.Prompt = string.IsNullOrWhiteSpace(request.Prompt) ? null : request.Prompt.Trim();
            provider.MaxOutputTokens = request.MaxOutputTokens;
            provider.DisableReasoning = request.DisableReasoning;
            provider.Priority = request.Priority;
            provider.Concurrency = request.Concurrency;
            provider.TakesOverflow = request.TakesOverflow;
            provider.Enabled = request.Enabled;
            provider.InputPricePerMillion = request.InputPricePerMillion;
            provider.OutputPricePerMillion = request.OutputPricePerMillion;
            provider.BudgetUsd = request.BudgetUsd;
            provider.BudgetPeriod = period;
            if (request.ApiKey is not null)
            {
                provider.ApiKeyProtected = request.ApiKey.Length == 0 ? null : secrets.Protect(request.ApiKey.Trim());
            }
            // A changed setup deserves a fresh start: the old error may not apply.
            provider.LastError = null;
            provider.LastErrorAt = null;
            return errors;
        }

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    }
}

using Nunam.Core.AI;

namespace Nunam.Core.Entities
{
    /// <summary>
    /// A vision model the AI worker can describe images with. Several can be set up: the worker uses
    /// the lowest <see cref="Priority"/> number available, and a lower-priority one steps in when those
    /// are disabled, failing or over budget, or, with <see cref="TakesOverflow"/>, when they are busy.
    /// </summary>
    public class DescribeProvider : EntityBase
    {
        public string Name { get; set; } = default!;
        public AIProvider Kind { get; set; }
        public string BaseUrl { get; set; } = default!;

        /// <summary>Encrypted with the server's secrets key; never returned to browsers.</summary>
        public string? ApiKeyProtected { get; set; }

        public string Model { get; set; } = default!;

        /// <summary>Instruction sent with each image; <c>null</c> for <see cref="DescribePrompts.Default"/>.</summary>
        public string? Prompt { get; set; }

        public int MaxOutputTokens { get; set; } = 1024;
        public bool DisableReasoning { get; set; } = true;

        /// <summary>1 is tried first; equal numbers share the queue.</summary>
        public int Priority { get; set; } = 1;

        /// <summary>Images described at the same time.</summary>
        public int Concurrency { get; set; } = 1;

        /// <summary>Also takes jobs while every higher-priority provider is busy, not only while they are unavailable.</summary>
        public bool TakesOverflow { get; set; }

        public bool Enabled { get; set; } = true;

        /// <summary>US dollars per million tokens, for the spend that budgets count; 0 for a local model.</summary>
        public decimal InputPricePerMillion { get; set; }
        public decimal OutputPricePerMillion { get; set; }

        /// <summary>Spend after which the provider stops for the rest of <see cref="BudgetPeriod"/>; <c>null</c> for none.</summary>
        public decimal? BudgetUsd { get; set; }
        public BudgetPeriod BudgetPeriod { get; set; } = BudgetPeriod.Monthly;

        public DateTimeOffset? LastUsedAt { get; set; }
        public string? LastError { get; set; }
        public DateTimeOffset? LastErrorAt { get; set; }
    }

    public enum BudgetPeriod
    {
        /// <summary>Everything the provider has spent.</summary>
        Total,

        /// <summary>The current calendar month, in UTC.</summary>
        Monthly,
    }
}

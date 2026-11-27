namespace Nunam.Core.Entities
{
    /// <summary>One provider's requests, tokens and spend on one day (UTC).</summary>
    public class DescribeUsage
    {
        public Guid ProviderId { get; set; }
        public DescribeProvider Provider { get; set; } = default!;
        public DateOnly Day { get; set; }

        public int Requests { get; set; }
        public int Failures { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public decimal CostUsd { get; set; }
    }
}

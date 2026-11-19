namespace Nunam.API
{
    public class SearchOptions
    {
        public const string SectionName = "Search";

        /// <summary>
        /// How many top hits each side (keyword, semantic) contributes before they're fused. Pages past
        /// this depth come back empty, so it also caps <c>total</c>.
        /// </summary>
        public int CandidatesPerSide { get; set; } = 200;

        /// <summary>
        /// The embedding backend is on another machine that may be off. Past this, hybrid search
        /// answers from keywords alone rather than hanging.
        /// </summary>
        public int QueryEmbeddingTimeoutSeconds { get; set; } = 10;

        public int MaxLimit { get; set; } = 100;

        /// <summary>
        /// Semantic hits are kept only within this distance of the best hit's cosine similarity.
        /// Similarity has no absolute meaning across queries (a right answer can score 0.54 for one
        /// query while noise scores 0.59 for another), but within one query the relevant files sit
        /// close to the top and the rest trail off.
        /// </summary>
        public double SemanticRelativeMargin { get; set; } = 0.08;
    }

    public class CorsOptions
    {
        public const string SectionName = "Cors";

        /// <summary>Origins allowed to call the API from a browser. <c>*</c> allows any origin.</summary>
        public string[] AllowedOrigins { get; set; } = [];
    }
}

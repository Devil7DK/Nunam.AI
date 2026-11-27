namespace Nunam.Core.AI
{
    public static class DescribePrompts
    {
        public const string Default =
            "Describe this image in detail for a search index. Mention any visible text, " +
            "objects, people, scene, and notable attributes. Be concise but thorough.";

        /// <summary>
        /// For paid models: about a third of the output tokens of <see cref="Default"/>, keeping the
        /// visible text and what the image shows.
        /// </summary>
        public const string Short =
            "Describe this image for a search index in one plain paragraph of at most 80 words, without " +
            "headings or lists. Include any visible text verbatim, the kind of image, objects, people and setting.";
    }
}

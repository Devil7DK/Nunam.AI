namespace Nunam.Core.Agents
{
    /// <summary>How an indexer or the AI worker reaches the API.</summary>
    public class ServerOptions
    {
        public const string SectionName = "Server";

        /// <summary>The web UI's address, such as <c>https://nunam.example</c>; the API is under <c>/api</c>.</summary>
        public string? Url { get; set; }

        public string? Token { get; set; }

        /// <summary>
        /// SHA-256 of the server's certificate, in hex. When set, that certificate is accepted even if
        /// self-signed and no other is; when not, the usual certificate checks apply.
        /// </summary>
        public string? CertificateSha256 { get; set; }
    }
}

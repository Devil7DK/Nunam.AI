using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nunam.Core.Agents
{
    public static class AgentHttp
    {
        /// <summary>The wire format both ends use: camelCase, enums by name.</summary>
        public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        /// <summary>A client for the API at <see cref="ServerOptions.Url"/>, with the token when there is one.</summary>
        public static HttpClient Create(ServerOptions options, TimeSpan timeout)
        {
            if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException($"{ServerOptions.SectionName}:Url must be the server's http:// or https:// address.");
            }

            var handler = new SocketsHttpHandler
            {
                // DNS changes and a server moving to a new address are picked up without a restart.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            };
            if (Normalize(options.CertificateSha256) is { } pinned)
            {
                handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate is not null && Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())) == pinned;
            }

            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(url.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/"),
                Timeout = timeout,
            };
            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Nunam", version));
            if (!string.IsNullOrWhiteSpace(options.Token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token.Trim());
            }
            return client;
        }

        private static string? Normalize(string? hex) =>
            string.IsNullOrWhiteSpace(hex) ? null : hex.Replace(":", "").Replace(" ", "").ToUpperInvariant();

        /// <summary>Sends JSON and reads JSON; a status outside 2xx throws <see cref="HttpRequestException"/> with it.</summary>
        public static async Task<T> SendAsync<T>(this HttpClient client, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            using var response = await SendCoreAsync(client, method, path, body, cancellationToken);
            return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
        }

        public static async Task SendAsync(this HttpClient client, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            using var response = await SendCoreAsync(client, method, path, body, cancellationToken);
        }

        private static async Task<HttpResponseMessage> SendCoreAsync(
            HttpClient client, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);
            var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return response;

            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            throw new HttpRequestException(
                $"{method} {path} answered {(int)response.StatusCode} {response.ReasonPhrase}{(detail.Length > 0 ? ": " + Truncate(detail, 500) : "")}",
                null, response.StatusCode);
        }

        private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
    }
}

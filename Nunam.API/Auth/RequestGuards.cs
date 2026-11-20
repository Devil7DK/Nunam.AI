namespace Nunam.API.Auth
{
    public static class RequestGuards
    {
        /// <summary>Sent by the web UI on every state-changing request.</summary>
        public const string CsrfHeader = "X-Nunam-Request";

        /// <summary>
        /// Rejects state-changing <c>/api</c> requests without <see cref="CsrfHeader"/>. A page on another
        /// site can only add a custom header after a CORS preflight, which this API never answers, so
        /// this holds even where <c>SameSite</c> cookies don't (older browsers, sibling subdomains).
        /// </summary>
        public static IApplicationBuilder UseCsrfHeaderCheck(this IApplicationBuilder app) => app.Use(async (http, next) =>
        {
            var method = http.Request.Method;
            var safe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
            if (!safe && http.Request.Path.StartsWithSegments("/api") && http.Request.Headers[CsrfHeader] != "1")
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                await http.Response.WriteAsJsonAsync(new { title = $"Missing the {CsrfHeader} header." });
                return;
            }

            await next();
        });

        /// <summary>The API only returns JSON and images, so nothing it serves needs to run, load or be framed.</summary>
        public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) => app.Use((http, next) =>
        {
            var headers = http.Response.Headers;
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            if (http.Request.Path.StartsWithSegments("/api/auth")) headers.CacheControl = "no-store";
            return next();
        });
    }
}

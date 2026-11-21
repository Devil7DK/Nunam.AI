using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Nunam.API;
using Nunam.API.Auth;
using Nunam.API.Endpoints;
using Nunam.API.Services;
using Nunam.Core.AI;
using Nunam.Core.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SearchOptions>(builder.Configuration.GetSection(SearchOptions.SectionName));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

builder.Services.AddDbContext<NunamDbContext>(opt => opt
    .UseNpgsql(builder.Configuration.GetConnectionString("Postgres"), npg => npg.UseVector())
    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

builder.Services.AddNunamAI(builder.Configuration);
builder.Services.AddScoped<SearchService>();
builder.Services.AddSingleton<ThumbnailService>();
builder.Services.AddMemoryCache(o => o.SizeLimit = 64 * 1024 * 1024);

builder.Services.AddSingleton<Passwords>();
builder.Services.AddSingleton<SetupCode>();
builder.Services.AddHostedService<SetupCodeAnnouncer>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<MachineScope>();

builder.Services
    .AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization(Policies.Add);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(Policies.PasswordAttempts, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = auth.SignInAttemptsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

if (auth.TrustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

if (auth.TrustForwardedHeaders) app.UseForwardedHeaders();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseApiSecurityHeaders();
app.UseCsrfHeaderCheck();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapMachineAccessEndpoints();
app.MapSearchEndpoints();
app.MapFileEndpoints();
app.MapStatusEndpoints();
app.MapIndexerEndpoints();

app.Run();

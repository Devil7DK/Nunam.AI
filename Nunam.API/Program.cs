using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Nunam.API;
using Nunam.API.Endpoints;
using Nunam.API.Services;
using Nunam.Core.AI;
using Nunam.Core.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SearchOptions>(builder.Configuration.GetSection(SearchOptions.SectionName));
var cors = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();

builder.Services.AddDbContext<NunamDbContext>(opt => opt
    .UseNpgsql(builder.Configuration.GetConnectionString("Postgres"), npg => npg.UseVector())
    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

builder.Services.AddNunamAI(builder.Configuration);
builder.Services.AddScoped<SearchService>();
builder.Services.AddSingleton<ThumbnailService>();
builder.Services.AddMemoryCache(o => o.SizeLimit = 64 * 1024 * 1024);

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddCors(o => o.AddDefaultPolicy(policy =>
{
    if (cors.AllowedOrigins.Contains("*")) policy.AllowAnyOrigin();
    else policy.WithOrigins(cors.AllowedOrigins);
    policy.WithMethods("GET").AllowAnyHeader();
}));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
});
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapSearchEndpoints();
app.MapFileEndpoints();
app.MapStatusEndpoints();

app.Run();

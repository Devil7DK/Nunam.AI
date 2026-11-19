using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Nunam.Core.Data;
using Nunam.Core.Jobs;
using Nunam.Indexer;
using Nunam.Indexer.Indexing;
using Nunam.Indexer.Parsing;
using Nunam.Indexer.Workers;

// A Windows service starts in System32; config files and natives live next to the executable.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
});

// Per-machine settings and secrets (watch paths, DB password, Syncfusion key) for installs that
// don't run in Development, where user-secrets aren't read. Environment variables still win.
builder.Configuration
    .AddJsonFile(Path.Combine(builder.Environment.ContentRootPath, "appsettings.Local.json"), optional: true)
    .AddEnvironmentVariables();

builder.Services.AddWindowsService(o => o.ServiceName = "Nunam.Indexer");
builder.Services.AddSystemd();

builder.Services.Configure<IndexerOptions>(
    builder.Configuration.GetSection(IndexerOptions.SectionName));

// Stored paths must be absolute, or the same file indexed from two working directories would
// look like two files.
builder.Services.PostConfigure<IndexerOptions>(o =>
    o.WatchPaths = [.. o.WatchPaths.Select(p => Path.GetFullPath(p))]);

// Shared DB + queue access.
builder.Services.AddDbContext<NunamDbContext>(opt =>
    opt.UseNpgsql(
        builder.Configuration.GetConnectionString("Postgres"),
        npg => npg.UseVector()));
builder.Services.AddScoped<JobQueue>();

// Machine identity + path filtering (resolved once, shared everywhere).
builder.Services.AddSingleton<MachineContext>();
builder.Services.AddSingleton<PathFilter>();
builder.Services.AddSingleton<FileTree>();

// Parsing pipeline. Registration order matters: specialized parsers first, fallback last.
builder.Services.AddSingleton<FileTypeDetector>();
builder.Services.AddSingleton<ParserSelector>();
builder.Services.AddSingleton<OcrService>();
builder.Services.AddSingleton<IFileParser, TextParser>();
builder.Services.AddSingleton<IFileParser, PdfParser>();
builder.Services.AddSingleton<IFileParser, WordParser>();
builder.Services.AddSingleton<IFileParser, ExcelParser>();
builder.Services.AddSingleton<IFileParser, PowerPointParser>();
builder.Services.AddSingleton<IFileParser, ImageParser>();
builder.Services.AddSingleton<IFileParser, AudioParser>();
builder.Services.AddSingleton<IFileParser, VideoParser>();
builder.Services.AddSingleton<IFileParser, FallbackParser>();

// Resolve this machine's identity before any worker runs.
builder.Services.AddHostedService<MachineInitializer>();

// Producers (enqueue Parse jobs) + consumer (parses and enqueues AI follow-ups).
builder.Services.AddHostedService<ScanWorker>();
builder.Services.AddHostedService<WatcherWorker>();
builder.Services.AddHostedService<ParseWorker>();

var host = builder.Build();

// Unlicensed, Syncfusion still parses but stamps evaluation notices into its output.
var syncfusionKey = builder.Configuration["Syncfusion:LicenseKey"];
if (string.IsNullOrWhiteSpace(syncfusionKey))
{
    host.Services.GetRequiredService<ILogger<Program>>()
        .LogWarning("Syncfusion:LicenseKey is not set; document parsing runs unlicensed");
}
else
{
    Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(syncfusionKey);
}

host.Run();

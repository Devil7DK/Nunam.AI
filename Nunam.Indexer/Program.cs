using Npgsql;
using Nunam.Indexer.Management;

// Config files live next to the executable, and a service doesn't start there (a Windows service
// starts in System32), so the content root is the executable's folder rather than the current one.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Per-machine settings and secrets (watch paths, DB password, Syncfusion key) for installs that
// don't run in Development, where user-secrets aren't read. Environment variables still win.
builder.Configuration
    .AddJsonFile(Path.Combine(builder.Environment.ContentRootPath, "appsettings.Local.json"), optional: true)
    .AddEnvironmentVariables();

builder.Services.AddWindowsService(o => o.ServiceName = "Nunam.Indexer");
builder.Services.AddSystemd();

// The process only keeps the service alive and the logs flowing; IndexerSupervisor runs the workers.
var databaseLogs = new DatabaseLoggerProvider();
builder.Logging.AddProvider(databaseLogs);
builder.Services.AddSingleton(databaseLogs);
builder.Services.AddSingleton<IndexerControl>();
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Postgres")!));
builder.Services.AddHostedService<DatabaseLogShipper>();
builder.Services.AddHostedService<IndexerSupervisor>();

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

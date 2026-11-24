using Nunam.Core.Agents;
using Nunam.Indexer.Management;
using Nunam.Indexer.Server;

if (CommandLine.Handles(args)) return await CommandLine.RunAsync(args);

// Config files live next to the executable, and a service doesn't start there (a Windows service
// starts in System32), so the content root is the executable's folder rather than the current one.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Per-machine settings and the machine token, for installs that don't run in Development, where
// user-secrets aren't read. Environment variables still win.
builder.Configuration
    .AddJsonFile(Path.Combine(builder.Environment.ContentRootPath, "appsettings.Local.json"), optional: true)
    .AddEnvironmentVariables();

builder.Services.AddWindowsService(o => o.ServiceName = "Nunam.Indexer");
builder.Services.AddSystemd();

var server = builder.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>() ?? new ServerOptions();
var enrolled = !string.IsNullOrWhiteSpace(server.Url) && !string.IsNullOrWhiteSpace(server.Token);

// The process only keeps the service alive and the logs flowing; IndexerSupervisor runs the workers.
var serverLogs = new ServerLoggerProvider();
builder.Logging.AddProvider(serverLogs);
builder.Services.AddSingleton(serverLogs);
builder.Services.AddSingleton<IndexerControl>();
if (enrolled)
{
    builder.Services.AddSingleton(_ => new IndexerServer(AgentHttp.Create(server, TimeSpan.FromSeconds(100))));
    builder.Services.AddHostedService<ServerLogShipper>();
    builder.Services.AddHostedService<IndexerSupervisor>();
}

using var host = builder.Build();
if (!enrolled)
{
    host.Services.GetRequiredService<ILogger<Program>>().LogCritical(
        "This machine isn't connected to a Nunam server: Server:Url and Server:Token are missing from appsettings.Local.json. "
        + "In the web UI, open Machines > Add machine and run the command it shows here.");
    return 1;
}
await host.RunAsync();
return 0;

using Nunam.AIWorker;
using Nunam.AIWorker.Workers;
using Nunam.Core.Agents;
using Nunam.Core.AI;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AIWorkerOptions>(
    builder.Configuration.GetSection(AIWorkerOptions.SectionName));

// Jobs and files come from the API, signed in with the worker token.
var server = builder.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>() ?? new ServerOptions();
if (string.IsNullOrWhiteSpace(server.Token))
{
    throw new InvalidOperationException($"{ServerOptions.SectionName}:Token is missing: set it to the API's Agents:WorkerToken.");
}
builder.Services.AddSingleton(new WorkerServer(AgentHttp.Create(server, TimeSpan.FromSeconds(100))));

// IAIInferenceService + chat/embedding clients, backend chosen from the "AI" config section.
builder.Services.AddNunamAI(builder.Configuration);

// Queue consumers.
builder.Services.AddHostedService<DescribeWorker>();
builder.Services.AddHostedService<EmbedWorker>();

var host = builder.Build();
host.Run();

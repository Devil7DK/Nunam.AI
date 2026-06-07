using Nunam.AIWorker;
using Nunam.Core.AI;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddNunamAI(builder.Configuration);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

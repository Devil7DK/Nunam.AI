using Microsoft.EntityFrameworkCore;
using Nunam.AIWorker;
using Nunam.AIWorker.Workers;
using Nunam.Core.AI;
using Nunam.Core.Data;
using Nunam.Core.Jobs;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AIWorkerOptions>(
    builder.Configuration.GetSection(AIWorkerOptions.SectionName));

// Shared DB + queue access.
builder.Services.AddDbContext<NunamDbContext>(opt =>
    opt.UseNpgsql(
        builder.Configuration.GetConnectionString("Postgres"),
        npg => npg.UseVector()));
builder.Services.AddScoped<JobQueue>();

// IAIInferenceService + chat/embedding clients, backend chosen from the "AI" config section.
builder.Services.AddNunamAI(builder.Configuration);

// Queue consumers.
builder.Services.AddHostedService<DescribeWorker>();
builder.Services.AddHostedService<EmbedWorker>();
builder.Services.AddHostedService<JobCleanupWorker>();

var host = builder.Build();
host.Run();

using Microsoft.EntityFrameworkCore;
using Nunam.Core.Data;
using Nunam.Core.Jobs;
using Nunam.Core.Management;
using Nunam.Indexer.Indexing;
using Nunam.Indexer.Abstractions;
using Nunam.Indexer.Parsing;
using Nunam.Indexer.Providers;
using Nunam.Indexer.Workers;

namespace Nunam.Indexer.Management
{
    /// <summary>
    /// Builds the host that runs the indexing workers. The service process starts one and replaces it
    /// to restart, so options bound at start (watch paths, limits) take new values without the
    /// Windows or systemd service ever stopping.
    /// </summary>
    public static class IndexerHost
    {
        public static IHost Build(
            IConfiguration configuration, ILoggerFactory loggerFactory, IndexerControl control, RemoteIndexerSettings? remote)
        {
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
            {
                ContentRootPath = AppContext.BaseDirectory,
            });
            builder.Configuration.AddConfiguration(configuration);

            // Logging and the service lifetime belong to the process, not to one run of the workers.
            builder.Services.AddSingleton(loggerFactory);
            builder.Services.AddSingleton<IHostLifetime, SupervisedLifetime>();
            builder.Services.AddSingleton(control);

            builder.Services.Configure<IndexerOptions>(builder.Configuration.GetSection(IndexerOptions.SectionName));
            if (remote is not null) builder.Services.PostConfigure<IndexerOptions>(remote.ApplyTo);

            // Stored paths must be absolute, or the same file indexed from two working directories would
            // look like two files.
            builder.Services.PostConfigure<IndexerOptions>(o =>
                o.WatchPaths = [.. o.WatchPaths.Select(p => Path.GetFullPath(p))]);

            builder.Services.AddDbContext<NunamDbContext>(opt =>
                opt.UseNpgsql(
                    builder.Configuration.GetConnectionString("Postgres"),
                    npg => npg.UseVector()));
            builder.Services.AddScoped<JobQueue>();

            builder.Services.AddSingleton<MachineContext>();
            builder.Services.AddSingleton<PathFilter>();
            builder.Services.AddSingleton<FileTree>();
            builder.Services.AddSingleton<IndexScope>();

            // Built-in parsers are offered in this order when no parser list is configured for an extension.
            builder.Services.AddSingleton<FileTypeDetector>();
            builder.Services.AddSingleton<ProviderRegistry>();
            builder.Services.AddSingleton<IFileParser, TextParser>();
            builder.Services.AddSingleton<IFileParser, PdfParser>();
            builder.Services.AddSingleton<IFileParser, Word97Parser>();
            builder.Services.AddSingleton<IFileParser, OpenXmlWordParser>();
            builder.Services.AddSingleton<IFileParser, RtfParser>();
            builder.Services.AddSingleton<IFileParser, ExcelParser>();
            builder.Services.AddSingleton<IFileParser, PowerPointParser>();
            builder.Services.AddSingleton<IFileParser, ImageParser>();
            builder.Services.AddSingleton<IFileParser, AudioParser>();
            builder.Services.AddSingleton<IFileParser, VideoParser>();
            builder.Services.AddSingleton<IFileParser, FallbackParser>();

            // Resolves this machine's identity before any worker runs.
            builder.Services.AddHostedService<MachineInitializer>();
            builder.Services.AddHostedService<ManagementWorker>();
            builder.Services.AddHostedService<ScanWorker>();
            builder.Services.AddHostedService<WatcherWorker>();
            builder.Services.AddHostedService<ParseWorker>();

            return builder.Build();
        }

        /// <summary>The service process handles Ctrl+C, SIGTERM and the service manager; this host only follows it.</summary>
        private sealed class SupervisedLifetime : IHostLifetime
        {
            public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}

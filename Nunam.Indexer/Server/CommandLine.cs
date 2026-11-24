using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nunam.Core.Agents;
using Nunam.Core.Management;
using Nunam.Indexer.Management;

namespace Nunam.Indexer.Server
{
    /// <summary>The commands the install scripts run: <c>enroll</c> and <c>watch-paths</c>.</summary>
    public static class CommandLine
    {
        public static bool Handles(string[] args) => args is ["enroll", ..] or ["watch-paths", ..];

        public static Task<int> RunAsync(string[] args) => args[0] switch
        {
            "enroll" => EnrollAsync(args[1..]),
            _ => WatchPathsAsync(),
        };

        private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json");

        /// <summary>
        /// Trades a one-time code from the web UI for this machine's token and saves it, with the
        /// server's address, in <c>appsettings.Local.json</c>.
        /// </summary>
        private static async Task<int> EnrollAsync(string[] args)
        {
            string? server = null, code = null, name = null, certificate = null;
            var remoteManagement = false;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--server" when i + 1 < args.Length: server = args[++i]; break;
                    case "--code" when i + 1 < args.Length: code = args[++i]; break;
                    case "--name" when i + 1 < args.Length: name = args[++i]; break;
                    case "--certificate-sha256" when i + 1 < args.Length: certificate = args[++i]; break;
                    case "--remote-management": remoteManagement = true; break;
                    default:
                        Console.Error.WriteLine($"Unknown option: {args[i]}");
                        return 2;
                }
            }
            if (server is null || code is null)
            {
                Console.Error.WriteLine("Usage: Nunam.Indexer enroll --server <url> --code <code> [--name <name>] [--remote-management] [--certificate-sha256 <hex>]");
                return 2;
            }

            var options = new ServerOptions { Url = server.TrimEnd('/'), CertificateSha256 = certificate };
            EnrollResponse enrolled;
            try
            {
                using var http = AgentHttp.Create(options, TimeSpan.FromSeconds(30));
                enrolled = await http.SendAsync<EnrollResponse>(HttpMethod.Post, "api/agent/enroll",
                    new EnrollRequest(code.Trim(), Environment.MachineName, name, System.Runtime.InteropServices.RuntimeInformation.OSDescription),
                    CancellationToken.None);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                Console.Error.WriteLine($"Could not enroll with {options.Url}: {ex.Message}");
                return 1;
            }

            var settings = ReadSettings();
            var serverNode = settings["Server"] as JsonObject ?? [];
            serverNode["Url"] = options.Url;
            serverNode["Token"] = enrolled.Token;
            if (certificate is not null) serverNode["CertificateSha256"] = certificate;
            settings["Server"] = serverNode;
            settings.Remove("ConnectionStrings");
            if (remoteManagement)
            {
                var indexer = settings["Indexer"] as JsonObject ?? [];
                indexer["RemoteManagement"] = true;
                settings["Indexer"] = indexer;
            }
            WriteSettings(settings);

            Console.WriteLine($"Enrolled as machine \"{enrolled.Name}\" with {options.Url}.");
            return 0;
        }

        /// <summary>
        /// Prints the watch paths in effect, those from the web UI included, one per line, so the Windows
        /// configure script can grant the service account read access to each.
        /// </summary>
        private static async Task<int> WatchPathsAsync()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Local.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
            var options = configuration.GetSection(IndexerOptions.SectionName).Get<IndexerOptions>() ?? new IndexerOptions();
            var server = configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>() ?? new ServerOptions();

            if (options.RemoteManagement && !string.IsNullOrWhiteSpace(server.Token))
            {
                try
                {
                    using var http = AgentHttp.Create(server, TimeSpan.FromSeconds(30));
                    var remote = await new IndexerServer(http).GetSettingsAsync(CancellationToken.None);
                    if (remote.Settings is { } settings && settings.Validate().Count == 0) settings.ApplyTo(options);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
                {
                    Console.Error.WriteLine($"Could not read the settings from the server; showing this machine's own: {ex.Message}");
                }
            }

            foreach (var path in options.WatchPaths) Console.WriteLine(Path.GetFullPath(path));
            return 0;
        }

        private static JsonObject ReadSettings()
        {
            if (!File.Exists(SettingsPath)) return [];
            var text = File.ReadAllText(SettingsPath);
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                as JsonObject ?? [];
        }

        /// <summary>
        /// Rewrites the file in place, so it keeps its permissions (on Linux it's a link into
        /// /etc/nunam-indexer). A new file on Windows starts readable only by Administrators and SYSTEM;
        /// the configure script then adds the service account.
        /// </summary>
        private static void WriteSettings(JsonObject settings)
        {
            if (OperatingSystem.IsWindows() && !File.Exists(SettingsPath))
            {
                var security = new FileSecurity();
                security.SetAccessRuleProtection(true, false);
                foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
                {
                    security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, AccessControlType.Allow));
                }
                new FileInfo(SettingsPath).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, security).Dispose();
            }

            File.WriteAllText(SettingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        }
    }
}

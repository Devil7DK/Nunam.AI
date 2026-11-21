using Microsoft.Extensions.Options;
using Nunam.Core.Management;
using Nunam.Indexer.Abstractions;
using Nunam.Indexer.Parsing;

namespace Nunam.Indexer.Providers
{
    /// <summary>
    /// Every parser, OCR engine and PDF renderer this indexer has (built in, from <c>Indexer:Commands</c>
    /// and from plugins), and the choice among them that the settings make.
    /// </summary>
    public sealed class ProviderRegistry
    {
        private readonly IndexerOptions _options;
        private readonly ILogger<ProviderRegistry> _logger;
        private readonly HashSet<IFileParser> _builtIn;
        private readonly List<IFileParser> _offered;
        private readonly Dictionary<string, IFileParser> _parsers = new(StringComparer.OrdinalIgnoreCase);
        private readonly IFileParser _fallback;

        public ProviderRegistry(
            IEnumerable<IFileParser> builtInParsers, IServiceProvider services, IOptions<IndexerOptions> options,
            ILoggerFactory loggerFactory, ILogger<ProviderRegistry> logger)
        {
            _options = options.Value;
            _logger = logger;

            // Registration order is the order built-ins are offered in.
            var builtIn = builtInParsers.ToList();
            _builtIn = [.. builtIn];
            _fallback = builtIn.Single(p => p.Name == IndexerProviders.Parsers.Fallback);

            var ocrEngines = new List<IOcrEngine> { ActivatorUtilities.CreateInstance<TesseractOcr>(services), new NoOcr() };
            var renderers = new List<IPdfRenderer> { new PdfiumRenderer(), new NoPdfRenderer() };
            var external = new List<IFileParser>();

            foreach (var (name, command) in _options.Commands)
            {
                if (command.Use.Equals("ocr", StringComparison.OrdinalIgnoreCase))
                {
                    ocrEngines.Add(new CommandOcr(name, command, _options.OcrLanguage, _options.MaxContentChars, logger));
                }
                else
                {
                    external.Add(new CommandParser(name, command));
                }
            }

            var pluginFolder = _options.PluginFolder ?? Path.Combine(AppContext.BaseDirectory, "plugins");
            foreach (var type in PluginLoader.ProviderTypes(pluginFolder, loggerFactory.CreateLogger(typeof(PluginLoader))))
            {
                try
                {
                    var provider = ActivatorUtilities.CreateInstance(services, type);
                    if (provider is IFileParser parser) external.Add(parser);
                    if (provider is IOcrEngine ocr) ocrEngines.Add(ocr);
                    if (provider is IPdfRenderer renderer) renderers.Add(renderer);
                }
                catch (Exception ex) when (ex is InvalidOperationException or MissingMethodException or System.Reflection.TargetInvocationException)
                {
                    logger.LogError(ex, "Could not create plugin provider {Type}", type.FullName);
                }
            }

            foreach (var parser in builtIn.Concat(external))
            {
                if (!_parsers.TryAdd(parser.Name, parser))
                {
                    logger.LogWarning("A second parser is named {Name}; only the first ({Type}) is used", parser.Name, _parsers[parser.Name].GetType().Name);
                }
            }
            _offered = [.. external.Where(p => _parsers[p.Name] == p), .. builtIn.Where(p => p != _fallback)];

            Ocr = _options.EnableOcr ? Choose(ocrEngines, _options.OcrEngine, "OCR engine", new NoOcr()) : new NoOcr();
            PdfRenderer = Choose(renderers, _options.PdfRenderer, "PDF renderer", new NoPdfRenderer());

            foreach (var name in _options.Parsers.Values.SelectMany(n => n).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!_parsers.ContainsKey(name)) logger.LogWarning("Indexer:Parsers names {Name}, which isn't a parser here; it is skipped", name);
            }
            logger.LogInformation("Parsers: {Parsers}; OCR: {Ocr}; PDF renderer: {Renderer}",
                string.Join(", ", _parsers.Keys), Ocr.Name, PdfRenderer.Name);
        }

        public IOcrEngine Ocr { get; }

        public IPdfRenderer PdfRenderer { get; }

        /// <summary>Built-in parsers hand in final previews; anyone else's are normalized by the indexer.</summary>
        public bool IsBuiltIn(IFileParser parser) => _builtIn.Contains(parser);

        /// <summary>
        /// The parsers to try for a file, in order. The fallback is always last: a file nothing else reads
        /// is still indexed by name.
        /// </summary>
        public IReadOnlyList<IFileParser> ChainFor(FileKind kind, string extension)
        {
            var chain = _options.Parsers.TryGetValue(extension, out var names)
                ? names.Select(n => _parsers.GetValueOrDefault(n)).OfType<IFileParser>().Where(p => p != _fallback).ToList()
                : _offered.Where(p => p.CanParse(kind, extension)).ToList();
            chain.Add(_fallback);
            return chain;
        }

        private T Choose<T>(List<T> candidates, string name, string what, T none) where T : class
        {
            var chosen = candidates.FirstOrDefault(c => string.Equals(NameOf(c), name, StringComparison.OrdinalIgnoreCase));
            if (chosen is null)
            {
                _logger.LogWarning("{What} {Name} isn't available here; using none. Available: {Names}",
                    what, name, string.Join(", ", candidates.Select(NameOf)));
            }
            return chosen ?? none;
        }

        private static string NameOf(object provider) => provider switch
        {
            IOcrEngine ocr => ocr.Name,
            IPdfRenderer renderer => renderer.Name,
            _ => "",
        };
    }
}

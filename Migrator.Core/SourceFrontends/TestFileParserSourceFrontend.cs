using Migrator.Core;
using Migrator.Core.Models.Ir;

namespace Migrator.Core.SourceFrontends;

/// <summary>
/// Compatibility frontend that wraps any legacy ITestFileParser and exposes IR V2 documents.
/// </summary>
public class TestFileParserSourceFrontend : ISourceFrontend
{
    readonly ITestFileParser _parser;

    public TestFileParserSourceFrontend(SourceSpec source, ITestFileParser parser, IReadOnlyCollection<string>? aliases = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        Aliases = aliases ?? Array.Empty<string>();
    }

    public SourceSpec Source { get; }
    public IReadOnlyCollection<string> Aliases { get; }

    public bool CanParse(MigrationRequest request) =>
        request != null && string.Equals(request.Source.Id, Source.Id, StringComparison.OrdinalIgnoreCase);

    public SourceParseResult Parse(MigrationRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var models = Directory.Exists(request.InputPath)
            ? _parser.ParseDirectory(request.InputPath).ToArray()
            : new[] { _parser.Parse(request.InputPath) };

        var documents = models.Select(m => LegacyIrBridge.ToDocument(m, Source, request.Target)).ToArray();
        var diagnostics = documents.SelectMany(d => d.Diagnostics).ToArray();
        return new SourceParseResult(documents, diagnostics);
    }
}

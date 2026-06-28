using Migrator.Core;
using Migrator.Core.Models.Ir;

namespace Migrator.Core.SourceFrontends;

/// <summary>
/// Compatibility frontend that wraps any legacy ITestFileParser and exposes IR V2 documents.
/// </summary>
public class TestFileParserSourceFrontend : ISourceFrontend
{
    public TestFileParserSourceFrontend(SourceSpec source, ITestFileParser parser, IReadOnlyCollection<string>? aliases = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Parser = parser ?? throw new ArgumentNullException(nameof(parser));
        Aliases = aliases ?? Array.Empty<string>();
    }

    public SourceSpec Source { get; }
    public IReadOnlyCollection<string> Aliases { get; }
    public ITestFileParser Parser { get; }

    public bool CanParse(MigrationRequest request) =>
        request != null && string.Equals(request.Source.Id, Source.Id, StringComparison.OrdinalIgnoreCase);

    public SourceParseResult Parse(MigrationRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var models = Directory.Exists(request.InputPath)
            ? Parser.ParseDirectory(request.InputPath).ToArray()
            : new[] { Parser.Parse(request.InputPath) };

        var documents = models.Select(m => Migrator.Core.Models.Ir.LegacyIrBridge.ToDocument(m, Source, request.Target)).ToArray();
        var diagnostics = documents.SelectMany(d => d.Diagnostics).ToArray();
        return new SourceParseResult(documents, diagnostics);
    }
}

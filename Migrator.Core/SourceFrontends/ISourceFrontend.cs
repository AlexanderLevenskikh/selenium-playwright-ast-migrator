using Migrator.Core;
using Migrator.Core.Models.Ir;

namespace Migrator.Core.SourceFrontends;

/// <summary>
/// Source frontend contract. A frontend owns source language parsing and lowering into IR V2.
/// </summary>
public interface ISourceFrontend
{
    SourceSpec Source { get; }
    IReadOnlyCollection<string> Aliases { get; }
    SourceCapabilityReport Capabilities { get; }
    bool CanParse(MigrationRequest request);
    SourceParseResult Parse(MigrationRequest request);
}

public sealed record SourceParseResult(
    IReadOnlyList<MigrationDocument> Documents,
    IReadOnlyList<IrDiagnostic> Diagnostics
);

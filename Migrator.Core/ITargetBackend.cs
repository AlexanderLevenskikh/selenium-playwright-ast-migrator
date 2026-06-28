using Migrator.Core.Models;
using Migrator.Core.Models.Ir;

namespace Migrator.Core;

/// <summary>
/// Target backend contract for source-to-source migration.
/// A backend owns target-specific rendering decisions and output file naming.
/// Existing renderers remain supported through IRenderer for backwards compatibility.
/// </summary>
public interface ITargetBackend
{
    /// <summary>Stable backend identity, for example playwright-dotnet or playwright-typescript.</summary>
    TargetSpec Target { get; }

    /// <summary>User-facing aliases accepted by registries/CLI, for example dotnet or ts.</summary>
    IReadOnlyCollection<string> Aliases { get; }

    /// <summary>Render the target test source for one migrated legacy test model.</summary>
    string Render(TestFileModel model);

    /// <summary>
    /// Experimental IR V2 rendering entry point. Backends may render the document directly;
    /// during migration they may lower IR V2 through the compatibility bridge.
    /// </summary>
    string RenderDocument(MigrationDocument document);

    /// <summary>Return the default generated file name for one migrated test model.</summary>
    string GetDefaultFileName(TestFileModel model);
}

using Migrator.Core.Models;

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

    /// <summary>Render the target test source for one migrated test model.</summary>
    string Render(TestFileModel model);

    /// <summary>Return the default generated file name for one migrated test model.</summary>
    string GetDefaultFileName(TestFileModel model);
}

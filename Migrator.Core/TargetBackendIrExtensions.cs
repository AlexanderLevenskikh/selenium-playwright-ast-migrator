using Migrator.Core.Models.Ir;

namespace Migrator.Core;

/// <summary>
/// Transitional IR V2 rendering bridge. Real target backends can later render IR directly;
/// until then this preserves existing renderer behavior by lowering IR V2 back to legacy model.
/// </summary>
public static class TargetBackendIrExtensions
{
    public static string RenderDocument(this ITargetBackend backend, MigrationDocument document)
    {
        if (backend == null)
            throw new ArgumentNullException(nameof(backend));
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        return backend.Render(LegacyIrBridge.ToLegacyTestFile(document));
    }
}

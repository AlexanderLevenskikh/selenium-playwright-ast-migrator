using Migrator.Core.Models.Ir;

namespace Migrator.Core;

/// <summary>
/// Transitional helpers for IR V2 target rendering. The main RenderDocument contract now lives
/// on ITargetBackend; this helper keeps the common bridge implementation in one place.
/// </summary>
public static class TargetBackendIrExtensions
{
    public static string RenderDocumentViaLegacyBridge(this ITargetBackend backend, MigrationDocument document)
    {
        if (backend == null)
            throw new ArgumentNullException(nameof(backend));
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        return backend.Render(LegacyIrBridge.ToLegacyTestFile(document));
    }
}

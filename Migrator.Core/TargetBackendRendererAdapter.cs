using Migrator.Core.Models;

namespace Migrator.Core;

/// <summary>
/// Compatibility adapter so existing pipeline code can keep using IRenderer while newer code
/// resolves targets through ITargetBackend.
/// </summary>
public sealed class TargetBackendRendererAdapter : IRenderer
{
    readonly ITargetBackend _backend;

    public TargetBackendRendererAdapter(ITargetBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public string Render(TestFileModel model) => _backend.Render(model);
}

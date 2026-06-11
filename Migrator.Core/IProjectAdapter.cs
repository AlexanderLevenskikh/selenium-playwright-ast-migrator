using Migrator.Core.Models;

namespace Migrator.Core;

/// <summary>
/// Adapts source IR (unresolved expressions) to target IR (resolved expressions)
/// using project-specific mapping configuration.
/// </summary>
public interface IProjectAdapter
{
    /// <summary>
    /// Resolve a source UI expression (e.g. "page.User") to a target expression.
    /// Pure — no side effects.
    /// </summary>
    TargetExpression ResolveTarget(string sourceExpression);

    /// <summary>
    /// Get the variable name for a page object type.
    /// </summary>
    string? ResolvePageObjectVariable(string sourceType);

    /// <summary>
    /// Resolve a source method name to target method name.
    /// </summary>
    string? ResolveMethodTarget(string sourceMethod);

    /// <summary>
    /// Apply adapter mappings to a parsed file model, producing target IR.
    /// ClickAction and SendKeysAction will carry resolved TargetExpression.
    /// </summary>
    TestFileModel Adapt(TestFileModel sourceModel);
}

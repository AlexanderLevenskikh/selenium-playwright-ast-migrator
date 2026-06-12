namespace Migrator.Core;

/// <summary>
/// JSON-serializable config for mapping source expressions to Playwright targets.
/// Loaded from adapter project or config file, never contains project-specific logic.
/// </summary>
public record ProjectAdapterConfig(
    string SourceProjectName,
    UiTargetMapping[] UiTargets,
    PageObjectMapping[] PageObjects,
    MethodMapping[] Methods
);

public record UiTargetMapping(
    string SourceExpression,
    string TargetExpression,
    string TargetKind
);

public record PageObjectMapping(
    string SourceType,
    string TargetType,
    string VariableName,
    string ConstructorStrategy
);

public record MethodMapping(
    string SourceMethod,
    string? TargetMethod,
    string? Description,
    string[]? TargetStatements,
    bool RequiresReview
);

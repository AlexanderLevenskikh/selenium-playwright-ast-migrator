namespace Migrator.Core.Models.Ir;

/// <summary>
/// Source-neutral locator intent. Target backends decide how to render it.
/// </summary>
public abstract record LocatorRef;

public sealed record ByTestId(string Value, string? Attribute = null, string? Match = null, int? NthIndex = null, string? NthIndexExpression = null) : LocatorRef;
public sealed record ByCss(string Selector, string? Match = null, int? NthIndex = null, string? NthIndexExpression = null) : LocatorRef;
public sealed record ByXpath(string Selector, string? Match = null, int? NthIndex = null, string? NthIndexExpression = null) : LocatorRef;
public sealed record ByText(string Text, string? Match = null, int? NthIndex = null, string? NthIndexExpression = null) : LocatorRef;
public sealed record ByRole(string Role, string? Name = null, string? Match = null, int? NthIndex = null, string? NthIndexExpression = null) : LocatorRef;
public sealed record ByClassNamePrefix(string Prefix, string? Match = null, int? NthIndex = null, string? NthIndexExpression = null) : LocatorRef;
public sealed record PageObjectLocator(string Expression) : LocatorRef;

/// <summary>
/// Playwright-specific locator expression. Transitional IR V2 bridge node used to preserve
/// exact legacy target mappings while renderers move off TestFileModel.
/// </summary>
public sealed record PlaywrightLocatorRef(string Expression, string? TestIdAttribute = null, string? Match = null, int? NthIndex = null, string? NthIndexExpression = null) : LocatorRef;

/// <summary>
/// Escape hatch for target-specific locator expressions. Must always carry Language.
/// </summary>
public sealed record RawLocatorExpression(string Expression, string Language, string? Match = null, int? NthIndex = null, string? NthIndexExpression = null) : LocatorRef;

public sealed record UnresolvedLocator(string SourceExpression) : LocatorRef;

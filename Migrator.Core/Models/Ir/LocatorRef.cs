namespace Migrator.Core.Models.Ir;

/// <summary>
/// Source-neutral locator intent. Target backends decide how to render it.
/// </summary>
public abstract record LocatorRef;

public sealed record ByTestId(string Value, string? Attribute = null, string? Match = null, int? NthIndex = null) : LocatorRef;
public sealed record ByCss(string Selector, string? Match = null, int? NthIndex = null) : LocatorRef;
public sealed record ByXpath(string Selector, string? Match = null, int? NthIndex = null) : LocatorRef;
public sealed record ByText(string Text, string? Match = null, int? NthIndex = null) : LocatorRef;
public sealed record ByRole(string Role, string? Name = null, string? Match = null, int? NthIndex = null) : LocatorRef;
public sealed record PageObjectLocator(string Expression) : LocatorRef;

/// <summary>
/// Escape hatch for target-specific locator expressions. Must always carry Language.
/// </summary>
public sealed record RawLocatorExpression(string Expression, string Language) : LocatorRef;

public sealed record UnresolvedLocator(string SourceExpression) : LocatorRef;

namespace Migrator.Core.Models.Ir;

public abstract record AssertionIntent;
public sealed record TextAssertionIntent(LocatorRef Target, string Kind, ValueExpr? Expected) : AssertionIntent;
public sealed record VisibilityAssertionIntent(LocatorRef Target, string Kind) : AssertionIntent;
public sealed record UrlAssertionIntent(string Kind, ValueExpr Expected) : AssertionIntent;
public sealed record RawAssertionIntent(string SourceText, string Reason) : AssertionIntent;

public abstract record WaitIntent;
public sealed record LocatorWaitIntent(LocatorRef Target, string Kind, string SourceMethod) : WaitIntent;
public sealed record RawWaitIntent(string SourceText, string Reason) : WaitIntent;

public abstract record NavigationIntent;
public sealed record UrlNavigationIntent(ValueExpr Url, string? ResultVariable = null, string? TargetStatement = null) : NavigationIntent;
public sealed record RawNavigationIntent(string SourceText, string Reason) : NavigationIntent;

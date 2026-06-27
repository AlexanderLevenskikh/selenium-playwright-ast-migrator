namespace Migrator.Core.Models.Ir;

/// <summary>
/// Source-neutral value expression used by assertions/fills/templates.
/// </summary>
public abstract record ValueExpr;

public sealed record LiteralValue(string Value) : ValueExpr;
public sealed record RawValueExpression(string Expression, string Language) : ValueExpr;
public sealed record UnresolvedValueExpression(string SourceExpression) : ValueExpr;

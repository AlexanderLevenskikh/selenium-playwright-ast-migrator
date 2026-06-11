using Migrator.Core;

namespace Migrator.SeleniumCSharp;

public record class SeleniumCSharpConfig(
    Dictionary<string, PageObjectMapping> PageObjects,
    Dictionary<string, string> SortOrders,
    string UnresolvedStrategy
);

public record class FilterMapping(
    string TargetFilter,
    string TargetColumn,
    Dictionary<string, MethodMapping>? SourceMethods
);

public record class ControlMapping(
    string TargetExpression
);

public record class TableMapping(
    string EnumName,
    string EnumImportPath,
    List<string> Columns,
    List<object> SourceIndexLayout,
    bool HasCheckbox,
    bool HasActions
);

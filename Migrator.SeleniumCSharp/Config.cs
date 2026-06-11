using System.Text.Json;
using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.SeleniumCSharp;

public record class SeleniumCSharpConfig(
    Dictionary<string, PageObjectMapping> PageObjects,
    Dictionary<string, string> SortOrders,
    string UnresolvedStrategy
);

public record class PageObjectMapping(
    string TargetPageClass,
    string TargetImportPath,
    string TargetVariableName,
    string Route,
    Dictionary<string, FilterMapping>? Filters,
    Dictionary<string, ControlMapping>? Controls,
    TableMapping? Table
);

public record class FilterMapping(
    string TargetFilter,
    string TargetColumn,
    Dictionary<string, MethodMapping>? SourceMethods
);

public record class ControlMapping(
    string TargetExpression
);

public record class MethodMapping(
    string TargetMethod,
    string ArgsMode
);

public record class TableMapping(
    string EnumName,
    string EnumImportPath,
    List<string> Columns,
    List<object> SourceIndexLayout,
    bool HasCheckbox,
    bool HasActions
);

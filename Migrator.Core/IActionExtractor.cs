using Migrator.Core.Models;

namespace Migrator.Core;

public interface IActionExtractor
{
    IEnumerable<TestAction> Extract(MethodDeclarationInfo methodInfo);
}

public record MethodDeclarationInfo(
    string Name,
    string BodyText,
    int StartLine,
    IEnumerable<FieldInfo> Fields,
    IEnumerable<string> Usings,
    object? SemanticModel,
    object? SyntaxTree
);

public record FieldInfo(
    string Name,
    string Type,
    int Line
);

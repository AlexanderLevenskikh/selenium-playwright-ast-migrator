namespace Migrator.Core.Models;

public sealed class PageObjectFieldAction : TestAction
{
    public string FieldName { get; }
    public string FieldType { get; }
    public string? InitializationValue { get; }
    public string FullDeclaration { get; }

    public PageObjectFieldAction(int sourceLine, string fieldName, string fieldType, string? initializationValue = null, string? fullDeclaration = null)
        : base(sourceLine)
    {
        FieldName = fieldName;
        FieldType = fieldType;
        InitializationValue = initializationValue;
        FullDeclaration = fullDeclaration ?? $"{fieldType} {fieldName}";
    }
}

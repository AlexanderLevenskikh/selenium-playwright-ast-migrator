namespace Migrator.Core.Models;

public sealed class PageObjectFieldAction : TestAction
{
    public string FieldName { get; }
    public string FieldType { get; }

    public PageObjectFieldAction(int sourceLine, string fieldName, string fieldType)
        : base(sourceLine)
    {
        FieldName = fieldName;
        FieldType = fieldType;
    }
}

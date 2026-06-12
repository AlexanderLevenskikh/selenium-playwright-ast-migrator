namespace Migrator.Core.Models;

/// <summary>
/// A recognized local variable declaration with extracted name and value.
/// Used for meaningful declarations like var code = ..., var name = ..., var text = ....
/// Renders as an actual var declaration in the output.
/// </summary>
public sealed class LocalDeclarationAction : TestAction
{
    public string VariableName { get; }
    public string VariableType { get; }
    public string InitializationValue { get; }

    public LocalDeclarationAction(int sourceLine, string variableName, string variableType, string initializationValue)
        : base(sourceLine, RecognitionConfidence.SyntaxFallback)
    {
        VariableName = variableName;
        VariableType = variableType;
        InitializationValue = initializationValue;
    }
}

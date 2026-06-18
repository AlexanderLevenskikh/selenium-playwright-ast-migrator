namespace Migrator.Core.Models;

public sealed class MethodInvocationAction : TestAction
{
    public string ReceiverExpression { get; }
    public string MethodName { get; }
    public string FullSourceText { get; }
    public IReadOnlyList<string> ArgumentTexts { get; }

    /// <summary>
    /// Name of the local variable assigned from this invocation, when the source was
    /// a declaration such as "var page = Browser.GoToPage&lt;T&gt;(...)".
    /// Used by parameterized mappings to substitute the special {result} placeholder.
    /// </summary>
    public string? ResultVariable { get; }

    public MethodInvocationAction(int sourceLine, string receiverExpression, string methodName, string fullSourceText, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : this(sourceLine, receiverExpression, methodName, fullSourceText, Array.Empty<string>(), confidence)
    {
    }

    public MethodInvocationAction(int sourceLine, string receiverExpression, string methodName, string fullSourceText, IReadOnlyList<string> argumentTexts, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : this(sourceLine, receiverExpression, methodName, fullSourceText, argumentTexts, null, confidence)
    {
    }

    public MethodInvocationAction(int sourceLine, string receiverExpression, string methodName, string fullSourceText, IReadOnlyList<string> argumentTexts, string? resultVariable, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        ReceiverExpression = receiverExpression;
        MethodName = methodName;
        FullSourceText = fullSourceText;
        ArgumentTexts = argumentTexts;
        ResultVariable = resultVariable;
    }
}

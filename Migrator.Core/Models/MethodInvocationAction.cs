namespace Migrator.Core.Models;

public sealed class MethodInvocationAction : TestAction
{
    public string ReceiverExpression { get; }
    public string MethodName { get; }
    public string FullSourceText { get; }
    public IReadOnlyList<string> ArgumentTexts { get; }

    public MethodInvocationAction(int sourceLine, string receiverExpression, string methodName, string fullSourceText, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : this(sourceLine, receiverExpression, methodName, fullSourceText, Array.Empty<string>(), confidence)
    {
    }

    public MethodInvocationAction(int sourceLine, string receiverExpression, string methodName, string fullSourceText, IReadOnlyList<string> argumentTexts, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        ReceiverExpression = receiverExpression;
        MethodName = methodName;
        FullSourceText = fullSourceText;
        ArgumentTexts = argumentTexts;
    }
}

namespace Migrator.Core.Models;

public sealed class MethodInvocationAction : TestAction
{
    public string ReceiverExpression { get; }
    public string MethodName { get; }
    public string FullSourceText { get; }

    public MethodInvocationAction(int sourceLine, string receiverExpression, string methodName, string fullSourceText, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : base(sourceLine, confidence)
    {
        ReceiverExpression = receiverExpression;
        MethodName = methodName;
        FullSourceText = fullSourceText;
    }
}

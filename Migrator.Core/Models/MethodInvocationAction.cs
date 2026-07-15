namespace Migrator.Core.Models;

public sealed class MethodInvocationAction : TestAction
{
    public string ReceiverExpression { get; }
    public string MethodName { get; }
    public string FullSourceText { get; }
    public IReadOnlyList<string> ArgumentTexts { get; }
    public bool IsAwaited { get; }

    /// <summary>
    /// Generic type argument texts from invocations such as Method&lt;TPage&gt;(...).
    /// Kept separately so adapter mappings can use stable placeholders such as {T},
    /// {genericType}, {type0}, etc. without parsing source text again.
    /// </summary>
    public IReadOnlyList<string> GenericArgumentTexts { get; }

    /// <summary>
    /// Local result binding assigned from this invocation. Usually this is a single
    /// variable name (for example "page"), but it may also be a deconstruction binding
    /// such as "(_, page)". Used to substitute the special {result} placeholder.
    /// </summary>
    public string? ResultVariable { get; }

    public MethodInvocationAction(int sourceLine, string receiverExpression, string methodName, string fullSourceText, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : this(sourceLine, receiverExpression, methodName, fullSourceText, Array.Empty<string>(), Array.Empty<string>(), null, confidence, false)
    {
    }

    public MethodInvocationAction(int sourceLine, string receiverExpression, string methodName, string fullSourceText, IReadOnlyList<string> argumentTexts, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : this(sourceLine, receiverExpression, methodName, fullSourceText, argumentTexts, Array.Empty<string>(), null, confidence, false)
    {
    }

    public MethodInvocationAction(int sourceLine, string receiverExpression, string methodName, string fullSourceText, IReadOnlyList<string> argumentTexts, string? resultVariable, RecognitionConfidence confidence = RecognitionConfidence.Semantic)
        : this(sourceLine, receiverExpression, methodName, fullSourceText, argumentTexts, Array.Empty<string>(), resultVariable, confidence, false)
    {
    }

    public MethodInvocationAction(
        int sourceLine,
        string receiverExpression,
        string methodName,
        string fullSourceText,
        IReadOnlyList<string> argumentTexts,
        IReadOnlyList<string> genericArgumentTexts,
        string? resultVariable,
        RecognitionConfidence confidence = RecognitionConfidence.Semantic,
        bool isAwaited = false)
        : base(sourceLine, confidence)
    {
        ReceiverExpression = receiverExpression;
        MethodName = methodName;
        FullSourceText = fullSourceText;
        ArgumentTexts = argumentTexts;
        GenericArgumentTexts = genericArgumentTexts;
        ResultVariable = resultVariable;
        IsAwaited = isAwaited;
    }
}

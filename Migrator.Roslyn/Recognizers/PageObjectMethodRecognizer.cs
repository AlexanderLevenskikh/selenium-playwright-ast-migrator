using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public class PageObjectMethodRecognizer : IInvocationRecognizer
{
    static readonly HashSet<string> KnownMethods = new()
    {
        "ClickAndOpen", "ValidateLoading", "Get", "Visible",
        "OpenSearchPage",
        "InputAndSelect", "InputTextAndSelectValue", "InputTextAndSelect",
        "ManualInputValue", "ExcludeValue", "SortSc", "ClearSort", "Sort",
        "InputText"
    };

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (KnownMethods.Contains(ctx.MethodName) && !string.IsNullOrEmpty(ctx.ReceiverText))
            return new MethodInvocationAction(ctx.SourceLine, ctx.ReceiverText, ctx.MethodName, ctx.FullText, RecognitionConfidence.SyntaxFallback);

        return null;
    }
}

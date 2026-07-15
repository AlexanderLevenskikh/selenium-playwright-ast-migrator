using Migrator.Core;
using Migrator.Core.Models;
using Migrator.Roslyn;

namespace Migrator.Roslyn.Recognizers;

/// <summary>
/// Recognizes configured select/dropdown methods and preserves them as structured
/// MethodInvocationAction instances for adapter mappings.
/// </summary>
public class SelectValueRecognizer : IInvocationRecognizer
{
    readonly IReadOnlySet<string> _selectMethods;

    public SelectValueRecognizer()
        : this(RecognizerOptions.Default.SelectMethods)
    {
    }

    public SelectValueRecognizer(IEnumerable<string> selectMethods)
    {
        _selectMethods = new HashSet<string>(
            selectMethods
                .Select(method => method?.Trim())
                .Where(method => !string.IsNullOrWhiteSpace(method))
                .Select(method => method!),
            StringComparer.Ordinal);
    }

    public TestAction? TryRecognize(InvocationContext ctx)
    {
        if (_selectMethods.Contains(ctx.MethodName) && !string.IsNullOrEmpty(ctx.ReceiverText))
        {
            return new MethodInvocationAction(
                ctx.SourceLine,
                ctx.ReceiverText,
                ctx.MethodName,
                ctx.FullText,
                ctx.ArgumentTexts,
                ctx.GenericArgumentTexts ?? Array.Empty<string>(),
                resultVariable: null,
                confidence: RecognitionConfidence.SyntaxFallback,
                isAwaited: ctx.IsAwaited);
        }

        return null;
    }
}

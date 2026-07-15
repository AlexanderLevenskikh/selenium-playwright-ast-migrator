using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

public record InvocationContext(
    string MethodName,
    string ReceiverText,
    string FullText,
    int SourceLine,
    bool SymbolResolved,
    IReadOnlyList<string> ArgumentTexts,
    IReadOnlyList<string>? GenericArgumentTexts = null,
    bool IsAwaited = false
);

public interface IInvocationRecognizer
{
    TestAction? TryRecognize(InvocationContext ctx);
}

using Migrator.Core.Models;

namespace Migrator.Roslyn.Recognizers;

/// <summary>
/// Deterministic arbitration for syntax recognizers.
///
/// Registration order is not policy. Built-in recognizers have an explicit stable precedence that
/// preserves the current intended pipeline. Unknown/custom recognizers share neutral priority 0;
/// if more than one neutral recognizer accepts the same invocation the parser emits a stable
/// AMBIGUOUS_RECOGNITION blocker instead of silently selecting whichever happened to be registered first.
/// </summary>
internal static class RecognizerArbitrator
{
    public static TestAction? Recognize(
        IReadOnlyList<IInvocationRecognizer> recognizers,
        InvocationContext context)
    {
        ArgumentNullException.ThrowIfNull(recognizers);
        ArgumentNullException.ThrowIfNull(context);

        var candidates = recognizers
            .Where(recognizer => recognizer is not UnknownInvocationRecognizer)
            .Select(recognizer => new Candidate(
                Recognizer: recognizer,
                Id: GetRecognizerId(recognizer),
                Priority: GetPriority(recognizer),
                Action: recognizer.TryRecognize(context)))
            .Where(candidate => candidate.Action is not null)
            .ToArray();

        if (candidates.Length == 0)
            return null;

        var highestPriority = candidates.Max(candidate => candidate.Priority);
        var winners = candidates
            .Where(candidate => candidate.Priority == highestPriority)
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();

        if (winners.Length == 1)
            return winners[0].Action;

        var evidence = string.Join(
            ", ",
            winners.Select(candidate =>
                $"{candidate.Id}->{candidate.Action!.GetType().Name}"));

        return new UnsupportedAction(
            context.SourceLine,
            context.FullText,
            $"AMBIGUOUS_RECOGNITION: priority={highestPriority}; candidates=[{evidence}]");
    }

    static string GetRecognizerId(IInvocationRecognizer recognizer) =>
        recognizer.GetType().FullName
        ?? recognizer.GetType().Name;

    static int GetPriority(IInvocationRecognizer recognizer) =>
        recognizer switch
        {
            // Explicit policy corresponding to the established built-in semantics.
            // The values, rather than List order, are now authoritative.
            WebDriverFindElementRecognizer => 2000,
            TableInvocationRecognizer => 1900,
            ProjectAssertionHelperRecognizer => 1850,
            FluentTextAssertionRecognizer => 1800,
            VisibilityAssertionRecognizer => 1750,
            WaitPresenceRecognizer => 1700,
            UrlAssertionRecognizer => 1650,
            FluentAssertionsRecognizer => 1600,
            AssertInvocationRecognizer => 1550,
            PlaywrightAssertionRecognizer => 1500,
            SelectValueRecognizer => 1450,
            NavigationRecognizer => 1400,
            WaitInvocationRecognizer => 1350,
            AsyncPlaywrightRecognizer => 1300,
            SendKeysInvocationRecognizer => 1200,
            ClickInvocationRecognizer => 1100,

            // Generic receiver-based preservation must never outrank a specific recognizer.
            PageObjectMethodRecognizer => -1000,

            // UnknownInvocationRecognizer is filtered before arbitration.
            UnknownInvocationRecognizer => int.MinValue,

            // Extension/custom recognizers are intentionally unordered relative to each other.
            // Multiple matches at this level become AMBIGUOUS_RECOGNITION.
            _ => 0
        };

    sealed record Candidate(
        IInvocationRecognizer Recognizer,
        string Id,
        int Priority,
        TestAction? Action);
}

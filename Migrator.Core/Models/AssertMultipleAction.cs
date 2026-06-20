using System.Collections.Generic;

namespace Migrator.Core.Models;

/// <summary>
/// Represents an NUnit Assert.Multiple(() => { ... }) wrapper.
/// The wrapper itself is not a Playwright assertion; nested actions should be
/// rendered individually while preserving a migration marker for review.
/// </summary>
public sealed class AssertMultipleAction : TestAction
{
    public string FullSourceText { get; }
    public IReadOnlyList<TestAction> Actions { get; }

    public AssertMultipleAction(
        int sourceLine,
        string fullSourceText,
        IReadOnlyList<TestAction> actions,
        RecognitionConfidence confidence = RecognitionConfidence.SyntaxFallback)
        : base(sourceLine, confidence)
    {
        FullSourceText = fullSourceText;
        Actions = actions;
    }
}

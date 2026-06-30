using System.Text.RegularExpressions;

namespace Migrator.Core;

/// <summary>
/// Converts raw report counters into a stable, explainable migration-quality board.
/// This intentionally does not invent selectors or project semantics; it points the
/// user/agent to source truth, POM/helper inventory, or a focused regression test.
/// </summary>
public static class MigrationQualityAnalyzer
{
    static readonly Regex TodoWithMigratorCode = new(@"//\s*TODO:\s*(?<message>.*?)(?:\s*\[MIGRATOR:(?<code>[A-Z0-9_]+)\])?(?:\r?$)", RegexOptions.Compiled);

    public static MigrationQualityReport Analyze(MigrationSummaryReport summary)
    {
        var todoCategories = BuildTodoCategories(summary)
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .ToList();

        var unsupportedCategories = summary.TopUnsupportedActions
            .OrderByDescending(a => a.Count)
            .ThenBy(a => a.MethodOrSourceText, StringComparer.Ordinal)
            .Select(a => new MigrationQualityUnsupportedCategory(
                MethodOrSourceText: a.MethodOrSourceText,
                Count: a.Count,
                ExampleFile: a.ExampleFile,
                ExampleLine: a.ExampleLine,
                RootCause: DescribeUnsupportedRootCause(a.MethodOrSourceText),
                NextAction: DescribeUnsupportedNextAction(a.MethodOrSourceText),
                RegressionTestIdea: DescribeUnsupportedRegressionTest(a.MethodOrSourceText)))
            .ToList();

        var unmappedTargets = summary.TopUnmappedTargets
            .OrderByDescending(t => t.Usages)
            .ThenBy(t => t.SourceExpression, StringComparer.Ordinal)
            .Select(t => new MigrationQualityUnmappedTarget(
                SourceExpression: t.SourceExpression,
                Usages: t.Usages,
                ExampleFile: t.ExampleFile,
                ExampleLine: t.ExampleLine,
                SuggestedTargetExpression: t.SuggestedTargetExpression,
                EvidenceRequired: DescribeSelectorEvidence(t.SourceExpression),
                NextAction: DescribeUnmappedNextAction(t.SourceExpression),
                RegressionTestIdea: DescribeUnmappedRegressionTest(t.SourceExpression)))
            .ToList();

        var report = new MigrationQualityReport(
            Summary: BuildSummary(summary),
            TopTodoCategories: todoCategories,
            TopUnsupportedActions: unsupportedCategories,
            TopUnmappedTargets: unmappedTargets,
            RecommendedTickets: Array.Empty<MigrationQualityTicket>(),
            Guardrails: BuildGuardrails(summary, todoCategories, unsupportedCategories, unmappedTargets));

        return report with { RecommendedTickets = BuildTickets(report) };
    }

    static MigrationQualitySummary BuildSummary(MigrationSummaryReport summary)
    {
        var totalTargets = summary.MappedTargets + summary.UnmappedTargets;
        var mappingCoverage = totalTargets == 0 ? 100.0 : Math.Round(summary.MappedTargets * 100.0 / totalTargets, 2);
        var tests = Math.Max(summary.TestsFound, 1);
        var todoDensity = Math.Round(summary.TodoComments * 1.0 / tests, 2);
        var unsupportedDensity = Math.Round(summary.UnsupportedActions * 1.0 / tests, 2);

        var qualityLevel = summary.UnsupportedActions == 0 && summary.UnmappedTargets == 0 && summary.TodoComments == 0
            ? "clean"
            : mappingCoverage >= 90 && unsupportedDensity <= 0.10 && todoDensity <= 0.25
                ? "near_ready"
                : mappingCoverage >= 70
                    ? "needs_profile_iteration"
                    : "needs_discovery";

        return new MigrationQualitySummary(
            FilesProcessed: summary.FilesProcessed,
            TestsFound: summary.TestsFound,
            ActionsFound: summary.ActionsFound,
            MappedTargets: summary.MappedTargets,
            UnmappedTargets: summary.UnmappedTargets,
            UnsupportedActions: summary.UnsupportedActions,
            TodoComments: summary.TodoComments,
            TargetMappingCoveragePercent: mappingCoverage,
            TodoCommentsPerTest: todoDensity,
            UnsupportedActionsPerTest: unsupportedDensity,
            GeneratedFilesWithWarnings: summary.FilesWithWarnings,
            QualityLevel: qualityLevel);
    }

    static IEnumerable<MigrationQualityTodoCategory> BuildTodoCategories(MigrationSummaryReport summary)
    {
        var groups = new Dictionary<string, TodoAccumulator>(StringComparer.Ordinal);

        foreach (var report in summary.PerFileReports)
        {
            foreach (var todo in ExtractTodos(report.GeneratedOutput ?? string.Empty))
            {
                if (!groups.TryGetValue(todo.Code, out var acc))
                {
                    acc = new TodoAccumulator(todo.Message, report.SourceFilePath, todo.Line);
                    groups.Add(todo.Code, acc);
                }

                acc.Count++;
            }
        }

        foreach (var kv in groups)
        {
            yield return new MigrationQualityTodoCategory(
                Code: kv.Key,
                Count: kv.Value.Count,
                ExampleFile: kv.Value.ExampleFile,
                ExampleLine: kv.Value.ExampleLine,
                ExampleMessage: kv.Value.ExampleMessage,
                RootCause: DescribeTodoRootCause(kv.Key, kv.Value.ExampleMessage),
                NextAction: DescribeTodoNextAction(kv.Key, kv.Value.ExampleMessage),
                RegressionTestIdea: DescribeTodoRegressionTest(kv.Key, kv.Value.ExampleMessage),
                SafetyRisk: DescribeTodoSafetyRisk(kv.Key, kv.Value.ExampleMessage));
        }
    }

    static IEnumerable<(string Code, string Message, int Line)> ExtractTodos(string generatedOutput)
    {
        var lines = generatedOutput.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("// TODO:", StringComparison.Ordinal))
                continue;

            var match = TodoWithMigratorCode.Match(trimmed);
            if (!match.Success)
            {
                yield return ("UNCATEGORIZED_TODO", trimmed["// TODO:".Length..].Trim(), i + 1);
                continue;
            }

            var code = match.Groups["code"].Success && !string.IsNullOrWhiteSpace(match.Groups["code"].Value)
                ? match.Groups["code"].Value.Trim()
                : InferTodoCode(match.Groups["message"].Value);

            yield return (code, match.Groups["message"].Value.Trim(), i + 1);
        }
    }

    static string InferTodoCode(string message)
    {
        if (message.Contains("depends on unresolved symbol", StringComparison.OrdinalIgnoreCase))
            return "DEPENDS_ON_UNRESOLVED_SYMBOL";
        if (message.Contains("source-only identifier", StringComparison.OrdinalIgnoreCase))
            return "SOURCE_ONLY_IDENTIFIER";
        if (message.Contains("raw statement", StringComparison.OrdinalIgnoreCase))
            return "RAW_STATEMENT";
        if (message.Contains("mapped method requires manual review", StringComparison.OrdinalIgnoreCase))
            return "MAPPED_METHOD_MANUAL_REVIEW";
        if (message.Contains("map source expression", StringComparison.OrdinalIgnoreCase) || message.Contains("missing mapping", StringComparison.OrdinalIgnoreCase))
            return "MISSING_MAPPING";
        if (message.Contains("table mapping required", StringComparison.OrdinalIgnoreCase))
            return "TABLE_MAPPING_REQUIRED";
        if (message.Contains("empty after suppression", StringComparison.OrdinalIgnoreCase))
            return "EMPTY_TEST_AFTER_SUPPRESSION";
        return "UNCATEGORIZED_TODO";
    }

    static IReadOnlyList<MigrationQualityGuardrail> BuildGuardrails(
        MigrationSummaryReport summary,
        IReadOnlyList<MigrationQualityTodoCategory> todoCategories,
        IReadOnlyList<MigrationQualityUnsupportedCategory> unsupportedCategories,
        IReadOnlyList<MigrationQualityUnmappedTarget> unmappedTargets)
    {
        var guardrails = new List<MigrationQualityGuardrail>();

        var emptyAfterSuppression = todoCategories.FirstOrDefault(c => c.Code == "EMPTY_TEST_AFTER_SUPPRESSION");
        guardrails.Add(new MigrationQualityGuardrail(
            Id: "unsafe-suppression",
            Status: emptyAfterSuppression == null ? "pass" : "attention_required",
            Description: "Suppression must never turn a test into an apparently green empty test.",
            NextAction: emptyAfterSuppression == null
                ? "No empty-test-after-suppression TODOs were found in this run."
                : $"Review {emptyAfterSuppression.Count} empty test(s), undo broad suppression, or replace it with explicit target mappings."));

        var pomLikeUnmapped = unmappedTargets.Where(t => LooksLikePomExpression(t.SourceExpression)).ToList();
        var helperLikeUnsupported = unsupportedCategories.Where(t => LooksLikeHelperCall(t.MethodOrSourceText)).ToList();
        guardrails.Add(new MigrationQualityGuardrail(
            Id: "pom-helper-recovery",
            Status: pomLikeUnmapped.Count == 0 && helperLikeUnsupported.Count == 0 ? "pass" : "attention_required",
            Description: "POM/helper recovery should use source POMs and helper bodies before raw locators or suppression.",
            NextAction: pomLikeUnmapped.Count == 0 && helperLikeUnsupported.Count == 0
                ? "No obvious POM/helper backlog was found in the top categories."
                : "Run helper-inventory and POM index, inspect helper bodies/source POMs, then add PageObject/UiTarget/ParameterizedMethods mappings backed by source truth."));

        guardrails.Add(new MigrationQualityGuardrail(
            Id: "selector-evidence",
            Status: summary.UnmappedTargets == 0 ? "pass" : "attention_required",
            Description: "Selector mappings require evidence from Selenium POMs, helper bodies, target HTML, or existing Playwright components.",
            NextAction: summary.UnmappedTargets == 0
                ? "All target expressions are mapped in this run."
                : $"Collect selector evidence for the top {Math.Min(10, unmappedTargets.Count)} unmapped target(s) before adding config."));

        return guardrails;
    }

    static IReadOnlyList<MigrationQualityTicket> BuildTickets(MigrationQualityReport report)
    {
        var tickets = new List<MigrationQualityTicket>();
        var seq = 1;

        foreach (var target in report.TopUnmappedTargets.Take(5))
        {
            tickets.Add(new MigrationQualityTicket(
                Id: $"MQ-{seq++:000}",
                Priority: PriorityFor(target.Usages),
                Title: $"Map UiTarget `{target.SourceExpression}`",
                Category: "unmapped-target",
                Occurrences: target.Usages,
                ExampleFile: target.ExampleFile,
                ExampleLine: target.ExampleLine,
                RootCause: "The source target has no adapter config mapping, so the renderer must leave a TODO locator/comment.",
                NextAction: target.NextAction,
                AcceptanceCriteria: "The unmapped target count decreases, generated code contains no TODO for this source expression, and a focused regression test covers the mapping.",
                RegressionTestIdea: target.RegressionTestIdea));
        }

        foreach (var unsupported in report.TopUnsupportedActions.Take(5))
        {
            tickets.Add(new MigrationQualityTicket(
                Id: $"MQ-{seq++:000}",
                Priority: PriorityFor(unsupported.Count),
                Title: $"Recover unsupported action `{Shorten(unsupported.MethodOrSourceText, 72)}`",
                Category: "unsupported-action",
                Occurrences: unsupported.Count,
                ExampleFile: unsupported.ExampleFile,
                ExampleLine: unsupported.ExampleLine,
                RootCause: unsupported.RootCause,
                NextAction: unsupported.NextAction,
                AcceptanceCriteria: "The unsupported action count decreases, generated output stays compilable, and a regression test covers the recovered pattern.",
                RegressionTestIdea: unsupported.RegressionTestIdea));
        }

        foreach (var todo in report.TopTodoCategories.Take(5))
        {
            tickets.Add(new MigrationQualityTicket(
                Id: $"MQ-{seq++:000}",
                Priority: PriorityFor(todo.Count),
                Title: $"Reduce TODO category `{todo.Code}`",
                Category: "todo-category",
                Occurrences: todo.Count,
                ExampleFile: todo.ExampleFile,
                ExampleLine: todo.ExampleLine,
                RootCause: todo.RootCause,
                NextAction: todo.NextAction,
                AcceptanceCriteria: "The category count decreases in migration-quality-dashboard.json and generated code remains compile-safe.",
                RegressionTestIdea: todo.RegressionTestIdea));
        }

        return tickets
            .OrderByDescending(t => PriorityRank(t.Priority))
            .ThenByDescending(t => t.Occurrences)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .Take(15)
            .ToList();
    }

    static string DescribeTodoRootCause(string code, string message) => code switch
    {
        "DEPENDS_ON_SUPPRESSED_SIDE_EFFECT" => "A statement depends on a previous helper/action that was suppressed, so executing it could change test semantics.",
        "DEPENDS_ON_UNRESOLVED_SYMBOL" => "A downstream statement uses a variable or page object that the migrator could not safely reconstruct.",
        "SOURCE_ONLY_IDENTIFIER" => "Generated output references a source-only constant/helper that is not known to exist in the target project.",
        "RAW_STATEMENT" => "The parser preserved an unknown source statement as a comment because no semantic migration pattern exists yet.",
        "TABLE_MAPPING_REQUIRED" => "The source uses table/list row access without a configured row target mapping.",
        "MISSING_MAPPING" => "A source expression reached rendering without a UiTarget/PageObject mapping.",
        "EMPTY_TEST_AFTER_SUPPRESSION" => "Suppression removed all actionable statements from a test body.",
        "ASSERTION_SUPPRESSION_BLOCKED" => "A suppression pattern matched an assertion/check, but the renderer kept it visible to avoid hiding verification logic.",
        _ when message.Contains("helper", StringComparison.OrdinalIgnoreCase) => "A project helper has no reviewed target semantics yet.",
        _ => "The generated TODO does not have a dedicated migrator code yet; it needs categorization or a renderer diagnostic code."
    };

    static string DescribeTodoNextAction(string code, string message) => code switch
    {
        "DEPENDS_ON_SUPPRESSED_SIDE_EFFECT" => "Inspect the suppressed helper body; replace broad suppression with a Method/ParameterizedMethods mapping or an explicit safe comment if the side effect is truly irrelevant.",
        "DEPENDS_ON_UNRESOLVED_SYMBOL" => "Recover the page object/helper chain that defines the symbol, then add PageObject/UiTarget mappings before touching downstream assertions.",
        "SOURCE_ONLY_IDENTIFIER" => "Map the identifier through NavigationUrls, TargetKnownIdentifiers/Types, or a target-side helper; do not inline environment-specific values.",
        "RAW_STATEMENT" => "Create a recognizer or MethodMapping for the repeated source statement; keep one-off business logic as a manual-review TODO.",
        "TABLE_MAPPING_REQUIRED" => "Add a Tables entry with RowTarget/CountTarget backed by target DOM evidence and cover ElementAt/count with a regression test.",
        "MISSING_MAPPING" => "Find the selector in Selenium POM, helper inventory, target HTML, or existing Playwright POM; add a config mapping with evidence.",
        "EMPTY_TEST_AFTER_SUPPRESSION" => "Undo or narrow the suppression; a test that became empty must be skipped explicitly or migrated with source-backed behavior.",
        "ASSERTION_SUPPRESSION_BLOCKED" => "Replace suppression with an assertion mapping or keep a visible TODO until target semantics are confirmed.",
        _ => "Add a MIGRATOR diagnostic code if this is common; otherwise create a focused regression ticket from the example line."
    };

    static string DescribeTodoRegressionTest(string code, string message) => code switch
    {
        "TABLE_MAPPING_REQUIRED" => "Add a fixture where ElementAt/count access becomes a configured table locator without TODO comments.",
        "MISSING_MAPPING" => "Add a fixture with the source expression and adapter UiTarget mapping; assert generated code uses the configured locator.",
        "EMPTY_TEST_AFTER_SUPPRESSION" => "Add a fixture proving suppression cannot produce an apparently passing empty test without an explicit TODO/skip.",
        "DEPENDS_ON_SUPPRESSED_SIDE_EFFECT" => "Add a fixture where a suppressed helper feeds a later assertion and verify the dependency remains visible.",
        "DEPENDS_ON_UNRESOLVED_SYMBOL" => "Add a fixture with a recovered page object/helper assignment and assert downstream statements are unblocked.",
        _ => "Add a minimal source fixture reproducing this TODO category and assert the intended renderer/config behavior."
    };

    static string DescribeTodoSafetyRisk(string code, string message) => code switch
    {
        "EMPTY_TEST_AFTER_SUPPRESSION" => "High: may hide lost test coverage behind a green compile/run.",
        "ASSERTION_SUPPRESSION_BLOCKED" => "High: assertion suppression can hide verification logic.",
        "DEPENDS_ON_SUPPRESSED_SIDE_EFFECT" => "High: suppressed setup/action side effects can invalidate downstream assertions.",
        "SOURCE_ONLY_IDENTIFIER" => "Medium: target code may compile only in one private environment or leak source-specific values.",
        "RAW_STATEMENT" => "Medium: unknown business logic may be lost if the comment is ignored.",
        _ => "Low/Medium: manual TODO remains visible and should be tracked by the quality dashboard."
    };

    static string DescribeUnsupportedRootCause(string sourceText)
    {
        if (LooksLikeHelperCall(sourceText))
            return "A project-specific helper/POM method was not mapped to target semantics.";
        if (sourceText.Contains("Assert", StringComparison.OrdinalIgnoreCase) || sourceText.Contains("Should", StringComparison.OrdinalIgnoreCase))
            return "An assertion pattern is not recognized or not safely mappable yet.";
        return "The recognizer pipeline does not have a semantic pattern for this source action.";
    }

    static string DescribeUnsupportedNextAction(string sourceText)
    {
        if (LooksLikeHelperCall(sourceText))
            return "Run helper-inventory, inspect the helper body and Selenium POM, then add Method/ParameterizedMethods mapping or a recognizer backed by tests.";
        if (sourceText.Contains("Assert", StringComparison.OrdinalIgnoreCase) || sourceText.Contains("Should", StringComparison.OrdinalIgnoreCase))
            return "Add an assertion recognizer/rendering case and a regression fixture proving the Playwright assertion keeps the same semantics.";
        return "Create a minimal fixture for the source action and decide whether it belongs in recognizer code, config Method mapping, or manual-review TODO.";
    }

    static string DescribeUnsupportedRegressionTest(string sourceText)
    {
        if (LooksLikeHelperCall(sourceText))
            return "Fixture: source helper call with reviewed target mapping; assert unsupported-actions count drops and no unsafe active TODO is rendered.";
        return "Fixture: source statement reproduces the unsupported category; assert a stable migrated action or explicit categorized TODO.";
    }

    static string DescribeSelectorEvidence(string sourceExpression)
    {
        if (LooksLikePomExpression(sourceExpression))
            return "Selenium POM locator, target DOM/test id, or existing Playwright POM property. Do not infer selector names from C# property names alone.";
        return "Target DOM/test id or reviewed adapter config entry. Do not infer selector names from C# property names alone.";
    }

    static string DescribeUnmappedNextAction(string sourceExpression)
    {
        if (LooksLikePomExpression(sourceExpression))
            return "Open the Selenium POM for this expression, copy the real By/DataTid/TestId evidence into UiTargets/PageObjects, then re-run migrate/verify.";
        return "Find source truth for this expression and add the smallest UiTarget mapping that preserves target semantics.";
    }

    static string DescribeUnmappedRegressionTest(string sourceExpression)
    {
        if (sourceExpression.Contains("ElementAt", StringComparison.Ordinal) || sourceExpression.Contains("Rows", StringComparison.Ordinal))
            return "Add table/list fixture covering row access and assert RowTarget/Nth rendering is used.";
        return "Add a migration fixture with this source expression and an adapter mapping; assert no TODO locator/comment is emitted for it.";
    }

    static bool LooksLikePomExpression(string value) =>
        value.Contains("page.", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Page.", StringComparison.Ordinal) ||
        value.Contains("Rows", StringComparison.Ordinal) ||
        value.Contains("Loader", StringComparison.Ordinal);

    static bool LooksLikeHelperCall(string value) =>
        value.Contains(".", StringComparison.Ordinal) && value.Contains("(", StringComparison.Ordinal);

    static string PriorityFor(int occurrences) => occurrences switch
    {
        >= 25 => "P0",
        >= 10 => "P1",
        >= 3 => "P2",
        _ => "P3"
    };

    static int PriorityRank(string priority) => priority switch
    {
        "P0" => 4,
        "P1" => 3,
        "P2" => 2,
        _ => 1
    };

    static string Shorten(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 3)] + "...";

    sealed class TodoAccumulator
    {
        public TodoAccumulator(string exampleMessage, string exampleFile, int exampleLine)
        {
            ExampleMessage = exampleMessage;
            ExampleFile = exampleFile;
            ExampleLine = exampleLine;
        }

        public int Count { get; set; }
        public string ExampleMessage { get; }
        public string ExampleFile { get; }
        public int ExampleLine { get; }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Migrator.Core.Models;

namespace Migrator.Core.Coverage;

/// <summary>
/// Builds the <c>migrator-coverage/v1</c> performance/coverage accounting for a run.
///
/// Design rules (see also docs/vnext/coverage-accounting.md):
/// * Units are enumerated once from the adapted target model exactly as the renderer sees it;
///   nested constructs appear exactly once (no double counting).
/// * A unit is <see cref="MigrationCoverageContract.States.Transformed"/> only when the
///   conservative proof (<see cref="ExecutableTargetSemantics"/>) attests an executable target
///   statement for it; anything less becomes requires_review / unsupported / ambiguous.
/// * Source→generated linkage is preserved where renderer data provides it (mapped methods carry
///   the emitted target statements); other transformed units emit at least one active statement.
/// * The report contains no timestamps and only relative paths; the CoverageSha256 is stable for
///   identical inputs (order-insensitive, canonical JSON).
/// </summary>
public static class CoverageReportBuilder
{
    public static string BuildProjectId(IEnumerable<string> relativePaths) =>
        CanonicalJsonHasher.ComputeSha256(relativePaths.OrderBy(p => p, StringComparer.Ordinal).ToArray());

    public static string BuildFileId(string relativePath) =>
        CanonicalJsonHasher.ComputeSha256(relativePath);

    public static CoverageProjectReport Build(
        IReadOnlyList<PipelineResult> results,
        string inputRoot,
        string? backendId,
        IReadOnlyList<CoverageExcludedFile>? excludedFiles = null,
        IReadOnlyList<string>? discoveredButUnsurveyed = null)
    {
        if (results == null) throw new ArgumentNullException(nameof(results));

        var backend = string.IsNullOrWhiteSpace(backendId) ? "playwright-dotnet" : backendId!;
        var inputRootNormalized = NormalizeRoot(inputRoot);
        var excluded = (excludedFiles ?? Array.Empty<CoverageExcludedFile>())
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var gapFiles = (discoveredButUnsurveyed ?? Array.Empty<string>())
            .Select(p => RelativeOf(p, inputRootNormalized))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        var filesDetail = new List<CoverageFileUnit>(results.Count + gapFiles.Length);
        var allTransformed = 0;
        var allRequiresReview = 0;
        var allUnsupported = 0;
        var allAmbiguous = 0;
        var allParseError = 0;
        var allConstructs = 0;
        var allTests = 0;
        var detectionComplete = gapFiles.Length == 0;

        foreach (var result in results.OrderBy(r => RelativeOf(r.SourceModel.FilePath, inputRootNormalized), StringComparer.Ordinal))
        {
            var fileUnit = BuildFile(result, inputRootNormalized, backend);
            filesDetail.Add(fileUnit);
            detectionComplete = detectionComplete && fileUnit.SurveyComplete;
            allTransformed += fileUnit.Transformed;
            allRequiresReview += fileUnit.RequiresReview;
            allUnsupported += fileUnit.Unsupported;
            allAmbiguous += fileUnit.Ambiguous;
            allParseError += fileUnit.ParseError;
            allConstructs += fileUnit.ConstructCount;
            allTests += fileUnit.Tests.Count;
        }

        foreach (var gap in gapFiles)
        {
            filesDetail.Add(new CoverageFileUnit(
                Id: CanonicalJsonHasher.ComputeSha256(gap),
                RelativePath: gap,
                State: "none",
                ConstructCount: 0,
                Transformed: 0,
                RequiresReview: 0,
                Unsupported: 0,
                Ambiguous: 0,
                ParseError: 0,
                SurveyComplete: false,
                FailureMessage: "Discovered under the input root as a *.cs fixture but produced no surveyed test model (no test class). The survey is partial until every discovered file is surveyed or explicitly excluded.",
                Tests: Array.Empty<CoverageTestUnit>(),
                SetUp: Array.Empty<CoverageConstructUnit>()));
        }

        var unexplainedResidual = allRequiresReview + allAmbiguous + allParseError;
        var classificationComplete = allConstructs == 0
            ? detectionComplete // an empty successful survey is still complete as a survey
            : true; // every surveyed construct always receives a state by construction
        var surveyComplete = detectionComplete && classificationComplete;
        var transformationComplete = surveyComplete
                                     && unexplainedResidual == 0
                                     && allUnsupported == 0;

        var surveyStatus = surveyComplete ? "complete" : "partial";
        var transformationStatus = transformationComplete ? "complete" : "incomplete";

        var report = new CoverageProjectReport(
            SchemaVersion: MigrationCoverageContract.SchemaVersion,
            Id: BuildProjectId(filesDetail.Select(f => f.RelativePath)),
            SourceRootRelative: SourceRootLabel(inputRootNormalized),
            TargetBackend: backend,
            SurveyStatus: surveyStatus,
            TransformationStatus: transformationStatus,
            AcceptanceStatus: null,
            DetectionComplete: detectionComplete,
            ClassificationComplete: classificationComplete,
            Files: filesDetail.Count,
            Tests: allTests,
            Constructs: allConstructs,
            Transformed: allTransformed,
            RequiresReview: allRequiresReview,
            Unsupported: allUnsupported,
            Ambiguous: allAmbiguous,
            ParseError: allParseError,
            ExcludedFiles: excluded.Length,
            UnexplainedResidual: unexplainedResidual,
            CoverageSha256: string.Empty,
            FilesDetail: filesDetail,
            Excluded: excluded);

        return report with { CoverageSha256 = CanonicalJsonHasher.ComputeSha256(report) };
    }

    static CoverageFileUnit BuildFile(PipelineResult result, string inputRoot, string backend)
    {
        var targetModel = result.TargetModel;
        var relativePath = RelativeOf(targetModel.FilePath, inputRoot);

        var proof = ExecutableTargetSemantics.Analyze(
            TestActionTraversal.Flatten(AllActions(targetModel)));
        var proofIssuesByLine = proof.Issues
            .Where(i => i.SourceLine is > 0)
            .GroupBy(i => i.SourceLine!.Value)
            .ToDictionary(g => g.Key, g => g.Select(i => i.Category).ToArray(), comparer: null);
        var hasUnattributedProofIssue = proof.Issues.Any(i => i.SourceLine is null or <= 0);

        var setupUnits = new List<CoverageConstructUnit>();
        Walk(targetModel.SetUpActions, blocked: false, backend, proofIssuesByLine, "SetUp", setupUnits);

        var tests = new List<CoverageTestUnit>();
        foreach (var test in targetModel.Tests)
        {
            var constructs = new List<CoverageConstructUnit>();
            Walk(test.BodyActions, blocked: false, backend, proofIssuesByLine, test.Name, constructs);
            tests.Add(BuildTest(test, constructs));
        }

        var fileTransformed = setupUnits.Count(u => u.State == MigrationCoverageContract.States.Transformed)
                              + tests.Sum(t => t.TransformedCount);
        var fileRequiresReview = setupUnits.Count(u => u.State == MigrationCoverageContract.States.RequiresReview)
                                 + tests.Sum(t => t.Constructs.Count(u => u.State == MigrationCoverageContract.States.RequiresReview));
        var fileUnsupported = setupUnits.Count(u => u.State == MigrationCoverageContract.States.Unsupported)
                              + tests.Sum(t => t.Constructs.Count(u => u.State == MigrationCoverageContract.States.Unsupported));
        var fileAmbiguous = setupUnits.Count(u => u.State == MigrationCoverageContract.States.Ambiguous)
                            + tests.Sum(t => t.Constructs.Count(u => u.State == MigrationCoverageContract.States.Ambiguous));
        var fileParseError = setupUnits.Count(u => u.State == MigrationCoverageContract.States.ParseError)
                             + tests.Sum(t => t.Constructs.Count(u => u.State == MigrationCoverageContract.States.ParseError));
        var constructCount = fileTransformed + fileRequiresReview + fileUnsupported + fileAmbiguous + fileParseError;

        var state = DeriveAggregateState(constructCount, fileTransformed, fileRequiresReview, fileUnsupported, fileAmbiguous, fileParseError);

        return new CoverageFileUnit(
            Id: CanonicalJsonHasher.ComputeSha256(relativePath),
            RelativePath: relativePath,
            State: state,
            ConstructCount: constructCount,
            Transformed: fileTransformed,
            RequiresReview: fileRequiresReview,
            Unsupported: fileUnsupported,
            Ambiguous: fileAmbiguous,
            ParseError: fileParseError,
            SurveyComplete: !hasUnattributedProofIssue,
            FailureMessage: hasUnattributedProofIssue
                ? "ExecutableTargetSemantics reports a proof issue without an attributable source line; classification is conservative until it is attributed."
                : null,
            Tests: tests,
            SetUp: setupUnits);
    }

    static CoverageTestUnit BuildTest(TestModel test, IReadOnlyList<CoverageConstructUnit> constructs)
    {
        var transformed = constructs.Count(u => u.State == MigrationCoverageContract.States.Transformed);
        var actionable = constructs.Count(u => u.State is
            MigrationCoverageContract.States.RequiresReview
            or MigrationCoverageContract.States.Ambiguous
            or MigrationCoverageContract.States.ParseError);
        var unsupportedOnly = constructs.Count > 0 && transformed == 0
                              && constructs.All(u => u.State == MigrationCoverageContract.States.Unsupported);
        var ambiguousAny = constructs.Any(u => u.State == MigrationCoverageContract.States.Ambiguous);
        var reviewAny = constructs.Any(u => u.State == MigrationCoverageContract.States.RequiresReview);
        var parseAny = constructs.Any(u => u.State == MigrationCoverageContract.States.ParseError);

        string state;
        if (constructs.Count == 0)
            state = "none";
        else if (constructs.All(u => u.State == MigrationCoverageContract.States.Transformed))
            state = MigrationCoverageContract.States.Transformed;
        else if (transformed > 0)
            state = "partial";
        else if (parseAny)
            state = MigrationCoverageContract.States.ParseError;
        else if (ambiguousAny)
            state = MigrationCoverageContract.States.Ambiguous;
        else if (reviewAny)
            state = MigrationCoverageContract.States.RequiresReview;
        else if (unsupportedOnly)
            state = MigrationCoverageContract.States.Unsupported;
        else
            state = MigrationCoverageContract.States.RequiresReview;

        return new CoverageTestUnit(
            Id: CanonicalJsonHasher.ComputeSha256(new { test.Name }),
            Name: test.Name,
            State: state,
            ConstructCount: constructs.Count,
            TransformedCount: transformed,
            ActionableCount: actionable,
            Constructs: constructs);
    }

    /// <summary>
    /// Recursively walks a block in source order, emitting one construct unit per node
    /// (containers and leaves), and propagates "inside a blocked container" so leaves under
    /// an execution-unsafe collection are never counted as transformed.
    /// </summary>
    static void Walk(
        IEnumerable<TestAction> actions,
        bool blocked,
        string backend,
        IReadOnlyDictionary<int, string[]> proofIssuesByLine,
        string scope,
        List<CoverageConstructUnit> sink)
    {
        foreach (var action in actions)
        {
            var selfBlocking = !blocked && IsBlockedContainer(action);
            var containerBlocked = blocked || selfBlocking;
            var unit = Classify(action, containerBlocked, backend, proofIssuesByLine, scope, selfBlocking);
            sink.Add(unit);

            switch (action)
            {
                case ConditionalBlockAction conditional:
                    Walk(conditional.IfActions, containerBlocked, backend, proofIssuesByLine, scope, sink);
                    foreach (var (_, branchActions) in conditional.ElseIfActions)
                        Walk(branchActions, containerBlocked, backend, proofIssuesByLine, scope, sink);
                    Walk(conditional.ElseActions, containerBlocked, backend, proofIssuesByLine, scope, sink);
                    break;
                case CollectionForEachAction collection:
                    Walk(collection.BodyActions, containerBlocked, backend, proofIssuesByLine, scope, sink);
                    break;
                case AssertMultipleAction multiple:
                    Walk(multiple.Actions, containerBlocked, backend, proofIssuesByLine, scope, sink);
                    break;
            }
        }
    }

    /// <summary>
    /// A collection foreach whose collection target is unresolved cannot execute its body;
    /// the renderer/verifier proof (ExecutableTargetSemantics) treats it as SemanticNoOp.
    /// </summary>
    static bool IsBlockedContainer(TestAction action)
    {
        if (action is not CollectionForEachAction collection)
            return false;
        var target = ReportBuilder.GetTargetOf(collection);
        return target is { Kind: TargetKind.Unresolved };
    }

    static CoverageConstructUnit Classify(
        TestAction action,
        bool blocked,
        string backend,
        IReadOnlyDictionary<int, string[]> proofIssuesByLine,
        string scope,
        bool selfBlocking = false)
    {
        var line = action.SourceLine;
        var kind = action.GetType().Name;

        if (blocked)
        {
            var reason = selfBlocking
                ? "Collection execution target is unresolved; the body cannot run as generated code, so the whole block requires review."
                : "Inside a collection whose execution target is unresolved; the body cannot run as generated code.";
            return Unit(kind, MigrationCoverageContract.States.RequiresReview, line, null, null, reason, null, scope);
        }

        switch (action)
        {
            case UnsupportedAction unsupported:
            {
                var state = unsupported.Reason.StartsWith("AMBIGUOUS_RECOGNITION", StringComparison.Ordinal)
                    ? MigrationCoverageContract.States.Ambiguous
                    : MigrationCoverageContract.States.Unsupported;
                return Unit(kind, state, line, 0, null, unsupported.Reason, null, scope);
            }
            case RawStatementAction:
                return Unit(kind, MigrationCoverageContract.States.RequiresReview, line, 0, null,
                    "Raw statement passthrough (no renderer proof of safety); requires review.", null, scope);
            case WaitForAction wait when wait.Kind == WaitForKind.ReviewRequired:
                return Unit(kind, MigrationCoverageContract.States.RequiresReview, line, 0, null,
                    $"Custom wait '{wait.SourceMethod}' is emitted only as TODO/comment until a concrete product-state assertion is provided.", null, scope);
            case MappedMethodInvocationAction mapped:
            {
                var statements = mapped.GetTargetStatements(backend)
                    .SelectMany(s => s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Where(s => s.Length > 0 && !s.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    .Take(64)
                    .ToArray();
                var requiresReview = proofIssuesByLine.TryGetValue(line, out var categories)
                                     || statements.Length == 0
                                     || mapped.RequiresReview;
                if (requiresReview)
                {
                    var detail = categories is { Length: > 0 } ? $" proof={string.Join(",", categories)}" : string.Empty;
                    return Unit(kind, MigrationCoverageContract.States.RequiresReview, line, 0, null,
                        $"Mapped method '{mapped.SourceMethod ?? mapped.FullSourceText}' emits no provably executable code{detail}.", null, scope);
                }
                return Unit(kind, MigrationCoverageContract.States.Transformed, line,
                    statements.Length, TruncateEach(statements),
                    null, null, scope);
            }
            case MappedExpressionAssertionAction mappedAssertion when mappedAssertion.RequiresReview
                                                                   || proofIssuesByLine.ContainsKey(line):
                return Unit(kind, MigrationCoverageContract.States.RequiresReview, line, 0, null,
                    $"Mapped expression assertion '{mappedAssertion.SourceMethod ?? mappedAssertion.FullSourceText}' cannot be emitted as an executable assertion.", null, scope);
            case MethodInvocationAction:
                return Unit(kind, MigrationCoverageContract.States.RequiresReview, line, 0, null,
                    "Generic method invocation is emitted as comment + TODO (manual review): the renderer produces no executable target code with a proven equivalent. (Narrow mapped data-assertion case is the documented exception; this construct stays review until proven.)", null, scope);
            case AssertMultipleAction:
                return Unit(kind, MigrationCoverageContract.States.RequiresReview, line, 0, null,
                    "Assert.Multiple wrapper is elided to a comment; nested assertions are classified individually and are never silently assumed preserved.", null, scope);
        }

        var target = ReportBuilder.GetTargetOf(action);
        if (target is { Kind: TargetKind.Unresolved })
        {
            return Unit(kind, MigrationCoverageContract.States.RequiresReview, line, 0, null,
                $"Target mapping unresolved for '{target.SourceExpression}' on {scope}.", null, scope);
        }

        if (proofIssuesByLine.TryGetValue(line, out var issueCategories))
        {
            return Unit(kind, MigrationCoverageContract.States.RequiresReview, line, 0, null,
                $"Renderer proof issue on this line ({string.Join(",", issueCategories)}); not counted as transformed.", null, scope);
        }

        return Unit(kind, MigrationCoverageContract.States.Transformed, line, 1, null, null, null, scope);
    }

    static CoverageConstructUnit Unit(
        string kind,
        string state,
        int line,
        int? outputOperationCount,
        IReadOnlyList<string>? outputOperations,
        string? reason,
        string? policySource,
        string scope)
    {
        var id = CanonicalJsonHasher.ComputeSha256(new { scope, line, kind, state });
        return new CoverageConstructUnit(
            Id: id,
            Kind: kind,
            State: state,
            SourceLine: line,
            OutputOperationCount: outputOperationCount,
            OutputOperations: outputOperations,
            Reason: reason,
            PolicySource: policySource);
    }

    static IReadOnlyList<string> TruncateEach(IReadOnlyList<string> statements) =>
        statements.Select(s => s.Length <= 180 ? s : s.Substring(0, 180) + "…").ToArray();

    static string DeriveAggregateState(
        int constructCount, int transformed, int requiresReview, int unsupported, int ambiguous, int parseError)
    {
        if (constructCount == 0)
            return "none";
        if (transformed == constructCount)
            return MigrationCoverageContract.States.Transformed;
        if (parseError > 0 && transformed == 0)
            return MigrationCoverageContract.States.ParseError;
        if (ambiguous > 0 && transformed == 0)
            return MigrationCoverageContract.States.Ambiguous;
        if (unsupported > 0 && transformed == 0 && requiresReview == 0)
            return MigrationCoverageContract.States.Unsupported;
        if (requiresReview > 0 && transformed == 0)
            return MigrationCoverageContract.States.RequiresReview;
        return "partial";
    }

    static IEnumerable<TestAction> AllActions(TestFileModel model) =>
        model.SetUpActions.Concat(model.Tests.SelectMany(t => t.BodyActions));

    static string RelativeOf(string filePath, string inputRoot)
    {
        if (string.IsNullOrEmpty(filePath))
            return filePath ?? string.Empty;
        var full = Path.GetFullPath(filePath);
        var root = Path.GetFullPath(inputRoot);
        var relative = Path.GetRelativePath(root, full);
        return NormalizeRelative(relative);
    }

    static string NormalizeRoot(string root) =>
        string.IsNullOrWhiteSpace(root)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(root);

    /// <summary>
    /// A stable, machine-independent label for the surveyed root: relative to its own parent
    /// directory (e.g. "MyApp.Tests"), falling back to the leaf name. Never an absolute path,
    /// so the report stays diffable across machines and working directories.
    /// </summary>
    static string SourceRootLabel(string inputRootNormalized)
    {
        var parent = Path.GetDirectoryName(inputRootNormalized);
        if (!string.IsNullOrEmpty(parent))
            return RelativeOf(inputRootNormalized, parent);
        var name = Path.GetFileName(inputRootNormalized);
        return string.IsNullOrEmpty(name) ? inputRootNormalized : name;
    }

    static string NormalizeRelative(string relative) =>
        relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
}

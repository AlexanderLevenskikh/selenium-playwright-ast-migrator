using System;
using Migrator.Core;

// Small DTOs used by CLI report builders and command classes.
// Keeping them out of Program.cs makes the entrypoint readable while preserving
// the existing JSON/report contracts.

record ProfileInputFile(string Path, string Text);
record ProfileRuleInfo(string Section, string Key, int Weight);
record ProfileMatchReport(DateTimeOffset GeneratedAtUtc, string InputPath, string[] ConfigLayers, double OverallScore, string Recommendation, ProfileLayerMatch[] Layers, ProjectProfileSignal[] ProjectSignals, string[] Gaps, string[] RecommendedNextActions);
record ProfileLayerMatch(string ConfigPath, string SourceProjectName, int TotalRules, int MatchedRules, double Score, string Verdict, ProfileRuleMatch[] TopMatchedRules, ProfileRuleMatch[] UnusedRules);
record ProfileRuleMatch(string Section, string Key, int Hits, string ExampleFile, int ExampleLine);
record ProjectProfileSignal(string Expression, int Occurrences, string ExampleFile, int ExampleLine, string CoveredBy);
class ProjectProfileSignalBuilder
{
    public string Expression { get; }
    public string ExampleFile { get; }
    public int ExampleLine { get; }
    public int Occurrences { get; set; }

    public ProjectProfileSignalBuilder(string expression, string exampleFile, int exampleLine)
    {
        Expression = expression;
        ExampleFile = exampleFile;
        ExampleLine = exampleLine;
    }

    public ProjectProfileSignal ToSignal(string coveredBy) => new(Expression, Occurrences, ExampleFile, ExampleLine, coveredBy);
}

class ArtifactSummary
{
    public int FilesProcessed { get; set; }
    public int TestsFound { get; set; }
    public int ActionsFound { get; set; }
    public int SemanticActions { get; set; }
    public int SyntaxFallbackActions { get; set; }
    public int UnsupportedActions { get; set; }
    public int MappedTargets { get; set; }
    public int UnmappedTargets { get; set; }
    public int TodoComments { get; set; }
    public int SyntaxErrors { get; set; }
    public string? VerifyStatus { get; set; }
}

record DotnetBuildResult(int ExitCode, string Command, string StdOut, string StdErr);
record BootstrapProjectReport(DateTimeOffset GeneratedAtUtc, string ProjectName, string InputPath, string BaseProfilePath, string ProjectProfilePath, string? NearestProjectPath, string[] Warnings);
record ProjectVerifyReport(
    DateTimeOffset GeneratedAtUtc,
    string Status,
    int ExitCode,
    string[] GeneratedFiles,
    string HarnessProject,
    string BaseDirectory,
    string? Solution,
    string BuildWorkingDirectory,
    string[] ProjectReferences,
    ProjectReferenceDiscovery[] ProjectReferenceDiscovery,
    string[] AssemblyReferences,
    PackageReferenceConfig[] PackageReferences,
    string[] BuildFilesImported,
    string TargetFramework,
    string Command,
    string StdOut,
    string StdErr,
    string[] Diagnostics,
    ProjectVerifyDiagnostic[] ClassifiedDiagnostics);

record ProjectReferenceDiscovery(string Path, string Source, string Status, string Reason);
record ProjectVerifyDiagnostic(string Raw, string Code, string Severity, string Category, string? File, int? Line, string LikelyCause, string SuggestedAction);

record TodoExplanationReport(
    DateTimeOffset GeneratedAtUtc,
    string Source,
    string ArtifactRoot,
    bool RecursiveArtifactLookup,
    int FilesProcessed,
    int TestsFound,
    int ActionsFound,
    int SemanticActions,
    int SyntaxFallbackActions,
    int MappedTargets,
    int UnmappedTargets,
    int UnsupportedActions,
    int TodoComments,
    int SyntaxErrors,
    string? ProjectVerifyStatus,
    TodoInsight[] Insights,
    NormalizedTodoGroup[] NormalizedRootCauses,
    TableMappingCandidate[] TableMappingCandidates,
    string NextBestAction);

record TodoInsight(
    string Category,
    string Title,
    string Reason,
    int EstimatedImpact,
    string ExampleFile,
    int ExampleLine,
    string SuggestedAction,
    bool RequiresSourceTruth,
    bool RequiresDeveloper,
    string[] Evidence);
record NormalizedTodoGroup(
    string Category,
    string GroupKey,
    string DisplayName,
    int Count,
    string ExampleFile,
    int ExampleLine,
    string SuggestedAction,
    string[] RepresentativeFiles,
    string[] Evidence);
record TableMappingCandidate(
    string GroupKey,
    string SourceRoot,
    string AccessorKind,
    string AssertionKind,
    int Count,
    string ExampleFile,
    int ExampleLine,
    string SourceExpression,
    string SuggestedUiTargetRoot,
    string SuggestedConfigHint,
    string[] Evidence);
record AgentNextTaskPlan(string Priority, string Category, string Title, string Reason, string Action, string ExampleFile, int ExampleLine, string[] Evidence);

record GeneratedTestMethodStats(string File, string TestName, int StartLine, int ActiveLines, int TodoLines, int ExecutableLines, double ActiveRatio, int AwaitCount, int ExpectOrAssertCount, int LocatorCount);
record MigrationBoardReport(DateTimeOffset GeneratedAtUtc, string Source, string ArtifactRoot, bool RecursiveArtifactLookup, ArtifactLookupCandidate[] ArtifactCandidates, ArtifactSummary Summary, MigrationQualityGates QualityGates, string? ProjectVerifyStatus, int ProjectDiagnostics, int GeneratedFiles, int RuntimeReadyCandidates, int SmokeCandidates, MigrationBoardFileCard[] FileCards, TodoInsight[] TopInsights, NormalizedTodoGroup[] TopNormalizedRootCauses, TableMappingCandidate[] TableMappingCandidates, SmokeCandidate[] TopSmokeCandidates, string[] RecommendedNextActions, string[] Artifacts);
record ReportServeDashboardReport(DateTimeOffset GeneratedAtUtc, string Source, string ArtifactRoot, bool RecursiveArtifactLookup, MigrationBoardReport Current, ReportServeRunTrend[] Trends, ReportServeTodoCodeGroup[] TodoExplorer, ReportServeCountItem[] UnsupportedActions, ReportServeCountItem[] UnmappedTargets, RuntimeFailureGroup[] RuntimeFailures, string[] MissingArtifacts, string[] StaticFiles, string? EvidenceZipPath);
record ReportServeRunTrend(string Name, string Path, DateTimeOffset LastWriteTimeUtc, int FilesProcessed, int TestsFound, int GeneratedFiles, int TodoComments, int UnsupportedActions, int UnmappedTargets, int CompileErrors, string ProjectVerifyStatus);
record ReportServeTodoCodeGroup(string Code, int Count, string ExampleFile, int ExampleLine);
record ReportServeCountItem(string Name, int Count, string ExampleFile, int ExampleLine);
record MigrationQualityGates(string ProjectVerifyStatus, int CompileErrors, int EmptyTestsAfterSuppression, int SuppressedSideEffectDependencies, int? SuppressedMethodPatterns, int? SuspiciousSuppressionPatterns, string[] Warnings);
record MigrationBoardFileCard(string File, int Tests, int TodoLines, int CompileErrors, int CompileWarnings, double ActiveRatio, double BestScore, string BestReadinessLevel, string BestTestName);
record SmokePlanReport(DateTimeOffset GeneratedAtUtc, string Source, string ArtifactRoot, bool RecursiveArtifactLookup, string? ProjectVerifyStatus, int GeneratedFiles, int TestsFound, int RuntimeReadyCandidates, int SmokeCandidates, SmokeCandidate[] Candidates, string[] RecommendedNextActions);
record SmokeCandidate(string File, string TestName, int StartLine, int ActiveLines, int TodoLines, double ActiveRatio, int CompileErrors, int CompileWarnings, int AwaitCount, int ExpectOrAssertCount, int LocatorCount, double Score, string ReadinessLevel, string[] Checklist);
record ArtifactLookupCandidate(string FileName, string Path, DateTimeOffset LastWriteTimeUtc);
sealed class ArtifactLookupException : Exception
{
    public ArtifactLookupException(string message) : base(message)
    {
    }
}

record PomIndexReport(DateTimeOffset GeneratedAtUtc, string InputPath, int FilesScanned, PomFact[] Facts, PomUsageCandidate[] InferredCandidates, string[] Warnings);
record PomFact(string SourceExpression, string OwnerType, string MemberName, string MemberKind, string Selector, string SelectorKind, string TargetKindSuggestion, string TargetExpressionSuggestion, string SourceFile, int SourceLine, string Confidence, bool RequiresReview, string Notes);
record PomUsageCandidate(string SourceExpression, string SuggestedTargetExpression, string SuggestedTargetKind, int Usages, string ExampleFile, int ExampleLine, string Confidence, bool RequiresSourceTruth, string Notes);



record TypeScriptVerifyReport(DateTimeOffset GeneratedAtUtc, string Status, string InputPath, string TsProjectPath, string[] GeneratedFiles, string VerifyDirectory, string TsConfigPath, string Command, int ExitCode, string StdOut, string StdErr, string[] Diagnostics, TypeScriptVerifyDiagnostic[] ClassifiedDiagnostics);
record TypeScriptVerifyDiagnostic(string Raw, string Code, string Severity, string Category, string LikelyCause, string SuggestedAction);

record RuntimeFailureReport(DateTimeOffset GeneratedAtUtc, string Source, int FilesScanned, int Observations, RuntimeFailureGroup[] Groups, string[] RecommendedNextActions);
record RuntimeFailureGroup(string Category, int Count, string Severity, string LikelyCause, string SuggestedAction, RuntimeFailureObservation[] Examples);
record RuntimeFailureObservation(string Category, string File, int Line, string? TestName, string Message, string Snippet);
record ConfigSchemaReport(DateTimeOffset GeneratedAtUtc, string SchemaPath, string SchemaName, string[] SuggestedUsage);

record ConfigSafetyReport(DateTimeOffset GeneratedAtUtc, string ConfigPath, string ValidationMode, string Status, ConfigSafetyIssue[] Issues, ConfigMetric[] Metrics);
record ConfigSafetyIssue(string Severity, string Code, string Message, string? Location, string SuggestedAction);
record ConfigMetric(string Name, int Value);
record ConfigDiffReport(DateTimeOffset GeneratedAtUtc, string BeforePath, string AfterPath, ConfigDiffChange[] Changes, ConfigSafetyIssue[] Risks, string[] Summary);
record ConfigDiffChange(string Section, string ChangeType, string Key);
record ConfigDiffInput(string Path, string Kind, ProjectAdapterConfig Config);
record GuardReport(DateTimeOffset GeneratedAtUtc, string BeforePath, string AfterPath, string Status, GuardCheck[] Checks, string[] Summary);
record GuardCheck(string Name, string Status, string Message, int? Before, int? After);

record DoctorReport(DateTimeOffset GeneratedAtUtc, string Status, string InputPath, string InputKind, string[] ConfigLayers, string WorkspaceOutPath, DoctorCheck[] Checks, string[] RecommendedNextActions);
record DoctorCheck(string Status, string Code, string Message, string? Location, string SuggestedAction);
record SimpleProcessResult(int ExitCode, string StdOut, string StdErr);

record CliOptions(string Mode, string Input, string Out, string? Config, string[] Configs, string Format, bool FailOnUnsupported, bool FailOnTodo, string Workspace, string? Before, string? After, string Target, string Source, bool SourceExplicit, string? TsProject, bool RecursiveArtifacts, string IrVersion, string RenderIr, string ValidationMode, string? TargetTestFramework, bool Wizard, bool? InstallAgentKit, bool? TargetProjectExists, string? TargetProjectPath, string? DefaultTestIdAttribute, string? TargetNamespace, string? TargetBaseClass, bool Fix, bool Apply, bool DryRun, int Port, bool StaticOnly);

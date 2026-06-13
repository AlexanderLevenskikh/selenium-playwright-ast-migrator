using System;
using System.Collections.Generic;
using System.IO;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.Roslyn;
using Migrator.SeleniumCSharp;

var opts = ParseArgs(args);

if (opts == null)
    return 1;

string mode = opts.Mode;
string inputPath = opts.Input;
string outPath = opts.Out;
string? configPath = opts.Config;
string format = opts.Format;
bool failOnUnsupported = opts.FailOnUnsupported;
bool failOnTodo = opts.FailOnTodo;

if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
{
    Console.Error.WriteLine($"Input not found: {inputPath}");
    return 1;
}

var parser = new RoslynTestFileParser();
var renderer = new PlaywrightDotNetRenderer();

IProjectAdapter? adapter = null;

if (!string.IsNullOrEmpty(configPath))
{
    if (!File.Exists(configPath))
    {
        Console.Error.WriteLine($"Config not found: {configPath}");
        return 1;
    }
    adapter = new DefaultProjectAdapter(configPath);
    Console.WriteLine($"Loaded adapter config: {configPath}");
}

// Handle propose mode separately — input is a directory with report artifacts, not source files
if (mode == "propose")
{
    var proposeExitCode = RunPropose(inputPath, outPath, configPath, format);
    return proposeExitCode;
}

var pipeline = new MigrationPipeline(parser, renderer, adapter);

IEnumerable<PipelineResult> results;

if (Directory.Exists(inputPath))
{
    results = pipeline.ProcessDirectory(inputPath);
}
else
{
    results = new[] { pipeline.ProcessFile(inputPath) };
}

var resultsList = results.ToList();
var summary = BuildSummary(resultsList, out var allUnmapped);

switch (mode)
{
    case "analyze":
        RunAnalyze(summary, outPath, format, configPath, resultsList, allUnmapped);
        break;
    case "migrate":
        RunMigrate(summary, outPath, format, configPath, resultsList, allUnmapped);
        break;
    case "verify":
        {
            var verifyConfig = configPath != null ? System.Text.Json.JsonSerializer.Deserialize<ProjectAdapterConfig>(File.ReadAllText(configPath)) : null;
            var verifyAdapter = adapter as DefaultProjectAdapter;
            var verifyExitCode = RunVerify(summary, outPath, format, resultsList, verifyConfig, verifyAdapter);
            if (verifyExitCode != 0)
                return verifyExitCode;
        }
        break;
    default:
        Console.Error.WriteLine($"Unknown mode: {mode}");
        return 1;
}

int exitCode = ApplyQualityGate(summary, failOnUnsupported, failOnTodo);
return exitCode;

// --- Quality gate ---

static int ApplyQualityGate(MigrationSummaryReport summary, bool failOnUnsupported, bool failOnTodo)
{
    if (failOnUnsupported && summary.UnsupportedActions > 0)
    {
        Console.Error.WriteLine($"Quality gate: {summary.UnsupportedActions} unsupported action(s) found.");
        return 2;
    }

    if (failOnTodo && summary.TodoComments > 0)
    {
        Console.Error.WriteLine($"Quality gate: {summary.TodoComments} TODO comment(s) found.");
        return 3;
    }

    return 0;
}

// --- Mode implementations ---

static void RunAnalyze(MigrationSummaryReport summary, string outPath, string format, string? configPath, List<PipelineResult> results, IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnmapped)
{
    Directory.CreateDirectory(outPath);

    var allUnsupported = CollectAllUnsupported(results);
    WriteReports(summary, outPath, format, allUnmapped, allUnsupported);
    GenerateDraftConfig(allUnmapped, outPath, configPath);

    PrintSummary(summary);
    Console.WriteLine($"Analysis written to: {Path.GetFullPath(outPath)}");
}

static void RunMigrate(MigrationSummaryReport summary, string outPath, string format, string? configPath, List<PipelineResult> results, IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnmapped)
{
    Directory.CreateDirectory(outPath);

    int generated = 0;
    var writtenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var result in results)
    {
        string baseName = $"{result.SourceModel.ClassName}Playwright.cs";
        string outName = ResolveFileName(outPath, baseName, writtenNames);
        var fullOut = Path.Combine(outPath, outName);
        File.WriteAllText(fullOut, result.GeneratedOutput);
        generated++;
    }

    var summaryWithGenerated = summary with { GeneratedFiles = generated };

    var allUnsupported = CollectAllUnsupported(results);
    WriteReports(summaryWithGenerated, outPath, format, allUnmapped, allUnsupported);
    GenerateDraftConfig(allUnmapped, outPath, configPath);

    PrintSummary(summaryWithGenerated);
    Console.WriteLine($"Migration written to: {Path.GetFullPath(outPath)} ({generated} files generated)");
}

static string ResolveFileName(string dir, string baseName, ISet<string> usedNames)
{
    if (!usedNames.Contains(baseName))
    {
        usedNames.Add(baseName);
        return baseName;
    }

    var ext = Path.GetExtension(baseName);
    var stem = Path.GetFileNameWithoutExtension(baseName);
    int n = 2;
    while (true)
    {
        string candidate = $"{stem}_{n}{ext}";
        if (!usedNames.Contains(candidate))
        {
            usedNames.Add(candidate);
            return candidate;
        }
        n++;
    }
}

static int RunVerify(MigrationSummaryReport summary, string outPath, string format, List<PipelineResult> results, ProjectAdapterConfig? config, DefaultProjectAdapter? adapter)
{
    Directory.CreateDirectory(outPath);

    Console.WriteLine("=== Verify Mode ===");
    Console.WriteLine();

    // Roslyn syntax checker
    SyntaxCheckerDelegate? syntaxChecker = code =>
    {
        var parseOptions = Microsoft.CodeAnalysis.CSharp.CSharpParseOptions.Default
            .WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12);
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code, parseOptions);

        return tree.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => (d.Location.GetLineSpan().StartLinePosition.Line + 1, d.GetMessage()))
            .ToList();
    };

    // Scope checker
    Func<string, string?>? scopeChecker = adapter != null ? (Func<string, string?>)(path => adapter.GetActiveScope(path)) : null;

    var report = VerifyRunner.Run(results, config, syntaxChecker, scopeChecker);

    // Write reports
    File.WriteAllText(Path.Combine(outPath, "verify-report.txt"), VerifyReportWriter.ToText(report));
    File.WriteAllText(Path.Combine(outPath, "verify-report.json"), VerifyReportWriter.ToJson(report));

    // Print summary to console
    PrintVerifyReport(report);

    Console.WriteLine();
    Console.WriteLine($"Verify reports written to: {Path.GetFullPath(outPath)}");

    // Apply quality gates for exit code
    return VerifyRunner.ApplyQualityGates(report, config?.QualityGates, report.Issues);
}

static void PrintVerifyReport(VerifyReport report)
{
    Console.WriteLine("=== Verify Report ===");
    Console.WriteLine($"Files checked: {report.FilesChecked}");
    Console.WriteLine($"Syntax errors: {report.SyntaxErrors}");
    Console.WriteLine($"TODO comments: {report.TodoComments}");
    Console.WriteLine($"Page.TODO calls: {report.PageTodoCalls}");
    Console.WriteLine($"Placeholder leftovers: {report.PlaceholderLeftovers}");
    Console.WriteLine($"Suspicious literal variables: {report.SuspiciousLiteralVariables}");
    Console.WriteLine($"Duplicate local variables: {report.DuplicateLocalVariables}");
    Console.WriteLine($"Scope conflicts: {report.ScopeWarnings}");
    Console.WriteLine($"Config warnings: {report.ConfigWarnings}");

    if (report.Files.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Per-file issues:");
        foreach (var file in report.Files)
        {
            if (file.Issues.Count > 0)
            {
                Console.WriteLine($"  {file.SourceFile}: {file.Issues.Count} issue(s)");
                foreach (var issue in file.Issues)
                {
                    var severity = issue.Severity switch
                    {
                        IssueSeverity.Error => "ERROR",
                        IssueSeverity.Warning => "WARN",
                        _ => "INFO"
                    };
                    var lineInfo = issue.Line != null ? $" (line {issue.Line})" : "";
                    Console.WriteLine($"    [{severity}] {issue.Message}{lineInfo}");
                }
            }
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Verify result: {report.Status.ToUpper()}");
}

static void WriteReports(MigrationSummaryReport summary, string outPath, string format,
    IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnmapped,
    IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnsupported)
{
    if (format == "text" || format == "both")
    {
        File.WriteAllText(Path.Combine(outPath, "report.txt"), ReportWriter.ToText(summary));
    }

    if (format == "json" || format == "both")
    {
        File.WriteAllText(Path.Combine(outPath, "report.json"), ReportWriter.ToJson(summary));
    }

    if (allUnmapped.Count > 0)
    {
        if (format == "json" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "unmapped-targets.json"),
                WriteAllUnmappedJson(allUnmapped, summary));
        }
        if (format == "text" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "unmapped-targets.csv"),
                WriteAllUnmappedCsv(allUnmapped));
        }
    }

    if (allUnsupported.Count > 0)
    {
        if (format == "json" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "unsupported-actions.json"),
                WriteAllUnsupportedJson(allUnsupported));
        }
        if (format == "text" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "unsupported-actions.csv"),
                WriteAllUnsupportedCsv(allUnsupported));
        }
    }
}

static string WriteAllUnmappedJson(IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnmapped, MigrationSummaryReport summary)
{
    var items = allUnmapped
        .OrderByDescending(kv => kv.Value.Count)
        .Select(kv => new
        {
            SourceExpression = kv.Key,
            Usages = kv.Value.Count,
            ExampleFile = kv.Value.File,
            ExampleLine = kv.Value.Line,
            SuggestedTargetExpression = SuggestTargetExpression(kv.Key)
        }).ToArray();

    return System.Text.Json.JsonSerializer.Serialize(items, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}

static string WriteAllUnmappedCsv(IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnmapped)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("SourceExpression,Usages,ExampleFile,ExampleLine,SuggestedTargetExpression");
    foreach (var kv in allUnmapped.OrderByDescending(kv => kv.Value.Count))
    {
        sb.AppendLine($"{EscapeCsv(kv.Key)},{kv.Value.Count},{EscapeCsv(kv.Value.File)},{kv.Value.Line},{EscapeCsv(SuggestTargetExpression(kv.Key))}");
    }
    return sb.ToString();
}

static string WriteAllUnsupportedJson(IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnsupported)
{
    var items = allUnsupported
        .OrderByDescending(kv => kv.Value.Count)
        .Select(kv => new
        {
            MethodOrSourceText = kv.Key,
            Count = kv.Value.Count,
            ExampleFile = kv.Value.File,
            ExampleLine = kv.Value.Line
        }).ToArray();

    return System.Text.Json.JsonSerializer.Serialize(items, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}

static string WriteAllUnsupportedCsv(IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnsupported)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("MethodOrSourceText,Count,ExampleFile,ExampleLine");
    foreach (var kv in allUnsupported.OrderByDescending(kv => kv.Value.Count))
    {
        sb.AppendLine($"{EscapeCsv(kv.Key)},{kv.Value.Count},{EscapeCsv(kv.Value.File)},{kv.Value.Line}");
    }
    return sb.ToString();
}

static string EscapeCsv(string value)
{
    if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    return value;
}

static Dictionary<string, (int Count, string File, int Line)> CollectAllUnmapped(List<PipelineResult> results)
{
    var allUnmapped = new Dictionary<string, (int Count, string File, int Line)>();

    foreach (var result in results)
    {
        var report = result.Report;
        var allActions = result.TargetModel.Tests.SelectMany(t => t.BodyActions)
            .Concat(result.TargetModel.SetUpActions).ToList();

        foreach (var action in allActions)
        {
            var target = GetTarget(action);

            if (target is { Kind: TargetKind.Unresolved })
            {
                var key = target.SourceExpression;
                if (!allUnmapped.ContainsKey(key))
                    allUnmapped[key] = (1, report.SourceFilePath, action.SourceLine);
                else
                {
                    var existing = allUnmapped[key];
                    allUnmapped[key] = (existing.Count + 1, existing.File, existing.Line);
                }
            }
        }
    }

    return allUnmapped;
}

static TargetExpression? GetTarget(TestAction action)
{
    return action switch
    {
        ClickAction c => c.Target,
        SendKeysAction s => s.Target,
        PressAction p => p.Target,
        TextAssertionAction ta => ta.Target,
        VisibilityAssertionAction va => va.Target,
        WaitForAction wa => wa.Target,
        _ => null
    };
}

static Dictionary<string, (int Count, string File, int Line)> CollectAllUnsupported(List<PipelineResult> results)
{
    var allUnsupported = new Dictionary<string, (int Count, string File, int Line)>();

    foreach (var result in results)
    {
        var report = result.Report;
        var allActions = result.TargetModel.Tests.SelectMany(t => t.BodyActions)
            .Concat(result.TargetModel.SetUpActions).ToList();

        foreach (var action in allActions)
        {
            if (action is UnsupportedAction ua)
            {
                var key = ua.SourceText.Replace("\n", " ").Replace("\r", " ");
                if (!allUnsupported.ContainsKey(key))
                    allUnsupported[key] = (1, report.SourceFilePath, action.SourceLine);
                else
                {
                    var existing = allUnsupported[key];
                    allUnsupported[key] = (existing.Count + 1, existing.File, existing.Line);
                }
            }
        }
    }

    return allUnsupported;
}

// --- Summary builder ---

static MigrationSummaryReport BuildSummary(List<PipelineResult> results, out IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnmappedDict)
{
    int totalTests = 0;
    int totalActions = 0;
    int totalSemantic = 0;
    int totalSyntaxFallback = 0;
    int totalUnsupported = 0;
    int totalMapped = 0;
    int totalUnmapped = 0;
    int totalTodo = 0;
    int filesWithWarnings = 0;

    var processedFiles = new List<string>();
    var allUnsupported = new Dictionary<string, (int Count, string File, int Line)>();
    var allUnmapped = new Dictionary<string, (int Count, string File, int Line)>();
    var perFileReports = new List<MigrationReport>();

    foreach (var result in results)
    {
        var report = result.Report;
        perFileReports.Add(report);
        processedFiles.Add(report.SourceFilePath);

        totalTests += report.TotalTests;
        totalSemantic += report.SemanticActions;
        totalSyntaxFallback += report.SyntaxFallbackActions;
        totalUnsupported += report.UnsupportedCount;
        totalMapped += report.MappedTargets;
        totalUnmapped += report.UnmappedTargets;
        totalTodo += report.TodoComments;

        var allActions = result.TargetModel.Tests.SelectMany(t => t.BodyActions)
            .Concat(result.TargetModel.SetUpActions).ToList();
        totalActions += allActions.Count;

        if (report.TodoComments > 0)
            filesWithWarnings++;

        foreach (var action in allActions)
        {
            var target = GetTarget(action);

            if (target is { Kind: TargetKind.Unresolved })
            {
                var key = target.SourceExpression;
                if (!allUnmapped.ContainsKey(key))
                    allUnmapped[key] = (1, report.SourceFilePath, action.SourceLine);
                else
                {
                    var existing = allUnmapped[key];
                    allUnmapped[key] = (existing.Count + 1, existing.File, existing.Line);
                }
            }

            if (action is UnsupportedAction ua)
            {
                var key = ua.SourceText.Replace("\n", " ").Replace("\r", " ");
                if (!allUnsupported.ContainsKey(key))
                    allUnsupported[key] = (1, report.SourceFilePath, action.SourceLine);
                else
                {
                    var existing = allUnsupported[key];
                    allUnsupported[key] = (existing.Count + 1, existing.File, existing.Line);
                }
            }
        }
    }

    var topUnmapped = allUnmapped
        .OrderByDescending(kv => kv.Value.Count)
        .Take(20)
        .Select(kv => new UnmappedTargetInfo(
            SourceExpression: kv.Key,
            Usages: kv.Value.Count,
            ExampleFile: kv.Value.File,
            ExampleLine: kv.Value.Line,
            SuggestedTargetExpression: SuggestTargetExpression(kv.Key)
        ))
        .ToList();

    var topUnsupported = allUnsupported
        .OrderByDescending(kv => kv.Value.Count)
        .Take(20)
        .Select(kv => new UnsupportedMethodInfo(
            MethodOrSourceText: kv.Key,
            Count: kv.Value.Count,
            ExampleFile: kv.Value.File,
            ExampleLine: kv.Value.Line
        ))
        .ToList();

    var summary = new MigrationSummaryReport(
        FilesProcessed: results.Count,
        TestsFound: totalTests,
        ActionsFound: totalActions,
        SemanticActions: totalSemantic,
        SyntaxFallbackActions: totalSyntaxFallback,
        UnsupportedActions: totalUnsupported,
        MappedTargets: totalMapped,
        UnmappedTargets: totalUnmapped,
        TodoComments: totalTodo,
        FilesWithWarnings: filesWithWarnings,
        GeneratedFiles: 0,
        ProcessedFiles: processedFiles,
        TopUnmappedTargets: topUnmapped,
        TopUnsupportedActions: topUnsupported,
        PerFileReports: perFileReports
    );

    allUnmappedDict = allUnmapped;

    return summary;
}

static string SuggestTargetExpression(string sourceExpression)
{
    var prop = sourceExpression;
    if (sourceExpression.Contains('.'))
        prop = sourceExpression.Substring(sourceExpression.LastIndexOf('.') + 1);

    var camel = char.ToLowerInvariant(prop[0]) + prop.Substring(1);
    return $"TODO_{camel}";
}

static void GenerateDraftConfig(IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnmapped, string outPath, string? existingConfigPath)
{
    var existingMappings = new HashSet<string>(StringComparer.Ordinal);

    if (!string.IsNullOrEmpty(existingConfigPath) && File.Exists(existingConfigPath))
    {
        var json = File.ReadAllText(existingConfigPath);
        var config = System.Text.Json.JsonSerializer.Deserialize<ProjectAdapterConfig>(json);
        if (config != null)
        {
            foreach (var tm in config.UiTargets)
                existingMappings.Add(tm.SourceExpression);
        }
    }

    var unmappedNotInConfig = allUnmapped
        .Where(kv => !existingMappings.Contains(kv.Key))
        .OrderByDescending(kv => kv.Value.Count)
        .ToList();

    if (unmappedNotInConfig.Count == 0)
        return;

    var draftConfig = new
    {
        SourceProjectName = "TODO: set your project name",
        PageObjects = new object[0],
        UiTargets = unmappedNotInConfig.Select(kv => new
        {
            SourceExpression = kv.Key,
            TargetExpression = SuggestTargetExpression(kv.Key),
            TargetKind = "TestId"
        }).ToArray(),
        Methods = new object[0]
    };

    var draftJson = System.Text.Json.JsonSerializer.Serialize(draftConfig,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    string draftPath;
    var baseDraft = Path.Combine(outPath, "adapter-config.draft.json");
    if (!File.Exists(baseDraft))
    {
        draftPath = baseDraft;
    }
    else
    {
        int n = 2;
        while (true)
        {
            draftPath = Path.Combine(outPath, $"adapter-config.draft.{n}.json");
            if (!File.Exists(draftPath))
                break;
            n++;
        }
    }

    File.WriteAllText(draftPath, draftJson);
    Console.WriteLine($"Draft adapter config written: {draftPath}");
}

static void PrintSummary(MigrationSummaryReport summary)
{
    Console.WriteLine();
    Console.WriteLine("=== Migration Summary ===");
    Console.WriteLine($"Files processed: {summary.FilesProcessed}");
    Console.WriteLine($"Tests found: {summary.TestsFound}");
    Console.WriteLine($"Actions found: {summary.ActionsFound}");
    Console.WriteLine($"  Semantic: {summary.SemanticActions}, SyntaxFallback: {summary.SyntaxFallbackActions}");
    Console.WriteLine($"  Unsupported: {summary.UnsupportedActions}");
    Console.WriteLine($"  Mapped: {summary.MappedTargets}, Unmapped: {summary.UnmappedTargets}");
    Console.WriteLine($"  TODO comments: {summary.TodoComments}");
    Console.WriteLine($"  Files with warnings: {summary.FilesWithWarnings}");

    if (summary.TopUnmappedTargets.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Top unmapped targets:");
        for (int i = 0; i < summary.TopUnmappedTargets.Count; i++)
        {
            var t = summary.TopUnmappedTargets[i];
            Console.WriteLine($"  {i + 1}. {t.SourceExpression} - {t.Usages} usages");
        }
    }

    if (summary.TopUnsupportedActions.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Top unsupported actions:");
        for (int i = 0; i < summary.TopUnsupportedActions.Count; i++)
        {
            var a = summary.TopUnsupportedActions[i];
            Console.WriteLine($"  {i + 1}. {a.MethodOrSourceText} - {a.Count} usages");
        }
    }
}

// --- Arg parsing ---

static int RunPropose(string inputPath, string outPath, string? configPath, string format)
{
    if (!Directory.Exists(inputPath))
    {
        Console.Error.WriteLine($"Propose mode expects a directory with report files: {inputPath}");
        return 1;
    }

    var warnings = new List<string>();

    // Load report.json
    MigrationSummaryReport? migrationReport = null;
    var reportJsonPath = Path.Combine(inputPath, "report.json");
    if (File.Exists(reportJsonPath))
    {
        migrationReport = System.Text.Json.JsonSerializer.Deserialize<MigrationSummaryReport>(File.ReadAllText(reportJsonPath));
        if (migrationReport == null)
            warnings.Add("report.json is empty or invalid");
    }
    else
    {
        warnings.Add("report.json not found, migration-based proposals skipped");
    }

    // Load unmapped-targets.json
    var unmappedTargets = new List<UnmappedTargetInfo>();
    var unmappedPath = Path.Combine(inputPath, "unmapped-targets.json");
    if (File.Exists(unmappedPath))
    {
        unmappedTargets = System.Text.Json.JsonSerializer.Deserialize<List<UnmappedTargetInfo>>(File.ReadAllText(unmappedPath)) ?? new List<UnmappedTargetInfo>();
    }
    else
    {
        // Try to extract from report
        if (migrationReport?.TopUnmappedTargets != null)
            unmappedTargets = new List<UnmappedTargetInfo>(migrationReport.TopUnmappedTargets);
        else
            warnings.Add("unmapped-targets.json not found, UiTarget proposals skipped");
    }

    // Load unsupported-actions.json
    var unsupportedActions = new List<UnsupportedMethodInfo>();
    var unsupportedPath = Path.Combine(inputPath, "unsupported-actions.json");
    if (File.Exists(unsupportedPath))
    {
        unsupportedActions = System.Text.Json.JsonSerializer.Deserialize<List<UnsupportedMethodInfo>>(File.ReadAllText(unsupportedPath)) ?? new List<UnsupportedMethodInfo>();
    }
    else
    {
        if (migrationReport?.TopUnsupportedActions != null)
            unsupportedActions = new List<UnsupportedMethodInfo>(migrationReport.TopUnsupportedActions);
        else
            warnings.Add("unsupported-actions.json not found, method proposals skipped");
    }

    // Load verify-report.json
    VerifyReport? verifyReport = null;
    var verifyPath = Path.Combine(inputPath, "verify-report.json");
    if (File.Exists(verifyPath))
    {
        verifyReport = System.Text.Json.JsonSerializer.Deserialize<VerifyReport>(File.ReadAllText(verifyPath));
        if (verifyReport == null)
            warnings.Add("verify-report.json is empty or invalid");
    }
    else
    {
        warnings.Add("verify-report.json not found, verify-based proposals skipped");
    }

    // Load adapter config
    ProjectAdapterConfig? existingConfig = null;
    if (!string.IsNullOrEmpty(configPath))
    {
        if (File.Exists(configPath))
        {
            existingConfig = System.Text.Json.JsonSerializer.Deserialize<ProjectAdapterConfig>(File.ReadAllText(configPath));
        }
        else
        {
            Console.Error.WriteLine($"Config not found: {configPath}");
            return 1;
        }
    }

    // Load generated files for additional context
    var generatedFiles = new List<string>();
    var generatedContents = new Dictionary<string, string>();
    var csFiles = Directory.GetFiles(inputPath, "*.cs");
    foreach (var cs in csFiles)
    {
        generatedFiles.Add(cs);
        generatedContents[cs] = File.ReadAllText(cs);
    }

    // Print warnings
    foreach (var w in warnings)
        Console.WriteLine($"Warning: {w}");

    // Generate proposals
    var input = new ProposalGenerator.ProposalInput
    {
        MigrationReport = migrationReport,
        VerifyReport = verifyReport,
        UnmappedTargets = unmappedTargets,
        UnsupportedActions = unsupportedActions,
        ExistingConfig = existingConfig,
        GeneratedFiles = generatedFiles,
        GeneratedFileContents = generatedContents
    };

    var generator = new ProposalGenerator();
    var proposals = generator.Generate(input);

    // Output
    if (proposals.Count == 0)
    {
        Console.WriteLine("No proposals generated. Current config appears to cover all detected patterns.");
    }

    // Write output
    Directory.CreateDirectory(outPath);

    var writeJson = format == "json" || format == "both";
    var writeMd = format == "md" || format == "txt" || format == "both";

    if (writeJson)
    {
        var jsonPath = Path.Combine(outPath, "mapping-proposals.json");
        File.WriteAllText(jsonPath, ProposalWriter.ToJson(proposals));
        Console.WriteLine($"Written: {jsonPath}");
    }

    if (writeMd)
    {
        var mdPath = Path.Combine(outPath, "mapping-proposals.md");
        File.WriteAllText(mdPath, ProposalWriter.ToMarkdown(proposals));
        Console.WriteLine($"Written: {mdPath}");
    }

    // Console summary
    Console.WriteLine();
    Console.WriteLine($"=== Proposal Summary ===");
    Console.WriteLine($"Total proposals: {proposals.Count}");
    Console.WriteLine($"  High:   {proposals.Count(p => p.Priority == ProposalPriority.High)}");
    Console.WriteLine($"  Medium: {proposals.Count(p => p.Priority == ProposalPriority.Medium)}");
    Console.WriteLine($"  Low:    {proposals.Count(p => p.Priority == ProposalPriority.Low)}");
    Console.WriteLine($"  Requires source truth: {proposals.Count(p => p.RequiresSourceTruth)}");

    return 0;
}

static CliOptions? ParseArgs(string[] args)
{
    string mode = "migrate";
    string? input = null;
    string? outDir = null;
    string? config = null;
    string format = "both";
    bool failOnUnsupported = false;
    bool failOnTodo = false;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--mode":
                if (i + 1 < args.Length)
                    mode = args[++i];
                else
                {
                    Console.Error.WriteLine("--mode requires a value: analyze|migrate|verify|propose");
                    return null;
                }
                break;
            case "--input":
                if (i + 1 < args.Length)
                    input = args[++i];
                else
                {
                    Console.Error.WriteLine("--input requires a value");
                    return null;
                }
                break;
            case "--out":
                if (i + 1 < args.Length)
                    outDir = args[++i];
                else
                {
                    Console.Error.WriteLine("--out requires a value");
                    return null;
                }
                break;
            case "--config":
                if (i + 1 < args.Length)
                    config = args[++i];
                else
                {
                    Console.Error.WriteLine("--config requires a value");
                    return null;
                }
                break;
            case "--format":
                if (i + 1 < args.Length)
                    format = args[++i];
                else
                {
                    Console.Error.WriteLine("--format requires a value: text|json|both");
                    return null;
                }
                break;
            case "--help":
            case "-h":
                PrintHelp();
                return null;
            case "--fail-on-unsupported":
                failOnUnsupported = true;
                break;
            case "--fail-on-todo":
                failOnTodo = true;
                break;
            default:
                if (args[i].StartsWith("-"))
                {
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return null;
                }
                if (input == null)
                    input = args[i];
                else
                {
                    Console.Error.WriteLine($"Unexpected argument: {args[i]}");
                    return null;
                }
                break;
        }
    }

    if (mode != "analyze" && mode != "migrate" && mode != "verify" && mode != "propose")
    {
        Console.Error.WriteLine($"Invalid mode: {mode}. Use: analyze|migrate|verify");
        return null;
    }

    if (string.IsNullOrEmpty(input))
    {
        Console.Error.WriteLine("--input is required");
        PrintHelp();
        return null;
    }

    if (string.IsNullOrEmpty(outDir))
    {
        outDir = mode switch
        {
            "analyze" => "./migration-analysis",
            "migrate" => "./generated-tests",
            "verify" => "./migration-verify",
            "propose" => "./mapping-proposals",
            _ => "./migration-output"
        };
    }

    if (format != "text" && format != "json" && format != "both")
    {
        Console.Error.WriteLine($"Invalid format: {format}. Use: text|json|both");
        return null;
    }

    return new CliOptions(mode, input, outDir, config, format, failOnUnsupported, failOnTodo);
}

static void PrintHelp()
{
    Console.WriteLine(@"
Usage: Migrator.Cli --mode <analyze|migrate|verify|propose> --input <path> [options]

Modes:
  analyze  Parse and analyze Selenium tests without generating output files.
            Produces reports and draft adapter-config.
  migrate  Parse, adapt, and generate Playwright C# files. Produces reports.
  verify   Validate generated code quality. Runs Roslyn syntax check,
             TODO/placeholder detection, config validation, scope matching,
             and quality gate evaluation. Outputs verify-report.json and
             verify-report.txt.
  propose  Analyze migration artifacts (reports, generated output) and
            generate mapping proposals. Reads report.json, unmapped-targets.json,
            unsupported-actions.json, verify-report.json. Outputs
            mapping-proposals.md and mapping-proposals.json. Does NOT modify config.

Options:
   --mode <analyze|migrate|verify|propose>   Operation mode (required)
   --input <file-or-directory>       Input .cs file or directory (required).
                                      For propose mode: directory with report files.
  --out <output-directory>          Output directory (optional, auto-defaults)
  --config <adapter-config.json>    Adapter config for target mapping (optional)
  --format <text|json|both>         Report format (default: both)
  --fail-on-unsupported             Exit code 2 if unsupported actions exist
  --fail-on-todo                    Exit code 3 if TODO comments exist
  --help, -h                        Show this help

Exit codes:
  0  Success (passed quality gates)
  1  CLI usage error / failed quality gate (verify mode)
  2  --fail-on-unsupported triggered / invalid config (verify mode)
  3  --fail-on-todo triggered / syntax error (verify mode)

Examples:
  Migrator.Cli --mode analyze --input ./OldTests --out ./analysis --format both
  Migrator.Cli --mode migrate --input ./OldTests --out ./Generated --config ./adapter-config.json
    Migrator.Cli --mode migrate --input ./OldTests --fail-on-unsupported --fail-on-todo
   Migrator.Cli --mode propose --input ./Generated --config ./adapter-config.json --format both
");
}

record CliOptions(string Mode, string Input, string Out, string? Config, string Format, bool FailOnUnsupported, bool FailOnTodo);

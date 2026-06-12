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
        RunVerify(summary, outPath, format, resultsList);
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

static void RunVerify(MigrationSummaryReport summary, string outPath, string format, List<PipelineResult> results)
{
    Directory.CreateDirectory(outPath);

    Console.WriteLine("Verify mode (experimental) — checking generated output...");
    Console.WriteLine("  Note: verify currently does not perform full compile-smoke. It only validates generated file structure.");
    Console.WriteLine();

    foreach (var result in results)
    {
        var canCheck = result.GeneratedOutput.Contains("class ") && result.GeneratedOutput.Contains("Task");
        if (canCheck)
        {
            Console.WriteLine($"  [PASS] Has valid structure: {result.SourceModel.ClassName}Playwright.cs");
        }
        else
        {
            Console.WriteLine($"  [SKIP] No valid generated output for: {result.SourceModel.ClassName}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("Verify complete (experimental): full compile-smoke is not yet implemented.");

    var allUnsupported = CollectAllUnsupported(results);
    var allUnmapped = CollectAllUnmapped(results);
    WriteReports(summary, outPath, format, allUnmapped, allUnsupported);
    PrintSummary(summary);
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
            var target = action switch
            {
                ClickAction c => c.Target,
                SendKeysAction s => s.Target,
                PressAction p => p.Target,
                _ => null
            };

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
            var target = action switch
            {
                ClickAction c => c.Target,
                SendKeysAction s => s.Target,
                PressAction p => p.Target,
                _ => null
            };

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
                    Console.Error.WriteLine("--mode requires a value: analyze|migrate|verify");
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

    if (mode != "analyze" && mode != "migrate" && mode != "verify")
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
Usage: Migrator.Cli --mode <analyze|migrate|verify> --input <path> [options]

Modes:
  analyze  Parse and analyze Selenium tests without generating output files.
           Produces reports and draft adapter-config.
  migrate  Parse, adapt, and generate Playwright C# files. Produces reports.
  verify   (experimental) Validate structure of generated files.
           Not yet a full compile-smoke — use with caution.

Options:
  --mode <analyze|migrate|verify>   Operation mode (required)
  --input <file-or-directory>       Input .cs file or directory (required)
  --out <output-directory>          Output directory (optional, auto-defaults)
  --config <adapter-config.json>    Adapter config for target mapping (optional)
  --format <text|json|both>         Report format (default: both)
  --fail-on-unsupported             Exit code 2 if unsupported actions exist
  --fail-on-todo                    Exit code 3 if TODO comments exist
  --help, -h                        Show this help

Exit codes:
  0  Success (no quality gate failures)
  1  CLI usage error
  2  --fail-on-unsupported triggered
  3  --fail-on-todo triggered

Examples:
  Migrator.Cli --mode analyze --input ./OldTests --out ./analysis --format both
  Migrator.Cli --mode migrate --input ./OldTests --out ./Generated --config ./adapter-config.json
    Migrator.Cli --mode migrate --input ./OldTests --fail-on-unsupported --fail-on-todo
");
}

record CliOptions(string Mode, string Input, string Out, string? Config, string Format, bool FailOnUnsupported, bool FailOnTodo);

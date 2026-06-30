using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Migrator.Core;
using Migrator.Core.Profiles;
using Migrator.Core.SourceFrontends;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.PlaywrightTypeScript;
using Migrator.Roslyn;
using Migrator.SeleniumCSharp;

if (args.Length > 0 && string.Equals(args[0], "kit", StringComparison.OrdinalIgnoreCase))
    return KitCommand.Run(args.Skip(1).ToArray());

args = NormalizeDirectCommand(args);

if (IsHelpRequest(args))
{
    var helpMode = FindOptionValue(args, "--mode");
    if (!string.IsNullOrWhiteSpace(helpMode))
        CliCommandCatalog.WriteCommandHelp(helpMode);
    else
        CliCommandCatalog.WriteGlobalHelp();
    return 0;
}

var opts = ParseArgs(args);

if (opts == null)
    return 1;

string mode = opts.Mode;
string inputPath = opts.Input;
string outPath = opts.Out;
string? configPath = opts.Config;
string[] configPaths = opts.Configs;
if (opts.Mode == "config-normalize" && configPaths.Length == 0 && !string.IsNullOrWhiteSpace(opts.Input))
    configPaths = new[] { opts.Input };
string? primaryConfigPath = configPaths.Length > 0 ? configPaths[^1] : null;
ProjectAdapterConfig? loadedConfig = null;
string format = opts.Format;
bool failOnUnsupported = opts.FailOnUnsupported;
bool failOnTodo = opts.FailOnTodo;
string? beforePath = opts.Before;
string? afterPath = opts.After;
string target = opts.Target;
string source = opts.Source;
bool sourceExplicit = opts.SourceExplicit;
string? tsProjectPath = opts.TsProject;
bool recursiveArtifacts = opts.RecursiveArtifacts;
string irVersion = opts.IrVersion;
string renderIr = opts.RenderIr;
string validationMode = opts.ValidationMode;
string? targetTestFramework = opts.TargetTestFramework;

// Agent-safety modes operate on config/report artifacts and do not process source files.
if (mode == "config-validate")
{
    var validateInputs = configPaths.Length > 0
        ? configPaths
        : (!string.IsNullOrWhiteSpace(inputPath) ? new[] { inputPath } : Array.Empty<string>());
    var validateTarget = CreateBuiltInTargetBackendRegistry().Resolve(opts.Target).Target;
    var validateExitCode = RunConfigValidate(validateInputs, outPath, format, validationMode, validateTarget);
    return validateExitCode;
}

if (mode == "config-diff")
{
    var diffExitCode = RunConfigDiff(beforePath, afterPath, outPath, format);
    return diffExitCode;
}

if (mode == "guard")
{
    var guardExitCode = RunGuard(beforePath, afterPath, outPath, format);
    return guardExitCode;
}

if (CliCommandCatalog.ShouldPreflightInputExists(mode) && !File.Exists(inputPath) && !Directory.Exists(inputPath))
{
    Console.Error.WriteLine($"Input not found: {inputPath}");
    return 1;
}

if (mode == "discover-target" && !Directory.Exists(inputPath))
{
    Console.Error.WriteLine($"Discover-target mode expects a directory (target Playwright project): {inputPath}");
    return 2;
}

ITargetBackend targetBackend;
try
{
    targetBackend = CreateBuiltInTargetBackendRegistry().Resolve(target);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

IRenderer renderer = new TargetBackendRendererAdapter(targetBackend);
target = targetBackend.Target.Id;

if (mode == "init")
{
    var initExitCode = RunInitWizard(opts, targetBackend);
    return initExitCode;
}

IProjectAdapter? adapter = null;

if (configPaths.Length > 0)
{
    foreach (var path in configPaths)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Config not found: {path}");
            return 1;
        }
    }

    try
    {
        loadedConfig = ProjectAdapterConfigMerger.LoadAndMerge(configPaths);
        if (!string.IsNullOrWhiteSpace(targetTestFramework))
            loadedConfig = ApplyTargetTestFrameworkOverride(loadedConfig, targetTestFramework);
        adapter = new DefaultProjectAdapter(loadedConfig);
    }
    catch (ConfigValidationError cvex)
    {
        Console.Error.WriteLine("Config error:");
        foreach (var err in cvex.Errors)
            Console.Error.WriteLine(err);
        return 2;
    }

    Console.WriteLine(configPaths.Length == 1
        ? $"Loaded adapter config: {configPaths[0]}"
        : $"Loaded adapter config layers: {string.Join(" -> ", configPaths)}");
}

if (configPaths.Length == 0 && !string.IsNullOrWhiteSpace(targetTestFramework))
{
    loadedConfig = ApplyTargetTestFrameworkOverride(new ProjectAdapterConfig(), targetTestFramework);
    adapter = new DefaultProjectAdapter(loadedConfig);
}

var sourceRegistry = CreateBuiltInSourceFrontendRegistry(loadedConfig);
if (mode == "capabilities")
{
    var capabilitiesExitCode = RunCapabilities(sourceRegistry, CreateBuiltInTargetBackendRegistry(), outPath, format);
    return capabilitiesExitCode;
}

SourceDetectionReport? sourceDetection = null;
if (ShouldAutoDetectSource(mode, source, sourceExplicit, inputPath))
{
    sourceDetection = SourceAutoDetector.Detect(inputPath);
    source = sourceDetection.DetectedSourceId;
    Console.WriteLine($"Detected source frontend: {source} ({sourceDetection.Confidence} confidence)");
    foreach (var reason in sourceDetection.Reasons.Take(3))
        Console.WriteLine($"  - {reason}");

    WriteSourceDetectionReport(sourceDetection, outPath, format, selectedSource: source, explicitSource: false);
}
else if (string.Equals(source, "auto", StringComparison.OrdinalIgnoreCase))
{
    source = "csharp-selenium";
}

ISourceFrontend sourceFrontend;
try
{
    sourceFrontend = sourceRegistry.Resolve(source);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
source = sourceFrontend.Source.Id;
if (ShouldWriteSourceCapabilityReport(mode))
{
    Console.WriteLine($"Source capability profile: {sourceFrontend.Source.Id} ({sourceFrontend.Capabilities.Status})");
    WriteSourceCapabilityReport(sourceFrontend.Capabilities, outPath, format);
}

if (ShouldWriteTargetCapabilityReport(mode))
{
    Console.WriteLine($"Target capability profile: {targetBackend.Target.Id} ({targetBackend.Capabilities.Status})");
    WriteTargetCapabilityReport(targetBackend.Capabilities, outPath, format);
}

if (mode == "config-normalize")
{
    if (loadedConfig == null)
    {
        Console.Error.WriteLine("--config or --input is required for config-normalize");
        return 1;
    }

    var normalizeExitCode = RunConfigNormalize(loadedConfig, configPaths, outPath, format, sourceFrontend.Source, targetBackend.Target);
    return normalizeExitCode;
}

ITestFileParser parser;
try
{
    parser = ResolveLegacyParser(sourceFrontend);
}
catch (NotSupportedException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

if (IsTypeScriptTarget(targetBackend) && mode == "orchestrate")
{
    Console.Error.WriteLine("--target ts is not supported in orchestrate yet. Use --mode migrate --target ts, then --mode verify-ts-project.");
    return 2;
}

// Handle doctor mode — validates environment/input/config/project context before migration.
if (mode == "doctor")
{
    var doctorExitCode = RunDoctor(inputPath, outPath, format, configPaths, loadedConfig, opts);
    return doctorExitCode;
}

// Handle propose mode separately — input is a directory with report artifacts, not source files
if (mode == "propose")
{
    var proposeExitCode = RunPropose(inputPath, outPath, loadedConfig, format);
    return proposeExitCode;
}

// Handle discover-target mode — scans a target Playwright .NET project
if (mode == "discover-target")
{
    var discoverExitCode = RunDiscoverTarget(inputPath, outPath, primaryConfigPath, format);
    return discoverExitCode;
}

// Handle index-pom mode — scans Selenium PageObjects/source truth and produces reviewable POM facts.
if (mode == "index-pom")
{
    var indexPomExitCode = RunIndexPom(inputPath, outPath, format);
    return indexPomExitCode;
}

// Handle helper-inventory mode — scans helper/POM method bodies and infers MethodSemantics candidates.
if (mode == "helper-inventory")
{
    var helperInventoryExitCode = HelperInventoryCommand.RunHelperInventory(inputPath, outPath, format);
    return helperInventoryExitCode;
}

// Handle explain-todo mode — explains migration TODO/root causes from existing artifacts.
if (mode == "explain-todo")
{
    var explainExitCode = RunExplainTodo(inputPath, outPath, format, recursiveArtifacts);
    return explainExitCode;
}

// Handle smoke-plan mode — ranks generated tests by runtime readiness and writes checklists.
if (mode == "smoke-plan")
{
    var smokeExitCode = RunSmokePlan(inputPath, outPath, format, recursiveArtifacts);
    return smokeExitCode;
}

// Handle runtime-classify mode — classifies Playwright runtime failures/logs after a smoke run.
if (mode == "runtime-classify")
{
    var runtimeExitCode = RuntimeFailureClassifierCommand.RunRuntimeClassify(inputPath, outPath, format);
    return runtimeExitCode;
}

// Handle config-schema mode — writes/copies adapter-config JSON Schema for editors and agents.
if (mode == "config-schema")
{
    var schemaExitCode = ConfigSchemaCommand.RunConfigSchema(outPath, format);
    return schemaExitCode;
}

// Handle report-serve mode — builds a product dashboard and optionally serves it locally.
if (mode == "report-serve")
{
    var reportServeExitCode = RunReportServe(inputPath, outPath, format, recursiveArtifacts, opts.Port, opts.StaticOnly);
    return reportServeExitCode;
}

// Handle migration-board mode — builds an HTML dashboard from migration artifacts.
if (mode == "migration-board")
{
    var boardExitCode = RunMigrationBoard(inputPath, outPath, format, recursiveArtifacts);
    return boardExitCode;
}

// Handle profile-match mode — estimates whether existing config/profile layers can be reused for a source project.
if (mode == "profile-match")
{
    var profileMatchExitCode = ProfileMatchCommand.RunProfileMatch(inputPath, outPath, format, configPaths);
    return profileMatchExitCode;
}


// Handle verify-ts-project mode — validates generated .spec.ts files inside a real Playwright TS project.
if (mode == "verify-ts-project")
{
    var verifyTsExitCode = RunVerifyTsProject(inputPath, outPath, format, tsProjectPath);
    return verifyTsExitCode;
}

// Handle scaffold mode — generates a minimal, compile-ready Playwright .NET test project
if (mode == "scaffold")
{
    var scaffoldResult = new ScaffoldWriter(new ScaffoldOptions { OutPath = outPath, Format = format, TargetTestFramework = targetTestFramework ?? "nunit" }).Write();
    if (scaffoldResult.Status == "failed")
    {
        foreach (var w in scaffoldResult.Warnings)
            Console.Error.WriteLine($"Scaffold failed: {w}");
        return 1;
    }
    WriteScaffoldReport(scaffoldResult, outPath, format);
    return 0;
}

// Handle bootstrap-project mode — creates reusable profile/project config skeletons.
if (mode == "bootstrap-project")
{
    var bootstrapExitCode = RunBootstrapProject(inputPath, outPath, format);
    return bootstrapExitCode;
}

// Handle orchestrate mode — runs analyze → migrate → verify → propose pipeline
if (mode == "orchestrate")
{
    try
    {
        var orchestrateExitCode = RunOrchestrate(inputPath, outPath, primaryConfigPath, format, parser, renderer, adapter, loadedConfig, targetBackend);
        return orchestrateExitCode;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Orchestrate failed before the normal report could be completed: {ex.Message}");
        return WriteEmergencyOrchestrationReport(inputPath, outPath, primaryConfigPath, ex);
    }
}

var pipeline = CreateMigrationPipeline(parser, renderer, adapter, targetBackend, renderIr, sourceFrontend.Source);

IEnumerable<PipelineResult> results;

try
{
    if (Directory.Exists(inputPath))
    {
        results = pipeline.ProcessDirectory(inputPath);
    }
    else
    {
        results = new[] { pipeline.ProcessFile(inputPath) };
    }
}
catch (InvalidOperationException ex) when (ex.Message.StartsWith("Syntax error in", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"Input parse error: {ex.Message}");
    return 2;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Input read error: {ex.Message}");
    return 2;
}

var resultsList = results.ToList();
var summary = BuildSummary(resultsList, out var allUnmapped);

switch (mode)
{
    case "analyze":
        RunAnalyze(summary, outPath, format, loadedConfig, resultsList, allUnmapped);
        break;
    case "dump-ir":
        RunDumpIr(outPath, format, resultsList, sourceFrontend.Source, targetBackend.Target, irVersion);
        break;
    case "migrate":
        RunMigrate(summary, outPath, format, loadedConfig, resultsList, allUnmapped, targetBackend);
        break;
    case "verify":
        {
            var verifyConfig = loadedConfig;
            var verifyAdapter = adapter as DefaultProjectAdapter;
            var verifyExitCode = RunVerify(summary, outPath, format, resultsList, verifyConfig, verifyAdapter);
            if (verifyExitCode != 0)
                return verifyExitCode;
        }
        break;
    case "verify-project":
        {
            var verifyConfig = loadedConfig ?? new ProjectAdapterConfig();
            var verifyExitCode = RunVerifyProject(summary, outPath, format, resultsList, verifyConfig, primaryConfigPath, inputPath, allUnmapped, CollectAllUnsupported(resultsList));
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

static int RunInitWizard(CliOptions opts, ITargetBackend targetBackend)
{
    var interactive = opts.Wizard || string.IsNullOrWhiteSpace(opts.Input);
    var sourcePath = opts.Input;
    if (string.IsNullOrWhiteSpace(sourcePath))
    {
        sourcePath = PromptRequired("Source Selenium test path");
        if (string.IsNullOrWhiteSpace(sourcePath))
            return 2;
    }

    if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
    {
        Console.Error.WriteLine($"Source path not found: {sourcePath}");
        return 1;
    }

    var detection = SourceAutoDetector.Detect(sourcePath);
    var sourceFramework = DetectSourceTestFramework(sourcePath, detection.DetectedSourceId);
    var targetId = targetBackend.Target.Id;
    var targetTestFrameworkValue = opts.TargetTestFramework ?? "nunit";

    if (interactive && IsInteractiveConsole())
    {
        Console.WriteLine("=== Migrator Init Wizard ===");
        Console.WriteLine($"Detected source: {detection.DetectedSourceId} ({detection.Confidence})");
        Console.WriteLine($"Detected source test framework: {sourceFramework}");
        targetId = PromptChoice("Target backend", new[] { "playwright-dotnet", "playwright-typescript" }, targetId);
        if (targetId.Equals("playwright-dotnet", StringComparison.OrdinalIgnoreCase))
            targetTestFrameworkValue = PromptChoice("Target test framework", new[] { "nunit", "xunit" }, targetTestFrameworkValue);
    }

    var targetExists = opts.TargetProjectExists ?? false;
    if (interactive && IsInteractiveConsole() && opts.TargetProjectExists == null)
        targetExists = PromptYesNo("Does a target Playwright project already exist?", defaultValue: false);

    var targetProjectPath = opts.TargetProjectPath;
    if (targetExists && string.IsNullOrWhiteSpace(targetProjectPath) && interactive && IsInteractiveConsole())
        targetProjectPath = PromptOptional("Target Playwright project path for discover-target");

    var testIdAttribute = string.IsNullOrWhiteSpace(opts.DefaultTestIdAttribute) ? "data-testid" : opts.DefaultTestIdAttribute!;
    if (interactive && IsInteractiveConsole() && string.IsNullOrWhiteSpace(opts.DefaultTestIdAttribute))
        testIdAttribute = PromptChoice("Default test id attribute", new[] { "data-testid", "data-test-id", "data-test", "data-tid", "custom" }, "data-testid");
    if (testIdAttribute.Equals("custom", StringComparison.OrdinalIgnoreCase) && interactive && IsInteractiveConsole())
        testIdAttribute = PromptRequired("Custom test id attribute");

    var installKit = opts.InstallAgentKit ?? false;
    if (interactive && IsInteractiveConsole() && opts.InstallAgentKit == null)
        installKit = PromptYesNo("Install lightweight agent loop files?", defaultValue: true);

    var options = new InitWizardOptions
    {
        WorkspacePath = opts.Out,
        SourcePath = sourcePath,
        SourceFrontendId = detection.DetectedSourceId,
        SourceLanguage = ResolveSourceLanguage(detection.DetectedSourceId),
        SourceTestFramework = sourceFramework,
        TargetBackendId = targetId,
        TargetTestFramework = targetTestFrameworkValue,
        TargetProjectExists = targetExists,
        TargetProjectPath = targetProjectPath,
        DefaultTestIdAttribute = testIdAttribute,
        InstallAgentKit = installKit,
        TargetNamespace = opts.TargetNamespace,
        TargetBaseClass = opts.TargetBaseClass
    };

    var result = new InitWizardWriter(options).Write();
    if (result.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var warning in result.Warnings)
            Console.Error.WriteLine(warning);
        return 1;
    }

    Console.WriteLine("=== Init Wizard ===");
    Console.WriteLine($"Workspace: {result.WorkspacePath}");
    Console.WriteLine($"Config: {result.ConfigPath}");
    Console.WriteLine($"Created files: {result.CreatedFiles.Length}");
    foreach (var warning in result.Warnings)
        Console.WriteLine($"Warning: {warning}");
    Console.WriteLine("Next steps:");
    foreach (var step in result.NextSteps)
        Console.WriteLine($"- {step}");
    return 0;
}

static bool IsInteractiveConsole() => !Console.IsInputRedirected;

static string PromptRequired(string label)
{
    Console.Write($"{label}: ");
    return Console.ReadLine()?.Trim() ?? "";
}

static string? PromptOptional(string label)
{
    Console.Write($"{label} (empty to skip): ");
    var value = Console.ReadLine()?.Trim();
    return string.IsNullOrWhiteSpace(value) ? null : value;
}

static string PromptChoice(string label, IReadOnlyList<string> choices, string defaultValue)
{
    var normalizedDefault = choices.FirstOrDefault(x => x.Equals(defaultValue, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
    Console.Write($"{label} [{normalizedDefault}] ({string.Join("/", choices)}): ");
    var value = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(value))
        return normalizedDefault;
    if (choices.Any(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)))
        return choices.First(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"Unsupported value '{value}', using {normalizedDefault}.");
    return normalizedDefault;
}

static bool PromptYesNo(string label, bool defaultValue)
{
    Console.Write($"{label} [{(defaultValue ? "Y/n" : "y/N")}] ");
    var value = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(value))
        return defaultValue;
    return value.Equals("y", StringComparison.OrdinalIgnoreCase)
        || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || value.Equals("true", StringComparison.OrdinalIgnoreCase);
}

static string ResolveSourceLanguage(string sourceId)
{
    if (sourceId.Contains("java", StringComparison.OrdinalIgnoreCase)) return "java";
    if (sourceId.Contains("python", StringComparison.OrdinalIgnoreCase)) return "python";
    return "csharp";
}

static string DetectSourceTestFramework(string sourcePath, string sourceId)
{
    var files = EnumerateSourceSamples(sourcePath).Take(250).ToArray();
    var samples = files.Select(ReadSmallText).ToArray();
    if (sourceId.Contains("java", StringComparison.OrdinalIgnoreCase))
    {
        if (samples.Any(t => t.Contains("org.testng", StringComparison.Ordinal) || t.Contains("@Test", StringComparison.Ordinal) && t.Contains("testng", StringComparison.OrdinalIgnoreCase)))
            return "testng";
        if (samples.Any(t => t.Contains("org.junit.jupiter", StringComparison.Ordinal)))
            return "junit5";
        if (samples.Any(t => t.Contains("org.junit.Test", StringComparison.Ordinal) || t.Contains("org.junit.Assert", StringComparison.Ordinal)))
            return "junit4";
        return "unknown-java";
    }

    if (sourceId.Contains("python", StringComparison.OrdinalIgnoreCase))
    {
        if (samples.Any(t => t.Contains("unittest.TestCase", StringComparison.Ordinal) || t.Contains("import unittest", StringComparison.Ordinal)))
            return "unittest";
        if (samples.Any(t => t.Contains("pytest", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(t, @"\bdef\s+test_")))
            return "pytest";
        return "unknown-python";
    }

    if (samples.Any(t => t.Contains("using Xunit", StringComparison.Ordinal) || t.Contains("[Fact", StringComparison.Ordinal) || t.Contains("[Theory", StringComparison.Ordinal)))
        return "xunit";
    if (samples.Any(t => t.Contains("using Microsoft.VisualStudio.TestTools.UnitTesting", StringComparison.Ordinal) || t.Contains("[TestMethod", StringComparison.Ordinal)))
        return "mstest";
    if (samples.Any(t => t.Contains("using NUnit.Framework", StringComparison.Ordinal) || t.Contains("[Test", StringComparison.Ordinal) || t.Contains("[TestCase", StringComparison.Ordinal)))
        return "nunit";
    return "unknown-csharp";
}

static IEnumerable<string> EnumerateSourceSamples(string sourcePath)
{
    if (File.Exists(sourcePath))
        return new[] { sourcePath };
    if (!Directory.Exists(sourcePath))
        return Array.Empty<string>();

    var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", ".idea", "node_modules", "dist", "build" };
    return Directory.EnumerateFiles(sourcePath, "*.*", SearchOption.AllDirectories)
        .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".java", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
        .Where(file => !file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => ignored.Contains(part)));
}

static string ReadSmallText(string file)
{
    try
    {
        using var stream = File.OpenRead(file);
        using var reader = new StreamReader(stream);
        var buffer = new char[32 * 1024];
        var read = reader.Read(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }
    catch
    {
        return "";
    }
}

static int RunConfigNormalize(ProjectAdapterConfig config, string[] configPaths, string outPath, string format, SourceSpec source, TargetSpec target)
{
    Directory.CreateDirectory(outPath);

    var result = ProjectAdapterConfigNormalizer.Normalize(
        config,
        source: source,
        target: target);

    const bool includeLegacyConfig = true;
    var profileJson = MigrationProfileWriter.ToJson(result.Profile, includeLegacyConfig);
    File.WriteAllText(Path.Combine(outPath, "migration-profile.v2.json"), profileJson);

    var report = MigrationProfileWriter.BuildReport(result, configPaths, includeLegacyConfig);

    if (format == "json" || format == "both")
        File.WriteAllText(Path.Combine(outPath, "config-normalize-report.json"), MigrationProfileWriter.ReportToJson(report));

    if (format == "text" || format == "both")
        File.WriteAllText(Path.Combine(outPath, "config-normalize-report.md"), MigrationProfileWriter.ReportToMarkdown(report));

    Console.WriteLine("=== Config Normalize ===");
    Console.WriteLine($"Source: {report.Source.Id} ({report.Source.Language}/{report.Source.Framework})");
    Console.WriteLine($"Target: {report.Target.Id} ({report.Target.Language}/{report.Target.Framework})");
    Console.WriteLine($"Methods: {report.Summary.Methods}");
    Console.WriteLine($"Parameterized methods: {report.Summary.ParameterizedMethods}");
    Console.WriteLine($"Warnings: {report.Summary.Warnings}");
    Console.WriteLine($"Normalized profile written to: {Path.GetFullPath(Path.Combine(outPath, "migration-profile.v2.json"))}");
    return 0;
}

static void RunDumpIr(string outPath, string format, List<PipelineResult> results, SourceSpec source, TargetSpec target, string irVersion)
{
    Directory.CreateDirectory(outPath);

    var writeJson = format == "json" || format == "both";
    var writeMarkdown = format == "text" || format == "both";
    var writeLegacy = irVersion == "legacy" || irVersion == "both";
    var writeV2 = irVersion == "v2" || irVersion == "both";

    LegacyIrDumpDocument? legacyDocument = null;
    V2IrDumpDocument? v2Document = null;

    if (writeLegacy)
    {
        legacyDocument = IrDumpWriter.Build(results);

        var jsonName = irVersion == "both" ? "ir-dump.legacy.json" : "ir-dump.json";
        var markdownName = irVersion == "both" ? "ir-dump.legacy.md" : "ir-dump.md";

        if (writeJson)
            File.WriteAllText(Path.Combine(outPath, jsonName), IrDumpWriter.ToJson(legacyDocument));

        if (writeMarkdown)
            File.WriteAllText(Path.Combine(outPath, markdownName), IrDumpWriter.ToMarkdown(legacyDocument));
    }

    if (writeV2)
    {
        v2Document = V2IrDumpWriter.Build(results, target, source);

        var jsonName = irVersion == "both" ? "ir-dump.v2.json" : "ir-dump.json";
        var markdownName = irVersion == "both" ? "ir-dump.v2.md" : "ir-dump.md";

        if (writeJson)
            File.WriteAllText(Path.Combine(outPath, jsonName), V2IrDumpWriter.ToJson(v2Document));

        if (writeMarkdown)
            File.WriteAllText(Path.Combine(outPath, markdownName), V2IrDumpWriter.ToMarkdown(v2Document));
    }

    if (v2Document != null && legacyDocument == null)
    {
        Console.WriteLine("=== IR V2 Dump ===");
        Console.WriteLine($"Files: {v2Document.Summary.Files}");
        Console.WriteLine($"Source tests: {v2Document.Summary.SourceTests}");
        Console.WriteLine($"Target tests: {v2Document.Summary.TargetTests}");
        Console.WriteLine($"Target statements: {v2Document.Summary.TargetStatements}");
        Console.WriteLine($"Unsupported statements: {v2Document.Summary.TargetUnsupportedStatements}");
        Console.WriteLine($"Diagnostics: {v2Document.Summary.TargetDiagnostics}");
    }
    else if (legacyDocument != null)
    {
        Console.WriteLine("=== Legacy IR Dump ===");
        Console.WriteLine($"Files: {legacyDocument.Summary.Files}");
        Console.WriteLine($"Source tests: {legacyDocument.Summary.SourceTests}");
        Console.WriteLine($"Target tests: {legacyDocument.Summary.TargetTests}");
        Console.WriteLine($"Target actions: {legacyDocument.Summary.TargetActions}");
        Console.WriteLine($"Unsupported actions: {legacyDocument.Summary.TargetUnsupportedActions}");
        Console.WriteLine($"Unresolved targets: {legacyDocument.Summary.TargetUnresolvedTargets}");
        if (v2Document != null)
        {
            Console.WriteLine("=== IR V2 Dump ===");
            Console.WriteLine($"Target statements: {v2Document.Summary.TargetStatements}");
            Console.WriteLine($"Unsupported statements: {v2Document.Summary.TargetUnsupportedStatements}");
            Console.WriteLine($"Diagnostics: {v2Document.Summary.TargetDiagnostics}");
        }
    }

    Console.WriteLine($"IR dump written to: {Path.GetFullPath(outPath)}");
}

static void RunAnalyze(MigrationSummaryReport summary, string outPath, string format, ProjectAdapterConfig? config, List<PipelineResult> results, IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnmapped)
{
    Directory.CreateDirectory(outPath);

    var allUnsupported = CollectAllUnsupported(results);
    WriteReports(summary, outPath, format, allUnmapped, allUnsupported);
    GenerateDraftConfig(allUnmapped, outPath, config);
    WriteExplainTodoArtifacts(summary, outPath, format, allUnmapped, allUnsupported, null);

    PrintSummary(summary);
    Console.WriteLine($"Analysis written to: {Path.GetFullPath(outPath)}");
}

static void RunMigrate(MigrationSummaryReport summary, string outPath, string format, ProjectAdapterConfig? config, List<PipelineResult> results, IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnmapped, ITargetBackend targetBackend)
{
    Directory.CreateDirectory(outPath);

    int generated = 0;
    var writtenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var result in results)
    {
        string baseName = targetBackend.GetDefaultFileName(result.SourceModel);
        string outName = ResolveFileName(outPath, baseName, writtenNames);
        var fullOut = Path.Combine(outPath, outName);
        File.WriteAllText(fullOut, result.GeneratedOutput);
        generated++;
    }

    var summaryWithGenerated = summary with { GeneratedFiles = generated };

    var allUnsupported = CollectAllUnsupported(results);
    WriteReports(summaryWithGenerated, outPath, format, allUnmapped, allUnsupported);
    GenerateDraftConfig(allUnmapped, outPath, config);
    WriteExplainTodoArtifacts(summaryWithGenerated, outPath, format, allUnmapped, allUnsupported, null);
    WriteSmokePlanArtifacts(outPath, outPath, format);
    WriteMigrationBoardArtifacts(outPath, outPath, format);

    PrintSummary(summaryWithGenerated);
    Console.WriteLine($"Migration written to: {Path.GetFullPath(outPath)} ({generated} files generated)");
}

static TargetBackendRegistry CreateBuiltInTargetBackendRegistry()
{
    return new TargetBackendRegistry()
        .Register(new PlaywrightDotNetBackend())
        .Register(new PlaywrightTypeScriptBackend());
}

static SourceFrontendRegistry CreateBuiltInSourceFrontendRegistry(ProjectAdapterConfig? config)
{
    return new SourceFrontendRegistry()
        .Register(new CSharpSeleniumFrontend(config))
        .Register(new JavaSeleniumFrontend())
        .Register(new PythonSeleniumFrontend());
}

static bool ShouldAutoDetectSource(string mode, string source, bool sourceExplicit, string inputPath)
{
    if (sourceExplicit && !string.Equals(source, "auto", StringComparison.OrdinalIgnoreCase))
        return false;

    if (!string.Equals(source, "auto", StringComparison.OrdinalIgnoreCase) && sourceExplicit)
        return false;

    if (string.IsNullOrWhiteSpace(inputPath) || (!File.Exists(inputPath) && !Directory.Exists(inputPath)))
        return false;

    return mode is "analyze" or "dump-ir" or "migrate" or "verify" or "verify-project" or "doctor" or "orchestrate";
}

static void WriteSourceDetectionReport(SourceDetectionReport report, string outPath, string format, string selectedSource, bool explicitSource)
{
    try
    {
        Directory.CreateDirectory(outPath);
        var reportObject = new
        {
            SchemaVersion = "source-detection/v1",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            ExplicitSource = explicitSource,
            SelectedSource = selectedSource,
            report.InputPath,
            report.DetectedSourceId,
            report.Confidence,
            report.FilesScanned,
            report.Reasons,
            Candidates = report.Candidates.Select(c => new
            {
                c.SourceId,
                c.Language,
                c.Framework,
                c.Score,
                c.Confidence,
                c.MatchingFiles,
                c.Reasons,
                SampleFiles = c.SampleFiles.Select(PathRedaction.Redact).ToArray()
            }).ToArray()
        };

        if (format == "json" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "source-detection-report.json"),
                JsonSerializer.Serialize(reportObject, new JsonSerializerOptions { WriteIndented = true }));
        }

        if (format == "text" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "source-detection-report.md"), BuildSourceDetectionMarkdown(report, selectedSource, explicitSource));
        }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Warning: could not write source detection report: {ex.Message}");
    }
}

static string BuildSourceDetectionMarkdown(SourceDetectionReport report, string selectedSource, bool explicitSource)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Source Detection Report");
    sb.AppendLine();
    sb.AppendLine($"- Selected source: `{selectedSource}`");
    sb.AppendLine($"- Detected source: `{report.DetectedSourceId}`");
    sb.AppendLine($"- Confidence: `{report.Confidence}`");
    sb.AppendLine($"- Explicit source: `{explicitSource}`");
    sb.AppendLine($"- Files scanned: {report.FilesScanned}");
    sb.AppendLine($"- Input: `{PathRedaction.Redact(report.InputPath)}`");
    sb.AppendLine();
    sb.AppendLine("## Reasons");
    foreach (var reason in report.Reasons)
        sb.AppendLine($"- {EscapeMd(reason)}");
    sb.AppendLine();
    sb.AppendLine("## Candidates");
    sb.AppendLine("| Source | Language | Score | Confidence | Matching files | Reasons |");
    sb.AppendLine("|---|---|---:|---|---:|---|");
    foreach (var candidate in report.Candidates)
    {
        var reasons = candidate.Reasons.Count == 0 ? "" : string.Join("; ", candidate.Reasons.Select(EscapeMd));
        sb.AppendLine($"| `{candidate.SourceId}` | `{candidate.Language}` | {candidate.Score} | `{candidate.Confidence}` | {candidate.MatchingFiles} | {reasons} |");
    }
    return sb.ToString();
}

static bool ShouldWriteSourceCapabilityReport(string mode) =>
    mode is "analyze" or "dump-ir" or "migrate" or "verify" or "verify-project" or "doctor" or "orchestrate" or "config-normalize";

static void WriteSourceCapabilityReport(SourceCapabilityReport report, string outPath, string format)
{
    try
    {
        Directory.CreateDirectory(outPath);
        var reportObject = new
        {
            report.SchemaVersion,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Source = new
            {
                report.Source.Id,
                report.Source.Language,
                report.Source.Framework
            },
            report.Status,
            report.Summary,
            report.Capabilities,
            report.Limitations,
            report.RecommendedValidation
        };

        if (format == "json" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "source-capabilities-report.json"),
                JsonSerializer.Serialize(reportObject, new JsonSerializerOptions { WriteIndented = true }));
        }

        if (format == "text" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "source-capabilities-report.md"), BuildSourceCapabilityMarkdown(report));
        }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Warning: could not write source capability report: {ex.Message}");
    }
}

static string BuildSourceCapabilityMarkdown(SourceCapabilityReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Source Capability Report");
    sb.AppendLine();
    sb.AppendLine($"- Source: `{report.Source.Id}`");
    sb.AppendLine($"- Language: `{report.Source.Language}`");
    sb.AppendLine($"- Framework: `{report.Source.Framework}`");
    sb.AppendLine($"- Status: `{report.Status}`");
    sb.AppendLine();
    sb.AppendLine(report.Summary);
    sb.AppendLine();
    sb.AppendLine("## Capability matrix");
    sb.AppendLine("| Area | Support | Details | Examples |");
    sb.AppendLine("|---|---|---|---|");
    foreach (var capability in report.Capabilities)
    {
        var examples = capability.Examples.Count == 0 ? "" : string.Join("; ", capability.Examples.Select(EscapeMd));
        sb.AppendLine($"| `{capability.Area}` | `{capability.Support}` | {EscapeMd(capability.Details)} | {examples} |");
    }

    if (report.Limitations.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("## Limitations");
        foreach (var limitation in report.Limitations)
            sb.AppendLine($"- {EscapeMd(limitation)}");
    }

    if (report.RecommendedValidation.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("## Recommended validation");
        foreach (var validation in report.RecommendedValidation)
            sb.AppendLine($"- {EscapeMd(validation)}");
    }

    return sb.ToString();
}


static bool ShouldWriteTargetCapabilityReport(string mode) =>
    mode is "analyze" or "dump-ir" or "migrate" or "verify" or "verify-project" or "doctor" or "orchestrate" or "config-normalize";

static void WriteTargetCapabilityReport(TargetCapabilityReport report, string outPath, string format)
{
    try
    {
        Directory.CreateDirectory(outPath);
        var reportObject = new
        {
            report.SchemaVersion,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Target = new
            {
                report.Target.Id,
                report.Target.Language,
                report.Target.Framework
            },
            report.Status,
            report.Summary,
            report.Capabilities,
            report.Limitations,
            report.RecommendedValidation
        };

        if (format == "json" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "target-capabilities-report.json"),
                JsonSerializer.Serialize(reportObject, new JsonSerializerOptions { WriteIndented = true }));
        }

        if (format == "text" || format == "both")
        {
            File.WriteAllText(Path.Combine(outPath, "target-capabilities-report.md"), BuildTargetCapabilityMarkdown(report));
        }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Warning: could not write target capability report: {ex.Message}");
    }
}

static string BuildTargetCapabilityMarkdown(TargetCapabilityReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Target Capability Report");
    sb.AppendLine();
    sb.AppendLine($"- Target: `{report.Target.Id}`");
    sb.AppendLine($"- Language: `{report.Target.Language}`");
    sb.AppendLine($"- Framework: `{report.Target.Framework}`");
    sb.AppendLine($"- Status: `{report.Status}`");
    sb.AppendLine();
    sb.AppendLine(report.Summary);
    sb.AppendLine();
    sb.AppendLine("## Capability matrix");
    sb.AppendLine("| Area | Support | Details | Examples |");
    sb.AppendLine("|---|---|---|---|");
    foreach (var capability in report.Capabilities)
    {
        var examples = capability.Examples.Count == 0 ? "" : string.Join("; ", capability.Examples.Select(EscapeMd));
        sb.AppendLine($"| `{capability.Area}` | `{capability.Support}` | {EscapeMd(capability.Details)} | {examples} |");
    }

    if (report.Limitations.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("## Limitations");
        foreach (var limitation in report.Limitations)
            sb.AppendLine($"- {EscapeMd(limitation)}");
    }

    if (report.RecommendedValidation.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("## Recommended validation");
        foreach (var validation in report.RecommendedValidation)
            sb.AppendLine($"- {EscapeMd(validation)}");
    }

    return sb.ToString();
}

static int RunCapabilities(SourceFrontendRegistry sourceRegistry, TargetBackendRegistry targetRegistry, string outPath, string format)
{
    Directory.CreateDirectory(outPath);
    var sourceReports = sourceRegistry.Frontends.Select(f => f.Capabilities).ToArray();
    var targetReports = targetRegistry.Backends.Select(b => b.Capabilities).ToArray();
    var reportObject = new
    {
        SchemaVersion = "migrator-capabilities/v1",
        GeneratedAtUtc = DateTimeOffset.UtcNow,
        Sources = sourceReports.Select(r => new
        {
            r.SchemaVersion,
            Source = new { r.Source.Id, r.Source.Language, r.Source.Framework },
            r.Status,
            r.Summary,
            r.Capabilities,
            r.Limitations,
            r.RecommendedValidation
        }),
        Targets = targetReports.Select(r => new
        {
            r.SchemaVersion,
            Target = new { r.Target.Id, r.Target.Language, r.Target.Framework },
            r.Status,
            r.Summary,
            r.Capabilities,
            r.Limitations,
            r.RecommendedValidation
        })
    };

    if (format == "json" || format == "both")
    {
        File.WriteAllText(Path.Combine(outPath, "capabilities-report.json"),
            JsonSerializer.Serialize(reportObject, new JsonSerializerOptions { WriteIndented = true }));
    }

    if (format == "text" || format == "both")
    {
        File.WriteAllText(Path.Combine(outPath, "capabilities-report.md"), BuildCapabilitiesMarkdown(sourceReports, targetReports));
    }

    Console.WriteLine($"Capability report written to: {Path.GetFullPath(outPath)}");
    return 0;
}

static string BuildCapabilitiesMarkdown(IReadOnlyList<SourceCapabilityReport> sourceReports, IReadOnlyList<TargetCapabilityReport> targetReports)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Migrator Capability Report");
    sb.AppendLine();
    sb.AppendLine("This report lists built-in source frontends and target backends. Stable entries are intended for public use; experimental entries require extra verification.");
    sb.AppendLine();
    sb.AppendLine("## Source frontends");
    sb.AppendLine("| Source | Language | Framework | Status | Summary |");
    sb.AppendLine("|---|---|---|---|---|");
    foreach (var report in sourceReports.OrderBy(r => r.Source.Id, StringComparer.OrdinalIgnoreCase))
        sb.AppendLine($"| `{report.Source.Id}` | `{report.Source.Language}` | `{report.Source.Framework}` | `{report.Status}` | {EscapeMd(report.Summary)} |");

    sb.AppendLine();
    sb.AppendLine("## Target backends");
    sb.AppendLine("| Target | Language | Framework | Status | Summary |");
    sb.AppendLine("|---|---|---|---|---|");
    foreach (var report in targetReports.OrderBy(r => r.Target.Id, StringComparer.OrdinalIgnoreCase))
        sb.AppendLine($"| `{report.Target.Id}` | `{report.Target.Language}` | `{report.Target.Framework}` | `{report.Status}` | {EscapeMd(report.Summary)} |");

    sb.AppendLine();
    sb.AppendLine("## Validation rule of thumb");
    sb.AppendLine("- Stable source/target pairs can be used for normal preview migrations with project verification.");
    sb.AppendLine("- Experimental source or target entries should be treated as MVP/spike output until their capability reports and generated TODOs are reviewed.");
    return sb.ToString();
}

static ITestFileParser ResolveLegacyParser(ISourceFrontend sourceFrontend)
{
    if (sourceFrontend is TestFileParserSourceFrontend legacyFrontend)
        return legacyFrontend.Parser;

    if (sourceFrontend is UnsupportedSourceFrontend unsupportedFrontend)
        throw unsupportedFrontend.CreateNotSupportedException();

    throw new NotSupportedException($"Source frontend '{sourceFrontend.Source.Id}' cannot be used by the legacy pipeline yet.");
}

static MigrationPipeline CreateMigrationPipeline(ITestFileParser parser, IRenderer renderer, IProjectAdapter? adapter, ITargetBackend targetBackend, string renderIr, SourceSpec source)
{
    if (string.Equals(renderIr, "v2", StringComparison.OrdinalIgnoreCase))
        return new MigrationPipeline(parser, targetBackend, adapter, MigrationPipelineRenderMode.IrV2, source);

    return new MigrationPipeline(parser, renderer, adapter, sourceSpec: source);
}

static bool IsTypeScriptTarget(ITargetBackend targetBackend)
{
    return string.Equals(targetBackend.Target.Id, "playwright-typescript", StringComparison.OrdinalIgnoreCase)
        || string.Equals(targetBackend.Target.Language, "typescript", StringComparison.OrdinalIgnoreCase);
}

static string? NormalizeTargetTestFramework(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;

    var normalized = value.Trim().ToLowerInvariant();
    return normalized switch
    {
        "nunit" or "n-unit" => "nunit",
        "xunit" or "x-unit" => "xunit",
        _ => null
    };
}

static IEnumerable<PackageReferenceConfig> GetDefaultVerificationPackageReferences(IEnumerable<string> targetTestFrameworks)
{
    yield return new PackageReferenceConfig { Include = "Microsoft.NET.Test.Sdk", Version = "17.12.0" };

    foreach (var targetTestFramework in targetTestFrameworks.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (string.Equals(targetTestFramework, "xunit", StringComparison.OrdinalIgnoreCase))
        {
            yield return new PackageReferenceConfig { Include = "Microsoft.Playwright.Xunit", Version = "1.52.0" };
            yield return new PackageReferenceConfig { Include = "xunit", Version = "2.9.2" };
            yield return new PackageReferenceConfig { Include = "xunit.runner.visualstudio", Version = "2.8.2" };
            continue;
        }

        yield return new PackageReferenceConfig { Include = "Microsoft.Playwright.NUnit", Version = "1.52.0" };
        yield return new PackageReferenceConfig { Include = "NUnit", Version = "4.2.2" };
        yield return new PackageReferenceConfig { Include = "NUnit3TestAdapter", Version = "4.6.0" };
    }
}

static IReadOnlyList<string> ResolveTargetTestFrameworksForVerification(ProjectAdapterConfig config)
{
    var frameworks = new List<string>();

    var globalFramework = NormalizeTargetTestFramework(config.TestHost?.TargetTestFramework);
    if (globalFramework != null)
        frameworks.Add(globalFramework);

    foreach (var scope in config.Scopes)
    {
        var scopeFramework = NormalizeTargetTestFramework(scope.TestHost?.TargetTestFramework);
        if (scopeFramework != null)
            frameworks.Add(scopeFramework);
    }

    if (frameworks.Count == 0)
        frameworks.Add("nunit");

    return frameworks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

static ProjectAdapterConfig ApplyTargetTestFrameworkOverride(ProjectAdapterConfig config, string targetTestFramework)
{
    var normalized = NormalizeTargetTestFramework(targetTestFramework)
        ?? throw new ArgumentException($"Unsupported target test framework '{targetTestFramework}'.", nameof(targetTestFramework));

    return new ProjectAdapterConfig
    {
        SchemaVersion = config.SchemaVersion,
        SourceProjectName = config.SourceProjectName,
        UiTargets = config.UiTargets,
        PageObjects = config.PageObjects,
        Methods = config.Methods,
        TargetKnownTypes = config.TargetKnownTypes,
        TargetKnownIdentifiers = config.TargetKnownIdentifiers,
        SourceOnlyIdentifiers = config.SourceOnlyIdentifiers,
        SuppressedMethods = config.SuppressedMethods,
        SuppressedMethodPatterns = config.SuppressedMethodPatterns,
        ParameterizedMethods = config.ParameterizedMethods,
        NavigationUrls = config.NavigationUrls,
        NavigationTargetStatement = config.NavigationTargetStatement,
        LocatorSettings = config.LocatorSettings,
        TestHost = ApplyTargetTestFrameworkToTestHost(config.TestHost, normalized),
        Scopes = config.Scopes.Select(scope => ApplyTargetTestFrameworkToScope(scope, normalized)).ToArray(),
        RecognizerAliases = config.RecognizerAliases,
        GenericResultMethods = config.GenericResultMethods,
        WaitPolicies = config.WaitPolicies,
        Verification = config.Verification,
        QualityGates = config.QualityGates,
        Tables = config.Tables,
        Pagination = config.Pagination
    };
}

static TestHostConfig ApplyTargetTestFrameworkToTestHost(TestHostConfig? testHost, string targetTestFramework)
{
    return new TestHostConfig
    {
        TargetTestFramework = targetTestFramework,
        Namespace = testHost?.Namespace,
        BaseClass = testHost?.BaseClass,
        ClassName = testHost?.ClassName,
        ClassAttributes = testHost?.ClassAttributes,
        Usings = testHost?.Usings,
        SetUpStatements = testHost?.SetUpStatements,
        TargetPageVariable = testHost?.TargetPageVariable
    };
}

static ProfileScope ApplyTargetTestFrameworkToScope(ProfileScope scope, string targetTestFramework)
{
    return new ProfileScope
    {
        Name = scope.Name,
        SourcePathPatterns = scope.SourcePathPatterns,
        TestHost = scope.TestHost == null ? null : ApplyTargetTestFrameworkToTestHost(scope.TestHost, targetTestFramework),
        UiTargets = scope.UiTargets,
        Methods = scope.Methods,
        ParameterizedMethods = scope.ParameterizedMethods,
        NavigationUrls = scope.NavigationUrls,
        NavigationTargetStatement = scope.NavigationTargetStatement,
        TargetKnownTypes = scope.TargetKnownTypes,
        TargetKnownIdentifiers = scope.TargetKnownIdentifiers,
        SuppressedMethods = scope.SuppressedMethods,
        SuppressedMethodPatterns = scope.SuppressedMethodPatterns,
        Tables = scope.Tables,
        Pagination = scope.Pagination
    };
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

    var syntaxChecker = CreateGeneratedCodeChecker();

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

static int RunVerifyProject(MigrationSummaryReport summary, string outPath, string format, List<PipelineResult> results, ProjectAdapterConfig config, string? configPath, string inputPath, IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnmapped, IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnsupported)
{
    Directory.CreateDirectory(outPath);

    Console.WriteLine("=== Verify Project Mode ===");
    Console.WriteLine("Creates a temporary verification project and runs dotnet build. Source project files are not modified.");
    Console.WriteLine();

    var generatedDir = Path.Combine(outPath, "generated");
    var harnessDir = Path.Combine(outPath, "project-verify");
    Directory.CreateDirectory(generatedDir);
    Directory.CreateDirectory(harnessDir);

    var writtenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var generatedFiles = new List<string>();
    foreach (var result in results)
    {
        var baseName = $"{result.SourceModel.ClassName}Playwright.cs";
        var outName = ResolveFileName(generatedDir, baseName, writtenNames);
        var fullOut = Path.Combine(generatedDir, outName);
        File.WriteAllText(fullOut, result.GeneratedOutput);
        generatedFiles.Add(fullOut);
    }

    var verification = config.Verification ?? new VerificationConfig();
    var baseDir = ResolveVerificationBaseDirectory(verification, configPath, inputPath);
    var solutionPath = ResolveSolutionPath(verification, baseDir, inputPath);
    var projectDiscovery = ResolveProjectReferences(verification, baseDir, inputPath).ToList();
    var projectReferences = projectDiscovery.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    var assemblyReferences = ResolvePathList(verification.AssemblyReferences, baseDir).ToList();
    var discoveredBuildFiles = DiscoverBuildFiles(verification, baseDir, projectReferences).ToList();
    var targetFramework = ResolveVerificationTargetFramework(verification, projectReferences);
    var packageReferences = BuildPackageReferences(verification, projectReferences, config).ToList();
    var buildWorkingDirectory = ResolveBuildWorkingDirectory(verification, baseDir, solutionPath);

    var csprojPath = Path.Combine(harnessDir, "Generated.Playwright.Verify.csproj");
    File.WriteAllText(csprojPath, BuildVerificationCsproj(
        verification,
        generatedDir,
        projectReferences,
        assemblyReferences,
        packageReferences,
        targetFramework,
        discoveredBuildFiles));

    var buildResult = RunDotnetBuild(csprojPath, verification, buildWorkingDirectory);
    var rawDiagnostics = ExtractBuildDiagnostics(buildResult.StdOut + "\n" + buildResult.StdErr);
    var classifiedDiagnostics = rawDiagnostics.Select(ClassifyBuildDiagnostic).ToArray();
    var report = new ProjectVerifyReport(
        GeneratedAtUtc: DateTimeOffset.UtcNow,
        Status: buildResult.ExitCode == 0 ? "passed" : "failed",
        ExitCode: buildResult.ExitCode,
        GeneratedFiles: generatedFiles.Select(Path.GetFullPath).ToArray(),
        HarnessProject: Path.GetFullPath(csprojPath),
        BaseDirectory: Path.GetFullPath(baseDir),
        Solution: solutionPath != null ? Path.GetFullPath(solutionPath) : null,
        BuildWorkingDirectory: Path.GetFullPath(buildWorkingDirectory),
        ProjectReferences: projectReferences.Select(Path.GetFullPath).ToArray(),
        ProjectReferenceDiscovery: projectDiscovery.ToArray(),
        AssemblyReferences: assemblyReferences.Select(Path.GetFullPath).ToArray(),
        PackageReferences: packageReferences.ToArray(),
        BuildFilesImported: discoveredBuildFiles.Select(Path.GetFullPath).ToArray(),
        TargetFramework: targetFramework,
        Command: buildResult.Command,
        StdOut: buildResult.StdOut,
        StdErr: buildResult.StdErr,
        Diagnostics: rawDiagnostics,
        ClassifiedDiagnostics: classifiedDiagnostics);

    File.WriteAllText(Path.Combine(outPath, "project-verify-report.json"),
        System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(Path.Combine(outPath, "project-verify-report.md"), WriteProjectVerifyMarkdown(report));
    WriteExplainTodoArtifacts(summary with { GeneratedFiles = generatedFiles.Count }, outPath, format, allUnmapped, allUnsupported, report);
    WriteSmokePlanArtifacts(outPath, outPath, format);
    WriteMigrationBoardArtifacts(outPath, outPath, format);

    PrintSummary(summary with { GeneratedFiles = generatedFiles.Count });
    Console.WriteLine();
    Console.WriteLine("=== Project Verify Summary ===");
    Console.WriteLine($"Status: {report.Status.ToUpperInvariant()}");
    Console.WriteLine($"Generated files: {report.GeneratedFiles.Length}");
    Console.WriteLine($"Harness project: {report.HarnessProject}");
    Console.WriteLine($"Base directory: {report.BaseDirectory}");
    Console.WriteLine($"Build working directory: {report.BuildWorkingDirectory}");
    Console.WriteLine($"Target framework: {report.TargetFramework}");
    Console.WriteLine($"Project references: {report.ProjectReferences.Length}");
    Console.WriteLine($"Package references: {report.PackageReferences.Length}");
    Console.WriteLine($"Imported build files: {report.BuildFilesImported.Length}");
    Console.WriteLine($"Diagnostics: {report.Diagnostics.Length}");
    if (report.Diagnostics.Length > 0)
    {
        foreach (var diagnostic in report.Diagnostics.Take(30))
            Console.WriteLine($"  {diagnostic}");
        if (report.Diagnostics.Length > 30)
            Console.WriteLine($"  ... and {report.Diagnostics.Length - 30} more");
    }
    Console.WriteLine($"Project verify reports written to: {Path.GetFullPath(outPath)}");

    return report.ExitCode == 0 ? 0 : 2;
}

static string ResolveVerificationBaseDirectory(VerificationConfig verification, string? configPath, string inputPath)
{
    if (!string.IsNullOrWhiteSpace(verification.BaseDirectory))
        return Path.GetFullPath(ExpandPath(verification.BaseDirectory));

    if (!string.IsNullOrWhiteSpace(configPath))
        return Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();

    if (Directory.Exists(inputPath))
        return Path.GetFullPath(inputPath);

    return Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Directory.GetCurrentDirectory();
}

static string? ResolveSolutionPath(VerificationConfig verification, string baseDir, string inputPath)
{
    if (!string.IsNullOrWhiteSpace(verification.Solution))
        return ResolvePath(verification.Solution, baseDir);

    var nearestSolution = FindNearestFile(inputPath, "*.sln");
    if (nearestSolution != null)
        return nearestSolution;

    var baseSolution = Directory.Exists(baseDir)
        ? new DirectoryInfo(baseDir).GetFiles("*.sln", SearchOption.TopDirectoryOnly).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
        : null;

    return baseSolution?.FullName;
}

static string ResolveBuildWorkingDirectory(VerificationConfig verification, string baseDir, string? solutionPath)
{
    if (!string.IsNullOrWhiteSpace(verification.BuildWorkingDirectory))
        return ResolvePath(verification.BuildWorkingDirectory, baseDir);

    if (!string.IsNullOrWhiteSpace(verification.BaseDirectory))
        return Path.GetFullPath(baseDir);

    if (solutionPath != null)
        return Path.GetDirectoryName(solutionPath) ?? baseDir;

    return Path.GetFullPath(baseDir);
}

static IEnumerable<ProjectReferenceDiscovery> ResolveProjectReferences(VerificationConfig verification, string baseDir, string inputPath)
{
    var discovered = new Dictionary<string, ProjectReferenceDiscovery>(StringComparer.OrdinalIgnoreCase);

    void AddProject(string path, string source, string reason)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            if (!discovered.ContainsKey(full))
                discovered[full] = new ProjectReferenceDiscovery(full, source, "missing", reason + " (file not found)");
            return;
        }

        if (!discovered.ContainsKey(full) || discovered[full].Source == "missing")
            discovered[full] = new ProjectReferenceDiscovery(full, source, "included", reason);
    }

    foreach (var path in ResolvePathList(verification.ProjectReferences, baseDir))
        AddProject(path, "config", "explicit Verification.ProjectReferences entry");

    var autoNearest = verification.AutoDiscoverNearestProject ?? true;
    if (autoNearest)
    {
        var nearest = FindNearestCsproj(inputPath);
        if (nearest != null)
            AddProject(nearest, "auto-nearest", "nearest .csproj discovered upward from --input");
    }

    var includeTransitive = verification.AutoDiscoverProjectReferences ?? true;
    if (includeTransitive)
    {
        var queue = new Queue<string>(discovered.Values.Where(x => x.Status == "included").Select(x => x.Path));
        while (queue.Count > 0)
        {
            var project = queue.Dequeue();
            foreach (var reference in ReadProjectReferences(project))
            {
                var before = discovered.Count;
                AddProject(reference, "transitive", $"ProjectReference from {Path.GetFileName(project)}");
                if (discovered.Count > before && File.Exists(reference))
                    queue.Enqueue(reference);
            }
        }
    }

    return discovered.Values
        .OrderBy(x => x.Source == "config" ? 0 : x.Source == "auto-nearest" ? 1 : 2)
        .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase);
}

static IEnumerable<string> ResolvePathList(IEnumerable<string> paths, string baseDir)
{
    foreach (var path in paths)
    {
        if (string.IsNullOrWhiteSpace(path))
            continue;
        yield return ResolvePath(path, baseDir);
    }
}

static string ResolvePath(string path, string baseDir)
{
    var normalized = ExpandPath(path.Trim());
    return Path.IsPathRooted(normalized)
        ? Path.GetFullPath(normalized)
        : Path.GetFullPath(Path.Combine(baseDir, normalized));
}

static string ExpandPath(string path) => Environment.ExpandEnvironmentVariables(path);

static string? FindNearestCsproj(string inputPath)
{
    var start = File.Exists(inputPath)
        ? Path.GetDirectoryName(Path.GetFullPath(inputPath))
        : Path.GetFullPath(inputPath);

    var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory());
    while (dir != null)
    {
        var projects = dir.GetFiles("*.csproj", SearchOption.TopDirectoryOnly)
            .Where(p => !p.FullName.Replace('\\', '/').Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                     && !p.FullName.Replace('\\', '/').Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (projects.Length == 1)
            return projects[0].FullName;

        if (projects.Length > 1)
        {
            var inputName = new DirectoryInfo(start ?? dir.FullName).Name;
            var preferred = projects.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p.Name).Equals(inputName, StringComparison.OrdinalIgnoreCase));
            return (preferred ?? projects[0]).FullName;
        }

        dir = dir.Parent;
    }

    return null;
}

static string FindRepoRootForVerification(string path)
{
    var start = File.Exists(path)
        ? Path.GetDirectoryName(Path.GetFullPath(path))
        : Path.GetFullPath(path);

    var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory());
    DirectoryInfo? best = null;
    while (dir != null)
    {
        if (dir.GetFiles("*.sln", SearchOption.TopDirectoryOnly).Length > 0
            || File.Exists(Path.Combine(dir.FullName, "NuGet.config"))
            || File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            best = dir;
        }
        dir = dir.Parent;
    }

    return best?.FullName ?? (start ?? Directory.GetCurrentDirectory());
}

static string? FindNearestFile(string inputPath, string pattern)
{
    var start = File.Exists(inputPath)
        ? Path.GetDirectoryName(Path.GetFullPath(inputPath))
        : Path.GetFullPath(inputPath);

    var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory());
    while (dir != null)
    {
        var match = dir.GetFiles(pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (match != null)
            return match.FullName;
        dir = dir.Parent;
    }

    return null;
}

static IEnumerable<string> ReadProjectReferences(string csprojPath)
{
    var projectDir = Path.GetDirectoryName(csprojPath) ?? Directory.GetCurrentDirectory();
    XDocument doc;
    try
    {
        doc = XDocument.Load(csprojPath);
    }
    catch
    {
        yield break;
    }

    foreach (var include in doc.Descendants().Where(x => x.Name.LocalName == "ProjectReference")
        .Select(x => (string?)x.Attribute("Include"))
        .Where(x => !string.IsNullOrWhiteSpace(x)))
    {
        var full = Path.GetFullPath(Path.Combine(projectDir, ExpandPath(include!)));
        yield return full;
    }
}

static string ResolveVerificationTargetFramework(VerificationConfig verification, IReadOnlyList<string> projectReferences)
{
    if (!string.IsNullOrWhiteSpace(verification.TargetFramework))
        return verification.TargetFramework.Trim();

    foreach (var project in projectReferences.Where(File.Exists))
    {
        var tfm = ReadTargetFramework(project);
        if (!string.IsNullOrWhiteSpace(tfm))
            return tfm!;
    }

    return "net8.0";
}

static string? ReadTargetFramework(string csprojPath)
{
    try
    {
        var doc = XDocument.Load(csprojPath);
        var targetFramework = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "TargetFramework")?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(targetFramework))
            return targetFramework;

        var targetFrameworks = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "TargetFrameworks")?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(targetFrameworks))
            return targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    }
    catch
    {
        return null;
    }

    return null;
}

static IEnumerable<string> DiscoverBuildFiles(VerificationConfig verification, string baseDir, IReadOnlyList<string> projectReferences)
{
    if (verification.AutoDiscoverBuildFiles == false)
        yield break;

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var anchors = new List<string> { baseDir };
    anchors.AddRange(projectReferences.Where(File.Exists).Select(p => Path.GetDirectoryName(p) ?? baseDir));

    foreach (var anchor in anchors)
    {
        foreach (var fileName in new[] { "Directory.Build.props", "Directory.Packages.props", "Directory.Build.targets" })
        {
            var found = FindNearestFile(anchor, fileName);
            if (found != null && seen.Add(found))
                yield return found;
        }
    }
}

static IEnumerable<PackageReferenceConfig> BuildPackageReferences(VerificationConfig verification, IReadOnlyList<string> projectReferences, ProjectAdapterConfig config)
{
    var result = new List<PackageReferenceConfig>();
    if (verification.DisableDefaultPackageReferences != true)
    {
        var targetTestFrameworks = ResolveTargetTestFrameworksForVerification(config);
        result.AddRange(GetDefaultVerificationPackageReferences(targetTestFrameworks));
    }

    if (verification.AutoDiscoverPackageReferences == true)
        result.AddRange(ReadPackageReferences(projectReferences));

    var byInclude = new Dictionary<string, PackageReferenceConfig>(StringComparer.OrdinalIgnoreCase);
    foreach (var package in result.Concat(verification.PackageReferences))
    {
        if (string.IsNullOrWhiteSpace(package.Include) || string.IsNullOrWhiteSpace(package.Version))
            continue;
        byInclude[package.Include.Trim()] = new PackageReferenceConfig
        {
            Include = package.Include.Trim(),
            Version = package.Version.Trim()
        };
    }

    return byInclude.Values.OrderBy(p => p.Include, StringComparer.OrdinalIgnoreCase);
}

static IEnumerable<PackageReferenceConfig> ReadPackageReferences(IEnumerable<string> projectReferences)
{
    foreach (var project in projectReferences.Where(File.Exists))
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(project);
        }
        catch
        {
            continue;
        }

        foreach (var pr in doc.Descendants().Where(x => x.Name.LocalName == "PackageReference"))
        {
            var include = ((string?)pr.Attribute("Include"))?.Trim();
            var version = ((string?)pr.Attribute("Version"))?.Trim()
                ?? pr.Elements().FirstOrDefault(x => x.Name.LocalName == "Version")?.Value.Trim();
            if (!string.IsNullOrWhiteSpace(include) && !string.IsNullOrWhiteSpace(version))
                yield return new PackageReferenceConfig { Include = include!, Version = version! };
        }
    }
}

static HashSet<string> ReadCentralPackageNames(IEnumerable<string> buildFiles)
{
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in buildFiles.Where(x => Path.GetFileName(x).Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase) && File.Exists(x)))
    {
        try
        {
            var doc = XDocument.Load(file);
            foreach (var packageVersion in doc.Descendants().Where(x => x.Name.LocalName == "PackageVersion"))
            {
                var include = ((string?)packageVersion.Attribute("Include"))?.Trim();
                if (!string.IsNullOrWhiteSpace(include))
                    result.Add(include!);
            }
        }
        catch
        {
            // Best effort only. If parsing fails, keep explicit PackageReference versions.
        }
    }
    return result;
}

static string BuildVerificationCsproj(
    VerificationConfig verification,
    string generatedDir,
    IReadOnlyList<string> projectReferences,
    IReadOnlyList<string> assemblyReferences,
    IReadOnlyList<PackageReferenceConfig> packageReferences,
    string targetFramework,
    IReadOnlyList<string> buildFiles)
{
    var generatedGlob = Path.Combine(generatedDir, "**", "*.cs");
    var props = buildFiles.Where(x => x.EndsWith(".props", StringComparison.OrdinalIgnoreCase)).ToArray();
    var targets = buildFiles.Where(x => x.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)).ToArray();
    var centralPackageNames = ReadCentralPackageNames(buildFiles);

    var sb = new StringBuilder();
    sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
    foreach (var propsPath in props)
        sb.AppendLine($"  <Import Project=\"{EscapeXml(propsPath)}\" Condition=\"Exists('{EscapeXml(propsPath)}')\" />");
    if (props.Length > 0)
        sb.AppendLine();
    sb.AppendLine("  <PropertyGroup>");
    sb.AppendLine($"    <TargetFramework>{EscapeXml(targetFramework)}</TargetFramework>");
    sb.AppendLine("    <IsPackable>false</IsPackable>");
    sb.AppendLine("    <Nullable>enable</Nullable>");
    sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
    sb.AppendLine("    <LangVersion>latest</LangVersion>");
    sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
    sb.AppendLine("  </PropertyGroup>");
    sb.AppendLine();
    sb.AppendLine("  <ItemGroup>");
    sb.AppendLine($"    <Compile Include=\"{EscapeXml(generatedGlob)}\" />");
    sb.AppendLine("  </ItemGroup>");

    if (packageReferences.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        foreach (var p in packageReferences)
        {
            if (centralPackageNames.Contains(p.Include))
                sb.AppendLine($"    <PackageReference Include=\"{EscapeXml(p.Include)}\" />");
            else
                sb.AppendLine($"    <PackageReference Include=\"{EscapeXml(p.Include)}\" Version=\"{EscapeXml(p.Version)}\" />");
        }
        sb.AppendLine("  </ItemGroup>");
    }

    if (projectReferences.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        foreach (var r in projectReferences)
            sb.AppendLine($"    <ProjectReference Include=\"{EscapeXml(r)}\" />");
        sb.AppendLine("  </ItemGroup>");
    }

    if (assemblyReferences.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        foreach (var r in assemblyReferences)
        {
            var name = Path.GetFileNameWithoutExtension(r);
            sb.AppendLine($"    <Reference Include=\"{EscapeXml(name)}\">");
            sb.AppendLine($"      <HintPath>{EscapeXml(r)}</HintPath>");
            sb.AppendLine("    </Reference>");
        }
        sb.AppendLine("  </ItemGroup>");
    }

    if (targets.Length > 0)
    {
        sb.AppendLine();
        foreach (var targetsPath in targets)
            sb.AppendLine($"  <Import Project=\"{EscapeXml(targetsPath)}\" Condition=\"Exists('{EscapeXml(targetsPath)}')\" />");
    }

    sb.AppendLine("</Project>");
    return sb.ToString();
}

static DotnetBuildResult RunDotnetBuild(string csprojPath, VerificationConfig verification, string workingDirectory)
{
    var args = new List<string>
    {
        "build",
        csprojPath,
        "-v:minimal",
        $"-c:{(string.IsNullOrWhiteSpace(verification.Configuration) ? "Debug" : verification.Configuration!.Trim())}"
    };

    if (verification.NoRestore == true)
        args.Add("--no-restore");

    if (!string.IsNullOrWhiteSpace(verification.RuntimeIdentifier))
    {
        args.Add("-r");
        args.Add(verification.RuntimeIdentifier!.Trim());
    }

    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Directory.GetCurrentDirectory(),
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var arg in args)
        psi.ArgumentList.Add(arg);

    try
    {
        using var process = Process.Start(psi);
        if (process == null)
            return new DotnetBuildResult(127, "dotnet " + string.Join(" ", args.Select(QuoteArg)), "", "Failed to start dotnet process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new DotnetBuildResult(process.ExitCode, "dotnet " + string.Join(" ", args.Select(QuoteArg)), stdout, stderr);
    }
    catch (Exception ex)
    {
        return new DotnetBuildResult(127, "dotnet " + string.Join(" ", args.Select(QuoteArg)), "", ex.Message);
    }
}

static string[] ExtractBuildDiagnostics(string text)
{
    return text.Replace("\r\n", "\n")
        .Split('\n')
        .Where(line => line.Contains(": error ", StringComparison.OrdinalIgnoreCase)
                    || line.Contains(": warning ", StringComparison.OrdinalIgnoreCase)
                    || System.Text.RegularExpressions.Regex.IsMatch(line, @"\bCS\d{4}\b"))
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

static ProjectVerifyDiagnostic ClassifyBuildDiagnostic(string diagnostic)
{
    var codeMatch = System.Text.RegularExpressions.Regex.Match(diagnostic, @"\b(?<code>CS\d{4}|NU\d{4}|MSB\d{4})\b");
    var code = codeMatch.Success ? codeMatch.Groups["code"].Value : "UNKNOWN";
    var severity = diagnostic.Contains(": warning ", StringComparison.OrdinalIgnoreCase) ? "warning" : "error";

    var locationMatch = System.Text.RegularExpressions.Regex.Match(diagnostic, @"^(?<file>.*?)(\((?<line>\d+),(?<column>\d+)\))?:\s*(error|warning)");
    var file = locationMatch.Success ? locationMatch.Groups["file"].Value.Trim() : null;
    var line = locationMatch.Success && int.TryParse(locationMatch.Groups["line"].Value, out var parsedLine) ? parsedLine : (int?)null;

    var category = "unknown";
    var likelyCause = "Требуется ручная классификация diagnostics.";
    var suggestedAction = "Посмотри raw diagnostic и ближайший generated-файл.";

    if (code == "CS0103")
    {
        category = "unknown-identifier";
        likelyCause = "Generated-код ссылается на идентификатор, которого нет в verification project: source-only leak, missing target known identifier или потерянная target-local переменная.";
        suggestedAction = "Проверь, не должен ли символ быть SourceOnlyIdentifiers, TargetKnownIdentifiers/TargetKnownTypes или результатом active TargetStatements.";
    }
    else if (code == "CS0246")
    {
        category = "missing-type-or-namespace";
        likelyCause = "Не найден тип/namespace: чаще всего не хватает ProjectReference/PackageReference или using указывает на сборку, которая не подключена в Verification.";
        suggestedAction = "Добавь нужный .csproj в Verification.ProjectReferences или пакет в Verification.PackageReferences.";
    }
    else if (code == "CS0234")
    {
        category = "missing-namespace-member";
        likelyCause = "Namespace найден частично, но вложенный namespace/type отсутствует: обычно missing project/package reference или неверный using.";
        suggestedAction = "Проверь ProjectReferences и реальные namespace в исходном проекте.";
    }
    else if (code == "CS1061")
    {
        category = "missing-member";
        likelyCause = "Тип найден, но метода/свойства нет: возможно mapping сгенерировал неверный Playwright/helper API.";
        suggestedAction = "Проверь adapter-config mapping и target helper API.";
    }
    else if (code == "CS1501" || code == "CS1503" || code == "CS7036")
    {
        category = "signature-mismatch";
        likelyCause = "Метод найден, но аргументы не совпали: mapping сохранил Selenium-семантику или неверно перенёс параметры.";
        suggestedAction = "Проверь ParameterizedMethodMapping/MappedMethodInvocationAction и placeholders.";
    }
    else if (code == "CS1998")
    {
        category = "async-without-await";
        likelyCause = "Сгенерированный async-тест не содержит await. Обычно не блокер, но может быть сигналом пустой/закомментированной миграции.";
        suggestedAction = "Проверь, не стал ли тест почти полностью TODO.";
    }
    else if (code.StartsWith("NU", StringComparison.OrdinalIgnoreCase))
    {
        category = "nuget-restore";
        likelyCause = "Проблема restore/package source/version. Часто связана с внутренним NuGet feed или отсутствующим NuGet.config.";
        suggestedAction = "Укажи Verification.BuildWorkingDirectory на корень repo с NuGet.config или добавь нужный package source локально.";
    }
    else if (code.StartsWith("MSB", StringComparison.OrdinalIgnoreCase))
    {
        category = "msbuild-project";
        likelyCause = "MSBuild не смог собрать temporary harness или один из referenced проектов.";
        suggestedAction = "Проверь imported Directory.Build.props/targets, TargetFramework и ProjectReferences.";
    }

    return new ProjectVerifyDiagnostic(
        Raw: diagnostic,
        Code: code,
        Severity: severity,
        Category: category,
        File: string.IsNullOrWhiteSpace(file) ? null : file,
        Line: line,
        LikelyCause: likelyCause,
        SuggestedAction: suggestedAction);
}

static Dictionary<string, int> CountDiagnosticCategories(IEnumerable<ProjectVerifyDiagnostic> diagnostics)
{
    return diagnostics
        .GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(g => g.Count())
        .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
}

static string WriteProjectVerifyMarkdown(ProjectVerifyReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Project Verify Report");
    sb.AppendLine();
    sb.AppendLine($"- **Generated**: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss zzz}");
    sb.AppendLine($"- **Status**: `{report.Status}`");
    sb.AppendLine($"- **Exit code**: `{report.ExitCode}`");
    sb.AppendLine($"- **Harness project**: `{report.HarnessProject}`");
    sb.AppendLine($"- **Base directory**: `{report.BaseDirectory}`");
    if (!string.IsNullOrWhiteSpace(report.Solution))
        sb.AppendLine($"- **Solution**: `{report.Solution}`");
    sb.AppendLine($"- **Build working directory**: `{report.BuildWorkingDirectory}`");
    sb.AppendLine($"- **Target framework**: `{report.TargetFramework}`");
    sb.AppendLine($"- **Command**: `{report.Command}`");
    sb.AppendLine();

    sb.AppendLine("## Discovery summary");
    sb.AppendLine();
    sb.AppendLine($"- Generated files: `{report.GeneratedFiles.Length}`");
    sb.AppendLine($"- Project references: `{report.ProjectReferences.Length}`");
    sb.AppendLine($"- Assembly references: `{report.AssemblyReferences.Length}`");
    sb.AppendLine($"- Package references: `{report.PackageReferences.Length}`");
    sb.AppendLine($"- Imported build files: `{report.BuildFilesImported.Length}`");
    sb.AppendLine();

    sb.AppendLine("## Project reference discovery");
    if (report.ProjectReferenceDiscovery.Length == 0)
    {
        sb.AppendLine("- none");
    }
    else
    {
        foreach (var r in report.ProjectReferenceDiscovery)
            sb.AppendLine($"- `{r.Path}` — **{r.Source}**, `{r.Status}`: {r.Reason}");
    }
    sb.AppendLine();

    sb.AppendLine("## Imported build files");
    foreach (var r in report.BuildFilesImported)
        sb.AppendLine($"- `{r}`");
    if (report.BuildFilesImported.Length == 0)
        sb.AppendLine("- none");
    sb.AppendLine();

    sb.AppendLine("## Project references");
    foreach (var r in report.ProjectReferences)
        sb.AppendLine($"- `{r}`");
    if (report.ProjectReferences.Length == 0)
        sb.AppendLine("- none");
    sb.AppendLine();

    sb.AppendLine("## Package references");
    foreach (var p in report.PackageReferences)
        sb.AppendLine($"- `{p.Include}` `{p.Version}`");
    if (report.PackageReferences.Length == 0)
        sb.AppendLine("- none");
    sb.AppendLine();

    sb.AppendLine("## Diagnostic categories");
    var categories = CountDiagnosticCategories(report.ClassifiedDiagnostics);
    if (categories.Count == 0)
    {
        sb.AppendLine("No diagnostics captured.");
    }
    else
    {
        foreach (var kv in categories)
            sb.AppendLine($"- **{kv.Key}**: {kv.Value}");
    }
    sb.AppendLine();

    sb.AppendLine("## Classified diagnostics");
    if (report.ClassifiedDiagnostics.Length == 0)
    {
        sb.AppendLine("No build diagnostics captured.");
    }
    else
    {
        foreach (var d in report.ClassifiedDiagnostics)
        {
            sb.AppendLine($"### `{d.Code}` — {d.Category}");
            sb.AppendLine();
            sb.AppendLine($"- **Severity**: `{d.Severity}`");
            if (!string.IsNullOrWhiteSpace(d.File))
                sb.AppendLine($"- **File**: `{d.File}`" + (d.Line.HasValue ? $":{d.Line.Value}" : ""));
            sb.AppendLine($"- **Likely cause**: {d.LikelyCause}");
            sb.AppendLine($"- **Suggested action**: {d.SuggestedAction}");
            sb.AppendLine();
            sb.AppendLine("```text");
            sb.AppendLine(d.Raw);
            sb.AppendLine("```");
            sb.AppendLine();
        }
    }

    sb.AppendLine("## Raw diagnostics");
    if (report.Diagnostics.Length == 0)
    {
        sb.AppendLine("No build diagnostics captured.");
    }
    else
    {
        foreach (var d in report.Diagnostics)
            sb.AppendLine($"- {d}");
    }
    sb.AppendLine();
    sb.AppendLine("## StdOut");
    sb.AppendLine("```text");
    sb.AppendLine(report.StdOut.TrimEnd());
    sb.AppendLine("```");
    if (!string.IsNullOrWhiteSpace(report.StdErr))
    {
        sb.AppendLine();
        sb.AppendLine("## StdErr");
        sb.AppendLine("```text");
        sb.AppendLine(report.StdErr.TrimEnd());
        sb.AppendLine("```");
    }
    return sb.ToString();
}

static string EscapeXml(string value) => System.Security.SecurityElement.Escape(value) ?? value;

static string QuoteArg(string arg) => arg.Contains(' ') || arg.Contains('"')
    ? "\"" + arg.Replace("\"", "\\\"") + "\""
    : arg;

static SyntaxCheckerDelegate CreateGeneratedCodeChecker()
{
    return code =>
    {
        var parseOptions = Microsoft.CodeAnalysis.CSharp.CSharpParseOptions.Default
            .WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12);
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code, parseOptions);

        var parseErrors = tree.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(ToVerifyDiagnostic)
            .ToList();
        if (parseErrors.Count > 0)
            return parseErrors;

        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "MigratorGeneratedVerify",
            new[] { tree },
            GetGeneratedCodeReferences(),
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Where(d => d.Id != "CS8019")
            .Select(ToVerifyDiagnostic)
            .ToList();
    };
}

static (int Line, string Message) ToVerifyDiagnostic(Microsoft.CodeAnalysis.Diagnostic diagnostic)
{
    var line = diagnostic.Location.IsInSource
        ? diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1
        : 0;
    return (line, $"{diagnostic.Id}: {diagnostic.GetMessage()}");
}

static IEnumerable<Microsoft.CodeAnalysis.MetadataReference> GetGeneratedCodeReferences()
{
    var refs = new Dictionary<string, Microsoft.CodeAnalysis.MetadataReference>(StringComparer.OrdinalIgnoreCase);

    void AddAssembly(System.Reflection.Assembly assembly)
    {
        if (!string.IsNullOrEmpty(assembly.Location) && File.Exists(assembly.Location))
            AddFile(assembly.Location);
    }

    void AddFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!refs.ContainsKey(fullPath))
            refs[fullPath] = Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(fullPath);
    }

    AddAssembly(typeof(object).Assembly);
    AddAssembly(typeof(Console).Assembly);
    AddAssembly(typeof(Enumerable).Assembly);
    AddAssembly(typeof(Task).Assembly);

    var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
    if (!string.IsNullOrEmpty(trustedPlatformAssemblies))
    {
        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
        {
            if (File.Exists(path))
                AddFile(path);
        }
    }

    var basePath = AppContext.BaseDirectory;
    foreach (var name in new[]
    {
        "Microsoft.Playwright.NUnit.dll",
        "Microsoft.Playwright.dll",
        "nunit.framework.dll",
        "Microsoft.Bcl.AsyncInterfaces.dll"
    })
    {
        var path = Path.Combine(basePath, name);
        if (File.Exists(path))
            AddFile(path);
    }

    foreach (var packageAssembly in new[]
    {
        ("microsoft.playwright.nunit", "Microsoft.Playwright.NUnit.dll"),
        ("microsoft.playwright", "Microsoft.Playwright.dll"),
        ("nunit", "nunit.framework.dll"),
        ("microsoft.bcl.asyncinterfaces", "Microsoft.Bcl.AsyncInterfaces.dll")
    })
    {
        AddNuGetPackageAssembly(packageAssembly.Item1, packageAssembly.Item2);
    }

    return refs.Values;

    void AddNuGetPackageAssembly(string packageId, string assemblyFileName)
    {
        foreach (var root in GetNuGetPackageRoots())
        {
            var packageRoot = Path.Combine(root, packageId);
            if (!Directory.Exists(packageRoot))
                continue;

            var assemblyPath = Directory.EnumerateFiles(packageRoot, assemblyFileName, SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                            || path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => path.Contains($"{Path.DirectorySeparatorChar}net8.0{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(path => path.Contains($"{Path.DirectorySeparatorChar}net6.0{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(path => path.Contains($"{Path.DirectorySeparatorChar}netstandard2.0{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (assemblyPath != null)
            {
                AddFile(assemblyPath);
                return;
            }
        }
    }

    static IEnumerable<string> GetNuGetPackageRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRoot(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            if (Directory.Exists(fullPath))
                seen.Add(fullPath);
        }

        AddRoot(Environment.GetEnvironmentVariable("NUGET_PACKAGES"));
        AddRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"));
        AddRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NuGet", "Cache"));

        return seen;
    }
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

    var qualityReport = MigrationQualityAnalyzer.Analyze(summary);
    if (format == "json" || format == "both")
    {
        File.WriteAllText(Path.Combine(outPath, "migration-quality-dashboard.json"),
            ReportWriter.MigrationQualityToJson(qualityReport));
    }
    if (format == "text" || format == "both")
    {
        File.WriteAllText(Path.Combine(outPath, "migration-quality-dashboard.md"),
            ReportWriter.MigrationQualityToMarkdown(qualityReport));
        File.WriteAllText(Path.Combine(outPath, "migration-quality-tickets.md"),
            ReportWriter.MigrationQualityTicketsToMarkdown(qualityReport));
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

static TargetExpression? GetTarget(TestAction action)
{
    return action switch
    {
        ClickAction c => c.Target,
        SendKeysAction s => s.Target,
        PressAction p => p.Target,
        TextAssertionAction ta => ta.Target,
        VisibilityAssertionAction va => va.Target,
        WaitForAction wa => wa.Kind == WaitForKind.ActionabilityElided ? null : wa.Target,
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

static void GenerateDraftConfig(IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnmapped, string outPath, ProjectAdapterConfig? existingConfig)
{
    var existingMappings = new HashSet<string>(StringComparer.Ordinal);

    if (existingConfig != null)
    {
        foreach (var tm in existingConfig.UiTargets)
            existingMappings.Add(tm.SourceExpression);
        foreach (var scope in existingConfig.Scopes)
        {
            foreach (var tm in scope.UiTargets)
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


static int RunExplainTodo(string inputPath, string outPath, string format, bool recursiveArtifacts)
{
    Directory.CreateDirectory(outPath);

    var artifactDir = File.Exists(inputPath)
        ? Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Directory.GetCurrentDirectory()
        : Path.GetFullPath(inputPath);

    if (!Directory.Exists(artifactDir))
    {
        Console.Error.WriteLine($"Explain-todo input directory not found: {artifactDir}");
        return 1;
    }

    TodoExplanationReport report;
    try
    {
        report = BuildExplainTodoReportFromArtifacts(artifactDir, recursiveArtifacts);
    }
    catch (ArtifactLookupException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }

    WriteExplainTodoReport(report, outPath, format);

    Console.WriteLine("=== Explain TODO ===");
    Console.WriteLine($"Input artifacts: {artifactDir}");
    Console.WriteLine($"Artifact lookup: {(recursiveArtifacts ? "recursive" : "direct-only")}");
    Console.WriteLine($"TODO comments: {report.TodoComments}");
    Console.WriteLine($"Insights: {report.Insights.Length}");
    if (report.Insights.Length > 0)
    {
        Console.WriteLine("Top next actions:");
        foreach (var insight in report.Insights.Take(5))
            Console.WriteLine($"  - [{insight.Category}] {insight.Title} (impact: {insight.EstimatedImpact})");
    }
    Console.WriteLine($"Explain TODO artifacts written to: {Path.GetFullPath(outPath)}");
    return 0;
}

static void WriteExplainTodoArtifacts(
    MigrationSummaryReport summary,
    string outPath,
    string format,
    IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnmapped,
    IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnsupported,
    ProjectVerifyReport? projectVerifyReport)
{
    var report = BuildExplainTodoReport(summary, allUnmapped, allUnsupported, projectVerifyReport);
    WriteExplainTodoReport(report, outPath, format);
}

static void WriteExplainTodoReport(TodoExplanationReport report, string outPath, string format)
{
    Directory.CreateDirectory(outPath);

    if (format == "json" || format == "both")
    {
        File.WriteAllText(Path.Combine(outPath, "explain-todo.json"),
            System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    if (format == "text" || format == "both")
    {
        File.WriteAllText(Path.Combine(outPath, "explain-todo.md"), WriteExplainTodoMarkdown(report));

        // agent-next-task is a convenience handoff artifact. Keep explain-todo itself
        // robust even if a newly-added handoff section has a formatting bug: the main
        // explain-todo.md/json artifacts should still be emitted for diagnosis.
        try
        {
            File.WriteAllText(Path.Combine(outPath, "agent-next-task.md"), WriteAgentNextTaskMarkdown(report));
        }
        catch (Exception ex)
        {
            File.WriteAllText(
                Path.Combine(outPath, "agent-next-task.md"),
                WriteAgentNextTaskFallbackMarkdown(report, ex));
        }
    }
}

static string WriteAgentNextTaskFallbackMarkdown(TodoExplanationReport report, Exception ex)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Agent Next Task");
    sb.AppendLine();
    sb.AppendLine("agent-next-task.md could not be rendered completely, but explain-todo.md/json were written.");
    sb.AppendLine();
    sb.AppendLine($"- Artifact root: `{PathRedaction.Redact(report.ArtifactRoot)}`");
    sb.AppendLine($"- TODO: `{report.TodoComments}`");
    sb.AppendLine($"- Insights: `{report.Insights.Length}`");
    sb.AppendLine($"- Error: `{EscapeMd(ex.GetType().Name)}: {EscapeMd(ex.Message)}`");
    sb.AppendLine();
    sb.AppendLine("Open explain-todo.md and fix the report-rendering bug before relying on this handoff file.");
    return sb.ToString();
}

static TodoExplanationReport BuildExplainTodoReport(
    MigrationSummaryReport summary,
    IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnmapped,
    IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnsupported,
    ProjectVerifyReport? projectVerifyReport)
{
    var insights = new List<TodoInsight>();

    if (projectVerifyReport != null && projectVerifyReport.Status != "passed")
    {
        var diagnostics = projectVerifyReport.Diagnostics.Take(10).ToArray();
        var title = projectVerifyReport.Diagnostics.Length == 0
            ? "Project verify failed without captured diagnostics"
            : "Project verify failed: inspect build diagnostics";
        insights.Add(new TodoInsight(
            Category: "PROJECT_VERIFY",
            Title: title,
            Reason: "Generated code does not compile in the temporary verification project.",
            EstimatedImpact: projectVerifyReport.Diagnostics.Length,
            ExampleFile: projectVerifyReport.HarnessProject,
            ExampleLine: 0,
            SuggestedAction: "Open project-verify-report.md. Fix missing ProjectReferences/PackageReferences in adapter-config.Verification or classify generated-code errors.",
            RequiresSourceTruth: false,
            RequiresDeveloper: projectVerifyReport.Diagnostics.Any(d => d.Contains("CS0103", StringComparison.OrdinalIgnoreCase)
                                                                     || d.Contains("CS0246", StringComparison.OrdinalIgnoreCase)),
            Evidence: diagnostics));
    }

    foreach (var kv in allUnmapped.OrderByDescending(kv => kv.Value.Count).Take(20))
    {
        insights.Add(new TodoInsight(
            Category: "MISSING_MAPPING",
            Title: $"Add mapping for {kv.Key}",
            Reason: "Source expression was not mapped to a Playwright locator/action.",
            EstimatedImpact: kv.Value.Count,
            ExampleFile: kv.Value.File,
            ExampleLine: kv.Value.Line,
            SuggestedAction: "Find POM/source truth for this expression, then add a UiTarget/Method/ParameterizedMethod mapping in adapter-config.json.",
            RequiresSourceTruth: true,
            RequiresDeveloper: false,
            Evidence: new[] { $"Suggested target draft: {SuggestTargetExpression(kv.Key)}" }));
    }

    foreach (var kv in allUnsupported.OrderByDescending(kv => kv.Value.Count).Take(20))
    {
        insights.Add(new TodoInsight(
            Category: "UNSUPPORTED_ACTION",
            Title: $"Classify unsupported action: {TrimForTitle(kv.Key, 90)}",
            Reason: "The source statement was preserved as unsupported/raw logic.",
            EstimatedImpact: kv.Value.Count,
            ExampleFile: kv.Value.File,
            ExampleLine: kv.Value.Line,
            SuggestedAction: "If this is a repeated helper/wrapper, add a Method/ParameterizedMethod mapping. If semantics are unclear, leave TODO and ask the project owner.",
            RequiresSourceTruth: true,
            RequiresDeveloper: LooksLikeGenericMigratorGap(kv.Key),
            Evidence: new[] { kv.Key }));
    }

    foreach (var smart in ExtractSmartTodoInsights(summary.PerFileReports))
    {
        // Unmapped/unsupported artifacts remain the primary source for high-impact issues.
        // Smart TODO markers fill gaps for raw statements, placeholders, source-only cascades, etc.
        if (!insights.Any(i => i.Category == smart.Category && i.Title == smart.Title))
            insights.Add(smart);
    }

    if (summary.TodoComments > 0 && insights.Count == 0)
    {
        insights.Add(new TodoInsight(
            Category: "TODO_REVIEW",
            Title: "TODO comments remain, but no unmapped/unsupported root cause was found in summary artifacts",
            Reason: "TODO may come from raw statements, placeholders, assertions, or generated-code safety checks.",
            EstimatedImpact: summary.TodoComments,
            ExampleFile: "",
            ExampleLine: 0,
            SuggestedAction: "Run verify or inspect generated TODO labels. If available, run --mode explain-todo against the verify-project output directory.",
            RequiresSourceTruth: false,
            RequiresDeveloper: false,
            Evidence: Array.Empty<string>()));
    }

    var ordered = insights
        .OrderByDescending(i => i.EstimatedImpact)
        .ThenBy(i => i.Category, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    return new TodoExplanationReport(
        GeneratedAtUtc: DateTimeOffset.UtcNow,
        Source: "pipeline",
        ArtifactRoot: "pipeline",
        RecursiveArtifactLookup: false,
        FilesProcessed: summary.FilesProcessed,
        TestsFound: summary.TestsFound,
        ActionsFound: summary.ActionsFound,
        SemanticActions: summary.SemanticActions,
        SyntaxFallbackActions: summary.SyntaxFallbackActions,
        MappedTargets: summary.MappedTargets,
        UnmappedTargets: summary.UnmappedTargets,
        UnsupportedActions: summary.UnsupportedActions,
        TodoComments: summary.TodoComments,
        SyntaxErrors: projectVerifyReport?.ClassifiedDiagnostics.Count(d => d.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
            ?? projectVerifyReport?.Diagnostics.Count(d => d.Contains(": error ", StringComparison.OrdinalIgnoreCase) || d.Contains("CS", StringComparison.OrdinalIgnoreCase))
            ?? 0,
        ProjectVerifyStatus: projectVerifyReport?.Status,
        Insights: ordered,
        NormalizedRootCauses: BuildNormalizedTodoGroups(ordered),
        TableMappingCandidates: BuildTableMappingCandidates(ordered),
        NextBestAction: ordered.FirstOrDefault()?.SuggestedAction ?? "No TODO/actionable issue found. Move to project verify or runtime smoke.");
}


static IEnumerable<TodoInsight> ExtractSmartTodoInsights(IEnumerable<MigrationReport> perFileReports)
{
    var buckets = new Dictionary<(string Code, string Message), (int Count, string File, int Line, List<string> Evidence)>();

    foreach (var report in perFileReports)
    {
        if (string.IsNullOrWhiteSpace(report.GeneratedOutput))
            continue;

        var lines = report.GeneratedOutput.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var match = System.Text.RegularExpressions.Regex.Match(
                line,
                @"//\s*TODO:\s*(?<message>.*?)\s*\[MIGRATOR:(?<code>[A-Z0-9_]+)\]");
            if (!match.Success)
                continue;

            var code = match.Groups["code"].Value;
            var message = match.Groups["message"].Value.Trim();
            var key = (code, message);

            if (!buckets.TryGetValue(key, out var existing))
                existing = (0, report.SourceFilePath, i + 1, new List<string>());

            existing.Count++;
            if (existing.Evidence.Count < 5)
                existing.Evidence.Add(line.Trim());

            buckets[key] = existing;
        }
    }

    foreach (var kv in buckets.OrderByDescending(kv => kv.Value.Count))
    {
        var code = kv.Key.Code;
        var message = kv.Key.Message;
        var (count, file, line, evidence) = kv.Value;

        yield return new TodoInsight(
            Category: code,
            Title: SmartTodoTitle(code, message),
            Reason: SmartTodoReason(code),
            EstimatedImpact: count,
            ExampleFile: file,
            ExampleLine: line,
            SuggestedAction: SmartTodoSuggestedAction(code),
            RequiresSourceTruth: SmartTodoRequiresSourceTruth(code),
            RequiresDeveloper: SmartTodoMayNeedDeveloper(code),
            Evidence: evidence.ToArray());
    }
}

static string SmartTodoTitle(string code, string message)
{
    return code switch
    {
        "MISSING_MAPPING" => $"Add source-backed mapping: {TrimForTitle(message, 90)}",
        "SOURCE_ONLY_IDENTIFIER" => $"Map or keep source-only statement: {TrimForTitle(message, 90)}",
        "UNRESOLVED_SYMBOL" => $"Fix upstream unresolved symbol: {TrimForTitle(message, 90)}",
        "UNAVAILABLE_SYMBOLS" => $"Classify unavailable target symbols: {TrimForTitle(message, 90)}",
        "UNRESOLVED_PLACEHOLDER" => $"Fix adapter-config placeholder: {TrimForTitle(message, 90)}",
        "TABLE_MAPPING_REQUIRED" => $"Add table/list mapping: {TrimForTitle(message, 90)}",
        "WAIT_MAPPING_REQUIRED" => $"Map product-state wait target: {TrimForTitle(message, 90)}",
        "WAIT_REQUIRES_STATE_ASSERTION" => $"Replace custom wait with state assertion: {TrimForTitle(message, 90)}",
        _ => $"Review TODO [{code}]: {TrimForTitle(message, 90)}"
    };
}

static string SmartTodoReason(string code)
{
    return code switch
    {
        "MISSING_MAPPING" => "Generated code contains a source UI target that has no Playwright mapping.",
        "SOURCE_ONLY_IDENTIFIER" => "The statement references a Selenium/source-only root that must not be active in target code.",
        "UNRESOLVED_SYMBOL" => "The statement depends on a symbol blocked earlier in the same method/setup chain.",
        "UNAVAILABLE_SYMBOLS" => "The statement references identifiers not known in the target method/project context.",
        "RAW_STATEMENT" => "The statement was not recognized semantically and needs mapping or manual migration.",
        "RAW_LOCAL_DECLARATION" => "The local declaration initializer depends on source-side logic.",
        "MAPPED_REQUIRES_REVIEW" => "Adapter config deliberately marked the mapping as requiring review.",
        "UNRESOLVED_PLACEHOLDER" => "A TargetStatements placeholder could not be substituted from the source method pattern.",
        "ASSERTION_CONSTRAINT" => "The assertion was preserved because no direct Playwright assertion mapping was inferred.",
        "TABLE_MAPPING_REQUIRED" => "A table/list access pattern needs a Tables mapping with RowTarget.",
        "WAIT_MAPPING_REQUIRED" => "A product-state wait such as loader/table/modal synchronization needs a mapped Playwright target.",
        "WAIT_REQUIRES_STATE_ASSERTION" => "A custom wait is ambiguous and should be replaced with a concrete state assertion, not a fixed timeout.",
        "UNSUPPORTED_ACTION" => "The recognizer/adapter could not safely translate this source action.",
        "HELPER_METHOD_REQUIRES_MAPPING" => "A receiverless project/helper method was preserved structurally but has no target mapping yet.",
        _ => "Generated TODO contains a migrator classification code."
    };
}

static string SmartTodoSuggestedAction(string code)
{
    return code switch
    {
        "MISSING_MAPPING" => "Find POM/source truth and add UiTarget/Method/ParameterizedMethod/Table/Pagination mapping.",
        "SOURCE_ONLY_IDENTIFIER" => "Do not mark the source-only root as target-known. Map the whole expression or leave TODO.",
        "UNRESOLVED_SYMBOL" => "Find the first TODO that blocked the symbol; fix that root cause first.",
        "UNAVAILABLE_SYMBOLS" => "Add TargetKnownTypes/TargetKnownIdentifiers only for real target symbols; otherwise map/comment the expression.",
        "RAW_STATEMENT" => "If repeated, add Method/ParameterizedMethod mapping; otherwise keep manual TODO.",
        "RAW_LOCAL_DECLARATION" => "Map the initializer through adapter-config or keep the declaration commented.",
        "MAPPED_REQUIRES_REVIEW" => "Review target semantics and remove RequiresReview only when proven safe.",
        "UNRESOLVED_PLACEHOLDER" => "Fix SourceMethodPattern placeholders or TargetStatements names in adapter-config.",
        "ASSERTION_CONSTRAINT" => "Add reusable assertion mapping if this pattern appears often.",
        "TABLE_MAPPING_REQUIRED" => "Add a Tables mapping with source-backed RowTarget.",
        "WAIT_MAPPING_REQUIRED" => "Map the loader/table/modal/toast target or add a Method/ParameterizedMethod wait mapping.",
        "WAIT_REQUIRES_STATE_ASSERTION" => "Replace with loader/table/modal/toast/url/download assertion after checking source truth.",
        "UNSUPPORTED_ACTION" => "Classify as missing mapping, unsupported business semantics, or generic migrator gap.",
        "HELPER_METHOD_REQUIRES_MAPPING" => "Run --mode helper-inventory or inspect helper body, then add MethodSemantics/Methods/ParameterizedMethods mapping.",
        _ => "Inspect source truth and decide whether this is config work or developer escalation."
    };
}

static bool SmartTodoRequiresSourceTruth(string code)
{
    return code is "MISSING_MAPPING" or "RAW_STATEMENT" or "RAW_LOCAL_DECLARATION" or "TABLE_MAPPING_REQUIRED" or "WAIT_MAPPING_REQUIRED" or "WAIT_REQUIRES_STATE_ASSERTION" or "UNSUPPORTED_ACTION" or "HELPER_METHOD_REQUIRES_MAPPING";
}

static bool SmartTodoMayNeedDeveloper(string code)
{
    return code is "UNRESOLVED_SYMBOL" or "UNAVAILABLE_SYMBOLS" or "UNRESOLVED_PLACEHOLDER" or "WAIT_REQUIRES_STATE_ASSERTION";
}

static NormalizedTodoGroup[] BuildNormalizedTodoGroups(IEnumerable<TodoInsight> insights)
{
    var groups = new Dictionary<(string Category, string Key), NormalizedTodoGroupBuilder>();

    foreach (var insight in insights)
    {
        var normalized = NormalizeTodoGroup(insight);
        var key = (insight.Category, normalized.Key);
        if (!groups.TryGetValue(key, out var builder))
        {
            builder = new NormalizedTodoGroupBuilder(
                insight.Category,
                normalized.Key,
                normalized.DisplayName,
                normalized.SuggestedAction);
            groups[key] = builder;
        }

        builder.Count += Math.Max(1, insight.EstimatedImpact);
        if (string.IsNullOrWhiteSpace(builder.ExampleFile) && !string.IsNullOrWhiteSpace(insight.ExampleFile))
        {
            builder.ExampleFile = insight.ExampleFile;
            builder.ExampleLine = insight.ExampleLine;
        }

        if (!string.IsNullOrWhiteSpace(insight.ExampleFile) && builder.RepresentativeFiles.Count < 5)
            builder.RepresentativeFiles.Add(PathRedaction.Redact(insight.ExampleFile));

        foreach (var evidence in insight.Evidence)
        {
            if (builder.Evidence.Count >= 5)
                break;
            if (!string.IsNullOrWhiteSpace(evidence))
                builder.Evidence.Add(evidence.Trim());
        }
    }

    return groups.Values
        .OrderByDescending(g => g.Count)
        .ThenBy(g => g.Category, StringComparer.OrdinalIgnoreCase)
        .ThenBy(g => g.GroupKey, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.ToGroup())
        .ToArray();
}

static TableMappingCandidate[] BuildTableMappingCandidates(IEnumerable<TodoInsight> insights)
{
    var candidates = new Dictionary<string, TableMappingCandidateBuilder>(StringComparer.OrdinalIgnoreCase);

    foreach (var insight in insights)
    {
        if (!insight.Category.Equals("TABLE_MAPPING_REQUIRED", StringComparison.OrdinalIgnoreCase))
            continue;

        var allEvidence = insight.Evidence.Length == 0
            ? new[] { insight.Title }
            : insight.Evidence;

        var evidenceExpressions = allEvidence
            .Select(ExtractLikelySourceExpression)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var expression = evidenceExpressions.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(expression))
            expression = ExtractLikelySourceExpression(insight.Title);
        if (string.IsNullOrWhiteSpace(expression))
            continue;

        var sourceRoot = NormalizeTableRoot(expression);
        var accessor = ExtractTableAccessorKind(expression);
        var assertion = ExtractAssertionKind(expression);
        var groupKey = $"table:{sourceRoot}:{accessor}:{assertion}";

        if (!candidates.TryGetValue(groupKey, out var builder))
        {
            builder = new TableMappingCandidateBuilder(
                groupKey,
                sourceRoot,
                accessor,
                assertion,
                SuggestUiTargetRootForTable(sourceRoot),
                BuildTableMappingConfigHint(sourceRoot, accessor, assertion));
            candidates[groupKey] = builder;
        }

        builder.Count += Math.Max(1, insight.EstimatedImpact);
        if (string.IsNullOrWhiteSpace(builder.ExampleFile) && !string.IsNullOrWhiteSpace(insight.ExampleFile))
        {
            builder.ExampleFile = insight.ExampleFile;
            builder.ExampleLine = insight.ExampleLine;
        }

        if (string.IsNullOrWhiteSpace(builder.SourceExpression))
            builder.SourceExpression = expression;

        foreach (var e in evidenceExpressions)
        {
            if (builder.Evidence.Count >= 8)
                break;
            builder.Evidence.Add(e);
        }
    }

    return candidates.Values
        .OrderByDescending(x => x.Count)
        .ThenBy(x => x.SourceRoot, StringComparer.OrdinalIgnoreCase)
        .Select(x => x.ToCandidate())
        .ToArray();
}

static string SuggestUiTargetRootForTable(string sourceRoot)
{
    if (string.IsNullOrWhiteSpace(sourceRoot) || sourceRoot.Equals("unknown-table", StringComparison.OrdinalIgnoreCase))
        return "<source-backed-table-root>";

    return sourceRoot;
}

static string BuildTableMappingConfigHint(string sourceRoot, string accessor, string assertion)
{
    var root = string.IsNullOrWhiteSpace(sourceRoot) ? "<source-table-root>" : sourceRoot;
    return $"Add one source-backed UiTargets/Tables mapping for `{root}`; RowTarget should represent `{accessor}` rows and cover `{assertion}` assertions. Verify selector/POM truth before changing config.";
}

static (string Key, string DisplayName, string SuggestedAction) NormalizeTodoGroup(TodoInsight insight)
{
    var text = string.Join("\n", insight.Evidence.Prepend(insight.Title));
    return insight.Category switch
    {
        "MANUAL_REVIEW" or "RAW_STATEMENT" or "UNSUPPORTED_ACTION" or "HELPER_METHOD_REQUIRES_MAPPING" => NormalizeMethodFamily(insight.Category, text),
        "TABLE_MAPPING_REQUIRED" => NormalizeTableFamily(text),
        "SOURCE_ONLY_IDENTIFIER" or "UNAVAILABLE_SYMBOLS" or "UNRESOLVED_SYMBOL" => NormalizeSourceOnlyFamily(insight.Category, text),
        "DEPENDS_ON_SUPPRESSED_SIDE_EFFECT" => NormalizeSuppressedSideEffectFamily(text),
        _ => (NormalizeGenericKey(insight.Title), insight.Title, insight.SuggestedAction)
    };
}

static (string Key, string DisplayName, string SuggestedAction) NormalizeMethodFamily(string category, string text)
{
    var method = ExtractMethodFamily(text);
    if (!string.IsNullOrWhiteSpace(method))
    {
        return ($"method:{method}",
            $"{category}: method family `{method}`",
            "Group all occurrences of this helper/method family; inspect source/helper body or run --mode helper-inventory before adding MethodSemantics/ParameterizedMethods.");
    }

    return (NormalizeGenericKey(text),
        $"{category}: unclassified helper/raw family",
        "Inspect representative source snippets and classify this family before changing mappings or suppressions.");
}

static (string Key, string DisplayName, string SuggestedAction) NormalizeTableFamily(string text)
{
    var expression = ExtractLikelySourceExpression(text);
    var root = NormalizeTableRoot(expression);
    var accessor = ExtractTableAccessorKind(expression);
    var assertion = ExtractAssertionKind(expression);
    var key = $"table:{root}:{accessor}:{assertion}";
    return (key,
        $"Table/list mapping `{root}` via `{accessor}` ({assertion})",
        "Add or refine one source-backed table/list mapping for the base target; do not fix each row/index one by one.");
}

static (string Key, string DisplayName, string SuggestedAction) NormalizeSourceOnlyFamily(string category, string text)
{
    var root = ExtractSourceOnlyRoot(text);
    return ($"root:{root}",
        $"{category}: source-only root `{root}`",
        root.Equals("page", StringComparison.OrdinalIgnoreCase) || root.Equals("pagef", StringComparison.OrdinalIgnoreCase)
            ? "Fix the upstream lifecycle/mapping that introduced page/pagef; do not add page/pagef as target-known just to hide the issue."
            : "Map the full source expression or classify it explicitly; do not mark source-only roots as target-known unless they truly exist in target code.");
}

static (string Key, string DisplayName, string SuggestedAction) NormalizeSuppressedSideEffectFamily(string text)
{
    var source = ExtractSuppressedSideEffectSource(text);
    var method = ExtractMethodFamily(source);
    if (string.IsNullOrWhiteSpace(method))
        method = ExtractMethodFamily(text);
    if (string.IsNullOrWhiteSpace(method))
        method = "unknown-side-effect";

    return ($"suppressed-side-effect:{method}",
        $"Suppressed side-effect family `{method}`",
        "Do not keep downstream assertions active until this upstream side-effect is mapped or explicitly classified. If it is a project/POM helper, run --mode helper-inventory and add MethodSemantics/ParameterizedMethods.");
}

static string ExtractMethodFamily(string text)
{
    if (string.IsNullOrWhiteSpace(text))
        return "";

    var source = ExtractLikelySourceExpression(text);
    var matches = System.Text.RegularExpressions.Regex.Matches(
        source,
        @"(?<name>[A-Za-z_][A-Za-z0-9_]*(?:\s*<[^>]+>)?)\s*\(");
    if (matches.Count == 0)
        return "";

    var last = matches[matches.Count - 1].Groups["name"].Value;
    last = System.Text.RegularExpressions.Regex.Replace(last, @"\s+", "");
    var receiver = ExtractReceiverBeforeMethod(source, last);
    if (string.IsNullOrWhiteSpace(receiver))
        return last;

    var root = receiver.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? receiver;
    if (root.Equals("Loader", StringComparison.OrdinalIgnoreCase))
        return $"Loader.{last}";
    if (root.Equals("Navigation", StringComparison.OrdinalIgnoreCase) || root.Equals("Browser", StringComparison.OrdinalIgnoreCase))
        return $"{root}.{last}";

    return last;
}

static string ExtractReceiverBeforeMethod(string source, string method)
{
    var simpleMethod = method;
    var genericStart = simpleMethod.IndexOf('<');
    if (genericStart >= 0)
        simpleMethod = simpleMethod[..genericStart];

    var pattern = $@"(?<receiver>[A-Za-z_][A-Za-z0-9_\.<>]*)\.{System.Text.RegularExpressions.Regex.Escape(simpleMethod)}\s*(<[^>]+>)?\s*\(";
    var match = System.Text.RegularExpressions.Regex.Match(source, pattern);
    return match.Success ? match.Groups["receiver"].Value : "";
}

static string ExtractLikelySourceExpression(string text)
{
    if (string.IsNullOrWhiteSpace(text))
        return "";

    var sourceMatch = System.Text.RegularExpressions.Regex.Match(text, @"Source:\s*(?<source>.*?)(?:\r?\n|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (sourceMatch.Success)
        return CleanupSourceExpression(sourceMatch.Groups["source"].Value);

    var suppressedMatch = System.Text.RegularExpressions.Regex.Match(text, @"side-effect at line \d+:\s*(?<source>.*?)(?:\s*\[MIGRATOR:|\r?\n|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (suppressedMatch.Success)
        return CleanupSourceExpression(suppressedMatch.Groups["source"].Value);

    var todoMatch = System.Text.RegularExpressions.Regex.Match(text, @"//\s*TODO:\s*(?<source>.*?)(?:\s*\[MIGRATOR:|\r?\n|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (todoMatch.Success)
        return CleanupSourceExpression(todoMatch.Groups["source"].Value);

    return CleanupSourceExpression(text.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? text);
}

static string ExtractSuppressedSideEffectSource(string text)
{
    var source = ExtractLikelySourceExpression(text);
    if (!string.IsNullOrWhiteSpace(source))
        return source;

    var match = System.Text.RegularExpressions.Regex.Match(text, @"source suppressed:\s*(?<source>.*?)(?:\r?\n|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    return match.Success ? CleanupSourceExpression(match.Groups["source"].Value) : text;
}

static string CleanupSourceExpression(string value)
{
    var text = value.Trim().Trim('`');
    text = text.Replace("//", " ").Trim();
    text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
    return text.TrimEnd(';');
}

static string NormalizeTableRoot(string expression)
{
    if (string.IsNullOrWhiteSpace(expression))
        return "unknown-table";

    var source = CleanupSourceExpression(expression);

    // Most legacy POM tables look like page.Table.Items.ElementAt(i).Foo or page.Registry.Rows.ElementAt(i).Foo.
    // Group by the stable table/root expression, not by row index or expected assertion value.
    var collectionElementAt = System.Text.RegularExpressions.Regex.Match(source,
        @"(?<root>[A-Za-z_][A-Za-z0-9_\.]*?)\.(?:Items|Rows)\.ElementAt\(",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (collectionElementAt.Success)
        return NormalizeExpressionPlaceholders(collectionElementAt.Groups["root"].Value);

    var collectionRoot = System.Text.RegularExpressions.Regex.Match(source,
        @"(?<root>[A-Za-z_][A-Za-z0-9_\.]*?)\.(?:Items|Rows)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (collectionRoot.Success)
        return NormalizeExpressionPlaceholders(collectionRoot.Groups["root"].Value);

    var directElementAt = System.Text.RegularExpressions.Regex.Match(source,
        @"(?<root>[A-Za-z_][A-Za-z0-9_\.]*)\.ElementAt\(",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (directElementAt.Success)
        return NormalizeExpressionPlaceholders(directElementAt.Groups["root"].Value);

    var semanticTableName = System.Text.RegularExpressions.Regex.Match(source,
        @"(?<root>[A-Za-z_][A-Za-z0-9_\.]*?(?:Table|Registry|Grid|List)[A-Za-z0-9_\.]*)\.",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (semanticTableName.Success)
        return NormalizeExpressionPlaceholders(semanticTableName.Groups["root"].Value);

    return NormalizeExpressionPlaceholders(source.Length > 90 ? source[..90] : source);
}

static string ExtractTableAccessorKind(string expression)
{
    if (expression.Contains("Items.ElementAt", StringComparison.OrdinalIgnoreCase)) return "Items.ElementAt";
    if (expression.Contains("Rows.ElementAt", StringComparison.OrdinalIgnoreCase)) return "Rows.ElementAt";
    if (expression.Contains("ElementAt", StringComparison.OrdinalIgnoreCase)) return "ElementAt";
    if (expression.Contains("Nth", StringComparison.OrdinalIgnoreCase)) return "Nth";
    if (expression.Contains("Cells", StringComparison.OrdinalIgnoreCase)) return "Cells";
    return "table-access";
}

static string ExtractAssertionKind(string expression)
{
    if (expression.Contains(".Sum", StringComparison.OrdinalIgnoreCase)) return "Sum";
    if (expression.Contains(".Text", StringComparison.OrdinalIgnoreCase) || expression.Contains("ToHaveText", StringComparison.OrdinalIgnoreCase)) return "Text";
    if (expression.Contains(".Count", StringComparison.OrdinalIgnoreCase)) return "Count";
    if (expression.Contains("Visible", StringComparison.OrdinalIgnoreCase) || expression.Contains("ToBeVisible", StringComparison.OrdinalIgnoreCase)) return "Visibility";
    return "unknown-assertion";
}

static string ExtractSourceOnlyRoot(string text)
{
    var expression = ExtractLikelySourceExpression(text);
    var candidates = new[] { "pagef", "page", "Urls", "Browser", "Navigation", "WebDriver", "driver" };
    foreach (var candidate in candidates)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(expression, $@"\b{System.Text.RegularExpressions.Regex.Escape(candidate)}\b"))
            return candidate;
    }

    var match = System.Text.RegularExpressions.Regex.Match(expression, @"\b(?<root>[A-Za-z_][A-Za-z0-9_]*)\s*\.");
    return match.Success ? match.Groups["root"].Value : "unknown-root";
}

static string NormalizeGenericKey(string value)
{
    var cleaned = NormalizeExpressionPlaceholders(value);
    return cleaned.Length > 120 ? cleaned[..120] : cleaned;
}

static string NormalizeExpressionPlaceholders(string value)
{
    var text = value.Trim();
    text = System.Text.RegularExpressions.Regex.Replace(text, @"""(?:\\.|[^""])*""", "\"*\"");
    text = System.Text.RegularExpressions.Regex.Replace(text, @"'[^']*'", "'*'");
    text = System.Text.RegularExpressions.Regex.Replace(text, @"\b\d+\b", "#");
    text = System.Text.RegularExpressions.Regex.Replace(text, @"ElementAt\s*\([^)]*\)", "ElementAt(*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    text = System.Text.RegularExpressions.Regex.Replace(text, @"Nth\s*\([^)]*\)", "Nth(*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
    return text;
}

static TodoExplanationReport BuildExplainTodoReportFromArtifacts(string artifactDir, bool recursiveArtifacts = false)
{
    ValidateArtifactLookupRoot(artifactDir, recursiveArtifacts);
    var reportPath = FindFirstExisting(artifactDir, "report.json", recursiveArtifacts);
    var unmappedPath = FindFirstExisting(artifactDir, "unmapped-targets.json", recursiveArtifacts);
    var unsupportedPath = FindFirstExisting(artifactDir, "unsupported-actions.json", recursiveArtifacts);
    var verifyPath = FindFirstExisting(artifactDir, "verify-report.json", recursiveArtifacts);
    var projectVerifyPath = FindFirstExisting(artifactDir, "project-verify-report.json", recursiveArtifacts);

    var summary = new ArtifactSummary();
    if (reportPath != null)
        ReadSummaryReport(reportPath, summary);
    if (verifyPath != null)
        ReadVerifyReport(verifyPath, summary);
    ProjectVerifyReport? projectVerify = projectVerifyPath != null ? ReadProjectVerifyReport(projectVerifyPath) : null;

    var unmapped = ReadCountItems(unmappedPath, "SourceExpression", "Usages", "ExampleFile", "ExampleLine");
    if (unmapped.Count == 0 && reportPath != null)
        unmapped = ReadNestedCountItems(reportPath, "TopUnmappedTargets", "SourceExpression", "Usages", "ExampleFile", "ExampleLine");

    var unsupported = ReadCountItems(unsupportedPath, "MethodOrSourceText", "Count", "ExampleFile", "ExampleLine");
    if (unsupported.Count == 0 && reportPath != null)
        unsupported = ReadNestedCountItems(reportPath, "TopUnsupportedActions", "MethodOrSourceText", "Count", "ExampleFile", "ExampleLine");

    var generatedReports = ReadGeneratedFileReports(artifactDir);
    var generatedTodoCount = generatedReports.Sum(r => r.TodoComments);

    var fakeSummary = new MigrationSummaryReport(
        FilesProcessed: summary.FilesProcessed,
        TestsFound: summary.TestsFound,
        ActionsFound: summary.ActionsFound,
        SemanticActions: summary.SemanticActions,
        SyntaxFallbackActions: summary.SyntaxFallbackActions,
        UnsupportedActions: summary.UnsupportedActions,
        MappedTargets: summary.MappedTargets,
        UnmappedTargets: summary.UnmappedTargets,
        TodoComments: Math.Max(summary.TodoComments, generatedTodoCount),
        FilesWithWarnings: 0,
        GeneratedFiles: generatedReports.Count,
        ProcessedFiles: generatedReports.Select(r => r.SourceFilePath).ToArray(),
        TopUnmappedTargets: Array.Empty<UnmappedTargetInfo>(),
        TopUnsupportedActions: Array.Empty<UnsupportedMethodInfo>(),
        PerFileReports: generatedReports);

    var built = BuildExplainTodoReport(fakeSummary, unmapped, unsupported, projectVerify);
    return built with
    {
        Source = Path.GetFullPath(artifactDir),
        ArtifactRoot = Path.GetFullPath(artifactDir),
        RecursiveArtifactLookup = recursiveArtifacts,
        SyntaxErrors = Math.Max(built.SyntaxErrors, summary.SyntaxErrors),
        ProjectVerifyStatus = projectVerify?.Status ?? summary.VerifyStatus
    };
}



static void AppendQualityGatesHtml(StringBuilder sb, MigrationQualityGates gates)
{
    sb.AppendLine("<h2 class=\"section\">Quality gates</h2>");
    sb.AppendLine("<table><thead><tr><th>Gate</th><th>Value</th><th>Status</th></tr></thead><tbody>");
    QualityGateRow(sb, "Project verify", gates.ProjectVerifyStatus, QualityGateStatusCss(gates.ProjectVerifyStatus.Equals("passed", StringComparison.OrdinalIgnoreCase), gates.ProjectVerifyStatus.Equals("not-run", StringComparison.OrdinalIgnoreCase)));
    QualityGateRow(sb, "Compile errors", gates.CompileErrors.ToString(), QualityGateStatusCss(gates.CompileErrors == 0, false));
    QualityGateRow(sb, "EMPTY_TEST_AFTER_SUPPRESSION", gates.EmptyTestsAfterSuppression.ToString(), QualityGateStatusCss(gates.EmptyTestsAfterSuppression == 0, false));
    QualityGateRow(sb, "DEPENDS_ON_SUPPRESSED_SIDE_EFFECT", gates.SuppressedSideEffectDependencies.ToString(), QualityGateStatusCss(gates.SuppressedSideEffectDependencies == 0, false));
    QualityGateRow(sb, "SuppressedMethodPatterns", FormatNullableMetric(gates.SuppressedMethodPatterns), gates.SuppressedMethodPatterns.HasValue ? "" : "warn");
    QualityGateRow(sb, "Regex-looking suppressions", FormatNullableMetric(gates.SuspiciousSuppressionPatterns), QualityGateStatusCss((gates.SuspiciousSuppressionPatterns ?? 0) == 0, !gates.SuspiciousSuppressionPatterns.HasValue));
    sb.AppendLine("</tbody></table>");
    if (gates.Warnings.Length > 0)
    {
        sb.AppendLine("<div class=\"card\" style=\"margin-top:12px\"><strong>Quality gate warnings</strong><ul>");
        foreach (var warning in gates.Warnings)
            sb.AppendLine($"<li>{Html(warning)}</li>");
        sb.AppendLine("</ul></div>");
    }
}

static void QualityGateRow(StringBuilder sb, string gate, string value, string css)
{
    var status = string.IsNullOrWhiteSpace(css) ? "ok" : css == "bad" ? "risk" : css == "warn" ? "not-run" : css;
    sb.AppendLine($"<tr><td><code>{Html(gate)}</code></td><td>{Html(value)}</td><td><span class=\"pill {css}\">{Html(status)}</span></td></tr>");
}

static string QualityGateStatusCss(bool ok, bool unknown) => unknown ? "warn" : ok ? "ok" : "bad";

static IReadOnlyList<MigrationReport> ReadGeneratedFileReports(string artifactDir)
{
    if (!Directory.Exists(artifactDir))
        return Array.Empty<MigrationReport>();

    var files = Directory.EnumerateFiles(artifactDir, "*.cs", SearchOption.AllDirectories)
        .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                 && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var reports = new List<MigrationReport>();
    foreach (var file in files)
    {
        var text = File.ReadAllText(file);
        if (!text.Contains("// Generated by Migrator", StringComparison.Ordinal) &&
            !text.Contains("MIGRATOR:", StringComparison.Ordinal))
        {
            continue;
        }

        var todoCount = text.Split('\n').Count(l => l.TrimStart().StartsWith("// TODO:", StringComparison.Ordinal));
        reports.Add(new MigrationReport(
            SourceFilePath: file,
            TotalTests: 0,
            SuccessfullyConvertedTests: 0,
            UnsupportedActions: Array.Empty<UnsupportedAction>(),
            GeneratedOutput: text,
            SemanticActions: 0,
            SyntaxFallbackActions: 0,
            UnsupportedCount: 0,
            MappedTargets: 0,
            UnmappedTargets: 0,
            TodoComments: todoCount));
    }

    return reports;
}

static string[] ArtifactLookupFileNames() => new[]
{
    "report.json", "unmapped-targets.json", "unsupported-actions.json", "verify-report.json", "project-verify-report.json",
    "migration-quality-dashboard.json", "migration-quality-dashboard.md", "migration-quality-tickets.md",
    "source-capabilities-report.json", "source-capabilities-report.md", "target-capabilities-report.json", "target-capabilities-report.md",
    "capabilities-report.json", "capabilities-report.md",
    "explain-todo.json", "explain-todo.md", "agent-next-task.md", "smoke-plan.json", "smoke-plan.md",
    "runtime-checklist.md", "agent-runtime-next-task.md", "runtime-failure-report.json", "runtime-failure-report.md", "agent-runtime-failure-next-task.md",
    "migration-board.json", "migration-board.md", "migration-board.html", "report-dashboard.json", "report-dashboard.md", "report-dashboard.html",
    "config-validate-report.json", "config-validate-report.md"
};

static void ValidateArtifactLookupRoot(string dir, bool recursiveArtifacts)
{
    if (!Directory.Exists(dir))
        return;

    if (recursiveArtifacts)
        return;

    if (HasDirectArtifactEvidence(dir))
        return;

    var nested = CollectArtifactCandidates(dir).Take(20).ToArray();
    if (nested.Length == 0)
        return;

    var sb = new StringBuilder();
    sb.AppendLine($"Artifact directory does not look like a single run directory: {dir}");
    sb.AppendLine("Default artifact lookup is non-recursive to avoid mixing stale report/verify files from unrelated runs.");
    sb.AppendLine("Pass a concrete run directory, or use --recursive-artifacts after reviewing candidates.");
    sb.AppendLine("Nested artifact candidates found:");
    foreach (var candidate in nested)
        sb.AppendLine($"- {candidate.FileName}: {candidate.Path}");
    throw new ArtifactLookupException(sb.ToString().TrimEnd());
}

static bool HasDirectArtifactEvidence(string dir)
{
    if (ArtifactLookupFileNames().Any(name => File.Exists(Path.Combine(dir, name))))
        return true;

    if (Directory.Exists(Path.Combine(dir, "generated")))
        return true;

    return Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly)
        .Any(path =>
        {
            try
            {
                var text = File.ReadAllText(path);
                return text.Contains("// Generated by Migrator", StringComparison.Ordinal)
                    || text.Contains("MIGRATOR:", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        });
}

static ArtifactLookupCandidate[] CollectArtifactCandidates(string dir)
{
    if (!Directory.Exists(dir))
        return Array.Empty<ArtifactLookupCandidate>();

    return ArtifactLookupFileNames()
        .SelectMany(name => Directory.EnumerateFiles(dir, name, SearchOption.AllDirectories)
            .Where(path => !Path.GetDirectoryName(path)!.Equals(dir, StringComparison.OrdinalIgnoreCase))
            .Select(path => new ArtifactLookupCandidate(
                FileName: name,
                Path: Path.GetFullPath(path),
                LastWriteTimeUtc: File.GetLastWriteTimeUtc(path))))
        .OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static string? FindFirstExisting(string dir, string fileName, bool recursiveArtifacts = false)
{
    var direct = Path.Combine(dir, fileName);
    if (File.Exists(direct))
        return direct;

    if (!recursiveArtifacts)
        return null;

    var candidates = Directory.EnumerateFiles(dir, fileName, SearchOption.AllDirectories)
        .Where(path => !Path.GetFullPath(path).Equals(Path.GetFullPath(direct), StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(File.GetLastWriteTimeUtc)
        .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (candidates.Length == 0)
        return null;

    if (candidates.Length > 1)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Multiple artifact candidates found for {fileName} under {dir}.");
        sb.AppendLine("Recursive artifact lookup is intentionally fail-fast to prevent mixing unrelated runs.");
        sb.AppendLine("Pass a concrete run directory instead. Candidates:");
        foreach (var candidate in candidates.Take(20))
            sb.AppendLine($"- {candidate}");
        throw new ArtifactLookupException(sb.ToString().TrimEnd());
    }

    return candidates[0];
}


static void ReadSummaryReport(string path, ArtifactSummary summary)
{
    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
    var root = doc.RootElement;
    summary.FilesProcessed = ReadInt(root, "FilesProcessed");
    summary.TestsFound = ReadInt(root, "TestsFound");
    summary.ActionsFound = ReadInt(root, "ActionsFound");
    summary.SemanticActions = ReadInt(root, "SemanticActions");
    summary.SyntaxFallbackActions = ReadInt(root, "SyntaxFallbackActions");
    summary.UnsupportedActions = ReadInt(root, "UnsupportedActions");
    summary.MappedTargets = ReadInt(root, "MappedTargets");
    summary.UnmappedTargets = ReadInt(root, "UnmappedTargets");
    summary.TodoComments = ReadInt(root, "TodoComments");
}

static void ReadVerifyReport(string path, ArtifactSummary summary)
{
    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
    var root = doc.RootElement;
    if (root.TryGetProperty("summary", out var s))
    {
        summary.VerifyStatus = ReadString(s, "status");
        summary.TodoComments = Math.Max(summary.TodoComments, ReadInt(s, "todoComments"));
        summary.UnmappedTargets = Math.Max(summary.UnmappedTargets, ReadInt(s, "unmappedTargets"));
        summary.UnsupportedActions = Math.Max(summary.UnsupportedActions, ReadInt(s, "unsupportedActions"));
        summary.SyntaxErrors = Math.Max(summary.SyntaxErrors, ReadInt(s, "syntaxErrors"));
    }
}

static ProjectVerifyReport? ReadProjectVerifyReport(string path)
{
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var diagnostics = ReadStringArray(root, "Diagnostics");
        var classified = root.TryGetProperty("ClassifiedDiagnostics", out var cd) && cd.ValueKind == System.Text.Json.JsonValueKind.Array
            ? cd.EnumerateArray().Select(ReadProjectVerifyDiagnostic).ToArray()
            : diagnostics.Select(ClassifyBuildDiagnostic).ToArray();

        return new ProjectVerifyReport(
            GeneratedAtUtc: ReadDateTimeOffset(root, "GeneratedAtUtc") ?? DateTimeOffset.UtcNow,
            Status: ReadString(root, "Status") ?? "unknown",
            ExitCode: ReadInt(root, "ExitCode"),
            GeneratedFiles: ReadStringArray(root, "GeneratedFiles"),
            HarnessProject: ReadString(root, "HarnessProject") ?? "",
            BaseDirectory: ReadString(root, "BaseDirectory") ?? "",
            Solution: ReadString(root, "Solution"),
            BuildWorkingDirectory: ReadString(root, "BuildWorkingDirectory") ?? "",
            ProjectReferences: ReadStringArray(root, "ProjectReferences"),
            ProjectReferenceDiscovery: ReadProjectReferenceDiscoveryArray(root),
            AssemblyReferences: ReadStringArray(root, "AssemblyReferences"),
            PackageReferences: ReadPackageReferenceArray(root),
            BuildFilesImported: ReadStringArray(root, "BuildFilesImported"),
            TargetFramework: ReadString(root, "TargetFramework") ?? "",
            Command: ReadString(root, "Command") ?? "",
            StdOut: ReadString(root, "StdOut") ?? "",
            StdErr: ReadString(root, "StdErr") ?? "",
            Diagnostics: diagnostics,
            ClassifiedDiagnostics: classified);
    }
    catch
    {
        return null;
    }
}

static DateTimeOffset? ReadDateTimeOffset(System.Text.Json.JsonElement root, string name)
{
    if (root.TryGetProperty(name, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String && DateTimeOffset.TryParse(prop.GetString(), out var value))
        return value;
    return null;
}

static string[] ReadStringArray(System.Text.Json.JsonElement root, string name)
{
    if (!root.TryGetProperty(name, out var prop) || prop.ValueKind != System.Text.Json.JsonValueKind.Array)
        return Array.Empty<string>();
    return prop.EnumerateArray()
        .Where(x => x.ValueKind == System.Text.Json.JsonValueKind.String)
        .Select(x => x.GetString() ?? "")
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .ToArray();
}

static PackageReferenceConfig[] ReadPackageReferenceArray(System.Text.Json.JsonElement root)
{
    if (!root.TryGetProperty("PackageReferences", out var prop) || prop.ValueKind != System.Text.Json.JsonValueKind.Array)
        return Array.Empty<PackageReferenceConfig>();

    return prop.EnumerateArray()
        .Select(x => new PackageReferenceConfig
        {
            Include = ReadString(x, "Include") ?? "",
            Version = ReadString(x, "Version") ?? ""
        })
        .Where(x => !string.IsNullOrWhiteSpace(x.Include))
        .ToArray();
}

static ProjectReferenceDiscovery[] ReadProjectReferenceDiscoveryArray(System.Text.Json.JsonElement root)
{
    if (!root.TryGetProperty("ProjectReferenceDiscovery", out var prop) || prop.ValueKind != System.Text.Json.JsonValueKind.Array)
        return Array.Empty<ProjectReferenceDiscovery>();

    return prop.EnumerateArray()
        .Select(x => new ProjectReferenceDiscovery(
            Path: ReadString(x, "Path") ?? "",
            Source: ReadString(x, "Source") ?? "unknown",
            Status: ReadString(x, "Status") ?? "unknown",
            Reason: ReadString(x, "Reason") ?? ""))
        .Where(x => !string.IsNullOrWhiteSpace(x.Path))
        .ToArray();
}

static ProjectVerifyDiagnostic ReadProjectVerifyDiagnostic(System.Text.Json.JsonElement x)
{
    return new ProjectVerifyDiagnostic(
        Raw: ReadString(x, "Raw") ?? "",
        Code: ReadString(x, "Code") ?? "UNKNOWN",
        Severity: ReadString(x, "Severity") ?? "error",
        Category: ReadString(x, "Category") ?? "unknown",
        File: ReadString(x, "File"),
        Line: x.TryGetProperty("Line", out var lineProp) && lineProp.ValueKind == System.Text.Json.JsonValueKind.Number && lineProp.TryGetInt32(out var line) ? line : null,
        LikelyCause: ReadString(x, "LikelyCause") ?? "Требуется ручная классификация diagnostics.",
        SuggestedAction: ReadString(x, "SuggestedAction") ?? "Посмотри raw diagnostic.");
}

static Dictionary<string, (int Count, string File, int Line)> ReadCountItems(string? path, string nameProp, string countProp, string fileProp, string lineProp)
{
    var result = new Dictionary<string, (int Count, string File, int Line)>(StringComparer.OrdinalIgnoreCase);
    if (path == null || !File.Exists(path))
        return result;

    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
    if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
        return result;

    foreach (var item in doc.RootElement.EnumerateArray())
        AddArtifactItem(result, item, nameProp, countProp, fileProp, lineProp);
    return result;
}

static Dictionary<string, (int Count, string File, int Line)> ReadNestedCountItems(string path, string arrayProp, string nameProp, string countProp, string fileProp, string lineProp)
{
    var result = new Dictionary<string, (int Count, string File, int Line)>(StringComparer.OrdinalIgnoreCase);
    if (!File.Exists(path))
        return result;

    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
    if (!doc.RootElement.TryGetProperty(arrayProp, out var array) || array.ValueKind != System.Text.Json.JsonValueKind.Array)
        return result;

    foreach (var item in array.EnumerateArray())
        AddArtifactItem(result, item, nameProp, countProp, fileProp, lineProp);
    return result;
}

static void AddArtifactItem(Dictionary<string, (int Count, string File, int Line)> result, System.Text.Json.JsonElement item, string nameProp, string countProp, string fileProp, string lineProp)
{
    var name = ReadString(item, nameProp);
    if (string.IsNullOrWhiteSpace(name))
        return;
    var count = ReadInt(item, countProp);
    if (count <= 0)
        count = 1;
    var file = ReadString(item, fileProp) ?? "";
    var line = ReadInt(item, lineProp);
    result[name] = (count, file, line);
}

static int ReadInt(System.Text.Json.JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var p))
        return 0;
    if (p.ValueKind == System.Text.Json.JsonValueKind.Number && p.TryGetInt32(out var i))
        return i;
    if (p.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(p.GetString(), out i))
        return i;
    return 0;
}

static bool ReadBool(System.Text.Json.JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var p))
        return false;
    if (p.ValueKind == System.Text.Json.JsonValueKind.True)
        return true;
    if (p.ValueKind == System.Text.Json.JsonValueKind.False)
        return false;
    if (p.ValueKind == System.Text.Json.JsonValueKind.String && bool.TryParse(p.GetString(), out var value))
        return value;
    return false;
}

static string? ReadString(System.Text.Json.JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var p))
        return null;
    return p.ValueKind == System.Text.Json.JsonValueKind.String ? p.GetString() : p.ToString();
}

static string WriteExplainTodoMarkdown(TodoExplanationReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Explain TODO Report");
    sb.AppendLine();
    sb.AppendLine($"- **Generated**: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss zzz}");
    sb.AppendLine($"- **Source**: `{report.Source}`");
    sb.AppendLine($"- **Artifact root**: `{report.ArtifactRoot}`");
    sb.AppendLine($"- **Artifact lookup**: `{(report.RecursiveArtifactLookup ? "recursive" : "direct-only")}`");
    sb.AppendLine($"- **Files**: `{report.FilesProcessed}`");
    sb.AppendLine($"- **Tests**: `{report.TestsFound}`");
    sb.AppendLine($"- **Actions**: `{report.ActionsFound}`");
    sb.AppendLine($"- **Semantic / SyntaxFallback**: `{report.SemanticActions}` / `{report.SyntaxFallbackActions}`");
    sb.AppendLine($"- **Mapped / Unmapped**: `{report.MappedTargets}` / `{report.UnmappedTargets}`");
    sb.AppendLine($"- **Unsupported**: `{report.UnsupportedActions}`");
    sb.AppendLine($"- **TODO**: `{report.TodoComments}`");
    if (!string.IsNullOrWhiteSpace(report.ProjectVerifyStatus))
        sb.AppendLine($"- **Project verify**: `{report.ProjectVerifyStatus}`");
    if (report.SyntaxErrors > 0)
        sb.AppendLine($"- **Syntax/build diagnostics**: `{report.SyntaxErrors}`");
    sb.AppendLine();
    sb.AppendLine("## Следующий лучший шаг");
    sb.AppendLine();
    sb.AppendLine(report.NextBestAction);
    sb.AppendLine();
    AppendNormalizedRootCausesMarkdown(sb, report.NormalizedRootCauses);
    sb.AppendLine();
    AppendTableMappingCandidatesMarkdown(sb, report.TableMappingCandidates);
    sb.AppendLine();
    sb.AppendLine("## Что делать дальше");
    sb.AppendLine();
    if (report.Insights.Length == 0)
    {
        sb.AppendLine("Проблем для объяснения не найдено. Переходи к verify-project или runtime smoke.");
    }
    else
    {
        sb.AppendLine("| # | Категория | Что | Эффект | Где | Действие |");
        sb.AppendLine("|---|---|---|---:|---|---|");
        for (var i = 0; i < report.Insights.Length; i++)
        {
            var x = report.Insights[i];
            var where = string.IsNullOrWhiteSpace(x.ExampleFile) ? "" : $"`{PathRedaction.Redact(x.ExampleFile)}:{x.ExampleLine}`";
            sb.AppendLine($"| {i + 1} | `{EscapeMd(x.Category)}` | {EscapeMd(x.Title)} | {x.EstimatedImpact} | {where} | {EscapeMd(x.SuggestedAction)} |");
        }
    }
    sb.AppendLine();
    sb.AppendLine("## Детали");
    sb.AppendLine();
    foreach (var insight in report.Insights.Take(30))
    {
        sb.AppendLine($"### {EscapeMd(insight.Title)}");
        sb.AppendLine();
        sb.AppendLine($"- **Категория**: `{insight.Category}`");
        sb.AppendLine($"- **Причина**: {insight.Reason}");
        sb.AppendLine($"- **Оценка эффекта**: {insight.EstimatedImpact}");
        if (!string.IsNullOrWhiteSpace(insight.ExampleFile))
            sb.AppendLine($"- **Пример**: `{PathRedaction.Redact(insight.ExampleFile)}:{insight.ExampleLine}`");
        sb.AppendLine($"- **Нужен source truth**: {(insight.RequiresSourceTruth ? "да" : "нет")}");
        sb.AppendLine($"- **Нужен разработчик**: {(insight.RequiresDeveloper ? "возможно" : "нет")}");
        sb.AppendLine($"- **Действие**: {insight.SuggestedAction}");
        if (insight.Evidence.Length > 0)
        {
            sb.AppendLine("- **Факты**:");
            foreach (var e in insight.Evidence.Take(5))
                sb.AppendLine($"  - `{EscapeMd(e)}`");
        }
        sb.AppendLine();
    }
    return sb.ToString();
}

static void AppendNormalizedRootCausesMarkdown(StringBuilder sb, NormalizedTodoGroup[] groups)
{
    sb.AppendLine("## Top normalized root causes");
    sb.AppendLine();
    if (groups.Length == 0)
    {
        sb.AppendLine("Normalized root-cause groups are not available for this report.");
        return;
    }

    sb.AppendLine("| # | Category | Group | Count | Example | Suggested action |");
    sb.AppendLine("|---|---|---|---:|---|---|");
    for (var i = 0; i < Math.Min(20, groups.Length); i++)
    {
        var group = groups[i];
        var example = string.IsNullOrWhiteSpace(group.ExampleFile) ? "" : $"`{PathRedaction.Redact(group.ExampleFile)}:{group.ExampleLine}`";
        sb.AppendLine($"| {i + 1} | `{EscapeMd(group.Category)}` | {EscapeMd(group.DisplayName)} | {group.Count} | {example} | {EscapeMd(group.SuggestedAction)} |");
    }
}

static void AppendTableMappingCandidatesMarkdown(StringBuilder sb, TableMappingCandidate[] candidates)
{
    sb.AppendLine("## Table/list mapping candidates");
    sb.AppendLine();
    if (candidates.Length == 0)
    {
        sb.AppendLine("No table/list mapping candidates were inferred. If table TODOs exist, inspect raw evidence and improve TABLE_MAPPING_REQUIRED markers.");
        return;
    }

    sb.AppendLine("| # | Source root | Accessor | Assertion | Count | Suggested config hint | Example |");
    sb.AppendLine("|---|---|---|---|---:|---|---|");
    for (var i = 0; i < Math.Min(20, candidates.Length); i++)
    {
        var c = candidates[i];
        var example = string.IsNullOrWhiteSpace(c.ExampleFile) ? "" : $"`{PathRedaction.Redact(c.ExampleFile)}:{c.ExampleLine}`";
        sb.AppendLine($"| {i + 1} | `{EscapeMd(c.SourceRoot)}` | `{EscapeMd(c.AccessorKind)}` | `{EscapeMd(c.AssertionKind)}` | {c.Count} | {EscapeMd(c.SuggestedConfigHint)} | {example} |");
    }
}

static string WriteAgentNextTaskMarkdown(TodoExplanationReport report)
{
    var prioritized = SelectAgentNextTask(report);
    var topInsights = report.Insights.Take(10).ToArray();
    var emptyTests = CountInsightImpact(report, "EMPTY_TEST_AFTER_SUPPRESSION");
    var suppressedSideEffects = CountInsightImpact(report, "DEPENDS_ON_SUPPRESSED_SIDE_EFFECT");
    var helperRisks = CountHelperRelatedInsights(report);
    var verifyStatus = string.IsNullOrWhiteSpace(report.ProjectVerifyStatus) ? "not-run" : report.ProjectVerifyStatus!;

    var sb = new StringBuilder();
    sb.AppendLine("# Agent Next Task");
    sb.AppendLine();
    sb.AppendLine("Ты продолжаешь миграцию Selenium C# → Playwright .NET через AST Migrator.");
    sb.AppendLine("Работай как bounded batch: сначала проверь контекст и gates, затем сделай один измеримый шаг, обнови артефакты и handoff.");
    sb.AppendLine();

    sb.AppendLine("## Run context");
    sb.AppendLine();
    sb.AppendLine($"- Artifact root: `{PathRedaction.Redact(report.ArtifactRoot)}`");
    sb.AppendLine($"- Artifact lookup: `{(report.RecursiveArtifactLookup ? "recursive" : "direct-only")}`");
    sb.AppendLine($"- Project verify: `{verifyStatus}`");
    sb.AppendLine($"- Files/tests/actions: `{report.FilesProcessed}` / `{report.TestsFound}` / `{report.ActionsFound}`");
    sb.AppendLine($"- TODO/unmapped/unsupported: `{report.TodoComments}` / `{report.UnmappedTargets}` / `{report.UnsupportedActions}`");
    sb.AppendLine($"- Syntax/compile diagnostics: `{report.SyntaxErrors}`");
    sb.AppendLine();

    sb.AppendLine("## Quality gates / safety signals");
    sb.AppendLine();
    sb.AppendLine($"- EMPTY_TEST_AFTER_SUPPRESSION: `{emptyTests}`");
    sb.AppendLine($"- DEPENDS_ON_SUPPRESSED_SIDE_EFFECT: `{suppressedSideEffects}`");
    sb.AppendLine($"- Helper/POM semantics signals: `{helperRisks}`");
    if (IsVerifyMissingOrNotRun(report))
        sb.AppendLine("- Gate: `Project verify is not-run` — runtime-ready claims are not trustworthy until fresh verify-project exists.");
    if (emptyTests > 0 || suppressedSideEffects > 0)
        sb.AppendLine("- Gate: suppression safety risk exists — treat as safety work, not ordinary TODO reduction.");
    if (helperRisks > 0)
        sb.AppendLine("- Gate: helper/POM wrappers are involved — run or inspect `--mode helper-inventory` before adding suppressions or MethodSemantics guesses.");
    sb.AppendLine();

    sb.AppendLine("## Exact next task");
    sb.AppendLine();
    sb.AppendLine($"Priority: `{prioritized.Priority}`");
    sb.AppendLine($"Category: `{prioritized.Category}`");
    sb.AppendLine();
    sb.AppendLine($"Task: **{prioritized.Title}**");
    sb.AppendLine();
    sb.AppendLine($"Why: {prioritized.Reason}");
    sb.AppendLine();
    sb.AppendLine($"Action: {prioritized.Action}");
    if (!string.IsNullOrWhiteSpace(prioritized.ExampleFile))
        sb.AppendLine($"\nRepresentative example: `{PathRedaction.Redact(prioritized.ExampleFile)}:{prioritized.ExampleLine}`");
    if (prioritized.Evidence.Length > 0)
    {
        sb.AppendLine();
        sb.AppendLine("Evidence:");
        foreach (var evidence in prioritized.Evidence.Take(5))
            sb.AppendLine($"- `{EscapeMd(evidence)}`");
    }
    sb.AppendLine();

    sb.AppendLine("## Top root-cause candidates");
    sb.AppendLine();
    if (topInsights.Length == 0)
    {
        sb.AppendLine("No TODO/root-cause candidates were found in explain-todo. Move to verify-project or runtime smoke after checking generated artifacts.");
    }
    else
    {
        sb.AppendLine("| # | Category | Impact | Example | Suggested action |");
        sb.AppendLine("|---|---|---:|---|---|");
        for (var i = 0; i < topInsights.Length; i++)
        {
            var insight = topInsights[i];
            var example = string.IsNullOrWhiteSpace(insight.ExampleFile)
                ? ""
                : $"`{PathRedaction.Redact(insight.ExampleFile)}:{insight.ExampleLine}`";
            sb.AppendLine($"| {i + 1} | `{EscapeMd(insight.Category)}` | {insight.EstimatedImpact} | {example} | {EscapeMd(insight.SuggestedAction)} |");
        }
    }
    sb.AppendLine();

    sb.AppendLine("## Top normalized root causes");
    sb.AppendLine();
    if (report.NormalizedRootCauses.Length == 0)
    {
        sb.AppendLine("No normalized root-cause groups are available. Use raw candidates above.");
    }
    else
    {
        sb.AppendLine("| # | Category | Group | Count | Suggested action |");
        sb.AppendLine("|---|---|---|---:|---|");
        for (var i = 0; i < Math.Min(10, report.NormalizedRootCauses.Length); i++)
        {
            var group = report.NormalizedRootCauses[i];
            sb.AppendLine($"| {i + 1} | `{EscapeMd(group.Category)}` | {EscapeMd(group.DisplayName)} | {group.Count} | {EscapeMd(group.SuggestedAction)} |");
        }
    }
    sb.AppendLine();

    if (report.TableMappingCandidates.Length > 0)
    {
        sb.AppendLine("## Table/list mapping candidates");
        sb.AppendLine();
        sb.AppendLine("Use these as families. Do not fix row/index TODOs one by one.");
        sb.AppendLine();
        sb.AppendLine("| # | Source root | Accessor | Assertion | Count | Hint |");
        sb.AppendLine("|---|---|---|---|---:|---|");
        for (var i = 0; i < Math.Min(10, report.TableMappingCandidates.Length); i++)
        {
            var c = report.TableMappingCandidates[i];
            sb.AppendLine($"| {i + 1} | `{EscapeMd(c.SourceRoot)}` | `{EscapeMd(c.AccessorKind)}` | `{EscapeMd(c.AssertionKind)}` | {c.Count} | {EscapeMd(c.SuggestedConfigHint)} |");
        }
        sb.AppendLine();
    }

    sb.AppendLine("## Commands to run / update");
    sb.AppendLine();
    sb.AppendLine("Use concrete project paths from the current migration workspace; do not point report commands at a parent folder containing multiple runs.");
    sb.AppendLine();
    sb.AppendLine("```powershell");
    sb.AppendLine($"dotnet run --project Migrator.Cli -- --mode explain-todo --input \"{PathRedaction.Redact(report.ArtifactRoot)}\" --out \"<next-explain-out>\" --format both");
    sb.AppendLine($"dotnet run --project Migrator.Cli -- --mode migration-board --input \"{PathRedaction.Redact(report.ArtifactRoot)}\" --out \"<next-board-out>\" --format both");
    if (ShouldRecommendHelperInventory(report))
        sb.AppendLine("dotnet run --project Migrator.Cli -- --mode helper-inventory --input \"<selenium-tests-or-helper-root>\" --out \"<helper-inventory-out>\" --format both");
    sb.AppendLine("dotnet test Migrator.Tests/Migrator.Tests.csproj");
    sb.AppendLine("```");
    sb.AppendLine();
    if (IsVerifyMissingOrNotRun(report))
    {
        sb.AppendLine("Fresh verify-project is required before runtime-ready claims:");
        sb.AppendLine();
        sb.AppendLine("```powershell");
        sb.AppendLine("dotnet run --project Migrator.Cli -- --mode verify-project --input \"<selenium-tests-root>\" --config \"<adapter-config.json>\" --out \"<verify-out>\" --format both");
        sb.AppendLine("```");
        sb.AppendLine();
    }

    sb.AppendLine("## Helper inventory rule");
    sb.AppendLine();
    sb.AppendLine("Run/request `--mode helper-inventory` before changing suppressions or MethodSemantics for project/POM wrappers such as `InputAndAccept`, `ValidateLoading`, `ClickAndOpen`, `ManualInputValue`, unqualified helper calls, or unknown business helpers. Do not infer helper semantics by name alone.");
    sb.AppendLine();

    sb.AppendLine("## Acceptance criteria");
    sb.AppendLine();
    foreach (var criterion in BuildAgentAcceptanceCriteria(report, prioritized))
        sb.AppendLine($"- {criterion}");
    sb.AppendLine();

    sb.AppendLine("## Do not do");
    sb.AppendLine();
    sb.AppendLine("- Do not edit generated `.cs` files manually.");
    sb.AppendLine("- Do not add broad suppressions just to reduce TODO count.");
    sb.AppendLine("- Do not add `page`/`pagef` to known identifiers to hide a root cause.");
    sb.AppendLine("- Do not mark runtime-ready if project verify is missing or failed.");
    sb.AppendLine("- Do not guess selectors or helper semantics without source/POM/helper evidence.");
    sb.AppendLine("- If the fix requires engine code, add focused regression tests and keep the patch small.");
    sb.AppendLine();

    sb.AppendLine("## Required final response format");
    sb.AppendLine();
    sb.AppendLine("### Summary");
    sb.AppendLine("### Files changed");
    sb.AppendLine("### Commands run");
    sb.AppendLine("### Metrics before/after");
    sb.AppendLine("### Quality gate status");
    sb.AppendLine("### Remaining risks");
    sb.AppendLine("### Next exact task");
    return sb.ToString();
}

static AgentNextTaskPlan SelectAgentNextTask(TodoExplanationReport report)
{
    if (IsVerifyMissingOrNotRun(report))
    {
        return new AgentNextTaskPlan(
            Priority: "P0_VERIFY_PROJECT",
            Category: "PROJECT_VERIFY_NOT_RUN",
            Title: "Run fresh verify-project before runtime-ready or smoke claims",
            Reason: "The current artifact batch does not contain a passed fresh project verify. Compile/runtime claims can be stale until verify-project is regenerated for this exact run.",
            Action: "Run `--mode verify-project` for the current input/config, then regenerate migration-board/explain-todo from the concrete run directory.",
            ExampleFile: "",
            ExampleLine: 0,
            Evidence: Array.Empty<string>());
    }

    if (report.SyntaxErrors > 0 || report.ProjectVerifyStatus?.Equals("failed", StringComparison.OrdinalIgnoreCase) == true)
    {
        return new AgentNextTaskPlan(
            Priority: "P0_COMPILE",
            Category: "PROJECT_VERIFY_FAILED",
            Title: "Fix compile/project verify errors first",
            Reason: "Generated code is not compile-stable. TODO reduction should wait until compile errors are understood.",
            Action: "Open project-verify-report, classify top diagnostics, fix the smallest root cause, and rerun verify-project.",
            ExampleFile: "",
            ExampleLine: 0,
            Evidence: Array.Empty<string>());
    }

    var emptyTests = FindInsight(report, "EMPTY_TEST_AFTER_SUPPRESSION");
    var suppressedSideEffects = FindInsight(report, "DEPENDS_ON_SUPPRESSED_SIDE_EFFECT");
    var safety = suppressedSideEffects ?? emptyTests;
    if (safety != null)
    {
        return new AgentNextTaskPlan(
            Priority: "P1_SUPPRESSION_SAFETY",
            Category: safety.Category,
            Title: safety.Title,
            Reason: "Suppression safety issues can produce hollow or invalid tests even when compile is green.",
            Action: "Classify the upstream suppressed method family. Use `--mode helper-inventory` for project/POM helpers, then replace unsafe suppressions with MethodSemantics/explicit mappings or keep downstream code blocked.",
            ExampleFile: safety.ExampleFile,
            ExampleLine: safety.ExampleLine,
            Evidence: safety.Evidence);
    }

    var tableCandidate = report.TableMappingCandidates.FirstOrDefault();
    if (tableCandidate != null)
    {
        return new AgentNextTaskPlan(
            Priority: "P2_TABLE_MAPPING",
            Category: "TABLE_MAPPING_REQUIRED",
            Title: $"Add source-backed table/list mapping for `{tableCandidate.SourceRoot}`",
            Reason: "Table/list TODOs should be fixed as reusable table families, not one row/index at a time.",
            Action: tableCandidate.SuggestedConfigHint,
            ExampleFile: tableCandidate.ExampleFile,
            ExampleLine: tableCandidate.ExampleLine,
            Evidence: tableCandidate.Evidence);
    }

    var first = report.Insights.FirstOrDefault();
    if (first == null)
    {
        return new AgentNextTaskPlan(
            Priority: "P2_RUNTIME_SMOKE",
            Category: "NO_ACTIONABLE_TODO",
            Title: "No actionable TODO root cause found",
            Reason: "explain-todo did not find actionable TODO categories.",
            Action: "Run smoke-plan/runtime checklist or inspect verify-project output for residual risks.",
            ExampleFile: "",
            ExampleLine: 0,
            Evidence: Array.Empty<string>());
    }

    var priority = first.Category switch
    {
        "TABLE_MAPPING_REQUIRED" => "P2_TABLE_MAPPING",
        "MANUAL_REVIEW" => "P2_METHOD_FAMILY_GROUPING",
        "UNSUPPORTED_ACTION" => "P2_UNSUPPORTED_CLASSIFICATION",
        "HELPER_METHOD_REQUIRES_MAPPING" => "P2_HELPER_OR_MAPPING",
        "RAW_STATEMENT" => "P2_HELPER_OR_MAPPING",
        "WAIT_MAPPING_REQUIRED" => "P2_WAIT_MAPPING",
        _ => "P2_ROOT_CAUSE"
    };

    return new AgentNextTaskPlan(
        Priority: priority,
        Category: first.Category,
        Title: first.Title,
        Reason: first.Reason,
        Action: first.SuggestedAction,
        ExampleFile: first.ExampleFile,
        ExampleLine: first.ExampleLine,
        Evidence: first.Evidence);
}

static TodoInsight? FindInsight(TodoExplanationReport report, string category) =>
    report.Insights.FirstOrDefault(i => i.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

static int CountInsightImpact(TodoExplanationReport report, string category) =>
    report.Insights
        .Where(i => i.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
        .Sum(i => i.EstimatedImpact);

static int CountHelperRelatedInsights(TodoExplanationReport report) =>
    report.Insights
        .Where(i => IsHelperRelatedInsight(i))
        .Sum(i => Math.Max(1, i.EstimatedImpact));

static bool IsHelperRelatedInsight(TodoInsight insight)
{
    var text = string.Join(" ", new[] { insight.Category, insight.Title, insight.Reason, insight.SuggestedAction }.Concat(insight.Evidence));
    return text.Contains("helper", StringComparison.OrdinalIgnoreCase)
        || text.Contains("wrapper", StringComparison.OrdinalIgnoreCase)
        || text.Contains("POM", StringComparison.OrdinalIgnoreCase)
        || text.Contains("InputAndAccept", StringComparison.OrdinalIgnoreCase)
        || text.Contains("ValidateLoading", StringComparison.OrdinalIgnoreCase)
        || text.Contains("ClickAndOpen", StringComparison.OrdinalIgnoreCase)
        || text.Contains("ManualInputValue", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Loader", StringComparison.OrdinalIgnoreCase);
}

static bool ShouldRecommendHelperInventory(TodoExplanationReport report) =>
    CountHelperRelatedInsights(report) > 0
    || CountInsightImpact(report, "DEPENDS_ON_SUPPRESSED_SIDE_EFFECT") > 0
    || report.Insights.Any(i => i.Category is "UNSUPPORTED_ACTION" or "RAW_STATEMENT" or "WAIT_MAPPING_REQUIRED" or "MANUAL_REVIEW" or "HELPER_METHOD_REQUIRES_MAPPING");

static bool IsVerifyMissingOrNotRun(TodoExplanationReport report) =>
    string.IsNullOrWhiteSpace(report.ProjectVerifyStatus)
    || report.ProjectVerifyStatus.Equals("not-run", StringComparison.OrdinalIgnoreCase);

static IEnumerable<string> BuildAgentAcceptanceCriteria(TodoExplanationReport report, AgentNextTaskPlan plan)
{
    if (plan.Priority.StartsWith("P0_VERIFY_PROJECT", StringComparison.OrdinalIgnoreCase))
    {
        yield return "Fresh `project-verify-report.md/json` exists for the current concrete run/config.";
        yield return "`migration-board` regenerated from that concrete run directory; no parent-folder artifact mixing.";
        yield return "Runtime-ready/smoke claims are either updated from verify-project or explicitly withheld.";
        yield break;
    }

    if (plan.Priority.StartsWith("P0_COMPILE", StringComparison.OrdinalIgnoreCase))
    {
        yield return "Top compile diagnostic root cause is fixed or escalated with evidence.";
        yield return "`verify-project` rerun and compile error count does not increase.";
        yield break;
    }

    if (plan.Priority.StartsWith("P1_SUPPRESSION", StringComparison.OrdinalIgnoreCase))
    {
        yield return "No new broad suppressions are added.";
        yield return "Each touched suppressed method family is classified as SafeWaitElide, ProjectWaitHelper, RequiredSideEffect, ReadOnlyProbe, or UnknownUnsafe.";
        yield return "If project/POM helpers are involved, `--mode helper-inventory` output or source-body evidence is referenced.";
        yield return "`EMPTY_TEST_AFTER_SUPPRESSION` and `DEPENDS_ON_SUPPRESSED_SIDE_EFFECT` counts do not increase.";
        yield break;
    }

    if (plan.Priority.StartsWith("P2_TABLE", StringComparison.OrdinalIgnoreCase))
    {
        yield return "Table/list TODOs are grouped by source root/accessor/assertion kind; do not fix individual row/index TODOs one by one.";
        yield return "Any new UiTargets/Tables mapping is backed by source/POM selector truth.";
        yield return "Before/after metrics include TABLE_MAPPING_REQUIRED count and affected table family count.";
    }

    if (ShouldRecommendHelperInventory(report))
        yield return "If helper/POM wrappers are touched, helper-inventory evidence is generated or explicitly cited.";

    yield return "Focused regression tests are added for engine changes; config-only changes include before/after metrics.";
    yield return "Generated reports are refreshed from a concrete run directory, not a parent artifact folder.";
    yield return "Metrics before/after are reported: TODO, unmapped, unsupported, empty tests, suppressed side-effect dependencies.";
}

static string TrimForTitle(string value, int max)
{
    var oneLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
    return oneLine.Length <= max ? oneLine : oneLine.Substring(0, max - 1) + "…";
}

static bool LooksLikeGenericMigratorGap(string value)
{
    return value.Contains("TODO", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Page.", StringComparison.Ordinal)
        || value.Contains("Locator", StringComparison.OrdinalIgnoreCase)
        || value.Contains("ClickAndOpen", StringComparison.OrdinalIgnoreCase);
}

static string EscapeMd(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");


// --- Runtime readiness / smoke-plan mode ---

static int RunSmokePlan(string inputPath, string outPath, string format, bool recursiveArtifacts)
{
    if (!Directory.Exists(inputPath))
    {
        Console.Error.WriteLine($"Smoke-plan mode expects a directory with migration artifacts: {inputPath}");
        return 1;
    }

    Directory.CreateDirectory(outPath);
    SmokePlanReport report;
    try
    {
        report = BuildSmokePlanReportFromArtifacts(inputPath, recursiveArtifacts);
    }
    catch (ArtifactLookupException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }

    WriteSmokePlanReport(report, outPath, format);

    Console.WriteLine("=== Smoke Plan Summary ===");
    Console.WriteLine($"Source: {inputPath}");
    Console.WriteLine($"Artifact lookup: {(recursiveArtifacts ? "recursive" : "direct-only")}");
    Console.WriteLine($"Generated files: {report.GeneratedFiles}");
    Console.WriteLine($"Tests found: {report.TestsFound}");
    Console.WriteLine($"Runtime-ready: {report.RuntimeReadyCandidates}");
    Console.WriteLine($"Smoke candidates: {report.SmokeCandidates}");
    Console.WriteLine($"Project verify: {report.ProjectVerifyStatus ?? "not-run"}");
    Console.WriteLine($"Smoke-plan artifacts written to: {Path.GetFullPath(outPath)}");
    return 0;
}

static void WriteSmokePlanArtifacts(string artifactDir, string outPath, string format)
{
    try
    {
        var report = BuildSmokePlanReportFromArtifacts(artifactDir);
        if (report.TestsFound == 0)
            return;
        WriteSmokePlanReport(report, outPath, format);
    }
    catch
    {
        // Advisory only: smoke-plan must not break migrate/verify-project.
    }
}

static SmokePlanReport BuildSmokePlanReportFromArtifacts(string artifactDir, bool recursiveArtifacts = false)
{
    ValidateArtifactLookupRoot(artifactDir, recursiveArtifacts);
    var projectVerifyPath = FindFirstExisting(artifactDir, "project-verify-report.json", recursiveArtifacts);
    var projectVerify = projectVerifyPath != null ? ReadProjectVerifyReport(projectVerifyPath) : null;
    var explainPath = FindFirstExisting(artifactDir, "explain-todo.json", recursiveArtifacts);
    var explain = explainPath != null ? ReadTodoExplanationReport(explainPath) : null;
    var generatedFiles = FindGeneratedCsFiles(artifactDir).ToArray();

    var diagnosticsByFile = BuildDiagnosticsByFile(projectVerify);
    var candidates = new List<SmokeCandidate>();

    foreach (var file in generatedFiles)
    {
        foreach (var method in ExtractGeneratedTestMethods(file))
        {
            diagnosticsByFile.TryGetValue(Path.GetFileName(file), out var fileDiagnostics);
            fileDiagnostics ??= Array.Empty<ProjectVerifyDiagnostic>();
            var errorCount = fileDiagnostics.Count(d => d.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
            var warningCount = fileDiagnostics.Length - errorCount;
            candidates.Add(BuildSmokeCandidate(method, projectVerify?.Status, errorCount, warningCount, fileDiagnostics));
        }
    }

    var ordered = candidates
        .OrderByDescending(x => x.Score)
        .ThenBy(x => x.TodoLines)
        .ThenByDescending(x => x.ActiveRatio)
        .ThenBy(x => x.File, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.TestName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var ready = ordered.Count(x => x.ReadinessLevel.StartsWith("Level 5", StringComparison.OrdinalIgnoreCase));
    var smoke = ordered.Count(x => x.ReadinessLevel.StartsWith("Level 4", StringComparison.OrdinalIgnoreCase));

    return new SmokePlanReport(
        GeneratedAtUtc: DateTimeOffset.UtcNow,
        Source: Path.GetFullPath(artifactDir),
        ArtifactRoot: Path.GetFullPath(artifactDir),
        RecursiveArtifactLookup: recursiveArtifacts,
        ProjectVerifyStatus: projectVerify?.Status,
        GeneratedFiles: generatedFiles.Length,
        TestsFound: ordered.Length,
        RuntimeReadyCandidates: ready,
        SmokeCandidates: smoke,
        Candidates: ordered,
        RecommendedNextActions: BuildSmokeRecommendedActions(ordered, projectVerify, explain).ToArray());
}

static TodoExplanationReport? ReadTodoExplanationReport(string path)
{
    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        return new TodoExplanationReport(
            GeneratedAtUtc: ReadDateTimeOffset(root, "GeneratedAtUtc") ?? DateTimeOffset.UtcNow,
            Source: ReadString(root, "Source") ?? path,
            ArtifactRoot: ReadString(root, "ArtifactRoot") ?? ReadString(root, "Source") ?? path,
            RecursiveArtifactLookup: ReadBool(root, "RecursiveArtifactLookup"),
            FilesProcessed: ReadInt(root, "FilesProcessed"),
            TestsFound: ReadInt(root, "TestsFound"),
            ActionsFound: ReadInt(root, "ActionsFound"),
            SemanticActions: ReadInt(root, "SemanticActions"),
            SyntaxFallbackActions: ReadInt(root, "SyntaxFallbackActions"),
            MappedTargets: ReadInt(root, "MappedTargets"),
            UnmappedTargets: ReadInt(root, "UnmappedTargets"),
            UnsupportedActions: ReadInt(root, "UnsupportedActions"),
            TodoComments: ReadInt(root, "TodoComments"),
            SyntaxErrors: ReadInt(root, "SyntaxErrors"),
            ProjectVerifyStatus: ReadString(root, "ProjectVerifyStatus"),
            Insights: Array.Empty<TodoInsight>(),
            NormalizedRootCauses: ReadNormalizedTodoGroups(root),
            TableMappingCandidates: ReadTableMappingCandidates(root),
            NextBestAction: ReadString(root, "NextBestAction") ?? "Run smoke-plan or explain-todo.");
    }
    catch
    {
        return null;
    }
}

static TableMappingCandidate[] ReadTableMappingCandidates(System.Text.Json.JsonElement root)
{
    if (!root.TryGetProperty("TableMappingCandidates", out var candidates) || candidates.ValueKind != System.Text.Json.JsonValueKind.Array)
        return Array.Empty<TableMappingCandidate>();

    var result = new List<TableMappingCandidate>();
    foreach (var candidate in candidates.EnumerateArray())
    {
        result.Add(new TableMappingCandidate(
            GroupKey: ReadString(candidate, "GroupKey") ?? "unknown",
            SourceRoot: ReadString(candidate, "SourceRoot") ?? "unknown-table",
            AccessorKind: ReadString(candidate, "AccessorKind") ?? "table-access",
            AssertionKind: ReadString(candidate, "AssertionKind") ?? "unknown-assertion",
            Count: ReadInt(candidate, "Count"),
            ExampleFile: ReadString(candidate, "ExampleFile") ?? "",
            ExampleLine: ReadInt(candidate, "ExampleLine"),
            SourceExpression: ReadString(candidate, "SourceExpression") ?? "",
            SuggestedUiTargetRoot: ReadString(candidate, "SuggestedUiTargetRoot") ?? "<source-backed-table-root>",
            SuggestedConfigHint: ReadString(candidate, "SuggestedConfigHint") ?? "Add source-backed table/list mapping.",
            Evidence: ReadStringArray(candidate, "Evidence")));
    }

    return result
        .OrderByDescending(x => x.Count)
        .ThenBy(x => x.SourceRoot, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static NormalizedTodoGroup[] ReadNormalizedTodoGroups(System.Text.Json.JsonElement root)
{
    if (!root.TryGetProperty("NormalizedRootCauses", out var groups) || groups.ValueKind != System.Text.Json.JsonValueKind.Array)
        return Array.Empty<NormalizedTodoGroup>();

    var result = new List<NormalizedTodoGroup>();
    foreach (var group in groups.EnumerateArray())
    {
        result.Add(new NormalizedTodoGroup(
            Category: ReadString(group, "Category") ?? "UNKNOWN",
            GroupKey: ReadString(group, "GroupKey") ?? "unknown",
            DisplayName: ReadString(group, "DisplayName") ?? ReadString(group, "GroupKey") ?? "unknown",
            Count: ReadInt(group, "Count"),
            ExampleFile: ReadString(group, "ExampleFile") ?? "",
            ExampleLine: ReadInt(group, "ExampleLine"),
            SuggestedAction: ReadString(group, "SuggestedAction") ?? "Inspect representative source truth.",
            RepresentativeFiles: ReadStringArray(group, "RepresentativeFiles"),
            Evidence: ReadStringArray(group, "Evidence")));
    }

    return result
        .OrderByDescending(x => x.Count)
        .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.GroupKey, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static IEnumerable<string> FindGeneratedCsFiles(string artifactDir)
{
    var generatedDir = Path.Combine(artifactDir, "generated");
    var searchRoot = Directory.Exists(generatedDir) ? generatedDir : artifactDir;
    return Directory.EnumerateFiles(searchRoot, "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Replace('\\', '/').Contains("/project-verify/", StringComparison.OrdinalIgnoreCase))
        .Where(path => !path.Replace('\\', '/').Contains("/obj/", StringComparison.OrdinalIgnoreCase))
        .Where(path => !path.Replace('\\', '/').Contains("/bin/", StringComparison.OrdinalIgnoreCase))
        .Where(path => Path.GetFileName(path).EndsWith("Playwright.cs", StringComparison.OrdinalIgnoreCase)
                    || File.ReadLines(path).Take(80).Any(line => line.Contains("Microsoft.Playwright", StringComparison.Ordinal) || line.Contains("PageTest", StringComparison.Ordinal)))
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
}

static Dictionary<string, ProjectVerifyDiagnostic[]> BuildDiagnosticsByFile(ProjectVerifyReport? projectVerify)
{
    var result = new Dictionary<string, List<ProjectVerifyDiagnostic>>(StringComparer.OrdinalIgnoreCase);
    if (projectVerify == null)
        return new Dictionary<string, ProjectVerifyDiagnostic[]>(StringComparer.OrdinalIgnoreCase);

    foreach (var d in projectVerify.ClassifiedDiagnostics)
    {
        if (string.IsNullOrWhiteSpace(d.File))
            continue;
        var key = Path.GetFileName(d.File);
        if (!result.TryGetValue(key, out var list))
        {
            list = new List<ProjectVerifyDiagnostic>();
            result[key] = list;
        }
        list.Add(d);
    }

    return result.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
}

static IEnumerable<GeneratedTestMethodStats> ExtractGeneratedTestMethods(string filePath)
{
    var lines = File.ReadAllLines(filePath);
    var pendingTestAttribute = false;
    for (var i = 0; i < lines.Length; i++)
    {
        var trimmed = lines[i].Trim();
        if (trimmed.StartsWith("[Test", StringComparison.Ordinal) || trimmed.StartsWith("[Theory", StringComparison.Ordinal) || trimmed.StartsWith("[Fact", StringComparison.Ordinal))
        {
            pendingTestAttribute = true;
            continue;
        }

        if (!pendingTestAttribute)
            continue;

        var match = System.Text.RegularExpressions.Regex.Match(lines[i], @"\b(?:async\s+)?(?:Task|void)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(");
        if (!match.Success)
            continue;

        var methodName = match.Groups["name"].Value;
        var methodLines = CaptureMethodLines(lines, i, out var endIndex);
        yield return AnalyzeGeneratedTestMethod(filePath, methodName, i + 1, methodLines);
        i = Math.Max(i, endIndex);
        pendingTestAttribute = false;
    }
}

static string[] CaptureMethodLines(string[] lines, int signatureIndex, out int endIndex)
{
    var result = new List<string>();
    var depth = 0;
    var opened = false;
    endIndex = signatureIndex;

    for (var i = signatureIndex; i < lines.Length; i++)
    {
        var line = lines[i];
        result.Add(line);
        foreach (var ch in line)
        {
            if (ch == '{')
            {
                depth++;
                opened = true;
            }
            else if (ch == '}')
            {
                depth--;
            }
        }

        if (opened && depth <= 0)
        {
            endIndex = i;
            break;
        }
    }

    return result.ToArray();
}

static GeneratedTestMethodStats AnalyzeGeneratedTestMethod(string filePath, string testName, int startLine, string[] methodLines)
{
    var todoLines = methodLines.Count(line => line.Contains("TODO", StringComparison.OrdinalIgnoreCase));
    var activeLines = 0;
    var executableLines = 0;
    var awaitCount = 0;
    var expectCount = 0;
    var locatorCount = 0;

    foreach (var line in methodLines)
    {
        var t = line.Trim();
        if (string.IsNullOrWhiteSpace(t) || t == "{" || t == "}" || t.StartsWith("[", StringComparison.Ordinal))
            continue;
        if (t.Contains("TODO", StringComparison.OrdinalIgnoreCase))
            continue;

        executableLines++;
        if (!t.StartsWith("//", StringComparison.Ordinal))
        {
            activeLines++;
            if (t.Contains("await ", StringComparison.Ordinal)) awaitCount++;
            if (t.Contains("Expect(", StringComparison.Ordinal) || t.Contains("Assert.", StringComparison.Ordinal) || t.Contains("Should", StringComparison.Ordinal)) expectCount++;
            if (t.Contains("Locator(", StringComparison.Ordinal) || t.Contains("GetBy", StringComparison.Ordinal)) locatorCount++;
        }
    }

    var denominator = activeLines + todoLines;
    var activeRatio = denominator == 0 ? 0 : Math.Round((double)activeLines / denominator, 3);
    return new GeneratedTestMethodStats(Path.GetFullPath(filePath), testName, startLine, activeLines, todoLines, executableLines, activeRatio, awaitCount, expectCount, locatorCount);
}

static SmokeCandidate BuildSmokeCandidate(GeneratedTestMethodStats method, string? projectVerifyStatus, int compileErrors, int compileWarnings, ProjectVerifyDiagnostic[] diagnostics)
{
    var projectPassed = string.Equals(projectVerifyStatus, "passed", StringComparison.OrdinalIgnoreCase);
    var projectNotRun = string.IsNullOrWhiteSpace(projectVerifyStatus);
    var score = method.ActiveRatio * 100 - method.TodoLines * 7 - compileErrors * 25 - compileWarnings * 2;
    if (method.AwaitCount > 0) score += 5;
    if (method.ExpectOrAssertCount > 0) score += 5;
    if (method.LocatorCount > 0) score += 3;
    score = Math.Round(Math.Max(0, Math.Min(100, score)), 1);

    string level;
    if (compileErrors > 0)
        level = "Level 2 — compile cleanup";
    else if (projectNotRun)
        level = method.TodoLines <= 3 && method.ActiveRatio >= 0.7 ? "Level 3 — run verify-project first" : "Level 2 — needs project verify";
    else if (!projectPassed)
        level = method.TodoLines <= 3 && method.ActiveRatio >= 0.75 ? "Level 3 — close, but project verify failed" : "Level 2 — compile/project cleanup";
    else if (method.TodoLines == 0 && method.ActiveRatio >= 0.8)
        level = "Level 5 — runtime-ready candidate";
    else if (method.TodoLines <= 3 && method.ActiveRatio >= 0.7)
        level = "Level 4 — smoke candidate";
    else if (method.TodoLines <= 8 && method.ActiveRatio >= 0.5)
        level = "Level 3 — close to smoke";
    else
        level = "Level 2 — migration cleanup";

    return new SmokeCandidate(
        File: method.File,
        TestName: method.TestName,
        StartLine: method.StartLine,
        ActiveLines: method.ActiveLines,
        TodoLines: method.TodoLines,
        ActiveRatio: method.ActiveRatio,
        CompileErrors: compileErrors,
        CompileWarnings: compileWarnings,
        AwaitCount: method.AwaitCount,
        ExpectOrAssertCount: method.ExpectOrAssertCount,
        LocatorCount: method.LocatorCount,
        Score: score,
        ReadinessLevel: level,
        Checklist: BuildRuntimeChecklist(method, projectVerifyStatus, compileErrors, diagnostics).ToArray());
}

static IEnumerable<string> BuildRuntimeChecklist(GeneratedTestMethodStats method, string? projectVerifyStatus, int compileErrors, ProjectVerifyDiagnostic[] diagnostics)
{
    if (compileErrors > 0)
    {
        yield return "Сначала исправить compile errors в verify-project; runtime запуск пока не имеет смысла.";
        foreach (var d in diagnostics.Where(x => x.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)).Take(3))
            yield return $"Проверить `{d.Code}`/{d.Category}: {d.SuggestedAction}";
        yield break;
    }

    if (string.IsNullOrWhiteSpace(projectVerifyStatus))
        yield return "Запустить `--mode verify-project`, чтобы подтвердить компиляцию в контексте проекта.";
    else if (!projectVerifyStatus.Equals("passed", StringComparison.OrdinalIgnoreCase))
        yield return "Довести `verify-project` до pass или понять, какие ошибки не относятся к этому тесту.";

    yield return method.TodoLines > 0
        ? $"Разобрать оставшиеся TODO внутри теста: {method.TodoLines}."
        : "TODO внутри теста нет — можно готовить изолированный runtime запуск.";

    yield return method.LocatorCount > 0
        ? "Проверить ключевые locators/selectors против UI: data-tid, таблицы, модалки, лайтбоксы."
        : "Проверить, что тест реально взаимодействует с UI, а не стал пустым/почти полностью setup-only.";

    if (method.AwaitCount == 0)
        yield return "Проверить async-тест: нет `await`, возможно большая часть действий осталась TODO.";
    if (method.ExpectOrAssertCount == 0)
        yield return "Проверить наличие meaningful assertions: тест может выполнять действия без проверок.";

    yield return "Запускать первым один тест/fixture, не весь пакет: `dotnet test --filter FullyQualifiedName~<TestName>` или локальный аналог команды проекта.";
    yield return "После runtime failure классифицировать причину: locator, wait, test data/setup, navigation, assertion mismatch.";
}

static IEnumerable<string> BuildSmokeRecommendedActions(SmokeCandidate[] candidates, ProjectVerifyReport? projectVerify, TodoExplanationReport? explain)
{
    if (projectVerify == null)
        yield return "Сначала запусти `--mode verify-project`: runtime readiness без проектной компиляции ненадёжна.";
    else if (!projectVerify.Status.Equals("passed", StringComparison.OrdinalIgnoreCase))
        yield return "Сначала разберись с `project-verify-report.md`: runtime запускать рано, пока generated-код не компилируется.";

    var best = candidates.FirstOrDefault();
    if (best != null)
        yield return $"Первый кандидат на доводку: `{Path.GetFileName(best.File)}::{best.TestName}` ({best.ReadinessLevel}, score {best.Score}).";

    var close = candidates.Count(x => x.ReadinessLevel.StartsWith("Level 4", StringComparison.OrdinalIgnoreCase) || x.ReadinessLevel.StartsWith("Level 5", StringComparison.OrdinalIgnoreCase));
    if (close > 0)
        yield return $"Сфокусируйся на Level 4/5 тестах: {close} кандидатов ближе всего к runtime.";
    if (explain != null && explain.TodoComments > 0)
        yield return $"Для снижения TODO смотри `explain-todo.md`: следующий шаг — {explain.NextBestAction}";
    if (candidates.Length == 0)
        yield return "Generated тесты не найдены. Проверь, что smoke-plan запущен на папку с `generated/` или `*Playwright.cs`.";
}

static void WriteSmokePlanReport(SmokePlanReport report, string outPath, string format)
{
    Directory.CreateDirectory(outPath);
    if (format == "json" || format == "both")
    {
        File.WriteAllText(Path.Combine(outPath, "smoke-plan.json"), System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    if (format == "text" || format == "both")
    {
        File.WriteAllText(Path.Combine(outPath, "smoke-plan.md"), WriteSmokePlanMarkdown(report));
        File.WriteAllText(Path.Combine(outPath, "runtime-checklist.md"), WriteRuntimeChecklistMarkdown(report));
        File.WriteAllText(Path.Combine(outPath, "agent-runtime-next-task.md"), WriteAgentRuntimeNextTaskMarkdown(report));
    }
}

static string WriteSmokePlanMarkdown(SmokePlanReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Smoke Plan / Runtime Readiness");
    sb.AppendLine();
    sb.AppendLine($"- **Generated**: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss zzz}");
    sb.AppendLine($"- **Source**: `{report.Source}`");
    sb.AppendLine($"- **Artifact root**: `{report.ArtifactRoot}`");
    sb.AppendLine($"- **Artifact lookup**: `{(report.RecursiveArtifactLookup ? "recursive" : "direct-only")}`");
    sb.AppendLine($"- **Project verify**: `{report.ProjectVerifyStatus ?? "not-run"}`");
    sb.AppendLine($"- **Generated files**: `{report.GeneratedFiles}`");
    sb.AppendLine($"- **Tests found**: `{report.TestsFound}`");
    sb.AppendLine($"- **Runtime-ready candidates**: `{report.RuntimeReadyCandidates}`");
    sb.AppendLine($"- **Smoke candidates**: `{report.SmokeCandidates}`");
    sb.AppendLine();

    sb.AppendLine("## Рекомендуемые действия");
    foreach (var action in report.RecommendedNextActions)
        sb.AppendLine($"- {action}");
    if (report.RecommendedNextActions.Length == 0)
        sb.AppendLine("- Нет рекомендаций: проверь входные артефакты.");
    sb.AppendLine();

    sb.AppendLine("## Лучшие кандидаты");
    sb.AppendLine("| # | Level | Score | Test | Active | TODO | Compile errors | File | Первый пункт checklist |");
    sb.AppendLine("|---|---|---:|---|---:|---:|---:|---|---|");
    for (var i = 0; i < Math.Min(30, report.Candidates.Length); i++)
    {
        var c = report.Candidates[i];
        var checklist = c.Checklist.Length == 0 ? "" : EscapeMd(c.Checklist[0]);
        sb.AppendLine($"| {i + 1} | {EscapeMd(c.ReadinessLevel)} | {c.Score:0.0} | `{EscapeMd(c.TestName)}` | {c.ActiveRatio:P0} | {c.TodoLines} | {c.CompileErrors} | `{EscapeMd(PathRedaction.Redact(c.File))}:{c.StartLine}` | {checklist} |");
    }
    sb.AppendLine();
    sb.AppendLine("## Как читать уровни");
    sb.AppendLine("- **Level 5** — лучший кандидат на изолированный runtime запуск.");
    sb.AppendLine("- **Level 4** — почти готов, обычно нужно добить несколько TODO/проверок.");
    sb.AppendLine("- **Level 3** — близко, но сначала нужен verify-project или небольшая чистка.");
    sb.AppendLine("- **Level 2** — сначала маппинги/compile cleanup, runtime пока рано.");
    return sb.ToString();
}

static string WriteRuntimeChecklistMarkdown(SmokePlanReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Runtime Checklist");
    sb.AppendLine();
    sb.AppendLine("Используй этот список для ручной/агентской доводки первых runtime smoke-тестов.");
    sb.AppendLine();
    foreach (var c in report.Candidates.Take(15))
    {
        sb.AppendLine($"## `{c.TestName}`");
        sb.AppendLine();
        sb.AppendLine($"- **File**: `{PathRedaction.Redact(c.File)}:{c.StartLine}`");
        sb.AppendLine($"- **Level**: {c.ReadinessLevel}");
        sb.AppendLine($"- **Score**: {c.Score:0.0}");
        sb.AppendLine($"- **Active ratio**: {c.ActiveRatio:P0}");
        sb.AppendLine($"- **TODO**: {c.TodoLines}");
        sb.AppendLine($"- **Compile errors**: {c.CompileErrors}");
        sb.AppendLine();
        sb.AppendLine("### Checklist");
        foreach (var item in c.Checklist)
            sb.AppendLine($"- [ ] {item}");
        sb.AppendLine();
    }
    if (report.Candidates.Length == 0)
        sb.AppendLine("Generated tests were not found. Run smoke-plan against a migrate/verify-project output directory.");
    return sb.ToString();
}

static string WriteAgentRuntimeNextTaskMarkdown(SmokePlanReport report)
{
    var best = report.Candidates.FirstOrDefault();
    var sb = new StringBuilder();
    sb.AppendLine("# Agent Runtime Next Task");
    sb.AppendLine();
    sb.AppendLine("Ты продолжаешь миграцию Selenium C# → Playwright .NET. Сейчас этап runtime readiness: нужно выбрать самые близкие к запуску тесты и довести их до smoke.");
    sb.AppendLine();
    sb.AppendLine("## Статус");
    sb.AppendLine($"- Project verify: `{report.ProjectVerifyStatus ?? "not-run"}`");
    sb.AppendLine($"- Tests found: `{report.TestsFound}`");
    sb.AppendLine($"- Runtime-ready candidates: `{report.RuntimeReadyCandidates}`");
    sb.AppendLine($"- Smoke candidates: `{report.SmokeCandidates}`");
    sb.AppendLine();
    sb.AppendLine("## Следующий шаг");
    if (best == null)
    {
        sb.AppendLine("Generated тесты не найдены. Сначала запусти migrate или verify-project, затем повтори smoke-plan.");
    }
    else
    {
        sb.AppendLine($"Возьми первый кандидат: `{Path.GetFileName(best.File)}::{best.TestName}`.");
        sb.AppendLine($"Level: **{best.ReadinessLevel}**, score: **{best.Score:0.0}**, TODO: **{best.TodoLines}**.");
        sb.AppendLine("Работай только с этим тестом/fixture, не запускай весь пакет сразу.");
        sb.AppendLine("Checklist:");
        foreach (var item in best.Checklist)
            sb.AppendLine($"- {item}");
    }
    sb.AppendLine();
    sb.AppendLine("## Ограничения");
    sb.AppendLine("- Не редактируй generated `.cs` вручную как финальное решение.");
    sb.AppendLine("- Если нужен mapping — добавляй его в adapter-config/profile.");
    sb.AppendLine("- Если runtime failure связан с generic behavior мигратора — переходи к migrator-code fix loop или классифицируй blocker по `.agent-loops/03-stop-policy.md`.");
    sb.AppendLine("- После этапа дай краткий отчёт. Если статус `CONTINUE_AUTONOMOUSLY`, продолжай без вопроса пользователю.");
    return sb.ToString();
}



// --- Report serve dashboard mode ---

static int RunReportServe(string inputPath, string outPath, string format, bool recursiveArtifacts, int port, bool staticOnly)
{
    if (!Directory.Exists(inputPath))
    {
        Console.Error.WriteLine($"Report serve expects a directory with migration artifacts: {inputPath}");
        return 1;
    }

    Directory.CreateDirectory(outPath);
    ReportServeDashboardReport dashboard;
    try
    {
        dashboard = BuildReportServeDashboardReport(inputPath, recursiveArtifacts);
    }
    catch (ArtifactLookupException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }

    var staticFiles = WriteReportServeDashboardReport(dashboard, outPath, format).ToArray();
    var evidenceZip = CreateReportServeEvidenceZip(inputPath, outPath, dashboard, staticFiles);
    dashboard = dashboard with
    {
        StaticFiles = staticFiles.Select(Path.GetFullPath).ToArray(),
        EvidenceZipPath = Path.GetFullPath(evidenceZip)
    };
    WriteReportServeDashboardReport(dashboard, outPath, format);

    Console.WriteLine("=== Report Serve Dashboard ===");
    Console.WriteLine($"Source: {inputPath}");
    Console.WriteLine($"Artifact lookup: {(recursiveArtifacts ? "recursive" : "direct-only")}");
    Console.WriteLine($"Dashboard: {Path.GetFullPath(Path.Combine(outPath, "report-dashboard.html"))}");
    Console.WriteLine($"Evidence zip: {Path.GetFullPath(evidenceZip)}");
    Console.WriteLine($"Runs compared: {dashboard.Trends.Length}");
    Console.WriteLine($"TODO groups: {dashboard.TodoExplorer.Length}, Unsupported groups: {dashboard.UnsupportedActions.Length}, Unmapped groups: {dashboard.UnmappedTargets.Length}");

    if (port > 0 && !staticOnly)
        return ServeStaticDashboard(outPath, port);

    Console.WriteLine("Static dashboard written. Pass --port 5077 to serve it locally.");
    return 0;
}

static ReportServeDashboardReport BuildReportServeDashboardReport(string artifactDir, bool recursiveArtifacts)
{
    var board = BuildMigrationBoardReportFromArtifacts(artifactDir, recursiveArtifacts);
    var missing = RequiredReportServeArtifacts()
        .Where(name => FindFirstExisting(artifactDir, name, recursiveArtifacts) == null)
        .ToArray();

    var unsupportedPath = FindFirstExisting(artifactDir, "unsupported-actions.json", recursiveArtifacts);
    var unsupported = ReadCountItems(unsupportedPath, "MethodOrSourceText", "Count", "ExampleFile", "ExampleLine");
    if (unsupported.Count == 0)
    {
        var reportPath = FindFirstExisting(artifactDir, "report.json", recursiveArtifacts);
        if (reportPath != null)
            unsupported = ReadNestedCountItems(reportPath, "TopUnsupportedActions", "MethodOrSourceText", "Count", "ExampleFile", "ExampleLine");
    }

    var unmappedPath = FindFirstExisting(artifactDir, "unmapped-targets.json", recursiveArtifacts);
    var unmapped = ReadCountItems(unmappedPath, "SourceExpression", "Usages", "ExampleFile", "ExampleLine");
    if (unmapped.Count == 0)
    {
        var reportPath = FindFirstExisting(artifactDir, "report.json", recursiveArtifacts);
        if (reportPath != null)
            unmapped = ReadNestedCountItems(reportPath, "TopUnmappedTargets", "SourceExpression", "Usages", "ExampleFile", "ExampleLine");
    }

    var runtimeFailures = ReadRuntimeFailureGroups(artifactDir, recursiveArtifacts);

    return new ReportServeDashboardReport(
        GeneratedAtUtc: DateTimeOffset.UtcNow,
        Source: Path.GetFullPath(artifactDir),
        ArtifactRoot: Path.GetFullPath(artifactDir),
        RecursiveArtifactLookup: recursiveArtifacts,
        Current: board,
        Trends: BuildReportServeRunTrends(artifactDir).ToArray(),
        TodoExplorer: BuildTodoExplorerGroups(artifactDir, recursiveArtifacts).ToArray(),
        UnsupportedActions: unsupported
            .Select(kv => new ReportServeCountItem(kv.Key, kv.Value.Count, kv.Value.File, kv.Value.Line))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray(),
        UnmappedTargets: unmapped
            .Select(kv => new ReportServeCountItem(kv.Key, kv.Value.Count, kv.Value.File, kv.Value.Line))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray(),
        RuntimeFailures: runtimeFailures,
        MissingArtifacts: missing,
        StaticFiles: Array.Empty<string>(),
        EvidenceZipPath: null);
}

static IEnumerable<string> RequiredReportServeArtifacts() => new[]
{
    "report.json",
    "project-verify-report.json",
    "explain-todo.json",
    "smoke-plan.json",
    "runtime-failure-report.json"
};

static IEnumerable<ReportServeRunTrend> BuildReportServeRunTrends(string artifactDir)
{
    var root = Directory.GetParent(Path.GetFullPath(artifactDir));
    if (root == null || !root.Exists)
        yield break;

    foreach (var dir in root.EnumerateDirectories()
        .Where(d => HasDirectArtifactEvidence(d.FullName))
        .OrderByDescending(d => d.LastWriteTimeUtc)
        .Take(20))
    {
        var summary = new ArtifactSummary();
        var reportPath = FindFirstExisting(dir.FullName, "report.json", recursiveArtifacts: false);
        var verifyPath = FindFirstExisting(dir.FullName, "verify-report.json", recursiveArtifacts: false);
        var projectVerifyPath = FindFirstExisting(dir.FullName, "project-verify-report.json", recursiveArtifacts: false);
        if (reportPath != null)
            ReadSummaryReport(reportPath, summary);
        if (verifyPath != null)
            ReadVerifyReport(verifyPath, summary);
        var projectVerify = projectVerifyPath != null ? ReadProjectVerifyReport(projectVerifyPath) : null;
        var smoke = BuildSmokePlanReportSafely(dir.FullName);
        var compileErrors = Math.Max(summary.SyntaxErrors, projectVerify?.ClassifiedDiagnostics.Count(d => d.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)) ?? 0);
        yield return new ReportServeRunTrend(
            Name: dir.Name,
            Path: dir.FullName,
            LastWriteTimeUtc: dir.LastWriteTimeUtc,
            FilesProcessed: summary.FilesProcessed,
            TestsFound: summary.TestsFound,
            GeneratedFiles: smoke?.GeneratedFiles ?? 0,
            TodoComments: summary.TodoComments,
            UnsupportedActions: summary.UnsupportedActions,
            UnmappedTargets: summary.UnmappedTargets,
            CompileErrors: compileErrors,
            ProjectVerifyStatus: projectVerify?.Status ?? summary.VerifyStatus ?? "not-run");
    }
}

static SmokePlanReport? BuildSmokePlanReportSafely(string artifactDir)
{
    try
    {
        return BuildSmokePlanReportFromArtifacts(artifactDir, recursiveArtifacts: false);
    }
    catch
    {
        return null;
    }
}

static IEnumerable<ReportServeTodoCodeGroup> BuildTodoExplorerGroups(string artifactDir, bool recursiveArtifacts)
{
    ValidateArtifactLookupRoot(artifactDir, recursiveArtifacts);
    var files = Directory.EnumerateFiles(artifactDir, "*.*", recursiveArtifacts ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
        .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    var groups = new Dictionary<string, (int Count, string File, int Line)>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in files)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file);
        }
        catch
        {
            continue;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(lines[i], @"MIGRATOR:[A-Z0-9_:-]+"))
            {
                var code = match.Value;
                if (groups.TryGetValue(code, out var existing))
                    groups[code] = (existing.Count + 1, existing.File, existing.Line);
                else
                    groups[code] = (1, file, i + 1);
            }
        }
    }

    return groups
        .Select(kv => new ReportServeTodoCodeGroup(kv.Key, kv.Value.Count, kv.Value.File, kv.Value.Line))
        .OrderByDescending(x => x.Count)
        .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
        .Take(100);
}

static RuntimeFailureGroup[] ReadRuntimeFailureGroups(string artifactDir, bool recursiveArtifacts)
{
    var path = FindFirstExisting(artifactDir, "runtime-failure-report.json", recursiveArtifacts);
    if (path == null)
        return Array.Empty<RuntimeFailureGroup>();

    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("Groups", out var groups) || groups.ValueKind != System.Text.Json.JsonValueKind.Array)
            return Array.Empty<RuntimeFailureGroup>();

        return groups.EnumerateArray()
            .Select(ReadRuntimeFailureGroup)
            .Where(g => !string.IsNullOrWhiteSpace(g.Category))
            .OrderByDescending(g => g.Count)
            .ToArray();
    }
    catch
    {
        return Array.Empty<RuntimeFailureGroup>();
    }
}

static RuntimeFailureGroup ReadRuntimeFailureGroup(System.Text.Json.JsonElement group)
{
    var examples = group.TryGetProperty("Examples", out var examplesProp) && examplesProp.ValueKind == System.Text.Json.JsonValueKind.Array
        ? examplesProp.EnumerateArray().Select(ReadRuntimeFailureObservation).ToArray()
        : Array.Empty<RuntimeFailureObservation>();

    return new RuntimeFailureGroup(
        Category: ReadString(group, "Category") ?? "unknown-runtime-failure",
        Count: ReadInt(group, "Count"),
        Severity: ReadString(group, "Severity") ?? "unknown",
        LikelyCause: ReadString(group, "LikelyCause") ?? "Runtime failure requires manual review.",
        SuggestedAction: ReadString(group, "SuggestedAction") ?? "Inspect Playwright trace/log evidence.",
        Examples: examples);
}

static RuntimeFailureObservation ReadRuntimeFailureObservation(System.Text.Json.JsonElement observation) => new(
    Category: ReadString(observation, "Category") ?? "unknown-runtime-failure",
    File: ReadString(observation, "File") ?? "",
    Line: ReadInt(observation, "Line"),
    TestName: ReadString(observation, "TestName"),
    Message: ReadString(observation, "Message") ?? "",
    Snippet: ReadString(observation, "Snippet") ?? "");

static IEnumerable<string> WriteReportServeDashboardReport(ReportServeDashboardReport report, string outPath, string format)
{
    Directory.CreateDirectory(outPath);
    var htmlPath = Path.Combine(outPath, "report-dashboard.html");
    var mdPath = Path.Combine(outPath, "report-dashboard.md");
    var jsonPath = Path.Combine(outPath, "report-dashboard.json");
    File.WriteAllText(htmlPath, WriteReportServeHtml(report));
    yield return htmlPath;

    if (format == "json" || format == "both")
    {
        File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        yield return jsonPath;
    }

    if (format == "text" || format == "both")
    {
        File.WriteAllText(mdPath, WriteReportServeMarkdown(report));
        yield return mdPath;
    }
}

static string WriteReportServeMarkdown(ReportServeDashboardReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Report Serve Dashboard");
    sb.AppendLine();
    sb.AppendLine($"- **Generated**: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss zzz}");
    sb.AppendLine($"- **Source**: `{PathRedaction.Redact(report.Source)}`");
    sb.AppendLine($"- **Artifact lookup**: `{(report.RecursiveArtifactLookup ? "recursive" : "direct-only")}`");
    sb.AppendLine($"- **Evidence zip**: `{PathRedaction.Redact(report.EvidenceZipPath ?? "not-written")}`");
    sb.AppendLine();
    sb.AppendLine("## Overview");
    sb.AppendLine("| Metric | Value |");
    sb.AppendLine("|---|---:|");
    sb.AppendLine($"| Files | {report.Current.Summary.FilesProcessed} |");
    sb.AppendLine($"| Tests | {report.Current.Summary.TestsFound} |");
    sb.AppendLine($"| Generated files | {report.Current.GeneratedFiles} |");
    sb.AppendLine($"| Syntax/compile errors | {report.Current.Summary.SyntaxErrors} |");
    sb.AppendLine($"| TODO | {report.Current.Summary.TodoComments} |");
    sb.AppendLine($"| Unsupported actions | {report.Current.Summary.UnsupportedActions} |");
    sb.AppendLine($"| Unmapped targets | {report.Current.Summary.UnmappedTargets} |");
    sb.AppendLine();
    AppendReportServeTrendMarkdown(sb, report);
    AppendReportServeCountTable(sb, "TODO explorer", "Code", report.TodoExplorer.Select(x => new ReportServeCountItem(x.Code, x.Count, x.ExampleFile, x.ExampleLine)).ToArray());
    AppendReportServeCountTable(sb, "Unsupported actions", "Action/source", report.UnsupportedActions);
    AppendReportServeCountTable(sb, "Unmapped targets", "Source expression", report.UnmappedTargets);
    sb.AppendLine("## Verify/project-verify diagnostics");
    sb.AppendLine($"- Project verify: `{report.Current.ProjectVerifyStatus ?? "not-run"}`");
    sb.AppendLine($"- Diagnostics: `{report.Current.ProjectDiagnostics}`");
    sb.AppendLine();
    sb.AppendLine("## Runtime failures");
    if (report.RuntimeFailures.Length == 0)
        sb.AppendLine("Runtime failure report not found or no groups were classified.");
    else
    {
        sb.AppendLine("| Category | Count | Severity | Suggested action |");
        sb.AppendLine("|---|---:|---|---|");
        foreach (var group in report.RuntimeFailures.Take(25))
            sb.AppendLine($"| `{EscapeMd(group.Category)}` | {group.Count} | `{EscapeMd(group.Severity)}` | {EscapeMd(group.SuggestedAction)} |");
    }
    sb.AppendLine();
    sb.AppendLine("## Missing optional artifacts");
    if (report.MissingArtifacts.Length == 0)
        sb.AppendLine("All expected dashboard artifacts were found.");
    else
        foreach (var missing in report.MissingArtifacts)
            sb.AppendLine($"- `{missing}`");
    return sb.ToString();
}

static void AppendReportServeTrendMarkdown(StringBuilder sb, ReportServeDashboardReport report)
{
    sb.AppendLine("## Quality trend");
    sb.AppendLine("| Run | Tests | Generated | TODO | Unsupported | Unmapped | Compile errors | Project verify |");
    sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
    foreach (var run in report.Trends.Take(10))
        sb.AppendLine($"| `{EscapeMd(run.Name)}` | {run.TestsFound} | {run.GeneratedFiles} | {run.TodoComments} | {run.UnsupportedActions} | {run.UnmappedTargets} | {run.CompileErrors} | `{EscapeMd(run.ProjectVerifyStatus)}` |");
    if (report.Trends.Length == 0)
        sb.AppendLine("|  | 0 | 0 | 0 | 0 | 0 | 0 | `not-run` |");
    sb.AppendLine();
}

static void AppendReportServeCountTable(StringBuilder sb, string title, string nameHeader, ReportServeCountItem[] items)
{
    sb.AppendLine($"## {title}");
    sb.AppendLine($"| # | {nameHeader} | Count | Example |");
    sb.AppendLine("|---|---|---:|---|");
    for (var i = 0; i < Math.Min(30, items.Length); i++)
    {
        var item = items[i];
        var example = string.IsNullOrWhiteSpace(item.ExampleFile) ? "" : $"`{EscapeMd(PathRedaction.Redact(item.ExampleFile))}:{item.ExampleLine}`";
        sb.AppendLine($"| {i + 1} | `{EscapeMd(item.Name)}` | {item.Count} | {example} |");
    }
    if (items.Length == 0)
        sb.AppendLine($"|  | No {EscapeMd(title.ToLowerInvariant())} found. | 0 |  |");
    sb.AppendLine();
}

static string WriteReportServeHtml(ReportServeDashboardReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("<!doctype html>");
    sb.AppendLine("<html lang=\"ru\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
    sb.AppendLine("<title>Report Serve Dashboard</title>");
    sb.AppendLine("<style>");
    sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:0;background:#f6f7fb;color:#172033}header{background:#111827;color:white;padding:24px 32px}.wrap{padding:24px 32px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px}.card{background:white;border-radius:12px;padding:16px;box-shadow:0 1px 4px #0001}.metric{font-size:28px;font-weight:700}.label{color:#667085;font-size:13px}.ok{color:#17803d}.warn{color:#b66a00}.bad{color:#b42318}.pill{display:inline-block;border-radius:999px;padding:4px 10px;font-size:12px;background:#eef2ff}.pill.ok{background:#dcfae6;color:#067647}.pill.warn{background:#fef0c7;color:#93370d}.pill.bad{background:#fee4e2;color:#b42318}table{border-collapse:collapse;width:100%;background:white;border-radius:12px;overflow:hidden;box-shadow:0 1px 4px #0001}th,td{text-align:left;padding:10px 12px;border-bottom:1px solid #eaecf0;vertical-align:top}th{background:#f2f4f7;font-size:12px;text-transform:uppercase;color:#667085}code{background:#f2f4f7;padding:2px 4px;border-radius:4px}.section{margin:28px 0 14px}.small{font-size:12px;color:#667085}.empty{color:#667085;font-style:italic}.bar{height:8px;background:#eaecf0;border-radius:999px;overflow:hidden}.bar>span{display:block;height:8px;background:#3478f6}.action{border-left:4px solid #3478f6}a{color:#155eef;text-decoration:none}");
    sb.AppendLine("</style></head><body>");
    sb.AppendLine("<header><h1>Report Serve Dashboard</h1>");
    sb.AppendLine($"<div class=\"small\">Generated: {Html(report.GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm:ss zzz"))} · Source: <code>{Html(PathRedaction.Redact(report.Source))}</code></div>");
    sb.AppendLine("</header><main class=\"wrap\">");

    sb.AppendLine("<section class=\"grid\">");
    MetricCard(sb, "Files", report.Current.Summary.FilesProcessed.ToString(), "processed", "");
    MetricCard(sb, "Tests", report.Current.Summary.TestsFound.ToString(), "found", "");
    MetricCard(sb, "Generated", report.Current.GeneratedFiles.ToString(), "files", "");
    MetricCard(sb, "TODO", report.Current.Summary.TodoComments.ToString(), "remaining", report.Current.Summary.TodoComments == 0 ? "ok" : "warn");
    MetricCard(sb, "Unsupported", report.Current.Summary.UnsupportedActions.ToString(), "actions", report.Current.Summary.UnsupportedActions == 0 ? "ok" : "warn");
    MetricCard(sb, "Unmapped", report.Current.Summary.UnmappedTargets.ToString(), "targets", report.Current.Summary.UnmappedTargets == 0 ? "ok" : "warn");
    MetricCard(sb, "Compile errors", report.Current.Summary.SyntaxErrors.ToString(), "syntax/project verify", report.Current.Summary.SyntaxErrors == 0 ? "ok" : "bad");
    MetricCard(sb, "Project verify", report.Current.ProjectVerifyStatus ?? "not-run", "status", StatusCss(report.Current.ProjectVerifyStatus));
    sb.AppendLine("</section>");

    sb.AppendLine("<h2 class=\"section\">Downloadable evidence pack</h2>");
    var evidenceName = string.IsNullOrWhiteSpace(report.EvidenceZipPath) ? "report-dashboard-evidence.zip" : Path.GetFileName(report.EvidenceZipPath);
    sb.AppendLine($"<div class=\"card\">Evidence zip: <a href=\"{Html(evidenceName)}\">{Html(evidenceName)}</a><div class=\"small\">Includes reports, dashboard files, generated migration artifacts, manifest, and checksums. Source repository files are not included unless they are generated migration artifacts.</div></div>");

    AppendReportServeTrendHtml(sb, report);
    AppendReportServeActionsHtml(sb, report);
    AppendReportServeCountHtml(sb, "TODO explorer", "Code", report.TodoExplorer.Select(x => new ReportServeCountItem(x.Code, x.Count, x.ExampleFile, x.ExampleLine)).ToArray(), "MIGRATOR:* groups found in generated files and reports.");
    AppendReportServeCountHtml(sb, "Unsupported actions", "Action/source", report.UnsupportedActions, "High-frequency unsupported actions from unsupported-actions/report artifacts.");
    AppendReportServeCountHtml(sb, "Unmapped targets", "Source expression", report.UnmappedTargets, "Source expressions that still need UiTarget/POM/profile evidence.");
    AppendReportServeRuntimeHtml(sb, report);
    AppendReportServeArtifactsHtml(sb, report);
    sb.AppendLine("</main></body></html>");
    return sb.ToString();
}

static void AppendReportServeTrendHtml(StringBuilder sb, ReportServeDashboardReport report)
{
    sb.AppendLine("<h2 class=\"section\">Quality trend</h2>");
    sb.AppendLine("<table><thead><tr><th>Run</th><th>Tests</th><th>Generated</th><th>TODO</th><th>Unsupported</th><th>Unmapped</th><th>Compile errors</th><th>Project verify</th></tr></thead><tbody>");
    if (report.Trends.Length == 0)
        sb.AppendLine("<tr><td colspan=\"8\" class=\"empty\">No comparable sibling runs found.</td></tr>");
    foreach (var run in report.Trends.Take(20))
    {
        var verifyCss = StatusCss(run.ProjectVerifyStatus);
        sb.AppendLine($"<tr><td><code>{Html(run.Name)}</code><div class=\"small\">{Html(run.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm"))} UTC</div></td><td>{run.TestsFound}</td><td>{run.GeneratedFiles}</td><td>{run.TodoComments}</td><td>{run.UnsupportedActions}</td><td>{run.UnmappedTargets}</td><td>{run.CompileErrors}</td><td><span class=\"pill {verifyCss}\">{Html(run.ProjectVerifyStatus)}</span></td></tr>");
    }
    sb.AppendLine("</tbody></table>");
}

static void AppendReportServeActionsHtml(StringBuilder sb, ReportServeDashboardReport report)
{
    sb.AppendLine("<h2 class=\"section\">Proposed next tickets</h2>");
    if (report.Current.RecommendedNextActions.Length == 0)
        sb.AppendLine("<div class=\"card empty\">No next-ticket recommendations found.</div>");
    else
    {
        sb.AppendLine("<div class=\"grid\">");
        foreach (var action in report.Current.RecommendedNextActions.Take(8))
            sb.AppendLine($"<div class=\"card action\">{Html(action)}</div>");
        sb.AppendLine("</div>");
    }
}

static void AppendReportServeCountHtml(StringBuilder sb, string title, string nameHeader, ReportServeCountItem[] items, string description)
{
    sb.AppendLine($"<h2 class=\"section\">{Html(title)}</h2>");
    sb.AppendLine($"<div class=\"small\">{Html(description)}</div>");
    sb.AppendLine($"<table><thead><tr><th>#</th><th>{Html(nameHeader)}</th><th>Count</th><th>Example</th></tr></thead><tbody>");
    if (items.Length == 0)
        sb.AppendLine("<tr><td colspan=\"4\" class=\"empty\">No items found.</td></tr>");
    for (var i = 0; i < Math.Min(50, items.Length); i++)
    {
        var item = items[i];
        var example = string.IsNullOrWhiteSpace(item.ExampleFile) ? "" : $"{PathRedaction.Redact(item.ExampleFile)}:{item.ExampleLine}";
        sb.AppendLine($"<tr><td>{i + 1}</td><td><code>{Html(item.Name)}</code></td><td>{item.Count}</td><td><code>{Html(example)}</code></td></tr>");
    }
    sb.AppendLine("</tbody></table>");
}

static void AppendReportServeRuntimeHtml(StringBuilder sb, ReportServeDashboardReport report)
{
    sb.AppendLine("<h2 class=\"section\">Runtime failures</h2>");
    sb.AppendLine("<table><thead><tr><th>Category</th><th>Count</th><th>Severity</th><th>Likely cause</th><th>Suggested action</th></tr></thead><tbody>");
    if (report.RuntimeFailures.Length == 0)
        sb.AppendLine("<tr><td colspan=\"5\" class=\"empty\">No runtime-failure-report.json found or no runtime failures classified.</td></tr>");
    foreach (var group in report.RuntimeFailures.Take(25))
    {
        var css = group.Severity.Equals("high", StringComparison.OrdinalIgnoreCase) ? "bad" : group.Severity.Equals("medium", StringComparison.OrdinalIgnoreCase) ? "warn" : "";
        sb.AppendLine($"<tr><td><code>{Html(group.Category)}</code></td><td>{group.Count}</td><td><span class=\"pill {css}\">{Html(group.Severity)}</span></td><td>{Html(group.LikelyCause)}</td><td>{Html(group.SuggestedAction)}</td></tr>");
    }
    sb.AppendLine("</tbody></table>");
}

static void AppendReportServeArtifactsHtml(StringBuilder sb, ReportServeDashboardReport report)
{
    sb.AppendLine("<h2 class=\"section\">Verify/project-verify diagnostics</h2>");
    sb.AppendLine($"<div class=\"card\">Project verify: <span class=\"pill {StatusCss(report.Current.ProjectVerifyStatus)}\">{Html(report.Current.ProjectVerifyStatus ?? "not-run")}</span> · Diagnostics: <strong>{report.Current.ProjectDiagnostics}</strong></div>");
    sb.AppendLine("<h2 class=\"section\">Linked artifacts</h2>");
    sb.AppendLine("<div class=\"card\"><ul>");
    foreach (var artifact in report.Current.Artifacts)
        sb.AppendLine($"<li><code>{Html(PathRedaction.Redact(artifact))}</code></li>");
    if (report.Current.Artifacts.Length == 0)
        sb.AppendLine("<li class=\"empty\">No linked artifacts found.</li>");
    sb.AppendLine("</ul></div>");

    sb.AppendLine("<h2 class=\"section\">Missing optional artifacts</h2>");
    sb.AppendLine("<div class=\"card\"><ul>");
    foreach (var missing in report.MissingArtifacts)
        sb.AppendLine($"<li><code>{Html(missing)}</code></li>");
    if (report.MissingArtifacts.Length == 0)
        sb.AppendLine("<li>All expected dashboard artifacts were found.</li>");
    sb.AppendLine("</ul></div>");
}

static string CreateReportServeEvidenceZip(string inputPath, string outPath, ReportServeDashboardReport report, string[] staticFiles)
{
    var zipPath = Path.Combine(outPath, "report-dashboard-evidence.zip");
    if (File.Exists(zipPath))
        File.Delete(zipPath);

    var include = new List<string>();
    include.AddRange(staticFiles.Where(File.Exists));
    include.AddRange(report.Current.Artifacts.Where(File.Exists));
    include.AddRange(FindGeneratedMigrationArtifactFiles(inputPath));
    include = include
        .Select(Path.GetFullPath)
        .Where(path => IsInsideDirectory(path, outPath) || IsInsideDirectory(path, inputPath))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .Take(500)
        .ToList();

    var manifestEntries = new List<object>();
    using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
    {
        foreach (var file in include)
        {
            var relative = IsInsideDirectory(file, outPath)
                ? Path.Combine("dashboard", SafeRelativePath(outPath, file))
                : Path.Combine("artifacts", SafeRelativePath(inputPath, file));
            archive.CreateEntryFromFile(file, NormalizeZipEntry(relative), CompressionLevel.Optimal);
            manifestEntries.Add(new
            {
                Path = NormalizeZipEntry(relative),
                Source = IsInsideDirectory(file, outPath) ? "dashboard" : "artifact",
                SizeBytes = new FileInfo(file).Length,
                Sha256 = ComputeSha256(file)
            });
        }

        var manifest = new
        {
            SchemaVersion = "report-serve-evidence/v1",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Source = PathRedaction.Redact(Path.GetFullPath(inputPath)),
            Redaction = new
            {
                AbsolutePathsRedactedInReports = true,
                ZipEntryNamesAreRelative = true,
                SourceRepositoryFilesIncluded = false,
                GeneratedMigrationArtifactsIncluded = true
            },
            Entries = manifestEntries
        };
        var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    return zipPath;
}

static IEnumerable<string> FindGeneratedMigrationArtifactFiles(string inputPath)
{
    if (!Directory.Exists(inputPath))
        yield break;

    foreach (var file in Directory.EnumerateFiles(inputPath, "*.*", SearchOption.AllDirectories)
        .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch
        {
            continue;
        }

        if (text.Contains("Generated by Migrator", StringComparison.Ordinal) || text.Contains("MIGRATOR:", StringComparison.Ordinal))
            yield return file;
    }
}

static string SafeRelativePath(string root, string file)
{
    try
    {
        return Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(file));
    }
    catch
    {
        return Path.GetFileName(file);
    }
}

static bool IsInsideDirectory(string file, string directory)
{
    var fullFile = Path.GetFullPath(file).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var fullDir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    return fullFile.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase) || string.Equals(fullFile, fullDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}

static string NormalizeZipEntry(string path) => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

static string ComputeSha256(string file)
{
    using var stream = File.OpenRead(file);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static int ServeStaticDashboard(string outPath, int port)
{
    var root = Path.GetFullPath(outPath);
    var prefix = $"http://localhost:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    try
    {
        listener.Start();
    }
    catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException)
    {
        Console.Error.WriteLine($"Could not start local report server on {prefix}: {ex.Message}");
        Console.Error.WriteLine("Static dashboard files were still written; open report-dashboard.html directly.");
        return 2;
    }

    Console.WriteLine($"Serving report dashboard at {prefix}");
    Console.WriteLine("Press Ctrl+C to stop.");
    using var stopped = new ManualResetEventSlim(false);
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        stopped.Set();
        try { listener.Stop(); } catch { }
    };

    while (listener.IsListening && !stopped.IsSet)
    {
        HttpListenerContext context;
        try
        {
            context = listener.GetContext();
        }
        catch (HttpListenerException)
        {
            break;
        }
        catch (ObjectDisposedException)
        {
            break;
        }

        _ = ThreadPool.QueueUserWorkItem(_ => ServeDashboardRequest(context, root));
    }

    return 0;
}

static void ServeDashboardRequest(HttpListenerContext context, string root)
{
    try
    {
        var requestPath = WebUtility.UrlDecode(context.Request.Url?.AbsolutePath.TrimStart('/') ?? "") ?? "";
        if (string.IsNullOrWhiteSpace(requestPath))
            requestPath = "report-dashboard.html";
        var fullPath = Path.GetFullPath(Path.Combine(root, requestPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInsideDirectory(fullPath, root) || !File.Exists(fullPath))
        {
            context.Response.StatusCode = 404;
            using var writer = new StreamWriter(context.Response.OutputStream);
            writer.Write("Not found");
            return;
        }

        context.Response.ContentType = ContentTypeFor(fullPath);
        using var file = File.OpenRead(fullPath);
        file.CopyTo(context.Response.OutputStream);
    }
    catch
    {
        if (context.Response.OutputStream.CanWrite)
            context.Response.StatusCode = 500;
    }
    finally
    {
        try { context.Response.OutputStream.Close(); } catch { }
    }
}

static string ContentTypeFor(string path)
{
    var ext = Path.GetExtension(path).ToLowerInvariant();
    return ext switch
    {
        ".html" => "text/html; charset=utf-8",
        ".htm" => "text/html; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".md" => "text/markdown; charset=utf-8",
        ".zip" => "application/zip",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        _ => "application/octet-stream"
    };
}

// --- Migration board mode ---

static int RunMigrationBoard(string inputPath, string outPath, string format, bool recursiveArtifacts)
{
    if (!Directory.Exists(inputPath))
    {
        Console.Error.WriteLine($"Migration-board mode expects a directory with migration artifacts: {inputPath}");
        return 1;
    }

    Directory.CreateDirectory(outPath);
    MigrationBoardReport report;
    try
    {
        report = BuildMigrationBoardReportFromArtifacts(inputPath, recursiveArtifacts);
    }
    catch (ArtifactLookupException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }

    WriteMigrationBoardReport(report, outPath, format);

    Console.WriteLine("=== Migration Board ===");
    Console.WriteLine($"Source: {inputPath}");
    Console.WriteLine($"Artifact lookup: {(recursiveArtifacts ? "recursive" : "direct-only")}");
    Console.WriteLine($"Files: {report.Summary.FilesProcessed}, Tests: {report.Summary.TestsFound}, TODO: {report.Summary.TodoComments}");
    Console.WriteLine($"Project verify: {report.ProjectVerifyStatus ?? "not-run"}");
    Console.WriteLine($"Quality gates: empty-tests={report.QualityGates.EmptyTestsAfterSuppression}, suppressed-side-effects={report.QualityGates.SuppressedSideEffectDependencies}, regex-like-suppressions={FormatNullableMetric(report.QualityGates.SuspiciousSuppressionPatterns)}");
    Console.WriteLine($"Smoke candidates: {report.SmokeCandidates}, Runtime-ready: {report.RuntimeReadyCandidates}");
    Console.WriteLine($"Migration board written to: {Path.GetFullPath(outPath)}");
    return 0;
}

static void WriteMigrationBoardArtifacts(string artifactDir, string outPath, string format)
{
    try
    {
        var report = BuildMigrationBoardReportFromArtifacts(artifactDir);
        if (report.Summary.FilesProcessed == 0 && report.GeneratedFiles == 0 && report.TopInsights.Length == 0 && report.TopSmokeCandidates.Length == 0)
            return;
        WriteMigrationBoardReport(report, outPath, format);
    }
    catch
    {
        // Advisory only: board generation must not break migrate/verify-project.
    }
}

static MigrationBoardReport BuildMigrationBoardReportFromArtifacts(string artifactDir, bool recursiveArtifacts = false)
{
    ValidateArtifactLookupRoot(artifactDir, recursiveArtifacts);
    var summary = new ArtifactSummary();
    var reportPath = FindFirstExisting(artifactDir, "report.json", recursiveArtifacts);
    var verifyPath = FindFirstExisting(artifactDir, "verify-report.json", recursiveArtifacts);
    var projectVerifyPath = FindFirstExisting(artifactDir, "project-verify-report.json", recursiveArtifacts);

    if (reportPath != null)
        ReadSummaryReport(reportPath, summary);
    if (verifyPath != null)
        ReadVerifyReport(verifyPath, summary);

    var projectVerify = projectVerifyPath != null ? ReadProjectVerifyReport(projectVerifyPath) : null;
    if (projectVerify != null)
    {
        summary.VerifyStatus = projectVerify.Status;
        summary.SyntaxErrors = Math.Max(summary.SyntaxErrors, projectVerify.ClassifiedDiagnostics.Count(d => d.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)));
    }

    var explain = BuildExplainTodoReportFromArtifacts(artifactDir, recursiveArtifacts);
    summary.FilesProcessed = Math.Max(summary.FilesProcessed, explain.FilesProcessed);
    summary.TestsFound = Math.Max(summary.TestsFound, explain.TestsFound);
    summary.ActionsFound = Math.Max(summary.ActionsFound, explain.ActionsFound);
    summary.SemanticActions = Math.Max(summary.SemanticActions, explain.SemanticActions);
    summary.SyntaxFallbackActions = Math.Max(summary.SyntaxFallbackActions, explain.SyntaxFallbackActions);
    summary.MappedTargets = Math.Max(summary.MappedTargets, explain.MappedTargets);
    summary.UnmappedTargets = Math.Max(summary.UnmappedTargets, explain.UnmappedTargets);
    summary.UnsupportedActions = Math.Max(summary.UnsupportedActions, explain.UnsupportedActions);
    summary.TodoComments = Math.Max(summary.TodoComments, explain.TodoComments);
    summary.SyntaxErrors = Math.Max(summary.SyntaxErrors, explain.SyntaxErrors);
    summary.VerifyStatus ??= explain.ProjectVerifyStatus;

    var smoke = BuildSmokePlanReportFromArtifacts(artifactDir, recursiveArtifacts);
    var fileCards = BuildMigrationBoardFileCards(smoke, projectVerify).ToArray();
    var topInsights = explain.Insights
        .OrderByDescending(x => x.EstimatedImpact)
        .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
        .Take(25)
        .ToArray();
    var topNormalized = explain.NormalizedRootCauses
        .OrderByDescending(x => x.Count)
        .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.GroupKey, StringComparer.OrdinalIgnoreCase)
        .Take(25)
        .ToArray();
    var topSmoke = smoke.Candidates.Take(20).ToArray();
    var artifacts = FindBoardArtifacts(artifactDir, recursiveArtifacts).ToArray();
    var artifactCandidates = recursiveArtifacts ? CollectArtifactCandidates(artifactDir).ToArray() : Array.Empty<ArtifactLookupCandidate>();
    var qualityGates = BuildMigrationQualityGates(artifactDir, recursiveArtifacts, summary, projectVerify);

    return new MigrationBoardReport(
        GeneratedAtUtc: DateTimeOffset.UtcNow,
        Source: Path.GetFullPath(artifactDir),
        ArtifactRoot: Path.GetFullPath(artifactDir),
        RecursiveArtifactLookup: recursiveArtifacts,
        ArtifactCandidates: artifactCandidates,
        Summary: summary,
        QualityGates: qualityGates,
        ProjectVerifyStatus: projectVerify?.Status ?? summary.VerifyStatus,
        ProjectDiagnostics: projectVerify?.Diagnostics.Length ?? 0,
        GeneratedFiles: smoke.GeneratedFiles,
        RuntimeReadyCandidates: smoke.RuntimeReadyCandidates,
        SmokeCandidates: smoke.SmokeCandidates,
        FileCards: fileCards,
        TopInsights: topInsights,
        TopNormalizedRootCauses: topNormalized,
        TableMappingCandidates: explain.TableMappingCandidates.Take(25).ToArray(),
        TopSmokeCandidates: topSmoke,
        RecommendedNextActions: BuildMigrationBoardNextActions(summary, explain, smoke, projectVerify, qualityGates).ToArray(),
        Artifacts: artifacts);
}

static IEnumerable<MigrationBoardFileCard> BuildMigrationBoardFileCards(SmokePlanReport smoke, ProjectVerifyReport? projectVerify)
{
    var diagnosticsByFile = BuildDiagnosticsByFile(projectVerify);
    foreach (var group in smoke.Candidates.GroupBy(c => c.File, StringComparer.OrdinalIgnoreCase))
    {
        var tests = group.ToArray();
        var best = tests.OrderByDescending(x => x.Score).FirstOrDefault();
        diagnosticsByFile.TryGetValue(Path.GetFileName(group.Key), out var diagnostics);
        diagnostics ??= Array.Empty<ProjectVerifyDiagnostic>();
        var errors = diagnostics.Count(d => d.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
        var warnings = diagnostics.Length - errors;
        var todo = tests.Sum(t => t.TodoLines);
        var activeRatio = tests.Length == 0 ? 0 : Math.Round(tests.Average(t => t.ActiveRatio), 3);
        yield return new MigrationBoardFileCard(
            File: group.Key,
            Tests: tests.Length,
            TodoLines: todo,
            CompileErrors: errors,
            CompileWarnings: warnings,
            ActiveRatio: activeRatio,
            BestScore: best?.Score ?? 0,
            BestReadinessLevel: best?.ReadinessLevel ?? "unknown",
            BestTestName: best?.TestName ?? "");
    }
}

static IEnumerable<string> FindBoardArtifacts(string artifactDir, bool recursiveArtifacts = false)
{
    var names = new[]
    {
        "report.json", "verify-report.json", "project-verify-report.json", "explain-todo.md", "explain-todo.json",
        "agent-next-task.md", "migration-quality-dashboard.md", "migration-quality-dashboard.json", "migration-quality-tickets.md",
        "source-capabilities-report.md", "source-capabilities-report.json", "target-capabilities-report.md", "target-capabilities-report.json",
        "smoke-plan.md", "smoke-plan.json", "runtime-checklist.md", "agent-runtime-next-task.md",
        "runtime-failure-report.md", "runtime-failure-report.json", "agent-runtime-failure-next-task.md",
        "report-dashboard.html", "report-dashboard.md", "report-dashboard.json",
        "unmapped-targets.json", "unsupported-actions.json", "pom-index.generated.json", "doctor-report.md", "guard-report.md", "config-validate-report.md", "config-validate-report.json"
    };

    foreach (var name in names)
    {
        var found = FindFirstExisting(artifactDir, name, recursiveArtifacts);
        if (found != null)
            yield return Path.GetFullPath(found);
    }
}

static MigrationQualityGates BuildMigrationQualityGates(string artifactDir, bool recursiveArtifacts, ArtifactSummary summary, ProjectVerifyReport? projectVerify)
{
    var compileErrors = Math.Max(summary.SyntaxErrors, projectVerify?.ClassifiedDiagnostics.Count(d => d.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)) ?? 0);
    var emptyTests = CountGeneratedMarkerOccurrences(artifactDir, "MIGRATOR:EMPTY_TEST_AFTER_SUPPRESSION");
    var suppressedSideEffects = CountGeneratedMarkerOccurrences(artifactDir, "MIGRATOR:DEPENDS_ON_SUPPRESSED_SIDE_EFFECT");
    var (suppressedPatternCount, suspiciousSuppressionCount) = ReadSuppressionMetricsFromConfigValidate(artifactDir, recursiveArtifacts);
    var projectStatus = projectVerify?.Status ?? summary.VerifyStatus ?? "not-run";

    var warnings = new List<string>();
    if (projectVerify == null)
        warnings.Add("Project verify is not-run: runtime-ready claims are not trustworthy until fresh verify-project exists.");
    if (compileErrors > 0)
        warnings.Add($"Compile/syntax errors present: {compileErrors}. Fix compile truth before runtime work.");
    if (emptyTests > 0)
        warnings.Add($"Empty tests after suppression: {emptyTests}. Treat as safety risk, not ordinary TODO.");
    if (suppressedSideEffects > 0)
        warnings.Add($"Downstream code depends on suppressed side-effects: {suppressedSideEffects}. Map upstream side-effects before running those tests.");
    if ((suspiciousSuppressionCount ?? 0) > 0)
        warnings.Add($"Regex-looking suppression patterns: {suspiciousSuppressionCount}. Run config-validate and helper-inventory before trusting suppressions.");

    return new MigrationQualityGates(
        ProjectVerifyStatus: projectStatus,
        CompileErrors: compileErrors,
        EmptyTestsAfterSuppression: emptyTests,
        SuppressedSideEffectDependencies: suppressedSideEffects,
        SuppressedMethodPatterns: suppressedPatternCount,
        SuspiciousSuppressionPatterns: suspiciousSuppressionCount,
        Warnings: warnings.ToArray());
}

static int CountGeneratedMarkerOccurrences(string artifactDir, string marker)
{
    if (!Directory.Exists(artifactDir))
        return 0;

    var count = 0;
    foreach (var file in Directory.EnumerateFiles(artifactDir, "*.cs", SearchOption.AllDirectories)
        .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                 && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
    {
        try
        {
            var text = File.ReadAllText(file);
            var index = 0;
            while ((index = text.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += marker.Length;
            }
        }
        catch
        {
            // Ignore unreadable generated file candidates; board generation is advisory.
        }
    }

    return count;
}

static (int? SuppressedMethodPatterns, int? SuspiciousSuppressionPatterns) ReadSuppressionMetricsFromConfigValidate(string artifactDir, bool recursiveArtifacts)
{
    var configValidatePath = FindFirstExisting(artifactDir, "config-validate-report.json", recursiveArtifacts);
    if (configValidatePath == null)
        return (null, null);

    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configValidatePath));
        var root = doc.RootElement;
        int? suppressed = null;
        if (root.TryGetProperty("Metrics", out var metrics) && metrics.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var metric in metrics.EnumerateArray())
            {
                var name = ReadString(metric, "Name");
                if (name != null && name.Equals("SuppressedMethodPatterns", StringComparison.OrdinalIgnoreCase))
                    suppressed = ReadInt(metric, "Value");
            }
        }

        var suspicious = 0;
        if (root.TryGetProperty("Issues", out var issues) && issues.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            suspicious = issues.EnumerateArray()
                .Count(issue => string.Equals(ReadString(issue, "Code"), "REGEX_LIKE_SUPPRESSION_PATTERN", StringComparison.OrdinalIgnoreCase));
        }

        return (suppressed, suspicious);
    }
    catch
    {
        return (null, null);
    }
}

static IEnumerable<string> BuildMigrationBoardNextActions(ArtifactSummary summary, TodoExplanationReport explain, SmokePlanReport smoke, ProjectVerifyReport? projectVerify, MigrationQualityGates qualityGates)
{
    if (projectVerify == null)
        yield return "Запусти `--mode verify-project`, чтобы получить настоящую проектную компиляцию.";
    else if (!projectVerify.Status.Equals("passed", StringComparison.OrdinalIgnoreCase))
        yield return "Сначала открой `project-verify-report.md` и разрули compile errors: runtime пока рано.";

    if (qualityGates.EmptyTestsAfterSuppression > 0 || qualityGates.SuppressedSideEffectDependencies > 0)
        yield return $"Safety-gate batch: разбери EMPTY_TEST_AFTER_SUPPRESSION={qualityGates.EmptyTestsAfterSuppression}, DEPENDS_ON_SUPPRESSED_SIDE_EFFECT={qualityGates.SuppressedSideEffectDependencies} перед расширением runtime-ready списка.";

    if ((qualityGates.SuspiciousSuppressionPatterns ?? 0) > 0)
        yield return "Запусти/проверь `--mode config-validate` и `--mode helper-inventory`: есть regex-looking suppressions, которые могут быть no-op при glob semantics.";

    if (explain.NormalizedRootCauses.Length > 0)
    {
        var normalized = explain.NormalizedRootCauses.OrderByDescending(x => x.Count).First();
        yield return $"Top normalized root cause: {normalized.DisplayName} ({normalized.Count}). {normalized.SuggestedAction}";
    }

    if (explain.TableMappingCandidates.Length > 0)
    {
        var table = explain.TableMappingCandidates.OrderByDescending(x => x.Count).First();
        yield return $"Top table/list mapping candidate: `{table.SourceRoot}` via `{table.AccessorKind}` ({table.AssertionKind}, {table.Count}). {table.SuggestedConfigHint}";
    }

    if (explain.Insights.Length > 0)
        yield return $"Следующий лучший config-шаг: {explain.NextBestAction}";

    var best = smoke.Candidates.FirstOrDefault();
    if (best != null)
        yield return $"Лучший runtime-кандидат: `{Path.GetFileName(best.File)}::{best.TestName}` ({best.ReadinessLevel}, score {best.Score:0.0}).";

    if (summary.TodoComments > 0 && explain.Insights.Length == 0)
        yield return "TODO есть, но explain-todo не нашёл root cause. Проверь smart TODO markers в generated `.cs`.";

    if (summary.TodoComments == 0 && string.Equals(projectVerify?.Status, "passed", StringComparison.OrdinalIgnoreCase))
        yield return "TODO нет и project verify зелёный: выбирай smoke-кандидат и запускай один тест изолированно.";
}

static void WriteMigrationBoardReport(MigrationBoardReport report, string outPath, string format)
{
    Directory.CreateDirectory(outPath);
    if (format == "json" || format == "both")
    {
        File.WriteAllText(Path.Combine(outPath, "migration-board.json"),
            System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    if (format == "text" || format == "both")
    {
        File.WriteAllText(Path.Combine(outPath, "migration-board.html"), WriteMigrationBoardHtml(report));
        File.WriteAllText(Path.Combine(outPath, "migration-board.md"), WriteMigrationBoardMarkdown(report));
    }
}

static string WriteMigrationBoardMarkdown(MigrationBoardReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Migration Board");
    sb.AppendLine();
    sb.AppendLine($"- **Generated**: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss zzz}");
    sb.AppendLine($"- **Source**: `{PathRedaction.Redact(report.Source)}`");
    sb.AppendLine($"- **Artifact root**: `{PathRedaction.Redact(report.ArtifactRoot)}`");
    sb.AppendLine($"- **Artifact lookup**: `{(report.RecursiveArtifactLookup ? "recursive" : "direct-only")}`");
    if (report.ArtifactCandidates.Length > 0)
        sb.AppendLine($"- **Recursive candidates**: `{report.ArtifactCandidates.Length}`");
    sb.AppendLine($"- **Project verify**: `{report.ProjectVerifyStatus ?? "not-run"}`");
    sb.AppendLine($"- **TODO**: `{report.Summary.TodoComments}`");
    sb.AppendLine($"- **Syntax/compile errors**: `{report.Summary.SyntaxErrors}`");
    sb.AppendLine($"- **Runtime-ready**: `{report.RuntimeReadyCandidates}`");
    sb.AppendLine($"- **Smoke candidates**: `{report.SmokeCandidates}`");
    sb.AppendLine();
    AppendQualityGatesMarkdown(sb, report.QualityGates);
    sb.AppendLine();
    sb.AppendLine("## Recommended next actions");
    foreach (var action in report.RecommendedNextActions)
        sb.AppendLine($"- {action}");
    if (report.RecommendedNextActions.Length == 0)
        sb.AppendLine("- Нет рекомендаций: проверь входные артефакты.");
    sb.AppendLine();
    sb.AppendLine("## Top TODO / migration insights");
    sb.AppendLine("| # | Category | Impact | Title | Suggested action |");
    sb.AppendLine("|---|---|---:|---|---|");
    for (var i = 0; i < Math.Min(20, report.TopInsights.Length); i++)
    {
        var x = report.TopInsights[i];
        sb.AppendLine($"| {i + 1} | `{EscapeMd(x.Category)}` | {x.EstimatedImpact} | {EscapeMd(x.Title)} | {EscapeMd(x.SuggestedAction)} |");
    }
    sb.AppendLine();
    sb.AppendLine("## Top normalized root causes");
    sb.AppendLine("| # | Category | Group | Count | Suggested action |");
    sb.AppendLine("|---|---|---|---:|---|");
    for (var i = 0; i < Math.Min(20, report.TopNormalizedRootCauses.Length); i++)
    {
        var x = report.TopNormalizedRootCauses[i];
        sb.AppendLine($"| {i + 1} | `{EscapeMd(x.Category)}` | {EscapeMd(x.DisplayName)} | {x.Count} | {EscapeMd(x.SuggestedAction)} |");
    }
    if (report.TopNormalizedRootCauses.Length == 0)
        sb.AppendLine("|  |  | Normalized groups not available | 0 | Run explain-todo for this concrete run directory. |");
    sb.AppendLine();
    sb.AppendLine("## Table/list mapping candidates");
    sb.AppendLine("| # | Source root | Accessor | Assertion | Count | Suggested config hint | Example |");
    sb.AppendLine("|---|---|---|---|---:|---|---|");
    for (var i = 0; i < Math.Min(20, report.TableMappingCandidates.Length); i++)
    {
        var x = report.TableMappingCandidates[i];
        var example = string.IsNullOrWhiteSpace(x.ExampleFile) ? "" : $"`{EscapeMd(PathRedaction.Redact(x.ExampleFile))}:{x.ExampleLine}`";
        sb.AppendLine($"| {i + 1} | `{EscapeMd(x.SourceRoot)}` | `{EscapeMd(x.AccessorKind)}` | `{EscapeMd(x.AssertionKind)}` | {x.Count} | {EscapeMd(x.SuggestedConfigHint)} | {example} |");
    }
    if (report.TableMappingCandidates.Length == 0)
        sb.AppendLine("|  |  |  |  | 0 | No table/list candidates inferred. |  |");
    sb.AppendLine();
    sb.AppendLine("## Runtime candidates");
    sb.AppendLine("| # | Level | Score | Test | TODO | Active | File |");
    sb.AppendLine("|---|---|---:|---|---:|---:|---|");
    for (var i = 0; i < Math.Min(20, report.TopSmokeCandidates.Length); i++)
    {
        var c = report.TopSmokeCandidates[i];
        sb.AppendLine($"| {i + 1} | {EscapeMd(c.ReadinessLevel)} | {c.Score:0.0} | `{EscapeMd(c.TestName)}` | {c.TodoLines} | {c.ActiveRatio:P0} | `{EscapeMd(PathRedaction.Redact(c.File))}` |");
    }
    return sb.ToString();
}


static void AppendQualityGatesMarkdown(StringBuilder sb, MigrationQualityGates gates)
{
    sb.AppendLine("## Quality gates");
    sb.AppendLine("| Gate | Value | Status |");
    sb.AppendLine("|---|---:|---|");
    sb.AppendLine($"| Project verify | `{EscapeMd(gates.ProjectVerifyStatus)}` | {QualityGateStatus(gates.ProjectVerifyStatus.Equals("passed", StringComparison.OrdinalIgnoreCase), gates.ProjectVerifyStatus.Equals("not-run", StringComparison.OrdinalIgnoreCase))} |");
    sb.AppendLine($"| Compile errors | {gates.CompileErrors} | {QualityGateStatus(gates.CompileErrors == 0, false)} |");
    sb.AppendLine($"| EMPTY_TEST_AFTER_SUPPRESSION | {gates.EmptyTestsAfterSuppression} | {QualityGateStatus(gates.EmptyTestsAfterSuppression == 0, false)} |");
    sb.AppendLine($"| DEPENDS_ON_SUPPRESSED_SIDE_EFFECT | {gates.SuppressedSideEffectDependencies} | {QualityGateStatus(gates.SuppressedSideEffectDependencies == 0, false)} |");
    sb.AppendLine($"| SuppressedMethodPatterns | {FormatNullableMetric(gates.SuppressedMethodPatterns)} | {(gates.SuppressedMethodPatterns.HasValue ? "info" : "not-run")} |");
    sb.AppendLine($"| Regex-looking suppressions | {FormatNullableMetric(gates.SuspiciousSuppressionPatterns)} | {QualityGateStatus((gates.SuspiciousSuppressionPatterns ?? 0) == 0, !gates.SuspiciousSuppressionPatterns.HasValue)} |");
    if (gates.Warnings.Length > 0)
    {
        sb.AppendLine();
        sb.AppendLine("### Quality gate warnings");
        foreach (var warning in gates.Warnings)
            sb.AppendLine($"- {EscapeMd(warning)}");
    }
}

static string QualityGateStatus(bool ok, bool unknown) => unknown ? "not-run" : ok ? "ok" : "risk";

static string FormatNullableMetric(int? value) => value.HasValue ? value.Value.ToString() : "not-run";

static string WriteMigrationBoardHtml(MigrationBoardReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("<!doctype html>");
    sb.AppendLine("<html lang=\"ru\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
    sb.AppendLine("<title>Migration Board</title>");
    sb.AppendLine("<style>");
    sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:0;background:#f6f7fb;color:#172033}header{background:#172033;color:white;padding:24px 32px}.wrap{padding:24px 32px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px}.card{background:white;border-radius:12px;padding:16px;box-shadow:0 1px 4px #0001}.metric{font-size:28px;font-weight:700}.label{color:#667085;font-size:13px}.ok{color:#17803d}.warn{color:#b66a00}.bad{color:#b42318}.pill{display:inline-block;border-radius:999px;padding:4px 10px;font-size:12px;background:#eef2ff}.pill.ok{background:#dcfae6;color:#067647}.pill.warn{background:#fef0c7;color:#93370d}.pill.bad{background:#fee4e2;color:#b42318}table{border-collapse:collapse;width:100%;background:white;border-radius:12px;overflow:hidden;box-shadow:0 1px 4px #0001}th,td{text-align:left;padding:10px 12px;border-bottom:1px solid #eaecf0;vertical-align:top}th{background:#f2f4f7;font-size:12px;text-transform:uppercase;color:#667085}code{background:#f2f4f7;padding:2px 4px;border-radius:4px}.section{margin:28px 0 14px}.small{font-size:12px;color:#667085}.bar{height:8px;background:#eaecf0;border-radius:999px;overflow:hidden}.bar>span{display:block;height:8px;background:#3478f6}.action{border-left:4px solid #3478f6}.empty{color:#667085;font-style:italic}</style>");
    sb.AppendLine("</head><body>");
    sb.AppendLine("<header>");
    sb.AppendLine("<h1>Migration Board</h1>");
    sb.AppendLine($"<div class=\"small\">Generated: {Html(report.GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm:ss zzz"))} · Source: <code>{Html(PathRedaction.Redact(report.Source))}</code></div>");
    sb.AppendLine("</header><main class=\"wrap\">");

    sb.AppendLine("<section class=\"grid\">");
    MetricCard(sb, "Files", report.Summary.FilesProcessed.ToString(), "processed", "");
    MetricCard(sb, "Tests", report.Summary.TestsFound.ToString(), "found", "");
    MetricCard(sb, "TODO", report.Summary.TodoComments.ToString(), "remaining comments", report.Summary.TodoComments == 0 ? "ok" : "warn");
    MetricCard(sb, "Unmapped", report.Summary.UnmappedTargets.ToString(), "targets", report.Summary.UnmappedTargets == 0 ? "ok" : "warn");
    MetricCard(sb, "Unsupported", report.Summary.UnsupportedActions.ToString(), "actions", report.Summary.UnsupportedActions == 0 ? "ok" : "warn");
    MetricCard(sb, "Compile errors", report.Summary.SyntaxErrors.ToString(), "syntax/project verify", report.Summary.SyntaxErrors == 0 ? "ok" : "bad");
    MetricCard(sb, "Project verify", report.ProjectVerifyStatus ?? "not-run", "status", StatusCss(report.ProjectVerifyStatus));
    MetricCard(sb, "Smoke", $"{report.RuntimeReadyCandidates}/{report.SmokeCandidates}", "ready / candidates", report.RuntimeReadyCandidates > 0 ? "ok" : report.SmokeCandidates > 0 ? "warn" : "");
    sb.AppendLine("</section>");

    AppendQualityGatesHtml(sb, report.QualityGates);

    sb.AppendLine("<h2 class=\"section\">Recommended next actions</h2>");
    if (report.RecommendedNextActions.Length == 0)
        sb.AppendLine("<div class=\"card empty\">No recommendations found. Check input artifacts.</div>");
    else
    {
        sb.AppendLine("<div class=\"grid\">");
        foreach (var action in report.RecommendedNextActions.Take(6))
            sb.AppendLine($"<div class=\"card action\">{Html(action)}</div>");
        sb.AppendLine("</div>");
    }

    sb.AppendLine("<h2 class=\"section\">Top TODO / migration insights</h2>");
    sb.AppendLine("<table><thead><tr><th>#</th><th>Category</th><th>Impact</th><th>Title</th><th>Suggested action</th><th>Where</th></tr></thead><tbody>");
    if (report.TopInsights.Length == 0)
        sb.AppendLine("<tr><td colspan=\"6\" class=\"empty\">No insights found.</td></tr>");
    for (var i = 0; i < Math.Min(25, report.TopInsights.Length); i++)
    {
        var x = report.TopInsights[i];
        var where = string.IsNullOrWhiteSpace(x.ExampleFile) ? "" : $"{PathRedaction.Redact(x.ExampleFile)}:{x.ExampleLine}";
        sb.AppendLine($"<tr><td>{i + 1}</td><td><span class=\"pill warn\">{Html(x.Category)}</span></td><td>{x.EstimatedImpact}</td><td>{Html(x.Title)}</td><td>{Html(x.SuggestedAction)}</td><td><code>{Html(where)}</code></td></tr>");
    }
    sb.AppendLine("</tbody></table>");

    sb.AppendLine("<h2 class=\"section\">Top normalized root causes</h2>");
    sb.AppendLine("<table><thead><tr><th>#</th><th>Category</th><th>Group</th><th>Count</th><th>Suggested action</th></tr></thead><tbody>");
    if (report.TopNormalizedRootCauses.Length == 0)
        sb.AppendLine("<tr><td colspan=\"5\" class=\"empty\">No normalized root-cause groups found.</td></tr>");
    for (var i = 0; i < Math.Min(25, report.TopNormalizedRootCauses.Length); i++)
    {
        var x = report.TopNormalizedRootCauses[i];
        sb.AppendLine($"<tr><td>{i + 1}</td><td><span class=\"pill warn\">{Html(x.Category)}</span></td><td>{Html(x.DisplayName)}</td><td>{x.Count}</td><td>{Html(x.SuggestedAction)}</td></tr>");
    }
    sb.AppendLine("</tbody></table>");

    sb.AppendLine("<h2 class=\"section\">Table/list mapping candidates</h2>");
    sb.AppendLine("<table><thead><tr><th>#</th><th>Source root</th><th>Accessor</th><th>Assertion</th><th>Count</th><th>Suggested config hint</th><th>Example</th></tr></thead><tbody>");
    if (report.TableMappingCandidates.Length == 0)
        sb.AppendLine("<tr><td colspan=\"7\" class=\"empty\">No table/list mapping candidates inferred.</td></tr>");
    for (var i = 0; i < Math.Min(25, report.TableMappingCandidates.Length); i++)
    {
        var x = report.TableMappingCandidates[i];
        var example = string.IsNullOrWhiteSpace(x.ExampleFile) ? "" : $"{PathRedaction.Redact(x.ExampleFile)}:{x.ExampleLine}";
        sb.AppendLine($"<tr><td>{i + 1}</td><td><code>{Html(x.SourceRoot)}</code></td><td><code>{Html(x.AccessorKind)}</code></td><td><code>{Html(x.AssertionKind)}</code></td><td>{x.Count}</td><td>{Html(x.SuggestedConfigHint)}</td><td><code>{Html(example)}</code></td></tr>");
    }
    sb.AppendLine("</tbody></table>");

    sb.AppendLine("<h2 class=\"section\">Runtime candidates</h2>");
    sb.AppendLine("<table><thead><tr><th>#</th><th>Level</th><th>Score</th><th>Test</th><th>Active</th><th>TODO</th><th>Compile</th><th>File</th></tr></thead><tbody>");
    if (report.TopSmokeCandidates.Length == 0)
        sb.AppendLine("<tr><td colspan=\"8\" class=\"empty\">No generated tests found.</td></tr>");
    for (var i = 0; i < Math.Min(25, report.TopSmokeCandidates.Length); i++)
    {
        var c = report.TopSmokeCandidates[i];
        var levelCss = c.ReadinessLevel.StartsWith("Level 5", StringComparison.OrdinalIgnoreCase) ? "ok" : c.ReadinessLevel.StartsWith("Level 4", StringComparison.OrdinalIgnoreCase) ? "warn" : "";
        sb.AppendLine($"<tr><td>{i + 1}</td><td><span class=\"pill {levelCss}\">{Html(c.ReadinessLevel)}</span></td><td>{c.Score:0.0}</td><td><code>{Html(c.TestName)}</code></td><td><div class=\"bar\" title=\"{c.ActiveRatio:P0}\"><span style=\"width:{Math.Round(c.ActiveRatio * 100)}%\"></span></div><span class=\"small\">{c.ActiveRatio:P0}</span></td><td>{c.TodoLines}</td><td>{c.CompileErrors}</td><td><code>{Html(PathRedaction.Redact(c.File))}:{c.StartLine}</code></td></tr>");
    }
    sb.AppendLine("</tbody></table>");

    sb.AppendLine("<h2 class=\"section\">Files</h2>");
    sb.AppendLine("<table><thead><tr><th>File</th><th>Tests</th><th>Active avg</th><th>TODO</th><th>Compile errors</th><th>Best candidate</th></tr></thead><tbody>");
    if (report.FileCards.Length == 0)
        sb.AppendLine("<tr><td colspan=\"6\" class=\"empty\">No file cards found.</td></tr>");
    foreach (var f in report.FileCards.OrderByDescending(x => x.BestScore).ThenBy(x => x.TodoLines).Take(50))
    {
        sb.AppendLine($"<tr><td><code>{Html(PathRedaction.Redact(f.File))}</code></td><td>{f.Tests}</td><td>{f.ActiveRatio:P0}</td><td>{f.TodoLines}</td><td>{f.CompileErrors}</td><td>{Html(f.BestTestName)} <span class=\"small\">{Html(f.BestReadinessLevel)}</span></td></tr>");
    }
    sb.AppendLine("</tbody></table>");

    sb.AppendLine("<h2 class=\"section\">Linked artifacts</h2>");
    sb.AppendLine("<div class=\"card\"><ul>");
    foreach (var artifact in report.Artifacts)
        sb.AppendLine($"<li><code>{Html(PathRedaction.Redact(artifact))}</code></li>");
    if (report.Artifacts.Length == 0)
        sb.AppendLine("<li class=\"empty\">No related artifacts found.</li>");
    sb.AppendLine("</ul></div>");

    sb.AppendLine("</main></body></html>");
    return sb.ToString();
}

static void MetricCard(StringBuilder sb, string label, string value, string hint, string css)
{
    sb.AppendLine($"<div class=\"card\"><div class=\"label\">{Html(label)}</div><div class=\"metric {Html(css)}\">{Html(value)}</div><div class=\"small\">{Html(hint)}</div></div>");
}

static string StatusCss(string? status)
{
    if (string.Equals(status, "passed", StringComparison.OrdinalIgnoreCase)) return "ok";
    if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)) return "bad";
    if (string.IsNullOrWhiteSpace(status) || status.Equals("not-run", StringComparison.OrdinalIgnoreCase)) return "warn";
    return "";
}

static string Html(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "");

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

static void WriteScaffoldReport(ScaffoldResult result, string outPath, string format)
{
    Directory.CreateDirectory(outPath);

    var reportObject = new
    {
        GeneratedBy = "Migrator scaffold",
        Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        Status = result.Status,
        OutputPath = result.OutputPath,
        FilesCreated = result.CreatedFiles.Length,
        CreatedFiles = result.CreatedFiles,
        SkippedFiles = result.SkippedFiles,
        Warnings = result.Warnings,
        NextSteps = result.NextSteps,
        CompileOnly = true,
        RuntimePassClaimed = false
    };

    if (format != "text")
    {
        var jsonPath = Path.Combine(outPath, "scaffold-report.json");
        File.WriteAllText(jsonPath, System.Text.Json.JsonSerializer.Serialize(reportObject, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Scaffold report: {jsonPath}");
    }

    if (format != "json")
    {
        var mdPath = Path.Combine(outPath, "scaffold-report.md");
        var md = new System.Text.StringBuilder();
        md.AppendLine($"# Scaffold Report");
        md.AppendLine();
        md.AppendLine($"- **Generated**: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}");
        md.AppendLine($"- **Status**: {result.Status}");
        md.AppendLine($"- **Output**: `{result.OutputPath}`");
        md.AppendLine();
        md.AppendLine("## Files Created");
        md.AppendLine();
        foreach (var f in result.CreatedFiles)
        {
            md.AppendLine($"- `{f}`");
        }
        if (result.Warnings.Length > 0)
        {
            md.AppendLine();
            md.AppendLine("## Warnings");
            md.AppendLine();
            foreach (var w in result.Warnings)
            {
                md.AppendLine($"- {w}");
            }
        }
        md.AppendLine();
        md.AppendLine("## Next Steps");
        md.AppendLine();
        foreach (var s in result.NextSteps)
        {
            md.AppendLine($"1. {s}");
        }
        File.WriteAllText(mdPath, md.ToString());
        Console.WriteLine($"Scaffold report: {mdPath}");
    }
}










































static int RunPropose(string inputPath, string outPath, ProjectAdapterConfig? existingConfig, string format)
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

    // Existing config was already loaded/merged by CLI before entering propose mode.

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
    var writeMd = format == "text" || format == "both";

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

static int RunDiscoverTarget(string inputPath, string outPath, string? configPath, string format)
{
    if (!Directory.Exists(inputPath))
    {
        Console.Error.WriteLine($"Target project directory not found: {inputPath}");
        return 2;
    }

    Console.WriteLine($"Scanning target project: {inputPath}");

    var discovery = new TargetDiscovery(inputPath);
    TargetInventory inventory;
    try
    {
        inventory = discovery.Scan();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Discovery failed: {ex.Message}");
        return 1;
    }

    // Check for .csproj
    var csprojCount = Directory.GetFiles(inputPath, "*.csproj", SearchOption.AllDirectories).Length;
    if (csprojCount == 0)
    {
        Console.WriteLine("Warning: No .csproj file found. Framework detection may be incomplete.");
    }

    // Write output
    Directory.CreateDirectory(outPath);

    var writeJson = format == "json" || format == "both";
    var writeMd = format == "text" || format == "both";

    // target-inventory.json
    if (writeJson)
    {
        var jsonPath = Path.Combine(outPath, "target-inventory.json");
        File.WriteAllText(jsonPath, DiscoveryWriter.ToInventoryJson(inventory));
        Console.WriteLine($"Written: {jsonPath}");
    }

    // target-style-notes.md
    if (writeMd)
    {
        var mdPath = Path.Combine(outPath, "target-style-notes.md");
        File.WriteAllText(mdPath, DiscoveryWriter.ToStyleNotes(inventory));
        Console.WriteLine($"Written: {mdPath}");
    }

    // adapter-config.draft.json (always write, regardless of format)
    {
        var draftPath = Path.Combine(outPath, "adapter-config.draft.json");
        File.WriteAllText(draftPath, DiscoveryWriter.ToAdapterConfigDraft(inventory));
        Console.WriteLine($"Written: {draftPath}");
    }

    // discovery-warnings.txt (always write)
    {
        var warningsPath = Path.Combine(outPath, "discovery-warnings.txt");
        File.WriteAllText(warningsPath, DiscoveryWriter.ToWarningsText(inventory));
        Console.WriteLine($"Written: {warningsPath}");
    }

    // Console summary
    Console.WriteLine();
    Console.WriteLine("=== Discovery Summary ===");
    Console.WriteLine($"Framework: {string.Join(", ", inventory.DetectedFrameworks.Select(f => $"{f.Name} ({f.Confidence})"))}");
    Console.WriteLine($"Base classes: {string.Join(", ", inventory.DetectedTestHosts.Select(h => $"{h.BaseClass} ({h.Occurrences} occurrences)"))}");
    Console.WriteLine($"Locator attributes: {string.Join(", ", inventory.DetectedLocatorAttributes.Take(5).Select(a => $"{a.Attribute} ({a.Occurrences})"))}");
    Console.WriteLine($"Navigation patterns: {inventory.DetectedNavigationPatterns.Count}");
    Console.WriteLine($"Auth patterns: {inventory.DetectedAuthPatterns.Count}");
    Console.WriteLine($"Helper methods: {inventory.DetectedHelperMethods.Count}");
    Console.WriteLine($"Warnings: {inventory.Warnings.Count}");
    Console.WriteLine($"Redactions: {inventory.RedactionCount}");

    return 0;
}


// --- POM index mode ---

static int RunIndexPom(string inputPath, string outPath, string format)
{
    if (!Directory.Exists(inputPath) && !File.Exists(inputPath))
    {
        Console.Error.WriteLine($"POM index input not found: {inputPath}");
        return 2;
    }

    var inputBaseDir = Directory.Exists(inputPath)
        ? inputPath
        : Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Directory.GetCurrentDirectory();

    var files = File.Exists(inputPath)
        ? new[] { inputPath }
        : Directory.GetFiles(inputPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsGeneratedOrBuildArtifact(f))
            .ToArray();

    Directory.CreateDirectory(outPath);

    var facts = new List<PomFact>();
    var usages = new Dictionary<string, PomUsageCandidate>(StringComparer.OrdinalIgnoreCase);

    foreach (var file in files)
    {
        var text = File.ReadAllText(file);
        var relativeFile = Path.GetRelativePath(inputBaseDir, file);
        var className = FindFirstMatch(text, @"\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)") ?? Path.GetFileNameWithoutExtension(file);

        facts.AddRange(ExtractPomFacts(text, relativeFile, className));
        foreach (var usage in ExtractPomUsages(text, relativeFile))
        {
            if (!usages.TryGetValue(usage.SourceExpression, out var existing))
                usages[usage.SourceExpression] = usage;
            else
                usages[usage.SourceExpression] = existing with { Usages = existing.Usages + usage.Usages };
        }
    }

    var factExpressions = new HashSet<string>(facts.Select(f => f.SourceExpression), StringComparer.OrdinalIgnoreCase);
    var inferred = usages.Values
        .Where(u => !factExpressions.Contains(u.SourceExpression))
        .OrderByDescending(u => u.Usages)
        .ThenBy(u => u.SourceExpression, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var index = new PomIndexReport(
        GeneratedAtUtc: DateTimeOffset.UtcNow,
        InputPath: Path.GetFullPath(inputPath),
        FilesScanned: files.Length,
        Facts: facts.OrderBy(f => f.SourceExpression, StringComparer.OrdinalIgnoreCase).ToArray(),
        InferredCandidates: inferred,
        Warnings: BuildPomIndexWarnings(facts, inferred));

    var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };

    if (format == "json" || format == "both")
    {
        File.WriteAllText(Path.Combine(outPath, "pom-index.generated.json"), System.Text.Json.JsonSerializer.Serialize(index, jsonOptions));
        File.WriteAllText(Path.Combine(outPath, "inferred-pom-candidates.json"), System.Text.Json.JsonSerializer.Serialize(inferred, jsonOptions));
    }

    if (format == "text" || format == "both")
    {
        File.WriteAllText(Path.Combine(outPath, "pom-index.generated.md"), WritePomIndexMarkdown(index));
    }

    File.WriteAllText(Path.Combine(outPath, "adapter-config.pom-draft.json"), WritePomAdapterDraft(index));

    Console.WriteLine("=== POM Index ===");
    Console.WriteLine($"Files scanned: {files.Length}");
    Console.WriteLine($"POM facts: {facts.Count}");
    Console.WriteLine($"Inferred candidates requiring review: {inferred.Length}");
    Console.WriteLine($"Written to: {Path.GetFullPath(outPath)}");
    Console.WriteLine("Important: adapter-config.pom-draft.json is review-only. Do not auto-merge inferred candidates without source truth.");

    return 0;
}

static bool IsGeneratedOrBuildArtifact(string path)
{
    var normalized = path.Replace('\\', '/');
    return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("/generated/", StringComparison.OrdinalIgnoreCase)
        || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
        || normalized.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase);
}

static IEnumerable<PomFact> ExtractPomFacts(string text, string file, string className)
{
    var facts = new List<PomFact>();
    var lines = text.Replace("\r\n", "\n").Split('\n');

    // Properties/methods returning a wrapper initialized with By.CssSelector/By.XPath/By.Id/etc.
    var propertyRegex = new System.Text.RegularExpressions.Regex(
        """
        (?<type>[A-Za-z_][A-Za-z0-9_<>]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=>|\{\s*get\s*\{\s*return)\s*new\s+[A-Za-z_][A-Za-z0-9_<>]*\s*\([^;\n]*?By\.(?<by>CssSelector|XPath|Id|Name|ClassName|TagName)\s*\(\s*@?(?<quote>["'])(?<selector>.*?)(?<!\\)\k<quote>
        """,
        System.Text.RegularExpressions.RegexOptions.IgnorePatternWhitespace | System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.Compiled);

    foreach (System.Text.RegularExpressions.Match m in propertyRegex.Matches(text))
    {
        var name = m.Groups["name"].Value;
        var selector = m.Groups["selector"].Value;
        var by = m.Groups["by"].Value;
        facts.Add(new PomFact(
            SourceExpression: $"{className}.{name}",
            OwnerType: className,
            MemberName: name,
            MemberKind: "PropertyOrMethod",
            Selector: selector,
            SelectorKind: by,
            TargetKindSuggestion: SuggestTargetKind(selector, by),
            TargetExpressionSuggestion: SuggestTargetExpressionFromSelector(selector, name),
            SourceFile: file,
            SourceLine: GetLineNumber(text, m.Index),
            Confidence: "high",
            RequiresReview: false,
            Notes: "Found explicit Selenium By selector in POM."));
    }

    // Common data-tid/data-test constants or expression-bodied properties returning string selectors.
    var tidRegex = new System.Text.RegularExpressions.Regex(
        """
        (?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=>|=)\s*@?(?<quote>["'])(?<selector>\[[^\]\n]*(?:data-tid|data-test|data-testid|data-test-id)[^\]\n]*\])\k<quote>
        """,
        System.Text.RegularExpressions.RegexOptions.IgnorePatternWhitespace | System.Text.RegularExpressions.RegexOptions.Compiled);

    foreach (System.Text.RegularExpressions.Match m in tidRegex.Matches(text))
    {
        var name = m.Groups["name"].Value;
        var selector = m.Groups["selector"].Value;
        if (facts.Any(f => f.OwnerType == className && f.MemberName == name && f.Selector == selector))
            continue;
        facts.Add(new PomFact(
            SourceExpression: $"{className}.{name}",
            OwnerType: className,
            MemberName: name,
            MemberKind: "SelectorConstantOrProperty",
            Selector: selector,
            SelectorKind: "CssSelector",
            TargetKindSuggestion: SuggestTargetKind(selector, "CssSelector"),
            TargetExpressionSuggestion: SuggestTargetExpressionFromSelector(selector, name),
            SourceFile: file,
            SourceLine: GetLineNumber(text, m.Index),
            Confidence: "medium",
            RequiresReview: true,
            Notes: "Found selector-like string. Review whether it is a POM target or helper constant."));
    }

    return facts;
}

static IEnumerable<PomUsageCandidate> ExtractPomUsages(string text, string file)
{
    var candidates = new Dictionary<string, PomUsageCandidate>(StringComparer.OrdinalIgnoreCase);
    var usageRegex = new System.Text.RegularExpressions.Regex(
        @"\b(?<root>page|pagef|lightbox|modal|dialog|popup|[a-z][A-Za-z0-9_]*Page)\.(?<member>[A-Z][A-Za-z0-9_]*)\b",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    foreach (System.Text.RegularExpressions.Match m in usageRegex.Matches(StripCommentsAndStringsForPomUsage(text)))
    {
        var source = $"{m.Groups["root"].Value}.{m.Groups["member"].Value}";
        if (!candidates.TryGetValue(source, out var existing))
        {
            candidates[source] = new PomUsageCandidate(
                SourceExpression: source,
                SuggestedTargetExpression: m.Groups["member"].Value,
                SuggestedTargetKind: "TestId",
                Usages: 1,
                ExampleFile: file,
                ExampleLine: GetLineNumber(text, m.Index),
                Confidence: "low",
                RequiresSourceTruth: true,
                Notes: "No explicit POM selector fact found in scanned files. This is an inferred candidate, not source truth.");
        }
        else
        {
            candidates[source] = existing with { Usages = existing.Usages + 1 };
        }
    }

    return candidates.Values;
}

static int GetLineNumber(string text, int index)
{
    var line = 1;
    for (var i = 0; i < index && i < text.Length; i++)
    {
        if (text[i] == '\n')
            line++;
    }

    return line;
}

static string StripCommentsAndStringsForPomUsage(string text)
{
    var sb = new System.Text.StringBuilder(text);
    bool inString = false;
    char quote = '\0';
    bool inLineComment = false;
    bool inBlockComment = false;

    for (int i = 0; i < sb.Length; i++)
    {
        var c = sb[i];
        var next = i + 1 < sb.Length ? sb[i + 1] : '\0';

        if (inLineComment)
        {
            if (c == '\n') inLineComment = false;
            else sb[i] = ' ';
            continue;
        }

        if (inBlockComment)
        {
            if (c == '*' && next == '/')
            {
                sb[i] = ' ';
                sb[i + 1] = ' ';
                i++;
                inBlockComment = false;
            }
            else if (c != '\n') sb[i] = ' ';
            continue;
        }

        if (inString)
        {
            if (c == quote && (i == 0 || text[i - 1] != '\\'))
                inString = false;
            if (c != '\n') sb[i] = ' ';
            continue;
        }

        if (c == '/' && next == '/')
        {
            sb[i] = ' ';
            sb[i + 1] = ' ';
            i++;
            inLineComment = true;
            continue;
        }

        if (c == '/' && next == '*')
        {
            sb[i] = ' ';
            sb[i + 1] = ' ';
            i++;
            inBlockComment = true;
            continue;
        }

        if (c == '\"' || c == '\'')
        {
            quote = c;
            sb[i] = ' ';
            inString = true;
        }
    }
    return sb.ToString();
}

static string? FindFirstMatch(string text, string pattern)
{
    var m = System.Text.RegularExpressions.Regex.Match(text, pattern);
    return m.Success ? m.Groups["name"].Value : null;
}

static string SuggestTargetKind(string selector, string selectorKind)
{
    if (selectorKind == "XPath") return "Css";
    if (selector.Contains("data-tid", StringComparison.OrdinalIgnoreCase)
        || selector.Contains("data-test", StringComparison.OrdinalIgnoreCase)
        || selector.Contains("data-testid", StringComparison.OrdinalIgnoreCase)
        || selector.Contains("data-test-id", StringComparison.OrdinalIgnoreCase))
        return "TestId";
    return selectorKind == "Id" ? "TestId" : "Css";
}

static string SuggestTargetExpressionFromSelector(string selector, string fallback)
{
    var m = System.Text.RegularExpressions.Regex.Match(selector, """data-(?:tid|test|testid|test-id)['"\]\s]*[=]['"\s]*(?<value>[^'"\]]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    if (m.Success) return m.Groups["value"].Value;

    m = System.Text.RegularExpressions.Regex.Match(selector, @"#(?<id>[A-Za-z_][A-Za-z0-9_-]*)");
    if (m.Success) return m.Groups["id"].Value;

    return fallback;
}

static string[] BuildPomIndexWarnings(IReadOnlyList<PomFact> facts, IReadOnlyList<PomUsageCandidate> inferred)
{
    var warnings = new List<string>();
    if (facts.Count == 0)
        warnings.Add("No explicit Selenium By selector facts were found. Check input path: it may point only to tests without PageObjects.");
    if (inferred.Count > 0)
        warnings.Add($"{inferred.Count} inferred POM candidates were found without source-truth selectors. They require human/developer review before becoming adapter mappings.");
    return warnings.ToArray();
}

static string WritePomIndexMarkdown(PomIndexReport index)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("# POM index generated report");
    sb.AppendLine();
    sb.AppendLine($"Input: `{index.InputPath}`");
    sb.AppendLine($"Files scanned: {index.FilesScanned}");
    sb.AppendLine($"Facts found: {index.Facts.Length}");
    sb.AppendLine($"Inferred candidates requiring review: {index.InferredCandidates.Length}");
    sb.AppendLine();

    if (index.Warnings.Length > 0)
    {
        sb.AppendLine("## Warnings");
        foreach (var w in index.Warnings)
            sb.AppendLine($"- {w}");
        sb.AppendLine();
    }

    sb.AppendLine("## Source-truth POM facts");
    sb.AppendLine("| Source | Selector | Kind | File:Line | Confidence |");
    sb.AppendLine("|---|---|---|---|---|");
    foreach (var f in index.Facts.Take(200))
        sb.AppendLine($"| `{f.SourceExpression}` | `{f.Selector}` | {f.TargetKindSuggestion} | `{f.SourceFile}:{f.SourceLine}` | {f.Confidence} |");
    if (index.Facts.Length > 200)
        sb.AppendLine($"| ... | ... | ... | ... | {index.Facts.Length - 200} more |");
    sb.AppendLine();

    sb.AppendLine("## Inferred candidates, not source truth");
    sb.AppendLine("| Source | Suggested target | Usages | Example | Required action |");
    sb.AppendLine("|---|---|---:|---|---|");
    foreach (var c in index.InferredCandidates.Take(200))
        sb.AppendLine($"| `{c.SourceExpression}` | `{c.SuggestedTargetExpression}` | {c.Usages} | `{c.ExampleFile}:{c.ExampleLine}` | Find POM/source truth or ask developer |");
    if (index.InferredCandidates.Length > 200)
        sb.AppendLine($"| ... | ... | ... | ... | {index.InferredCandidates.Length - 200} more |");

    return sb.ToString();
}

static string WritePomAdapterDraft(PomIndexReport index)
{
    var highConfidence = index.Facts
        .Where(f => !f.RequiresReview && !string.IsNullOrWhiteSpace(f.TargetExpressionSuggestion))
        .Select(f => new
        {
            f.SourceExpression,
            TargetExpression = f.TargetExpressionSuggestion,
            TargetKind = f.TargetKindSuggestion,
            SourceTruth = $"{f.SourceFile}:{f.SourceLine}"
        })
        .ToArray();

    var reviewOnly = index.InferredCandidates
        .Take(200)
        .Select(c => new
        {
            c.SourceExpression,
            TargetExpression = c.SuggestedTargetExpression,
            TargetKind = c.SuggestedTargetKind,
            c.Usages,
            c.ExampleFile,
            c.ExampleLine,
            c.RequiresSourceTruth,
            c.Notes
        })
        .ToArray();

    var draft = new
    {
        Comment = "Review-only POM draft. Merge high-confidence UiTargets manually. InferredCandidates are NOT source truth and must not be auto-applied.",
        UiTargets = highConfidence,
        InferredCandidates = reviewOnly
    };
    return System.Text.Json.JsonSerializer.Serialize(draft, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}


// --- Bootstrap project mode ---

static int RunBootstrapProject(string inputPath, string outPath, string format)
{
    Directory.CreateDirectory(outPath);
    var profilesDir = Path.Combine(outPath, "profiles");
    var projectsDir = Path.Combine(profilesDir, "projects");
    Directory.CreateDirectory(profilesDir);
    Directory.CreateDirectory(projectsDir);

    var inputFullPath = string.IsNullOrWhiteSpace(inputPath)
        ? Directory.GetCurrentDirectory()
        : Path.GetFullPath(inputPath);
    var projectName = Directory.Exists(inputFullPath)
        ? new DirectoryInfo(inputFullPath).Name
        : Path.GetFileNameWithoutExtension(inputFullPath);
    if (string.IsNullOrWhiteSpace(projectName))
        projectName = "project";

    var safeProjectName = MakeSafeProfileName(projectName);
    var nearestProject = Directory.Exists(inputFullPath) || File.Exists(inputFullPath)
        ? FindNearestCsproj(inputFullPath)
        : null;

    var baseConfig = new ProjectAdapterConfig(
        SourceProjectName: "Infrastructure Selenium POM Base",
        UiTargets: Array.Empty<UiTargetMapping>(),
        PageObjects: Array.Empty<PageObjectMapping>(),
        Methods: Array.Empty<MethodMapping>(),
        LocatorSettings: new LocatorSettings("data-tid", new[] { "data-tid", "data-test", "data-testid" }),
        SourceOnlyIdentifiers: new[] { "page", "pagef", "lightbox", "modal", "dialog", "popup", "Driver", "WebDriver" },
        TargetKnownTypes: Array.Empty<string>(),
        TargetKnownIdentifiers: Array.Empty<string>(),
        Verification: new VerificationConfig
        {
            AutoDiscoverNearestProject = true,
            AutoDiscoverProjectReferences = true,
            AutoDiscoverBuildFiles = true,
            AutoDiscoverPackageReferences = false,
            Configuration = "Debug"
        });

    var projectConfig = new ProjectAdapterConfig(
        SourceProjectName: projectName,
        UiTargets: Array.Empty<UiTargetMapping>(),
        PageObjects: Array.Empty<PageObjectMapping>(),
        Methods: Array.Empty<MethodMapping>(),
        TargetKnownTypes: Array.Empty<string>(),
        TargetKnownIdentifiers: Array.Empty<string>(),
        Verification: new VerificationConfig
        {
            BaseDirectory = nearestProject != null ? FindRepoRootForVerification(nearestProject) : null,
            BuildWorkingDirectory = nearestProject != null ? FindRepoRootForVerification(nearestProject) : null,
            ProjectReferences = nearestProject != null ? new[] { nearestProject } : Array.Empty<string>(),
            AutoDiscoverNearestProject = nearestProject == null,
            AutoDiscoverProjectReferences = true,
            AutoDiscoverBuildFiles = true,
            AutoDiscoverPackageReferences = false,
            Configuration = "Debug"
        });

    var baseConfigPath = Path.Combine(profilesDir, "infrastructure-base.adapter.json");
    var projectConfigPath = Path.Combine(projectsDir, $"{safeProjectName}.adapter.json");
    var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
    File.WriteAllText(baseConfigPath, System.Text.Json.JsonSerializer.Serialize(baseConfig, jsonOptions));
    File.WriteAllText(projectConfigPath, System.Text.Json.JsonSerializer.Serialize(projectConfig, jsonOptions));

    var planPath = Path.Combine(outPath, "migration-profile-plan.md");
    File.WriteAllText(planPath, BuildBootstrapProjectPlan(projectName, inputFullPath, baseConfigPath, projectConfigPath, nearestProject));

    var nextTaskPath = Path.Combine(outPath, "agent-next-task.md");
    File.WriteAllText(nextTaskPath, BuildBootstrapAgentNextTask(projectName, inputFullPath, baseConfigPath, projectConfigPath));

    var report = new BootstrapProjectReport(
        DateTimeOffset.UtcNow,
        projectName,
        inputFullPath,
        baseConfigPath,
        projectConfigPath,
        nearestProject,
        new[]
        {
            "Review generated profile layers before use.",
            "Move reusable rules to infrastructure-base.adapter.json.",
            "Keep project-specific selectors and exceptions in profiles/projects/*.adapter.json."
        });

    if (format == "json" || format == "both")
        File.WriteAllText(Path.Combine(outPath, "bootstrap-project-report.json"), System.Text.Json.JsonSerializer.Serialize(report, jsonOptions));
    if (format == "text" || format == "both")
        File.WriteAllText(Path.Combine(outPath, "bootstrap-project-report.md"), BuildBootstrapProjectReport(report));

    Console.WriteLine("=== Bootstrap Project ===");
    Console.WriteLine($"Input: {inputFullPath}");
    Console.WriteLine($"Base profile: {baseConfigPath}");
    Console.WriteLine($"Project profile: {projectConfigPath}");
    Console.WriteLine($"Plan: {planPath}");
    Console.WriteLine($"Agent task: {nextTaskPath}");
    return 0;
}

static string MakeSafeProfileName(string name)
{
    var chars = name.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? char.ToLowerInvariant(ch) : '-').ToArray();
    var safe = new string(chars).Trim('-');
    return string.IsNullOrWhiteSpace(safe) ? "project" : safe;
}

static string BuildBootstrapProjectPlan(string projectName, string inputPath, string baseConfigPath, string projectConfigPath, string? nearestProject)
{
    var safeProjectName = MakeSafeProfileName(projectName);
    var sb = new StringBuilder();
    sb.AppendLine($"# Migration profile bootstrap: {projectName}");
    sb.AppendLine();
    sb.AppendLine($"Input: `{inputPath}`");
    if (!string.IsNullOrWhiteSpace(nearestProject))
        sb.AppendLine($"Nearest .csproj: `{nearestProject}`");
    else
        sb.AppendLine("Nearest .csproj: not found, verify-project will use AutoDiscoverNearestProject.");
    sb.AppendLine();
    sb.AppendLine("## Generated profile layers");
    sb.AppendLine($"1. Base profile: `{baseConfigPath}`");
    sb.AppendLine($"2. Project profile: `{projectConfigPath}`");
    sb.AppendLine();
    sb.AppendLine("Use them left-to-right:");
    sb.AppendLine();
    sb.AppendLine("```powershell");
    sb.AppendLine($"dotnet run --project .\\Migrator.Cli -- --mode migrate --input \"{inputPath}\" --config \"{baseConfigPath}\" --config \"{projectConfigPath}\" --out \"{safeProjectName}-migrate\" --format both");
    sb.AppendLine("```");
    sb.AppendLine();
    sb.AppendLine("## What goes where");
    sb.AppendLine("- `infrastructure-base.adapter.json`: reusable wrappers, common source-only rules, target-known symbols, generic UI/table/pagination mappings.");
    sb.AppendLine("- project adapter: concrete PageObject mappings, local selectors, project-specific navigation, local Verification references.");
    sb.AppendLine();
    sb.AppendLine("## Recommended first run");
    sb.AppendLine("1. Run `index-pom` on the Selenium project/PageObject directory.");
    sb.AppendLine("2. Move high-confidence reusable mappings to the base profile.");
    sb.AppendLine("3. Move project-only mappings to the project profile.");
    sb.AppendLine("4. Run `config-validate` with both `--config` layers.");
    sb.AppendLine("5. Run `migrate` and `verify-project` with both layers.");
    return sb.ToString();
}

static string BuildBootstrapAgentNextTask(string projectName, string inputPath, string baseConfigPath, string projectConfigPath)
{
    var safeProjectName = MakeSafeProfileName(projectName);
    var sb = new StringBuilder();
    sb.AppendLine($"# Следующая задача агенту: bootstrap profile for {projectName}");
    sb.AppendLine();
    sb.AppendLine("Работай только через config layers. C# код мигратора не менять.");
    sb.AppendLine();
    sb.AppendLine("## Прочитай");
    sb.AppendLine("- `docs/config-layering.md`");
    sb.AppendLine("- `docs/migration-profiles.md`");
    sb.AppendLine("- `.agent-loops/02-guardrails.md`");
    sb.AppendLine();
    sb.AppendLine("## Используй конфиги слева направо");
    sb.AppendLine($"1. `{baseConfigPath}`");
    sb.AppendLine($"2. `{projectConfigPath}`");
    sb.AppendLine();
    sb.AppendLine("## Команды");
    sb.AppendLine("```powershell");
    sb.AppendLine($"dotnet run --project .\\Migrator.Cli -- --mode config-validate --config \"{baseConfigPath}\" --config \"{projectConfigPath}\" --out config-validate");
    sb.AppendLine($"dotnet run --project .\\Migrator.Cli -- --mode index-pom --input \"{inputPath}\" --out pom-index --format both");
    sb.AppendLine($"dotnet run --project .\\Migrator.Cli -- --mode migrate --input \"{inputPath}\" --config \"{baseConfigPath}\" --config \"{projectConfigPath}\" --out {safeProjectName}-migrate --format both");
    sb.AppendLine($"dotnet run --project .\\Migrator.Cli -- --mode verify-project --input \"{inputPath}\" --config \"{baseConfigPath}\" --config \"{projectConfigPath}\" --out {safeProjectName}-verify-project --format both");
    sb.AppendLine("```");
    sb.AppendLine();
    sb.AppendLine("## Правила");
    sb.AppendLine("- reusable mappings клади в base profile;");
    sb.AppendLine("- project-specific mappings клади в project profile;");
    sb.AppendLine("- не дублируй правило в project profile, если оно уже корректно покрыто base profile;");
    sb.AppendLine("- после итерации запускай config-validate и guard;");
    sb.AppendLine("- если статус `CONTINUE_AUTONOMOUSLY`, продолжай без вопроса пользователю; останавливайся только по `.agent-loops/03-stop-policy.md`.");
    return sb.ToString();
}

static string BuildBootstrapProjectReport(BootstrapProjectReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine($"# Bootstrap project report: {report.ProjectName}");
    sb.AppendLine();
    sb.AppendLine($"Input: `{report.InputPath}`");
    sb.AppendLine($"Base profile: `{report.BaseProfilePath}`");
    sb.AppendLine($"Project profile: `{report.ProjectProfilePath}`");
    sb.AppendLine($"Nearest project: `{report.NearestProjectPath ?? "not found"}`");
    sb.AppendLine();
    sb.AppendLine("## Warnings");
    foreach (var warning in report.Warnings)
        sb.AppendLine($"- {warning}");
    return sb.ToString();
}

// --- Orchestrate mode ---

static int WriteEmergencyOrchestrationReport(string inputPath, string outPath, string? configPath, Exception exception)
{
    try
    {
        Directory.CreateDirectory(outPath);

        var report = new OrchestrationReport(
            Status: OrchestrationStageStatus.Failed,
            InputPath: PathSanitizer.MakeSafePath(inputPath),
            ConfigPath: configPath != null ? PathSanitizer.MakeSafePath(configPath) : null,
            OutputPath: PathSanitizer.MakeSafePath(outPath),
            Stages: new[]
            {
                new OrchestrationStage(
                    "orchestrate",
                    OrchestrationStageStatus.Failed,
                    1,
                    exception.Message,
                    null)
            },
            Metrics: new OrchestrationMetrics(
                FilesProcessed: 0,
                TestsFound: 0,
                GeneratedFiles: 0,
                SyntaxErrors: 0,
                TodoComments: 0,
                PageTodoCalls: 0,
                Proposals: 0),
            Issues: new[] { $"Orchestrate failed before report finalization: {exception.Message}" },
            TopProposals: Array.Empty<string>(),
            RecommendedNextActions: new[] { "Inspect the CLI stderr/stdout and fix the earliest orchestrate failure." },
            Warnings: new[] { exception.GetType().FullName ?? exception.GetType().Name });

        File.WriteAllText(
            Path.Combine(outPath, "orchestration-report.json"),
            System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(outPath, "orchestration-report.md"), ToOrchestrationReportMarkdown(report));
    }
    catch (Exception reportException)
    {
        Console.Error.WriteLine($"Could not write emergency orchestration report: {reportException.Message}");
    }

    return 1;
}

static int RunOrchestrate(string inputPath, string outPath, string? configPath, string format, ITestFileParser parser, IRenderer renderer, IProjectAdapter? adapter, ProjectAdapterConfig? config, ITargetBackend targetBackend)
{
    Console.WriteLine("=== Orchestrator Dry-Run ===");
    Console.WriteLine();

    // Sub-directories for each stage
    var analyzeDir = Path.Combine(outPath, "analyze");
    var generatedDir = Path.Combine(outPath, "generated");
    var verifyDir = Path.Combine(outPath, "verify");
    var proposeDir = Path.Combine(outPath, "propose");

    Directory.CreateDirectory(analyzeDir);
    Directory.CreateDirectory(generatedDir);
    Directory.CreateDirectory(verifyDir);
    Directory.CreateDirectory(proposeDir);

    var stages = new List<OrchestrationStage>();
    var warnings = new List<string>();
    var issues = new List<string>();
    VerifyReport? verifyReport = null;

    // ---- Stage 1: Analyze ----
    {
        var stage = new OrchestrationStage("analyze", OrchestrationStageStatus.NotStarted, 0, null, Path.GetRelativePath(outPath, analyzeDir));
        try
        {
            var pipeline = new MigrationPipeline(parser, renderer, adapter);
            var resultsList = Directory.Exists(inputPath)
                ? pipeline.ProcessDirectory(inputPath).ToList()
                : new[] { pipeline.ProcessFile(inputPath) }.ToList();

            if (resultsList.Count == 0)
            {
                stage = stage with { Status = OrchestrationStageStatus.Failed, Message = "No test files found" };
                issues.Add("Analyze: no test files found in input");
            }
            else
            {
                var summary = BuildSummary(resultsList, out var allUnmapped);
                var allUnsupported = CollectAllUnsupported(resultsList);
                WriteReports(summary, analyzeDir, format, allUnmapped, allUnsupported);
                GenerateDraftConfig(allUnmapped, analyzeDir, config);

                // Copy summary to generated/ for later stages
                WriteReports(summary, generatedDir, format, allUnmapped, allUnsupported);

                stage = stage with
                {
                    Status = summary.UnsupportedActions > 0 ? OrchestrationStageStatus.PassedWithWarnings : OrchestrationStageStatus.Passed,
                    Message = $"{summary.FilesProcessed} files, {summary.TestsFound} tests",
                };
            }
        }
        catch (Exception ex)
        {
            stage = stage with { Status = OrchestrationStageStatus.Failed, Message = ex.Message };
            issues.Add($"Analyze stage failed: {ex.Message}");
        }
        stages.Add(stage);
    }

    // ---- Stage 2: Migrate ----
    {
        var stage = new OrchestrationStage("migrate", OrchestrationStageStatus.NotStarted, 0, null, Path.GetRelativePath(outPath, generatedDir));
        try
        {
            var analyzeStage = stages[0];
            if (analyzeStage.Status == OrchestrationStageStatus.Failed)
            {
                stage = stage with { Status = OrchestrationStageStatus.Skipped, Message = "Skipped — analyze failed" };
            }
            else
            {
                var pipeline = new MigrationPipeline(parser, renderer, adapter);
                var resultsList = Directory.Exists(inputPath)
                    ? pipeline.ProcessDirectory(inputPath).ToList()
                    : new[] { pipeline.ProcessFile(inputPath) }.ToList();

                var summary = BuildSummary(resultsList, out var allUnmapped);
                var allUnsupported = CollectAllUnsupported(resultsList);

                int generated = 0;
                var writtenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var result in resultsList)
                {
                    string baseName = targetBackend.GetDefaultFileName(result.SourceModel);
                    string outName = ResolveFileName(generatedDir, baseName, writtenNames);
                    File.WriteAllText(Path.Combine(generatedDir, outName), result.GeneratedOutput);
                    generated++;
                }

                var summaryWithGenerated = summary with { GeneratedFiles = generated };

                // Write reports to both generated/ and generated/reports/
                WriteReports(summaryWithGenerated, generatedDir, format, allUnmapped, allUnsupported);
                GenerateDraftConfig(allUnmapped, generatedDir, config);

                stage = stage with
                {
                    Status = OrchestrationStageStatus.Passed,
                    Message = $"{generated} files generated",
                };
            }
        }
        catch (Exception ex)
        {
            stage = stage with { Status = OrchestrationStageStatus.Failed, Message = ex.Message };
            issues.Add($"Migrate stage failed: {ex.Message}");
        }
        stages.Add(stage);
    }

    // ---- Stage 3: Verify ----
    {
        var stage = new OrchestrationStage("verify", OrchestrationStageStatus.NotStarted, 0, null, Path.GetRelativePath(outPath, verifyDir));
        int verifyExitCode = 0;
        try
        {
            var migrateStage = stages[1];
            if (migrateStage.Status == OrchestrationStageStatus.Skipped || migrateStage.Status == OrchestrationStageStatus.Failed)
            {
                stage = stage with { Status = OrchestrationStageStatus.Skipped, Message = "Skipped — migrate not completed" };
            }
            else
            {
                // Rebuild pipeline results for verify
                var pipeline = new MigrationPipeline(parser, renderer, adapter);
                var resultsList = Directory.Exists(inputPath)
                    ? pipeline.ProcessDirectory(inputPath).ToList()
                    : new[] { pipeline.ProcessFile(inputPath) }.ToList();

                var syntaxChecker = CreateGeneratedCodeChecker();

                Func<string, string?>? scopeChecker = adapter is DefaultProjectAdapter da
                    ? (Func<string, string?>)(p => da.GetActiveScope(p))
                    : null;

                verifyReport = VerifyRunner.Run(resultsList, config, syntaxChecker, scopeChecker);
                verifyExitCode = VerifyRunner.ApplyQualityGates(verifyReport, config?.QualityGates, verifyReport.Issues);

                // Write verify reports
                File.WriteAllText(Path.Combine(verifyDir, "verify-report.txt"), VerifyReportWriter.ToText(verifyReport));
                File.WriteAllText(Path.Combine(verifyDir, "verify-report.json"), VerifyReportWriter.ToJson(verifyReport));

                string status;
                if (verifyReport.SyntaxErrors > 0)
                    status = OrchestrationStageStatus.Failed;
                else if (verifyExitCode != 0)
                    status = OrchestrationStageStatus.PassedWithWarnings;
                else
                    status = OrchestrationStageStatus.Passed;

                stage = stage with { Status = status, ExitCode = verifyExitCode, Message = verifyReport.Status };

                if (verifyReport.SyntaxErrors > 0)
                    issues.Add($"Verify: {verifyReport.SyntaxErrors} syntax error(s)");
            }
        }
        catch (Exception ex)
        {
            stage = stage with { Status = OrchestrationStageStatus.Failed, Message = ex.Message };
            issues.Add($"Verify stage failed: {ex.Message}");
        }
        stages.Add(stage);
    }

    // ---- Stage 4: Propose ----
    {
        var stage = new OrchestrationStage("propose", OrchestrationStageStatus.NotStarted, 0, null, Path.GetRelativePath(outPath, proposeDir));
        try
        {
            var migrateStage = stages[1];
            if (migrateStage.Status == OrchestrationStageStatus.Skipped || migrateStage.Status == OrchestrationStageStatus.Failed)
            {
                stage = stage with { Status = OrchestrationStageStatus.Skipped, Message = "Skipped — migrate not completed" };
            }
            else
            {
                // Propose runs even if verify failed — it reads from generated/ reports
                // Load report.json from generated/
                var summary = System.Text.Json.JsonSerializer.Deserialize<MigrationSummaryReport>(
                    File.ReadAllText(Path.Combine(generatedDir, "report.json")));

                // Load unmapped-targets.json
                var unmappedTargets = new List<UnmappedTargetInfo>();
                var unmappedPath = Path.Combine(generatedDir, "unmapped-targets.json");
                if (File.Exists(unmappedPath))
                {
                    unmappedTargets = System.Text.Json.JsonSerializer.Deserialize<List<UnmappedTargetInfo>>(File.ReadAllText(unmappedPath)) ?? new List<UnmappedTargetInfo>();
                }
                else if (summary?.TopUnmappedTargets != null)
                {
                    unmappedTargets = new List<UnmappedTargetInfo>(summary.TopUnmappedTargets);
                }

                // Load unsupported-actions.json
                var unsupportedActions = new List<UnsupportedMethodInfo>();
                var unsupportedPath = Path.Combine(generatedDir, "unsupported-actions.json");
                if (File.Exists(unsupportedPath))
                {
                    unsupportedActions = System.Text.Json.JsonSerializer.Deserialize<List<UnsupportedMethodInfo>>(File.ReadAllText(unsupportedPath)) ?? new List<UnsupportedMethodInfo>();
                }
                else if (summary?.TopUnsupportedActions != null)
                {
                    unsupportedActions = new List<UnsupportedMethodInfo>(summary.TopUnsupportedActions);
                }

                // Use in-memory VerifyReport from verify stage (JSON format is custom, not directly deserializable)
                VerifyReport? verifyRpt = verifyReport;

                // Load generated file contents for proposal analysis
                var generatedFiles = new Dictionary<string, string>();
                foreach (var csFile in Directory.GetFiles(generatedDir, "*.cs"))
                {
                    generatedFiles[Path.GetFileName(csFile)] = File.ReadAllText(csFile);
                }

                var existingConfig = config;

                var proposalInput = new ProposalGenerator.ProposalInput
                {
                    MigrationReport = summary,
                    VerifyReport = verifyRpt,
                    UnmappedTargets = unmappedTargets,
                    UnsupportedActions = unsupportedActions,
                    ExistingConfig = existingConfig,
                    GeneratedFiles = generatedFiles.Keys.ToList(),
                    GeneratedFileContents = generatedFiles
                };

                var generator = new ProposalGenerator();
                var proposals = generator.Generate(proposalInput);

                // Write proposals
                if (format == "json" || format == "both")
                {
                    File.WriteAllText(Path.Combine(proposeDir, "mapping-proposals.json"),
                        ProposalWriter.ToJson(proposals));
                }

                if (format == "text" || format == "both")
                {
                    File.WriteAllText(Path.Combine(proposeDir, "mapping-proposals.md"),
                        ProposalWriter.ToMarkdown(proposals));
                }

                stage = stage with
                {
                    Status = OrchestrationStageStatus.Passed,
                    Message = $"{proposals.Count} proposals generated",
                };
            }
        }
        catch (Exception ex)
        {
            stage = stage with { Status = OrchestrationStageStatus.Failed, Message = ex.Message };
            warnings.Add($"Propose stage failed (non-critical): {ex.Message}");
        }
        stages.Add(stage);
    }

    // ---- Build orchestration report (safe-load, never crashes) ----
    var analyzeSummary = TryLoadMigrationReport(Path.Combine(analyzeDir, "report.json"), warnings);
    var generatedSummary = TryLoadMigrationReport(Path.Combine(generatedDir, "report.json"), warnings);

    var overallStatus = DetermineOverallStatus(stages, verifyReport);

    var metrics = new OrchestrationMetrics(
        FilesProcessed: analyzeSummary?.FilesProcessed ?? 0,
        TestsFound: analyzeSummary?.TestsFound ?? 0,
        GeneratedFiles: generatedSummary?.GeneratedFiles ?? 0,
        SyntaxErrors: verifyReport?.SyntaxErrors ?? 0,
        TodoComments: analyzeSummary?.TodoComments ?? 0,
        PageTodoCalls: verifyReport?.PageTodoCalls ?? 0,
        Proposals: 0
    );

    // Count proposals from stage message
    var proposeStage = stages.FirstOrDefault(s => s.Name == "propose");
    if (proposeStage?.Message != null && int.TryParse(proposeStage.Message.Split(' ')[0], out var proposalCount))
    {
        metrics = metrics with { Proposals = proposalCount };
    }

    // Top proposals
    var topProposals = new List<string>();
    var proposalsJson = Path.Combine(proposeDir, "mapping-proposals.json");
    if (File.Exists(proposalsJson))
    {
        try
        {
            var proposalOutput = System.Text.Json.JsonSerializer.Deserialize<ProposalJsonOutput>(File.ReadAllText(proposalsJson));
            if (proposalOutput != null)
            {
                topProposals = proposalOutput.TopProposals
                    .Select(p => $"[{p.Priority}] {p.Title} (score: {p.Score})")
                    .ToList();
                metrics = metrics with { Proposals = proposalOutput.TotalProposals };
            }
        }
        catch
        {
            warnings.Add("propose/mapping-proposals.json is invalid — could not parse proposals");
        }
    }

    // Recommended next actions
    var recommendedActions = GenerateRecommendedNextActions(stages, verifyReport, analyzeSummary, topProposals);

    var report = new OrchestrationReport(
        Status: overallStatus,
        InputPath: PathSanitizer.MakeSafePath(inputPath),
        ConfigPath: configPath != null ? PathSanitizer.MakeSafePath(configPath) : null,
        OutputPath: PathSanitizer.MakeSafePath(outPath),
        Stages: stages,
        Metrics: metrics,
        Issues: issues,
        TopProposals: topProposals,
        RecommendedNextActions: recommendedActions,
        Warnings: warnings
    );

    // Write orchestration reports. Re-create the root in case a previous stage cleaned
    // or moved files unexpectedly; the orchestrator report is the contract artifact.
    Directory.CreateDirectory(outPath);
    var reportJsonPath = Path.Combine(outPath, "orchestration-report.json");
    File.WriteAllText(reportJsonPath, System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

    var reportMdPath = Path.Combine(outPath, "orchestration-report.md");
    File.WriteAllText(reportMdPath, ToOrchestrationReportMarkdown(report));

    // Console summary
    Console.WriteLine();
    Console.WriteLine("=== Orchestration Summary ===");
    Console.WriteLine($"Status: {overallStatus}");
    foreach (var s in stages)
        Console.WriteLine($"  {s.Name}: {s.Status} {(s.Message != null ? "- " + s.Message : "")}");
    Console.WriteLine();
    Console.WriteLine($"Metrics: {metrics.FilesProcessed} files, {metrics.TestsFound} tests, {metrics.GeneratedFiles} generated, {metrics.SyntaxErrors} syntax errors, {metrics.TodoComments} TODOs, {metrics.PageTodoCalls} Page.TODO_*, {metrics.Proposals} proposals");
    Console.WriteLine();
    if (recommendedActions.Count > 0)
    {
        Console.WriteLine("Recommended next actions:");
        for (int i = 0; i < recommendedActions.Count; i++)
            Console.WriteLine($"  {i + 1}. {recommendedActions[i]}");
    }
    Console.WriteLine();
    Console.WriteLine($"Reports written to: {Path.GetFullPath(outPath)}");

    // Exit code
    return DetermineExitCode(stages, verifyReport);
}

static MigrationSummaryReport? TryLoadMigrationReport(string path, List<string> warnings)
{
    if (!File.Exists(path))
    {
        warnings.Add($"{Path.GetFileName(Path.GetDirectoryName(path))}/report.json not found — stage may have failed before writing report");
        return null;
    }
    try
    {
        return System.Text.Json.JsonSerializer.Deserialize<MigrationSummaryReport>(File.ReadAllText(path));
    }
    catch
    {
        warnings.Add($"{Path.GetFileName(Path.GetDirectoryName(path))}/report.json is invalid — could not parse");
        return null;
    }
}

static string DetermineOverallStatus(List<OrchestrationStage> stages, VerifyReport? verifyReport)
{
    var hasSyntaxErrors = verifyReport?.SyntaxErrors > 0;
    var verifyStage = stages.FirstOrDefault(s => s.Name == "verify");
    var verifyFailed = verifyStage?.Status == OrchestrationStageStatus.Failed;
    var verifyWarnings = verifyStage?.Status == OrchestrationStageStatus.PassedWithWarnings;
    var anyStageFailed = stages.Any(s => s.Status == OrchestrationStageStatus.Failed);

    if (hasSyntaxErrors || verifyFailed || (anyStageFailed && stages.Any(s => s.Name != "propose" && s.Status == OrchestrationStageStatus.Failed)))
        return OrchestrationStageStatus.Failed;
    if (verifyWarnings || stages.Any(s => s.Status == OrchestrationStageStatus.PassedWithWarnings))
        return OrchestrationStageStatus.PassedWithWarnings;
    return OrchestrationStageStatus.Passed;
}

static int DetermineExitCode(List<OrchestrationStage> stages, VerifyReport? verifyReport)
{
    var analyzeStage = stages.FirstOrDefault(s => s.Name == "analyze");
    var migrateStage = stages.FirstOrDefault(s => s.Name == "migrate");
    var verifyStage = stages.FirstOrDefault(s => s.Name == "verify");

    // analyze/migrate unexpected failure → 3
    if (analyzeStage?.Status == OrchestrationStageStatus.Failed || migrateStage?.Status == OrchestrationStageStatus.Failed)
        return 3;

    // Syntax errors in generated code → 4 (highest)
    if (verifyReport?.SyntaxErrors > 0)
        return 4;

    // Preserve verify exit code: 3=syntax, 2=config error, 1=quality gate
    if (verifyStage != null && (verifyStage.Status == OrchestrationStageStatus.Failed
        || verifyStage.Status == OrchestrationStageStatus.PassedWithWarnings))
    {
        if (verifyStage.ExitCode == 3)
            return 4;
        if (verifyStage.ExitCode == 2)
            return 2;
        if (verifyStage.ExitCode >= 1)
            return 1;
    }

    if (stages.Any(s => s.Status == OrchestrationStageStatus.PassedWithWarnings))
        return 1;

    return 0;
}

static List<string> GenerateRecommendedNextActions(List<OrchestrationStage> stages, VerifyReport? verifyReport, MigrationSummaryReport? summary, IReadOnlyList<string> topProposals)
{
    var actions = new List<string>();

    if (verifyReport?.SyntaxErrors > 0)
    {
        actions.Add($"Fix {verifyReport.SyntaxErrors} syntax error(s) in generated code before proceeding. Check verify/verify-report.json for details.");
    }

    if (verifyReport?.ConfigWarnings > 0)
    {
        actions.Add($"Review {verifyReport.ConfigWarnings} config warning(s). Fix adapter-config.json to match source project structure.");
    }

    if (verifyReport != null && verifyReport.PageTodoCalls > 0)
    {
        actions.Add($"Map {verifyReport.PageTodoCalls} unmapped Page.TODO_* calls. Review propose/mapping-proposals.md for suggested UiTarget mappings.");
    }

    if (summary?.UnmappedTargets > 0)
    {
        actions.Add($"Add source-truth UiTarget mappings for {summary.UnmappedTargets} unmapped target(s). Review analyze/unmapped-targets.json and source truth before adding mappings.");
    }

    if (topProposals.Count > 0)
    {
        actions.Add("Review mapping-proposals.md for suggested config improvements.");
    }

    if (actions.Count == 0)
    {
        actions.Add("All stages passed. Attempt compile smoke test and manual runtime proof on 3-5 tests.");
    }

    actions.Add("Re-run orchestrator after applying changes to verify improvement.");

    return actions;
}

static string ToOrchestrationReportMarkdown(OrchestrationReport report)
{
    var sb = new System.Text.StringBuilder();

    sb.AppendLine("# Orchestration Report");
    sb.AppendLine();
    sb.AppendLine($"**Status:** {report.Status}");
    sb.AppendLine($"**Input:** {report.InputPath}");
    if (report.ConfigPath != null)
        sb.AppendLine($"**Config:** {report.ConfigPath}");
    sb.AppendLine($"**Output:** {report.OutputPath}");
    sb.AppendLine();

    // Stages table
    sb.AppendLine("## Stages");
    sb.AppendLine();
    sb.AppendLine("| Stage | Status | Exit Code | Message |");
    sb.AppendLine("|---|---|---:|---|");
    foreach (var stage in report.Stages)
    {
        sb.AppendLine($"| {stage.Name} | {stage.Status} | {stage.ExitCode} | {stage.Message ?? ""} |");
    }
    sb.AppendLine();

    // Metrics table
    sb.AppendLine("## Metrics");
    sb.AppendLine();
    sb.AppendLine("| Metric | Value |");
    sb.AppendLine("|---|---:|");
    sb.AppendLine($"| Files processed | {report.Metrics.FilesProcessed} |");
    sb.AppendLine($"| Tests found | {report.Metrics.TestsFound} |");
    sb.AppendLine($"| Generated files | {report.Metrics.GeneratedFiles} |");
    sb.AppendLine($"| Syntax errors | {report.Metrics.SyntaxErrors} |");
    sb.AppendLine($"| TODO comments | {report.Metrics.TodoComments} |");
    sb.AppendLine($"| Page.TODO_* | {report.Metrics.PageTodoCalls} |");
    sb.AppendLine($"| Proposals | {report.Metrics.Proposals} |");
    sb.AppendLine();

    // Issues
    if (report.Issues.Count > 0)
    {
        sb.AppendLine("## Issues");
        sb.AppendLine();
        foreach (var issue in report.Issues)
            sb.AppendLine($"- {issue}");
        sb.AppendLine();
    }

    // Top proposals
    if (report.TopProposals.Count > 0)
    {
        sb.AppendLine("## Top Proposals");
        sb.AppendLine();
        for (int i = 0; i < report.TopProposals.Count; i++)
            sb.AppendLine($"{i + 1}. {report.TopProposals[i]}");
        sb.AppendLine();
    }

    // Recommended next actions
    if (report.RecommendedNextActions.Count > 0)
    {
        sb.AppendLine("## Recommended Next Actions");
        sb.AppendLine();
        for (int i = 0; i < report.RecommendedNextActions.Count; i++)
            sb.AppendLine($"{i + 1}. {report.RecommendedNextActions[i]}");
        sb.AppendLine();
    }

    // Warnings
    if (report.Warnings.Count > 0)
    {
        sb.AppendLine("## Warnings");
        sb.AppendLine();
        foreach (var w in report.Warnings)
            sb.AppendLine($"- {w}");
        sb.AppendLine();
    }

    return sb.ToString();
}



static int RunDoctor(string inputPath, string outPath, string format, string[] configPaths, ProjectAdapterConfig? config, CliOptions opts)
{
    Directory.CreateDirectory(outPath);

    var checks = new List<DoctorCheck>();
    var fullInputPath = Path.GetFullPath(inputPath);
    var inputExists = File.Exists(inputPath) || Directory.Exists(inputPath);
    var inputKind = File.Exists(inputPath) ? "file" : Directory.Exists(inputPath) ? "directory" : "missing";

    AddDoctorCheck(checks, inputExists ? "passed" : "failed", "INPUT_EXISTS",
        inputExists ? $"Input {inputKind} exists." : $"Input not found: {inputPath}",
        fullInputPath,
        inputExists ? "Continue with migration preflight." : "Fix --input path before running migrate/verify-project.");

    var csFiles = inputExists ? SafeEnumerateFiles(inputPath, "*.cs", SearchOption.AllDirectories).ToArray() : Array.Empty<string>();
    AddDoctorCheck(checks, csFiles.Length > 0 ? "passed" : "warning", "CS_FILES_FOUND",
        csFiles.Length > 0 ? $"Found {csFiles.Length} C# file(s) under input." : "No C# files found under input.",
        fullInputPath,
        csFiles.Length > 0 ? "OK." : "Check that --input points to Selenium C# tests, not an empty/report directory.");

    var apiLikeFiles = csFiles.Count(p => p.Contains("ApiTests", StringComparison.OrdinalIgnoreCase)
                                     || p.Contains("WebApiTests", StringComparison.OrdinalIgnoreCase)
                                     || Path.GetFileName(p).Contains("Api", StringComparison.OrdinalIgnoreCase));
    if (csFiles.Length > 0 && apiLikeFiles > 0 && apiLikeFiles * 3 >= csFiles.Length)
    {
        AddDoctorCheck(checks, "warning", "INPUT_LOOKS_API_HEAVY",
            $"{apiLikeFiles}/{csFiles.Length} files look API-oriented. UI Selenium→Playwright migration may be using too broad input.",
            fullInputPath,
            "Prefer a UI/E2E test package folder. Do not include WebApiTests in a UI Playwright migration unless intentionally supported.");
    }

    var topLevelDirs = Directory.Exists(inputPath)
        ? SafeEnumerateDirectories(inputPath, "*", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()!
        : Array.Empty<string>();
    if (topLevelDirs.Length > 12)
    {
        AddDoctorCheck(checks, "warning", "INPUT_SCOPE_WIDE",
            $"Input has {topLevelDirs.Length} top-level directories. This may be a repo/root rather than a test package.",
            fullInputPath,
            "Prefer the narrowest test package directory for migrate/verify-project, then reuse profiles for other projects.");
    }

    if (configPaths.Length == 0)
    {
        AddDoctorCheck(checks, "warning", "CONFIG_NOT_PROVIDED",
            "No adapter-config layer was provided.",
            null,
            "Use --config for project/profile migration. Doctor can still inspect input/project context.");
    }
    else
    {
        foreach (var cfg in configPaths)
        {
            AddDoctorCheck(checks, File.Exists(cfg) ? "passed" : "failed", "CONFIG_LAYER_EXISTS",
                File.Exists(cfg) ? $"Config layer exists: {cfg}" : $"Config layer not found: {cfg}",
                cfg,
                File.Exists(cfg) ? "OK." : "Fix --config path or remove the missing layer.");
        }

        if (config != null)
        {
            var safetyIssues = AnalyzeConfigSafety(config).ToArray();
            var errors = safetyIssues.Count(i => i.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
            var warnings = safetyIssues.Count(i => i.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase));
            AddDoctorCheck(checks, errors > 0 ? "failed" : warnings > 0 ? "warning" : "passed", "CONFIG_SAFETY",
                errors > 0 || warnings > 0
                    ? $"Config safety found {errors} error(s), {warnings} warning(s)."
                    : "Config safety checks passed.",
                string.Join(" -> ", configPaths),
                errors > 0 || warnings > 0
                    ? "Run --mode config-validate for the full safety report before migration."
                    : "OK.");
        }
    }

    var nearestProject = inputExists ? FindNearestCsproj(inputPath) : null;
    AddDoctorCheck(checks, nearestProject != null ? "passed" : "warning", "NEAREST_CSPROJ",
        nearestProject != null ? $"Nearest .csproj found: {nearestProject}" : "No .csproj found upward from input.",
        nearestProject,
        nearestProject != null ? "verify-project can use this project as context when auto-discovery is enabled." : "Set Verification.ProjectReferences explicitly in adapter-config.");

    var nearestSolution = inputExists ? FindNearestFile(inputPath, "*.sln") : null;
    AddDoctorCheck(checks, nearestSolution != null ? "passed" : "warning", "NEAREST_SOLUTION",
        nearestSolution != null ? $"Nearest .sln found: {nearestSolution}" : "No .sln found upward from input.",
        nearestSolution,
        nearestSolution != null ? "Good for project-aware diagnostics." : "Not required, but useful for repo/context discovery.");

    var repoRoot = inputExists ? FindRepoRootForVerification(inputPath) : Directory.GetCurrentDirectory();
    var nugetConfig = FindNearestFileFromDirectory(repoRoot, "NuGet.config");
    AddDoctorCheck(checks, nugetConfig != null ? "passed" : "warning", "NUGET_CONFIG",
        nugetConfig != null ? $"NuGet.config found: {nugetConfig}" : "No NuGet.config found near inferred repo root.",
        repoRoot,
        nugetConfig != null ? "BuildWorkingDirectory should point at repo root so private package feeds are visible." : "If project uses private packages, set Verification.BuildWorkingDirectory to repo root with NuGet.config.");

    var buildFiles = new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" }
        .Select(name => FindNearestFileFromDirectory(repoRoot, name))
        .Where(x => x != null)
        .Cast<string>()
        .ToArray();
    AddDoctorCheck(checks, buildFiles.Length > 0 ? "passed" : "info", "BUILD_FILES",
        buildFiles.Length > 0 ? $"Found {buildFiles.Length} repo build file(s)." : "No Directory.Build.* / Directory.Packages.props found near repo root.",
        repoRoot,
        buildFiles.Length > 0 ? "verify-project should import these when AutoDiscoverBuildFiles is enabled." : "OK if project does not use repo-level build props.");

    if (config?.Verification == null)
    {
        AddDoctorCheck(checks, "warning", "VERIFICATION_CONFIG_MISSING",
            "adapter-config has no Verification section.",
            configPaths.Length > 0 ? string.Join(" -> ", configPaths) : null,
            "Add Verification.BaseDirectory/ProjectReferences/BuildWorkingDirectory for reliable verify-project.");
    }
    else
    {
        var verification = config.Verification;
        var baseDir = ResolveVerificationBaseDirectory(verification, configPaths.LastOrDefault(), inputPath);
        var solution = ResolveSolutionPath(verification, baseDir, inputPath);
        var refs = ResolveProjectReferences(verification, baseDir, inputPath).ToArray();
        var includedRefs = refs.Where(r => r.Status.Equals("included", StringComparison.OrdinalIgnoreCase)).ToArray();
        var missingRefs = refs.Where(r => r.Status.Equals("missing", StringComparison.OrdinalIgnoreCase)).ToArray();
        var buildWorkingDirectory = ResolveBuildWorkingDirectory(verification, baseDir, solution);

        AddDoctorCheck(checks, Directory.Exists(baseDir) ? "passed" : "failed", "VERIFICATION_BASE_DIRECTORY",
            Directory.Exists(baseDir) ? $"Verification base directory exists: {baseDir}" : $"Verification base directory does not exist: {baseDir}",
            baseDir,
            Directory.Exists(baseDir) ? "OK." : "Fix Verification.BaseDirectory or use a path relative to adapter-config.");

        AddDoctorCheck(checks, Directory.Exists(buildWorkingDirectory) ? "passed" : "failed", "BUILD_WORKING_DIRECTORY",
            Directory.Exists(buildWorkingDirectory) ? $"Build working directory exists: {buildWorkingDirectory}" : $"Build working directory does not exist: {buildWorkingDirectory}",
            buildWorkingDirectory,
            Directory.Exists(buildWorkingDirectory) ? "OK. dotnet build will run here." : "Set Verification.BuildWorkingDirectory to a real repo root, preferably where NuGet.config lives.");

        AddDoctorCheck(checks, includedRefs.Length > 0 ? "passed" : "warning", "PROJECT_REFERENCES",
            includedRefs.Length > 0 ? $"verify-project can use {includedRefs.Length} project reference(s)." : "No project references resolved for verify-project.",
            null,
            includedRefs.Length > 0 ? "OK." : "Add Verification.ProjectReferences or enable AutoDiscoverNearestProject.");

        foreach (var missing in missingRefs.Take(10))
        {
            AddDoctorCheck(checks, "failed", "PROJECT_REFERENCE_MISSING",
                $"ProjectReference does not exist: {missing.Path}",
                missing.Path,
                "Fix the path in Verification.ProjectReferences or BaseDirectory.");
        }
    }

    var pomFiles = inputExists ? FindPomLikeFiles(inputPath).Take(50).ToArray() : Array.Empty<string>();
    AddDoctorCheck(checks, pomFiles.Length > 0 ? "passed" : "warning", "POM_SOURCE_TRUTH",
        pomFiles.Length > 0 ? $"Found {pomFiles.Length} POM/source-truth candidate file(s)." : "No obvious PageObject/POM files found under input.",
        fullInputPath,
        pomFiles.Length > 0 ? "Run --mode index-pom before heavy adapter-config work." : "If POMs live elsewhere, run index-pom on the wider project/root or provide source truth manually.");

    var dotnet = RunSimpleProcess("dotnet", new[] { "--version" }, Directory.GetCurrentDirectory());
    AddDoctorCheck(checks, dotnet.ExitCode == 0 ? "passed" : "failed", "DOTNET_AVAILABLE",
        dotnet.ExitCode == 0 ? $"dotnet available: {dotnet.StdOut.Trim()}" : "dotnet CLI is not available or failed to run.",
        null,
        dotnet.ExitCode == 0 ? "OK." : "Install .NET SDK or run from a developer environment before verify-project/packaging.");

    var status = checks.Any(c => c.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
        ? "failed"
        : checks.Any(c => c.Status.Equals("warning", StringComparison.OrdinalIgnoreCase)) ? "warning" : "passed";

    var report = new DoctorReport(
        GeneratedAtUtc: DateTimeOffset.UtcNow,
        Status: status,
        InputPath: fullInputPath,
        InputKind: inputKind,
        ConfigLayers: configPaths.Select(Path.GetFullPath).ToArray(),
        WorkspaceOutPath: Path.GetFullPath(outPath),
        Checks: checks.ToArray(),
        RecommendedNextActions: BuildDoctorRecommendedActions(checks).ToArray());

    WriteDoctorReport(report, outPath, format);

    DoctorFixPlan? fixPlan = null;
    if (opts.Fix)
    {
        fixPlan = new DoctorFixPlanner(new DoctorFixOptions
        {
            InputPath = inputPath,
            WorkspacePath = outPath,
            ConfigPaths = configPaths,
            TargetTestFramework = opts.TargetTestFramework,
            Apply = opts.Apply,
            DryRun = opts.DryRun || !opts.Apply
        }).BuildAndMaybeApply();
        DoctorFixPlanner.WriteArtifacts(fixPlan, outPath, format);
    }

    Console.WriteLine("=== Doctor ===");
    Console.WriteLine($"Status: {report.Status.ToUpperInvariant()}");
    Console.WriteLine($"Input: {report.InputPath}");
    Console.WriteLine($"Config layers: {report.ConfigLayers.Length}");
    Console.WriteLine($"Checks: {report.Checks.Length} ({report.Checks.Count(c => c.Status == "failed")} failed, {report.Checks.Count(c => c.Status == "warning")} warning)");
    foreach (var check in report.Checks.Where(c => c.Status != "passed" && c.Status != "info").Take(30))
        Console.WriteLine($"[{check.Status.ToUpperInvariant()}] {check.Code}: {check.Message}");
    if (fixPlan != null)
    {
        Console.WriteLine($"Fix mode: {fixPlan.Status} ({(fixPlan.Apply ? "apply" : "dry-run")})");
        Console.WriteLine($"Fix actions: {fixPlan.Actions.Length}; applied files: {fixPlan.AppliedFiles.Length}");
    }
    Console.WriteLine($"Reports written to: {Path.GetFullPath(outPath)}");

    return report.Status == "failed" ? 2 : 0;
}

static void AddDoctorCheck(List<DoctorCheck> checks, string status, string code, string message, string? location, string suggestedAction)
{
    checks.Add(new DoctorCheck(status, code, message, location, suggestedAction));
}

static IEnumerable<string> BuildDoctorRecommendedActions(IEnumerable<DoctorCheck> checks)
{
    var important = checks
        .Where(c => c.Status.Equals("failed", StringComparison.OrdinalIgnoreCase) || c.Status.Equals("warning", StringComparison.OrdinalIgnoreCase))
        .OrderBy(c => c.Status.Equals("failed", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
        .ThenBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
        .Take(8)
        .Select(c => $"{c.Code}: {c.SuggestedAction}");

    foreach (var action in important)
        yield return action;

    if (!important.Any())
    {
        yield return "Preflight looks good. Run migrate/verify-project, then explain-todo/smoke-plan.";
    }
}

static string[] SafeEnumerateFiles(string path, string pattern, SearchOption searchOption)
{
    try
    {
        if (File.Exists(path))
        {
            var name = Path.GetFileName(path);
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            if (pattern == "*" || System.Text.RegularExpressions.Regex.IsMatch(name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return new[] { Path.GetFullPath(path) };
            return Array.Empty<string>();
        }

        if (!Directory.Exists(path))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(path, pattern, searchOption)
            .Where(p => !p.Replace('\\', '/').Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                     && !p.Replace('\\', '/').Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                     && !p.Replace('\\', '/').Contains("/.git/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
    catch
    {
        return Array.Empty<string>();
    }
}

static string[] SafeEnumerateDirectories(string path, string pattern, SearchOption searchOption)
{
    try
    {
        if (!Directory.Exists(path))
            return Array.Empty<string>();
        return Directory.EnumerateDirectories(path, pattern, searchOption)
            .Where(p => !p.Replace('\\', '/').Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                     && !p.Replace('\\', '/').Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                     && !p.Replace('\\', '/').Contains("/.git/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
    catch
    {
        return Array.Empty<string>();
    }
}

static IEnumerable<string> FindPomLikeFiles(string inputPath)
{
    var files = SafeEnumerateFiles(inputPath, "*.cs", SearchOption.AllDirectories);
    return files.Where(path =>
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var normalized = path.Replace('\\', '/');
        return name.Contains("Page", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pom", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Lightbox", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Modal", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/Pages/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/PageObjects/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/POM/", StringComparison.OrdinalIgnoreCase);
    });
}

static string? FindNearestFileFromDirectory(string startDirectory, string pattern)
{
    var dir = new DirectoryInfo(Directory.Exists(startDirectory) ? startDirectory : Directory.GetCurrentDirectory());
    while (dir != null)
    {
        var match = dir.GetFiles(pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (match != null)
            return match.FullName;
        dir = dir.Parent;
    }
    return null;
}

static SimpleProcessResult RunSimpleProcess(string fileName, string[] args, string workingDirectory)
{
    try
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null)
            return new SimpleProcessResult(127, string.Empty, "Process.Start returned null");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new SimpleProcessResult(process.ExitCode, stdout, stderr);
    }
    catch (Exception ex)
    {
        return new SimpleProcessResult(127, string.Empty, ex.Message);
    }
}

static void WriteDoctorReport(DoctorReport report, string outPath, string format)
{
    if (format is "json" or "both")
    {
        File.WriteAllText(Path.Combine(outPath, "doctor-report.json"),
            System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    if (format is "text" or "both")
    {
        File.WriteAllText(Path.Combine(outPath, "doctor-report.md"), WriteDoctorMarkdown(report));
        File.WriteAllText(Path.Combine(outPath, "agent-doctor-next-task.md"), WriteAgentDoctorNextTask(report));
    }
}

static string WriteDoctorMarkdown(DoctorReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Doctor Report");
    sb.AppendLine();
    sb.AppendLine($"- **Generated**: {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss zzz}");
    sb.AppendLine($"- **Status**: `{report.Status}`");
    sb.AppendLine($"- **Input**: `{report.InputPath}` (`{report.InputKind}`)");
    sb.AppendLine($"- **Workspace out**: `{report.WorkspaceOutPath}`");
    sb.AppendLine($"- **Config layers**: `{report.ConfigLayers.Length}`");
    foreach (var layer in report.ConfigLayers)
        sb.AppendLine($"  - `{layer}`");
    sb.AppendLine();

    sb.AppendLine("## Recommended next actions");
    foreach (var action in report.RecommendedNextActions)
        sb.AppendLine($"- {action}");
    sb.AppendLine();

    sb.AppendLine("## Checks");
    foreach (var check in report.Checks.OrderBy(c => DoctorStatusRank(c.Status)).ThenBy(c => c.Code, StringComparer.OrdinalIgnoreCase))
    {
        sb.AppendLine($"### `{check.Code}` — {check.Status}");
        sb.AppendLine();
        sb.AppendLine(check.Message);
        if (!string.IsNullOrWhiteSpace(check.Location))
            sb.AppendLine($"\nLocation: `{check.Location}`");
        sb.AppendLine($"\nSuggested action: {check.SuggestedAction}");
        sb.AppendLine();
    }
    return sb.ToString();
}

static string WriteAgentDoctorNextTask(DoctorReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Agent Doctor Next Task");
    sb.AppendLine();
    sb.AppendLine("Прочитай `doctor-report.md` и не начинай миграцию, пока failed checks не исправлены или явно не эскалированы.");
    sb.AppendLine();
    sb.AppendLine($"- Status: `{report.Status}`");
    sb.AppendLine($"- Input: `{report.InputPath}`");
    sb.AppendLine($"- Config layers: `{report.ConfigLayers.Length}`");
    sb.AppendLine();
    sb.AppendLine("## Следующие действия");
    foreach (var action in report.RecommendedNextActions)
        sb.AppendLine($"- {action}");
    sb.AppendLine();
    sb.AppendLine("## Ограничения");
    sb.AppendLine("- Не меняй C# мигратора без явного разрешения.");
    sb.AppendLine("- Не меняй исходный проект.");
    sb.AppendLine("- Не правь generated `.cs` вручную.");
    sb.AppendLine("- Если проблема в окружении/project references/NuGet — сначала поправь `adapter-config.Verification` или попроси разработчика.");
    sb.AppendLine("- После исправления preflight проблем снова запусти `--mode doctor`.");
    return sb.ToString();
}

static int DoctorStatusRank(string status) => status.ToLowerInvariant() switch
{
    "failed" => 0,
    "warning" => 1,
    "passed" => 2,
    "info" => 3,
    _ => 4
};

static int RunConfigValidate(string[] configPaths, string outPath, string format, string validationMode, TargetSpec target)
{
    if (configPaths.Length == 0)
    {
        Console.Error.WriteLine("config-validate requires --config <adapter-config.json> or --input <adapter-config.json>.");
        return 2;
    }

    Directory.CreateDirectory(outPath);

    var issues = new List<ConfigSafetyIssue>();
    ProjectAdapterConfig? config = null;
    foreach (var path in configPaths)
    {
        if (!File.Exists(path))
        {
            issues.Add(new ConfigSafetyIssue("error", "CONFIG_NOT_FOUND", $"Config not found: {path}", path, "Check the --config path."));
        }
    }

    if (!issues.Any(i => i.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)))
    {
        try
        {
            config = ProjectAdapterConfigMerger.LoadAndMerge(configPaths);
        }
        catch (ConfigValidationError ex)
        {
            foreach (var err in ex.Errors)
                issues.Add(new ConfigSafetyIssue("error", "STRUCTURAL_CONFIG_ERROR", err, string.Join(" -> ", configPaths), "Fix adapter-config layers before running migration."));
        }
        catch (Exception ex)
        {
            issues.Add(new ConfigSafetyIssue("error", "CONFIG_READ_ERROR", ex.Message, string.Join(" -> ", configPaths), "Check that all config files are valid JSON and accessible."));
        }
    }

    if (config != null)
    {
        issues.AddRange(AnalyzeConfigSafety(config));
        issues.AddRange(AnalyzeConfigValidationMode(config, validationMode, target));
    }

    var configPathLabel = string.Join(" -> ", configPaths.Select(Path.GetFullPath));
    var report = new ConfigSafetyReport(
        GeneratedAtUtc: DateTimeOffset.UtcNow,
        ConfigPath: configPathLabel,
        ValidationMode: validationMode,
        Status: issues.Any(i => i.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)) ? "failed" : "passed",
        Issues: issues.OrderBy(i => SeverityRank(i.Severity)).ThenBy(i => i.Code, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Location, StringComparer.OrdinalIgnoreCase).ToArray(),
        Metrics: config != null ? BuildConfigMetrics(config) : Array.Empty<ConfigMetric>());

    WriteConfigSafetyReport(report, outPath, format);
    if (config != null && configPaths.Length > 1)
    {
        File.WriteAllText(Path.Combine(outPath, "adapter-config.merged.json"),
            System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    Console.WriteLine("=== Config Validate ===");
    Console.WriteLine(configPaths.Length == 1 ? $"Config: {configPaths[0]}" : $"Config layers: {string.Join(" -> ", configPaths)}");
    Console.WriteLine($"Validation mode: {validationMode}");
    Console.WriteLine($"Status: {report.Status.ToUpperInvariant()}");
    Console.WriteLine($"Issues: {report.Issues.Length} ({report.Issues.Count(i => i.Severity == "error")} error, {report.Issues.Count(i => i.Severity == "warning")} warning)");
    foreach (var issue in report.Issues.Take(30))
        Console.WriteLine($"[{issue.Severity.ToUpperInvariant()}] {issue.Code}: {issue.Message}" + (string.IsNullOrWhiteSpace(issue.Location) ? "" : $" ({issue.Location})"));
    if (report.Issues.Length > 30)
        Console.WriteLine($"... and {report.Issues.Length - 30} more");
    Console.WriteLine($"Reports written to: {Path.GetFullPath(outPath)}");

    return report.Status == "passed" ? 0 : 2;
}



static int RunConfigDiff(string? beforePath, string? afterPath, string outPath, string format)
{
    if (string.IsNullOrWhiteSpace(beforePath) || string.IsNullOrWhiteSpace(afterPath))
    {
        Console.Error.WriteLine("config-diff requires --before <old-adapter-config-or-profile.json> --after <new-adapter-config-or-profile.json>.");
        return 2;
    }

    if (!File.Exists(beforePath) || !File.Exists(afterPath))
    {
        Console.Error.WriteLine($"Config diff input not found. before={beforePath}, after={afterPath}");
        return 2;
    }

    Directory.CreateDirectory(outPath);

    ConfigDiffInput beforeInput;
    ConfigDiffInput afterInput;
    try
    {
        beforeInput = ReadConfigDiffInput(beforePath);
        afterInput = ReadConfigDiffInput(afterPath);
    }
    catch (ConfigValidationError ex)
    {
        Console.Error.WriteLine("Config error:");
        foreach (var err in ex.Errors)
            Console.Error.WriteLine(err);
        return 2;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Config diff read error: {ex.Message}");
        return 2;
    }

    var before = beforeInput.Config;
    var after = afterInput.Config;
    var changes = BuildConfigChanges(before, after).ToArray();
    var risks = BuildConfigDiffRisks(before, after).ToArray();
    var semanticParity = changes.Length == 0 && risks.Length == 0;
    var report = new ConfigDiffReport(
        GeneratedAtUtc: DateTimeOffset.UtcNow,
        BeforePath: Path.GetFullPath(beforePath),
        AfterPath: Path.GetFullPath(afterPath),
        Changes: changes,
        Risks: risks,
        Summary: new[]
        {
            $"InputKinds: {beforeInput.Kind} → {afterInput.Kind}",
            $"SemanticParity: {(semanticParity ? "passed" : "changed")}",
            $"UiTargets: {before.UiTargets.Length} → {after.UiTargets.Length}",
            $"PageObjects: {before.PageObjects.Length} → {after.PageObjects.Length}",
            $"Methods: {before.Methods.Length} → {after.Methods.Length}",
            $"ParameterizedMethods: {before.ParameterizedMethods.Length} → {after.ParameterizedMethods.Length}",
            $"Tables: {before.Tables.Length} → {after.Tables.Length}",
            $"Pagination: {before.Pagination.Length} → {after.Pagination.Length}",
            $"Scopes: {before.Scopes.Length} → {after.Scopes.Length}",
            $"SourceOnlyIdentifiers: {before.SourceOnlyIdentifiers.Length} → {after.SourceOnlyIdentifiers.Length}",
            $"TargetKnownTypes: {before.TargetKnownTypes.Length} → {after.TargetKnownTypes.Length}",
            $"TargetKnownIdentifiers: {before.TargetKnownIdentifiers.Length} → {after.TargetKnownIdentifiers.Length}"
        });

    WriteConfigDiffReport(report, outPath, format);

    Console.WriteLine("=== Config Diff ===");
    Console.WriteLine($"Input kinds: {beforeInput.Kind} -> {afterInput.Kind}");
    Console.WriteLine($"Semantic parity: {(semanticParity ? "PASSED" : "CHANGED")}");
    Console.WriteLine($"Changes: {report.Changes.Length}");
    Console.WriteLine($"Risks: {report.Risks.Length}");
    foreach (var risk in report.Risks.Take(20))
        Console.WriteLine($"[RISK] {risk.Code}: {risk.Message}" + (string.IsNullOrWhiteSpace(risk.Location) ? "" : $" ({risk.Location})"));
    Console.WriteLine($"Reports written to: {Path.GetFullPath(outPath)}");
    return risks.Any(r => r.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)) ? 2 : 0;
}

static JsonSerializerOptions CreateConfigDiffJsonOptions() => new()
{
    PropertyNameCaseInsensitive = true
};

static ConfigDiffInput ReadConfigDiffInput(string path)
{
    var json = File.ReadAllText(path);
    using var document = JsonDocument.Parse(json);
    var root = document.RootElement;
    var schemaVersion = TryGetString(root, "SchemaVersion") ?? TryGetString(root, "schemaVersion");
    if (string.Equals(schemaVersion, "migration-profile/v2", StringComparison.OrdinalIgnoreCase))
        return new ConfigDiffInput(path, "migration-profile/v2", ExtractAdapterConfigFromMigrationProfileV2(root, path));

    return new ConfigDiffInput(path, "adapter-config/v1", ConfigValidator.ValidateJson(json, path));
}

static ProjectAdapterConfig ExtractAdapterConfigFromMigrationProfileV2(JsonElement root, string path)
{
    if (root.TryGetProperty("LegacyConfig", out var legacyConfig) && legacyConfig.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
    {
        var config = legacyConfig.Deserialize<ProjectAdapterConfig>(CreateConfigDiffJsonOptions());
        if (config == null)
            throw new InvalidOperationException($"Could not deserialize LegacyConfig from migration-profile v2: {path}");
        return config;
    }

    if (!root.TryGetProperty("Project", out var project))
        throw new InvalidOperationException($"migration-profile/v2 is missing Project section: {path}");

    root.TryGetProperty("Source", out var source);
    root.TryGetProperty("Target", out var target);

    return new ProjectAdapterConfig
    {
        SourceProjectName = TryGetString(root, "SourceProjectName") ?? "",
        UiTargets = ReadProfileSection<UiTargetMapping[]>(project, "UiTargets") ?? Array.Empty<UiTargetMapping>(),
        PageObjects = ReadProfileSection<PageObjectMapping[]>(project, "PageObjects") ?? Array.Empty<PageObjectMapping>(),
        Methods = ReadProfileSection<MethodMapping[]>(project, "Methods") ?? Array.Empty<MethodMapping>(),
        ParameterizedMethods = ReadProfileSection<ParameterizedMethodMapping[]>(project, "ParameterizedMethods") ?? Array.Empty<ParameterizedMethodMapping>(),
        Tables = ReadProfileSection<TableConfig[]>(project, "Tables") ?? Array.Empty<TableConfig>(),
        Pagination = ReadProfileSection<PaginationConfig[]>(project, "Pagination") ?? Array.Empty<PaginationConfig>(),
        NavigationUrls = ReadProfileSection<Dictionary<string, string>>(project, "NavigationUrls") ?? new Dictionary<string, string>(StringComparer.Ordinal),
        NavigationTargetStatement = TryGetString(project, "NavigationTargetStatement"),
        Scopes = ReadProfileSection<ProfileScope[]>(project, "Scopes") ?? Array.Empty<ProfileScope>(),
        QualityGates = ReadProfileSection<QualityGatesConfig>(project, "QualityGates"),
        Verification = ReadProfileSection<VerificationConfig>(project, "Verification"),
        SourceOnlyIdentifiers = ReadProfileSection<string[]>(source, "SourceOnlyIdentifiers") ?? Array.Empty<string>(),
        SuppressedMethods = ReadProfileSection<string[]>(source, "SuppressedMethods") ?? Array.Empty<string>(),
        SuppressedMethodPatterns = ReadProfileSection<string[]>(source, "SuppressedMethodPatterns") ?? Array.Empty<string>(),
        RecognizerAliases = ReadProfileSection<RecognizerAliasOptions>(source, "RecognizerAliases") ?? new RecognizerAliasOptions(),
        GenericResultMethods = ReadProfileSection<string[]>(source, "GenericResultMethods") ?? Array.Empty<string>(),
        WaitPolicies = ReadProfileSection<WaitPolicyMapping[]>(source, "WaitPolicies") ?? Array.Empty<WaitPolicyMapping>(),
        TargetKnownTypes = ReadProfileSection<string[]>(target, "TargetKnownTypes") ?? Array.Empty<string>(),
        TargetKnownIdentifiers = ReadProfileSection<string[]>(target, "TargetKnownIdentifiers") ?? Array.Empty<string>(),
        TestHost = ReadProfileSection<TestHostConfig>(target, "TestHost"),
        LocatorSettings = ReadProfileSection<LocatorSettings>(target, "LocatorSettings")
    };
}

static T? ReadProfileSection<T>(JsonElement parent, string propertyName)
{
    if (parent.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        return default;
    if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        return default;
    return value.Deserialize<T>(CreateConfigDiffJsonOptions());
}

static string? TryGetString(JsonElement parent, string propertyName)
{
    if (parent.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        return null;
    return parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : null;
}

static int RunGuard(string? beforePath, string? afterPath, string outPath, string format)
{
    if (string.IsNullOrWhiteSpace(beforePath) || string.IsNullOrWhiteSpace(afterPath))
    {
        Console.Error.WriteLine("guard requires --before <old-artifacts-dir> --after <new-artifacts-dir>.");
        return 2;
    }

    var beforeDir = ResolveArtifactDirectoryForGuard(beforePath);
    var afterDir = ResolveArtifactDirectoryForGuard(afterPath);
    if (!Directory.Exists(beforeDir) || !Directory.Exists(afterDir))
    {
        Console.Error.WriteLine($"Guard artifacts not found. before={beforeDir}, after={afterDir}");
        return 2;
    }

    Directory.CreateDirectory(outPath);
    var before = BuildExplainTodoReportFromArtifacts(beforeDir);
    var after = BuildExplainTodoReportFromArtifacts(afterDir);
    var checks = BuildGuardChecks(before, after).ToArray();
    var passed = checks.All(c => c.Status.Equals("passed", StringComparison.OrdinalIgnoreCase) || c.Status.Equals("warning", StringComparison.OrdinalIgnoreCase));
    var report = new GuardReport(
        GeneratedAtUtc: DateTimeOffset.UtcNow,
        BeforePath: Path.GetFullPath(beforeDir),
        AfterPath: Path.GetFullPath(afterDir),
        Status: passed ? "passed" : "failed",
        Checks: checks,
        Summary: new[]
        {
            $"TODO: {before.TodoComments} → {after.TodoComments}",
            $"UnmappedTargets: {before.UnmappedTargets} → {after.UnmappedTargets}",
            $"UnsupportedActions: {before.UnsupportedActions} → {after.UnsupportedActions}",
            $"SyntaxErrors: {before.SyntaxErrors} → {after.SyntaxErrors}",
            $"ProjectVerify: {before.ProjectVerifyStatus ?? "unknown"} → {after.ProjectVerifyStatus ?? "unknown"}"
        });

    WriteGuardReport(report, outPath, format);

    Console.WriteLine("=== Migration Guard ===");
    Console.WriteLine($"Status: {report.Status.ToUpperInvariant()}");
    foreach (var check in report.Checks)
        Console.WriteLine($"[{check.Status.ToUpperInvariant()}] {check.Name}: {check.Message}");
    Console.WriteLine($"Reports written to: {Path.GetFullPath(outPath)}");
    return report.Status == "passed" ? 0 : 2;
}

static ConfigMetric[] BuildConfigMetrics(ProjectAdapterConfig config)
{
    return new[]
    {
        new ConfigMetric("UiTargets", config.UiTargets.Length),
        new ConfigMetric("Methods", config.Methods.Length),
        new ConfigMetric("ParameterizedMethods", config.ParameterizedMethods.Length),
        new ConfigMetric("PageObjects", config.PageObjects.Length),
        new ConfigMetric("Tables", config.Tables.Length),
        new ConfigMetric("Pagination", config.Pagination.Length),
        new ConfigMetric("Scopes", config.Scopes.Length),
        new ConfigMetric("SourceOnlyIdentifiers", config.SourceOnlyIdentifiers.Length),
        new ConfigMetric("TargetKnownTypes", config.TargetKnownTypes.Length),
        new ConfigMetric("TargetKnownIdentifiers", config.TargetKnownIdentifiers.Length),
        new ConfigMetric("SuppressedMethodPatterns", FlattenSuppressedMethodPatterns(config).Count()),
        new ConfigMetric("RegexLikeSuppressedMethodPatterns", FlattenSuppressedMethodPatterns(config).Count(x => LooksLikeRegexSuppressedMethodPattern(x.Value)) )
    };
}

static IEnumerable<ConfigSafetyIssue> AnalyzeConfigSafety(ProjectAdapterConfig config)
{
    var issues = new List<ConfigSafetyIssue>();
    var forbiddenTargetKnown = new HashSet<string>(new[] { "page", "pagef", "lightbox", "modal", "dialog", "popup", "driver", "webdriver" }, StringComparer.OrdinalIgnoreCase);
    var sourceOnly = FlattenSourceOnlyIdentifiers(config).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var targetKnown = FlattenTargetKnownIdentifiers(config).Concat(FlattenTargetKnownTypes(config)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var symbol in targetKnown.Where(sourceOnly.Contains).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
    {
        issues.Add(new ConfigSafetyIssue("error", "TARGET_KNOWN_CONFLICTS_WITH_SOURCE_ONLY",
            $"'{symbol}' is both source-only and target-known.", "adapter-config.json",
            "Choose one meaning. Source-only symbols must not be rendered as active target code."));
    }

    foreach (var symbol in targetKnown.Where(forbiddenTargetKnown.Contains).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
    {
        issues.Add(new ConfigSafetyIssue("error", "FORBIDDEN_TARGET_KNOWN_SYMBOL",
            $"'{symbol}' looks like a Selenium/source-only root and must not be target-known.", "TargetKnownTypes/TargetKnownIdentifiers",
            "Keep page/pagef/lightbox/modal/dialog/popup/Driver/WebDriver source-only, or map the whole expression through adapter-config."));
    }

    AddDuplicateIssues(config.UiTargets.Select(x => x.SourceExpression), "UiTargets.SourceExpression", "DUPLICATE_UI_TARGET", issues);
    AddDuplicateIssues(config.Methods.Select(x => x.SourceMethod), "Methods.SourceMethod", "DUPLICATE_METHOD_MAPPING", issues);
    AddDuplicateIssues(config.ParameterizedMethods.Select(x => x.SourceMethodPattern), "ParameterizedMethods.SourceMethodPattern", "DUPLICATE_PARAMETERIZED_METHOD_MAPPING", issues);
    AddDuplicateIssues(config.SourceOnlyIdentifiers, "SourceOnlyIdentifiers", "DUPLICATE_SOURCE_ONLY_IDENTIFIER", issues);
    AddDuplicateIssues(config.TargetKnownTypes, "TargetKnownTypes", "DUPLICATE_TARGET_KNOWN_TYPE", issues);
    AddDuplicateIssues(config.TargetKnownIdentifiers, "TargetKnownIdentifiers", "DUPLICATE_TARGET_KNOWN_IDENTIFIER", issues);

    foreach (var (pattern, location) in FlattenSuppressedMethodPatterns(config))
    {
        if (LooksLikeRegexSuppressedMethodPattern(pattern))
        {
            issues.Add(new ConfigSafetyIssue("warning", "REGEX_LIKE_SUPPRESSION_PATTERN",
                $"SuppressedMethodPatterns entry '{pattern}' looks like a regular expression, but SuppressedMethodPatterns uses glob semantics.", location,
                "Use glob syntax such as *Loader.ValidateLoading(*) instead of regex syntax, or move project helper wrappers to MethodSemantics. Before classifying helper wrappers, run `--mode helper-inventory` to inspect helper/POM bodies."));
        }

        if (LooksLikeAssertionSuppression(pattern))
        {
            issues.Add(new ConfigSafetyIssue("error", "DANGEROUS_ASSERTION_SUPPRESSION",
                $"SuppressedMethodPatterns entry '{pattern}' can suppress assertions/checks.", location,
                "Do not suppress Should/Assert/Expect/EqualTo checks. Add assertion mappings or leave MANUAL_REVIEW/failing TODO."));
        }
        else if (LooksLikeBroadInteractionSuppression(pattern))
        {
            issues.Add(new ConfigSafetyIssue("error", "DANGEROUS_INTERACTION_SUPPRESSION",
                $"SuppressedMethodPatterns entry '{pattern}' is broad enough to hide user interactions or mapped UiTargets.", location,
                "Replace broad Click/SendKeys/Fill/Hover suppressions with UiTargets, Methods/ParameterizedMethods, or a narrower source-only helper pattern."));
        }
    }

    foreach (var (method, location) in FlattenSuppressedMethods(config))
    {
        if (LooksLikeAssertionSuppression(method))
        {
            issues.Add(new ConfigSafetyIssue("error", "DANGEROUS_ASSERTION_SUPPRESSION",
                $"SuppressedMethods entry '{method}' can suppress assertions/checks.", location,
                "Do not suppress Should/Assert/Expect checks. Add assertion mappings or leave MANUAL_REVIEW/failing TODO."));
        }
        else if (LooksLikeInteractionSuppressedMethod(method))
        {
            issues.Add(new ConfigSafetyIssue("warning", "SUSPICIOUS_INTERACTION_SUPPRESSION",
                $"SuppressedMethods entry '{method}' may suppress real user interaction.", location,
                "Prefer UiTargets or method mappings. Suppress only harmless waits/source-only diagnostics."));
        }
    }

    foreach (var target in config.UiTargets.Where(t => string.Equals(t.TargetKind, "RawExpression", StringComparison.OrdinalIgnoreCase)))
    {
        issues.Add(new ConfigSafetyIssue("warning", "RAW_EXPRESSION_MAPPING_REQUIRES_REVIEW",
            $"UiTarget '{target.SourceExpression}' uses RawExpression target kind.", "UiTargets",
            "Prefer TestId/Locator/Text. Keep RawExpression only with source truth and manual review."));
    }

    foreach (var method in config.Methods.Where(m => m.TargetStatements != null && m.TargetStatements.Any(ContainsRiskyGeneratedCode)))
    {
        issues.Add(new ConfigSafetyIssue("warning", "RISKY_TARGET_STATEMENT",
            $"Method mapping '{method.SourceMethod}' contains TODO/dynamic/object fallback-like target code.", "Methods",
            "Generated target statements should be real Playwright/test code, not hidden TODO or dummy declarations."));
    }

    foreach (var method in config.ParameterizedMethods.Where(m => m.TargetStatements != null && m.TargetStatements.Any(ContainsRiskyGeneratedCode)))
    {
        issues.Add(new ConfigSafetyIssue("warning", "RISKY_PARAMETERIZED_TARGET_STATEMENT",
            $"Parameterized mapping '{method.SourceMethodPattern}' contains TODO/dynamic/object fallback-like target code.", "ParameterizedMethods",
            "Avoid hiding migration gaps in config. Leave TODO in generated code or escalate."));
    }

    if (config.Verification == null)
    {
        issues.Add(new ConfigSafetyIssue("warning", "MISSING_PROJECT_VERIFICATION",
            "Verification section is missing, so verify-project may not compile generated code in the real project context.", "Verification",
            "Add Verification.ProjectReferences/PackageReferences or enable nearest project auto-discovery."));
    }

    return issues;
}


static IEnumerable<ConfigSafetyIssue> AnalyzeConfigValidationMode(ProjectAdapterConfig config, string validationMode, TargetSpec target)
{
    if (string.Equals(validationMode, "warn", StringComparison.OrdinalIgnoreCase))
        return Array.Empty<ConfigSafetyIssue>();

    var issues = new List<ConfigSafetyIssue>();
    var production = string.Equals(validationMode, "production", StringComparison.OrdinalIgnoreCase);
    var isTypeScriptTarget = string.Equals(target.Language, "typescript", StringComparison.OrdinalIgnoreCase)
        || target.Id.Contains("typescript", StringComparison.OrdinalIgnoreCase)
        || target.Id.Equals("ts", StringComparison.OrdinalIgnoreCase);

    foreach (var item in EnumerateMethodMappings(config))
    {
        var hasLegacyStatements = item.Mapping.TargetStatements is { Length: > 0 };
        var hasTargetStatements = HasTargetSpecificStatementsForTarget(item.Mapping.Targets, target);
        if (hasLegacyStatements && !hasTargetStatements)
        {
            var severity = production && isTypeScriptTarget ? "error" : "warning";
            var code = production && isTypeScriptTarget
                ? "TS_TARGET_STATEMENTS_REQUIRED"
                : "TARGET_SPECIFIC_STATEMENTS_MISSING";
            issues.Add(new ConfigSafetyIssue(severity, code,
                $"Method mapping '{item.Mapping.SourceMethod}' uses legacy TargetStatements without a target-specific override for '{target.Id}'.",
                item.Location,
                "Move target code into Targets.<target>.TargetStatements. For TypeScript, add Targets.playwright-typescript.TargetStatements before production migration."));
        }

        if (IsReviewRequiredForTarget(item.Mapping.RequiresReview, item.Mapping.Targets, target))
        {
            issues.Add(new ConfigSafetyIssue(production ? "error" : "warning", production ? "MAPPED_METHOD_REQUIRES_REVIEW" : "MAPPED_METHOD_REVIEW_REQUIRED",
                $"Method mapping '{item.Mapping.SourceMethod}' is marked RequiresReview for '{target.Id}'.",
                item.Location,
                "Review the mapping and set RequiresReview=false only when the target-specific statement is safe and verified."));
        }
    }

    foreach (var item in EnumerateParameterizedMappings(config))
    {
        var hasLegacyStatements = item.Mapping.TargetStatements is { Length: > 0 };
        var hasTargetStatements = HasTargetSpecificStatementsForTarget(item.Mapping.Targets, target);
        if (hasLegacyStatements && !hasTargetStatements)
        {
            var severity = production && isTypeScriptTarget ? "error" : "warning";
            var code = production && isTypeScriptTarget
                ? "TS_TARGET_STATEMENTS_REQUIRED"
                : "TARGET_SPECIFIC_STATEMENTS_MISSING";
            issues.Add(new ConfigSafetyIssue(severity, code,
                $"Parameterized mapping '{item.Mapping.SourceMethodPattern}' uses legacy TargetStatements without a target-specific override for '{target.Id}'.",
                item.Location,
                "Move target code into Targets.<target>.TargetStatements. For TypeScript, add Targets.playwright-typescript.TargetStatements before production migration."));
        }

        if (IsReviewRequiredForTarget(item.Mapping.RequiresReview, item.Mapping.Targets, target))
        {
            issues.Add(new ConfigSafetyIssue(production ? "error" : "warning", production ? "MAPPED_METHOD_REQUIRES_REVIEW" : "MAPPED_METHOD_REVIEW_REQUIRED",
                $"Parameterized mapping '{item.Mapping.SourceMethodPattern}' is marked RequiresReview for '{target.Id}'.",
                item.Location,
                "Review the mapping and set RequiresReview=false only when the target-specific statement is safe and verified."));
        }
    }

    if (production && config.Verification == null)
    {
        issues.Add(new ConfigSafetyIssue("error", "PROJECT_VERIFICATION_REQUIRED",
            "Verification section is required in production validation mode.", "Verification",
            "Add Verification.ProjectReferences/PackageReferences or enable nearest project auto-discovery so generated code is checked in project context."));
    }

    return issues;
}

static bool HasTargetSpecificStatementsForTarget(Dictionary<string, TargetStatementMapping>? targets, TargetSpec target)
{
    return FindTargetStatementMapping(targets, target)?.TargetStatements is { Length: > 0 };
}

static bool IsReviewRequiredForTarget(bool parentRequiresReview, Dictionary<string, TargetStatementMapping>? targets, TargetSpec target)
{
    return FindTargetStatementMapping(targets, target)?.RequiresReview ?? parentRequiresReview;
}

static TargetStatementMapping? FindTargetStatementMapping(Dictionary<string, TargetStatementMapping>? targets, TargetSpec target)
{
    if (targets == null || targets.Count == 0)
        return null;

    foreach (var key in GetTargetLookupKeys(target))
    {
        if (targets.TryGetValue(key, out var mapping))
            return mapping;
    }

    foreach (var kvp in targets)
    {
        if (GetTargetLookupKeys(target).Any(key => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase)))
            return kvp.Value;
    }

    return null;
}

static IEnumerable<string> GetTargetLookupKeys(TargetSpec target)
{
    yield return target.Id;
    if (!string.IsNullOrWhiteSpace(target.Language))
        yield return target.Language;

    if (target.Id.Contains("typescript", StringComparison.OrdinalIgnoreCase)
        || string.Equals(target.Language, "typescript", StringComparison.OrdinalIgnoreCase))
    {
        yield return "ts";
        yield return "typescript";
        yield return "pw-ts";
        yield return "playwright-ts";
        yield return "playwright-typescript";
    }

    if (target.Id.Contains("dotnet", StringComparison.OrdinalIgnoreCase)
        || string.Equals(target.Language, "csharp", StringComparison.OrdinalIgnoreCase))
    {
        yield return "dotnet";
        yield return "csharp";
        yield return "playwright-dotnet";
    }
}

static IEnumerable<(MethodMapping Mapping, string Location)> EnumerateMethodMappings(ProjectAdapterConfig config)
{
    for (var i = 0; i < config.Methods.Length; i++)
        yield return (config.Methods[i], $"Methods[{i}]");

    for (var si = 0; si < config.Scopes.Length; si++)
    {
        var scope = config.Scopes[si];
        var scopeName = string.IsNullOrWhiteSpace(scope.Name) ? si.ToString() : scope.Name;
        for (var mi = 0; mi < scope.Methods.Length; mi++)
            yield return (scope.Methods[mi], $"Scopes[{scopeName}].Methods[{mi}]");
    }
}

static IEnumerable<(ParameterizedMethodMapping Mapping, string Location)> EnumerateParameterizedMappings(ProjectAdapterConfig config)
{
    for (var i = 0; i < config.ParameterizedMethods.Length; i++)
        yield return (config.ParameterizedMethods[i], $"ParameterizedMethods[{i}]");

    for (var si = 0; si < config.Scopes.Length; si++)
    {
        var scope = config.Scopes[si];
        var scopeName = string.IsNullOrWhiteSpace(scope.Name) ? si.ToString() : scope.Name;
        for (var mi = 0; mi < scope.ParameterizedMethods.Length; mi++)
            yield return (scope.ParameterizedMethods[mi], $"Scopes[{scopeName}].ParameterizedMethods[{mi}]");
    }
}

static bool ContainsRiskyGeneratedCode(string statement)
{
    var s = statement.Trim();
    return s.Contains("TODO", StringComparison.OrdinalIgnoreCase)
        || s.Contains("dynamic ", StringComparison.OrdinalIgnoreCase)
        || s.StartsWith("object ", StringComparison.OrdinalIgnoreCase)
        || s.Contains("= null!", StringComparison.OrdinalIgnoreCase);
}

static IEnumerable<string> FlattenSourceOnlyIdentifiers(ProjectAdapterConfig config)
{
    foreach (var item in config.SourceOnlyIdentifiers) yield return item;
}

static IEnumerable<string> FlattenTargetKnownTypes(ProjectAdapterConfig config)
{
    foreach (var item in config.TargetKnownTypes) yield return item;
    foreach (var scope in config.Scopes)
        foreach (var item in scope.TargetKnownTypes)
            yield return item;
}

static IEnumerable<string> FlattenTargetKnownIdentifiers(ProjectAdapterConfig config)
{
    foreach (var item in config.TargetKnownIdentifiers) yield return item;
    foreach (var scope in config.Scopes)
        foreach (var item in scope.TargetKnownIdentifiers)
            yield return item;
}

static IEnumerable<(string Value, string Location)> FlattenSuppressedMethodPatterns(ProjectAdapterConfig config)
{
    foreach (var item in config.SuppressedMethodPatterns.Where(v => !string.IsNullOrWhiteSpace(v)))
        yield return (item, "SuppressedMethodPatterns");

    foreach (var scope in config.Scopes)
    {
        var scopeName = string.IsNullOrWhiteSpace(scope.Name) ? "<unnamed>" : scope.Name;
        foreach (var item in scope.SuppressedMethodPatterns.Where(v => !string.IsNullOrWhiteSpace(v)))
            yield return (item, $"Scopes[{scopeName}].SuppressedMethodPatterns");
    }
}

static IEnumerable<(string Value, string Location)> FlattenSuppressedMethods(ProjectAdapterConfig config)
{
    foreach (var item in config.SuppressedMethods.Where(v => !string.IsNullOrWhiteSpace(v)))
        yield return (item, "SuppressedMethods");

    foreach (var scope in config.Scopes)
    {
        var scopeName = string.IsNullOrWhiteSpace(scope.Name) ? "<unnamed>" : scope.Name;
        foreach (var item in scope.SuppressedMethods.Where(v => !string.IsNullOrWhiteSpace(v)))
            yield return (item, $"Scopes[{scopeName}].SuppressedMethods");
    }
}

static bool LooksLikeAssertionSuppression(string value)
{
    if (string.IsNullOrWhiteSpace(value))
        return false;

    var normalized = value.Replace(" ", string.Empty);
    return normalized.Contains("Should(", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("Should()", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("Assert", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("Expect", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("EqualTo(", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("BeEquivalentTo(", StringComparison.OrdinalIgnoreCase)
        || normalized.Contains("Contain", StringComparison.OrdinalIgnoreCase) && normalized.Contains("Should", StringComparison.OrdinalIgnoreCase);
}

static bool LooksLikeBroadInteractionSuppression(string pattern)
{
    if (string.IsNullOrWhiteSpace(pattern))
        return false;

    var normalized = pattern.Replace(" ", string.Empty);
    if (IsKnownHarmlessWaitSuppression(normalized))
        return false;

    if (!normalized.Contains('*'))
        return false;

    if (!normalized.Contains('(') && normalized.Count(ch => ch == '*') >= 2)
        return true;

    return new[]
    {
        ".Click(", ".ClickAsync(", ".SendKeys(", ".Fill(", ".FillAsync(",
        ".SetValue(", ".Press(", ".Hover(", ".HoverMouse(", ".SelectValue(",
        ".InputAndSelect(", ".InputInputAndAccept("
    }.Any(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase))
        && (normalized.StartsWith("*.", StringComparison.Ordinal)
            || normalized.Contains(".*.", StringComparison.Ordinal)
            || normalized.StartsWith("*lightbox", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("*modal", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("*page", StringComparison.OrdinalIgnoreCase));
}

static bool LooksLikeInteractionSuppressedMethod(string method)
{
    if (string.IsNullOrWhiteSpace(method))
        return false;

    return new[]
    {
        "Click", "ClickAsync", "SendKeys", "Fill", "FillAsync", "SetValue",
        "Press", "Hover", "HoverMouse", "SelectValue", "InputAndSelect", "InputInputAndAccept"
    }.Any(token => method.Equals(token, StringComparison.OrdinalIgnoreCase));
}

static bool LooksLikeRegexSuppressedMethodPattern(string pattern)
{
    if (string.IsNullOrWhiteSpace(pattern))
        return false;

    var suspiciousTokens = new[]
    {
        @".*", @"\.", @"\(", @"\d", @"\s", @"\w", @"\b",
        "^", "$", "[", "]", "+", "?", "{", "}", "|"
    };

    return suspiciousTokens.Any(token => pattern.Contains(token, StringComparison.Ordinal));
}

static bool IsKnownHarmlessWaitSuppression(string normalizedPattern)
{
    return normalizedPattern.Contains("WaitLoaded(", StringComparison.OrdinalIgnoreCase)
        || normalizedPattern.Contains("WaitExistAndVisible(", StringComparison.OrdinalIgnoreCase)
        || normalizedPattern.Contains("WaitDisabled(", StringComparison.OrdinalIgnoreCase)
        || normalizedPattern.Contains("WaitNotExists(", StringComparison.OrdinalIgnoreCase)
        || normalizedPattern.Contains("WaitUntil", StringComparison.OrdinalIgnoreCase);
}

static void AddDuplicateIssues(IEnumerable<string?> values, string location, string code, List<ConfigSafetyIssue> issues)
{
    foreach (var group in values.Where(v => !string.IsNullOrWhiteSpace(v)).GroupBy(v => v!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
    {
        issues.Add(new ConfigSafetyIssue("warning", code,
            $"Duplicate value '{group.Key}' appears {group.Count()} times.", location,
            "Remove duplicate entries or split project-specific overrides into Scopes/config layers."));
    }
}

static IEnumerable<ConfigDiffChange> BuildConfigChanges(ProjectAdapterConfig before, ProjectAdapterConfig after)
{
    foreach (var c in DiffStringSet("UiTargets", before.UiTargets.Select(x => x.SourceExpression), after.UiTargets.Select(x => x.SourceExpression))) yield return c;
    foreach (var c in DiffStringSet("Methods", before.Methods.Select(x => x.SourceMethod), after.Methods.Select(x => x.SourceMethod))) yield return c;
    foreach (var c in DiffStringSet("ParameterizedMethods", before.ParameterizedMethods.Select(x => x.SourceMethodPattern), after.ParameterizedMethods.Select(x => x.SourceMethodPattern))) yield return c;
    foreach (var c in DiffStringSet("SourceOnlyIdentifiers", before.SourceOnlyIdentifiers, after.SourceOnlyIdentifiers)) yield return c;
    foreach (var c in DiffStringSet("TargetKnownTypes", before.TargetKnownTypes, after.TargetKnownTypes)) yield return c;
    foreach (var c in DiffStringSet("TargetKnownIdentifiers", before.TargetKnownIdentifiers, after.TargetKnownIdentifiers)) yield return c;
    foreach (var c in DiffStringSet("Tables", before.Tables.Select(x => x.SourceExpression), after.Tables.Select(x => x.SourceExpression))) yield return c;
    foreach (var c in DiffStringSet("Pagination", before.Pagination.Select(x => x.SourceExpression), after.Pagination.Select(x => x.SourceExpression))) yield return c;
    foreach (var c in DiffStringSet("Scopes", before.Scopes.Select(x => x.Name), after.Scopes.Select(x => x.Name))) yield return c;
}

static IEnumerable<ConfigDiffChange> DiffStringSet(string section, IEnumerable<string?> beforeValues, IEnumerable<string?> afterValues)
{
    var before = beforeValues.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var after = afterValues.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var value in after.Except(before, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        yield return new ConfigDiffChange(section, "added", value);
    foreach (var value in before.Except(after, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        yield return new ConfigDiffChange(section, "removed", value);
}

static IEnumerable<ConfigSafetyIssue> BuildConfigDiffRisks(ProjectAdapterConfig before, ProjectAdapterConfig after)
{
    var risks = new List<ConfigSafetyIssue>();
    var forbiddenTargetKnown = new HashSet<string>(new[] { "page", "pagef", "lightbox", "modal", "dialog", "popup", "driver", "webdriver" }, StringComparer.OrdinalIgnoreCase);
    var removedSourceOnly = before.SourceOnlyIdentifiers.Except(after.SourceOnlyIdentifiers, StringComparer.OrdinalIgnoreCase).ToArray();
    foreach (var symbol in removedSourceOnly)
    {
        risks.Add(new ConfigSafetyIssue("warning", "SOURCE_ONLY_REMOVED",
            $"'{symbol}' was removed from SourceOnlyIdentifiers.", "SourceOnlyIdentifiers",
            "Verify that this symbol is now mapped or intentionally safe in target code."));
    }

    var addedTargetKnown = after.TargetKnownTypes.Concat(after.TargetKnownIdentifiers)
        .Except(before.TargetKnownTypes.Concat(before.TargetKnownIdentifiers), StringComparer.OrdinalIgnoreCase)
        .ToArray();
    foreach (var symbol in addedTargetKnown.Where(forbiddenTargetKnown.Contains))
    {
        risks.Add(new ConfigSafetyIssue("error", "FORBIDDEN_TARGET_KNOWN_ADDED",
            $"'{symbol}' was added as target-known.", "TargetKnownTypes/TargetKnownIdentifiers",
            "Do not mark Selenium roots as target-known."));
    }

    if (IsQualityGateLoosened(before.QualityGates?.MaxTodoComments, after.QualityGates?.MaxTodoComments))
        risks.Add(new ConfigSafetyIssue("warning", "QUALITY_GATE_LOOSENED", "MaxTodoComments was loosened or removed.", "QualityGates.MaxTodoComments", "Confirm this was intentional."));
    if (IsQualityGateLoosened(before.QualityGates?.MaxUnsupportedActions, after.QualityGates?.MaxUnsupportedActions))
        risks.Add(new ConfigSafetyIssue("warning", "QUALITY_GATE_LOOSENED", "MaxUnsupportedActions was loosened or removed.", "QualityGates.MaxUnsupportedActions", "Confirm this was intentional."));
    if (IsQualityGateLoosened(before.QualityGates?.MaxUnmappedTargets, after.QualityGates?.MaxUnmappedTargets))
        risks.Add(new ConfigSafetyIssue("warning", "QUALITY_GATE_LOOSENED", "MaxUnmappedTargets was loosened or removed.", "QualityGates.MaxUnmappedTargets", "Confirm this was intentional."));

    var beforeSuppressedPatterns = FlattenSuppressedMethodPatterns(before).Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var (pattern, location) in FlattenSuppressedMethodPatterns(after).Where(x => !beforeSuppressedPatterns.Contains(x.Value)))
    {
        if (LooksLikeRegexSuppressedMethodPattern(pattern))
        {
            risks.Add(new ConfigSafetyIssue("warning", "REGEX_LIKE_SUPPRESSION_PATTERN_ADDED",
                $"Regex-looking SuppressedMethodPatterns entry was added: '{pattern}'.", location,
                "SuppressedMethodPatterns is glob-based. Convert this to glob syntax or classify the helper through MethodSemantics after running `--mode helper-inventory`."));
        }

        if (LooksLikeAssertionSuppression(pattern))
        {
            risks.Add(new ConfigSafetyIssue("error", "DANGEROUS_ASSERTION_SUPPRESSION_ADDED",
                $"Dangerous assertion suppression was added: '{pattern}'.", location,
                "Do not add Should/Assert/Expect/EqualTo suppressions. Add assertion mappings or leave failing/manual TODO."));
        }
        else if (LooksLikeBroadInteractionSuppression(pattern))
        {
            risks.Add(new ConfigSafetyIssue("error", "DANGEROUS_INTERACTION_SUPPRESSION_ADDED",
                $"Broad interaction suppression was added: '{pattern}'.", location,
                "Do not hide Click/SendKeys/Fill/Hover behind suppression. Use UiTargets and method mappings."));
        }
    }

    return risks;
}

static bool IsQualityGateLoosened(int? before, int? after)
{
    if (before.HasValue && !after.HasValue)
        return true;
    if (before.HasValue && after.HasValue && after.Value > before.Value)
        return true;
    return false;
}

static IEnumerable<GuardCheck> BuildGuardChecks(TodoExplanationReport before, TodoExplanationReport after)
{
    yield return CompareNonIncreasing("TODO comments", before.TodoComments, after.TodoComments, "TODO should not grow after agent changes.");
    yield return CompareNonIncreasing("Unmapped targets", before.UnmappedTargets, after.UnmappedTargets, "Unmapped targets should not grow after config edits.");
    yield return CompareNonIncreasing("Unsupported actions", before.UnsupportedActions, after.UnsupportedActions, "Unsupported actions should not grow after config edits.");
    yield return CompareNonIncreasing("Syntax errors", before.SyntaxErrors, after.SyntaxErrors, "Generated/project syntax errors must not regress.");

    if (string.Equals(before.ProjectVerifyStatus, "passed", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(after.ProjectVerifyStatus, "passed", StringComparison.OrdinalIgnoreCase))
    {
        yield return new GuardCheck("Project verify", "failed", $"Project verify regressed: {before.ProjectVerifyStatus} → {after.ProjectVerifyStatus}.", null, null);
    }
    else
    {
        yield return new GuardCheck("Project verify", "passed", $"Project verify status: {before.ProjectVerifyStatus ?? "unknown"} → {after.ProjectVerifyStatus ?? "unknown"}.", null, null);
    }
}

static GuardCheck CompareNonIncreasing(string name, int before, int after, string guidance)
{
    if (after > before)
        return new GuardCheck(name, "failed", $"{name} grew: {before} → {after}. {guidance}", before, after);
    if (after < before)
        return new GuardCheck(name, "passed", $"{name} improved: {before} → {after}.", before, after);
    return new GuardCheck(name, "passed", $"{name} unchanged: {before} → {after}.", before, after);
}

static string ResolveArtifactDirectoryForGuard(string path)
{
    if (Directory.Exists(path))
        return path;
    if (File.Exists(path))
        return Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
    return path;
}

static int SeverityRank(string severity) => severity.Equals("error", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

static void WriteConfigSafetyReport(ConfigSafetyReport report, string outPath, string format)
{
    if (format is "json" or "both")
        File.WriteAllText(Path.Combine(outPath, "config-validate-report.json"), System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    if (format is "text" or "both")
        File.WriteAllText(Path.Combine(outPath, "config-validate-report.md"), WriteConfigSafetyMarkdown(report));
}

static string WriteConfigSafetyMarkdown(ConfigSafetyReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Config Validate Report");
    sb.AppendLine();
    sb.AppendLine($"Status: **{report.Status}**");
    sb.AppendLine($"Validation mode: `{report.ValidationMode}`");
    sb.AppendLine($"Config: `{report.ConfigPath}`");
    sb.AppendLine();
    sb.AppendLine("## Metrics");
    foreach (var m in report.Metrics)
        sb.AppendLine($"- {m.Name}: {m.Value}");
    sb.AppendLine();
    sb.AppendLine("## Issues");
    if (report.Issues.Length == 0)
        sb.AppendLine("No safety issues found.");
    foreach (var issue in report.Issues)
    {
        sb.AppendLine($"- **{issue.Severity.ToUpperInvariant()} {issue.Code}**: {issue.Message}");
        if (!string.IsNullOrWhiteSpace(issue.Location)) sb.AppendLine($"  - Location: `{issue.Location}`");
        if (!string.IsNullOrWhiteSpace(issue.SuggestedAction)) sb.AppendLine($"  - Suggested action: {issue.SuggestedAction}");
    }
    return sb.ToString();
}

static void WriteConfigDiffReport(ConfigDiffReport report, string outPath, string format)
{
    if (format is "json" or "both")
        File.WriteAllText(Path.Combine(outPath, "config-diff-report.json"), System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    if (format is "text" or "both")
        File.WriteAllText(Path.Combine(outPath, "config-diff-report.md"), WriteConfigDiffMarkdown(report));
}

static string WriteConfigDiffMarkdown(ConfigDiffReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Config Diff Report");
    sb.AppendLine();
    sb.AppendLine($"Before: `{report.BeforePath}`");
    sb.AppendLine($"After: `{report.AfterPath}`");
    sb.AppendLine();
    sb.AppendLine("## Summary");
    foreach (var s in report.Summary) sb.AppendLine($"- {s}");
    sb.AppendLine();
    sb.AppendLine("## Risks");
    if (report.Risks.Length == 0) sb.AppendLine("No high-risk changes detected.");
    foreach (var risk in report.Risks)
    {
        var suggestedAction = string.IsNullOrWhiteSpace(risk.SuggestedAction)
            ? string.Empty
            : $" Suggested action: {risk.SuggestedAction}";
        sb.AppendLine($"- **{risk.Severity.ToUpperInvariant()} {risk.Code}**: {risk.Message} ({risk.Location}){suggestedAction}");
    }
    sb.AppendLine();
    sb.AppendLine("## Changes");
    foreach (var change in report.Changes)
        sb.AppendLine($"- {change.Section}: **{change.ChangeType}** `{change.Key}`");
    return sb.ToString();
}

static void WriteGuardReport(GuardReport report, string outPath, string format)
{
    if (format is "json" or "both")
        File.WriteAllText(Path.Combine(outPath, "guard-report.json"), System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    if (format is "text" or "both")
        File.WriteAllText(Path.Combine(outPath, "guard-report.md"), WriteGuardMarkdown(report));
}

static string WriteGuardMarkdown(GuardReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Migration Guard Report");
    sb.AppendLine();
    sb.AppendLine($"Status: **{report.Status}**");
    sb.AppendLine($"Before: `{report.BeforePath}`");
    sb.AppendLine($"After: `{report.AfterPath}`");
    sb.AppendLine();
    sb.AppendLine("## Summary");
    foreach (var s in report.Summary) sb.AppendLine($"- {s}");
    sb.AppendLine();
    sb.AppendLine("## Checks");
    foreach (var check in report.Checks)
        sb.AppendLine($"- **{check.Status.ToUpperInvariant()} {check.Name}**: {check.Message}");
    return sb.ToString();
}


// --- TypeScript Playwright target / verification ---

static (bool IsValid, string[] Messages) ValidateTypeScriptPlaywrightProject(string? tsProjectPath)
{
    var messages = new List<string>();
    if (string.IsNullOrWhiteSpace(tsProjectPath))
    {
        messages.Add("--ts-project is required for --target ts.");
        return (false, messages.ToArray());
    }

    var root = Path.GetFullPath(tsProjectPath);
    if (!Directory.Exists(root))
    {
        messages.Add($"TS project directory not found: {root}");
        return (false, messages.ToArray());
    }

    if (!File.Exists(Path.Combine(root, "package.json")))
        messages.Add("package.json not found in TS project root.");

    if (!Directory.EnumerateFiles(root, "playwright.config.*", SearchOption.TopDirectoryOnly).Any())
        messages.Add("playwright.config.* not found in TS project root.");

    if (!File.Exists(Path.Combine(root, "tsconfig.json")))
        messages.Add("tsconfig.json not found in TS project root.");

    return (messages.Count == 0, messages.ToArray());
}

static int RunVerifyTsProject(string inputPath, string outPath, string format, string? tsProjectPath)
{
    Directory.CreateDirectory(outPath);

    var projectCheck = ValidateTypeScriptPlaywrightProject(tsProjectPath);
    if (!projectCheck.IsValid)
    {
        var failed = new TypeScriptVerifyReport(
            DateTimeOffset.UtcNow,
            "failed",
            inputPath,
            tsProjectPath ?? "",
            Array.Empty<string>(),
            "",
            "",
            "",
            -1,
            "",
            string.Join(Environment.NewLine, projectCheck.Messages),
            projectCheck.Messages,
            projectCheck.Messages.Select(x => new TypeScriptVerifyDiagnostic(
                x,
                "project-context",
                "error",
                "Missing TypeScript Playwright project context",
                "The TS target requires an existing Playwright TypeScript project as compilation context.",
                "Pass --ts-project pointing to a real Playwright TS project with package.json, tsconfig.json and playwright.config.*.")).ToArray());
        WriteTypeScriptVerifyArtifacts(failed, outPath, format);
        return 2;
    }

    var tsProjectRoot = Path.GetFullPath(tsProjectPath!);
    var generatedFiles = FindGeneratedTypeScriptFiles(inputPath).ToArray();
    if (generatedFiles.Length == 0)
    {
        var failed = new TypeScriptVerifyReport(
            DateTimeOffset.UtcNow,
            "failed",
            inputPath,
            tsProjectRoot,
            Array.Empty<string>(),
            "",
            "",
            "",
            -1,
            "",
            "No generated .ts/.spec.ts files found in input.",
            new[] { "No generated .ts/.spec.ts files found in input." },
            new[] { new TypeScriptVerifyDiagnostic(
                "No generated .ts/.spec.ts files found in input.",
                "generated-files",
                "error",
                "Generated files not found",
                "verify-ts-project can only check TypeScript files generated by migrate --target ts.",
                "Run migrate with --target ts first, then pass that migration output folder to verify-ts-project.") });
        WriteTypeScriptVerifyArtifacts(failed, outPath, format);
        return 2;
    }

    var verifyDir = Path.Combine(outPath, "ts-verify");
    Directory.CreateDirectory(verifyDir);
    var copied = new List<string>();
    foreach (var file in generatedFiles)
    {
        var relative = MakeSafeRelativeName(inputPath, file);
        var destination = Path.Combine(verifyDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(file, destination, overwrite: true);
        copied.Add(destination);
    }

    var tsconfigPath = Path.Combine(verifyDir, "tsconfig.migrator.json");
    WriteTypeScriptVerifyTsConfig(tsconfigPath, tsProjectRoot, copied);

    var command = "npx tsc -p " + QuoteForShell(tsconfigPath) + " --noEmit";
    var result = RunSimpleProcess("npx", new[] { "tsc", "-p", tsconfigPath, "--noEmit" }, tsProjectRoot);
    var diagnostics = ExtractTypeScriptDiagnostics(result.StdOut + Environment.NewLine + result.StdErr).ToArray();
    var status = result.ExitCode == 0 ? "passed" : "failed";

    var report = new TypeScriptVerifyReport(
        DateTimeOffset.UtcNow,
        status,
        inputPath,
        tsProjectRoot,
        copied.ToArray(),
        verifyDir,
        tsconfigPath,
        command,
        result.ExitCode,
        result.StdOut,
        result.StdErr,
        diagnostics.Select(x => x.Raw).ToArray(),
        diagnostics);

    WriteTypeScriptVerifyArtifacts(report, outPath, format);
    Console.WriteLine($"TypeScript project verification: {status}");
    Console.WriteLine($"Report written to: {Path.GetFullPath(outPath)}");
    return result.ExitCode == 0 ? 0 : 2;
}

static IEnumerable<string> FindGeneratedTypeScriptFiles(string inputPath)
{
    if (File.Exists(inputPath) && (inputPath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || inputPath.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)))
        return new[] { Path.GetFullPath(inputPath) };

    if (!Directory.Exists(inputPath))
        return Array.Empty<string>();

    var root = Path.GetFullPath(inputPath);
    var candidates = Directory.EnumerateFiles(root, "*.ts", SearchOption.AllDirectories)
        .Where(x => !x.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .Where(x => !Path.GetFileName(x).Equals("tsconfig.migrator.json", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    var specFiles = candidates.Where(x => x.EndsWith(".spec.ts", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase)).ToArray();
    return specFiles.Length > 0 ? specFiles : candidates;
}

static string MakeSafeRelativeName(string basePath, string filePath)
{
    var root = Directory.Exists(basePath) ? Path.GetFullPath(basePath) : Path.GetDirectoryName(Path.GetFullPath(basePath)) ?? Directory.GetCurrentDirectory();
    var full = Path.GetFullPath(filePath);
    var relative = Path.GetRelativePath(root, full);
    if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        relative = Path.GetFileName(full);
    return relative;
}

static void WriteTypeScriptVerifyTsConfig(string path, string tsProjectRoot, IReadOnlyList<string> files)
{
    var projectTsconfig = Path.Combine(tsProjectRoot, "tsconfig.json");
    var extendsPath = NormalizeJsonPath(Path.GetRelativePath(Path.GetDirectoryName(path)!, projectTsconfig));
    var fileItems = files.Select(x => "    " + System.Text.Json.JsonSerializer.Serialize(NormalizeJsonPath(Path.GetRelativePath(Path.GetDirectoryName(path)!, x))));
    var content = $$"""
{
  "extends": "{{extendsPath}}",
  "compilerOptions": {
    "noEmit": true
  },
  "files": [
{{string.Join(",\n", fileItems)}}
  ]
}
""";
    File.WriteAllText(path, content);
}

static string NormalizeJsonPath(string path) => path.Replace('\\', '/');

static IEnumerable<TypeScriptVerifyDiagnostic> ExtractTypeScriptDiagnostics(string text)
{
    foreach (var rawLine in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
    {
        var line = rawLine.Trim();
        if (line.Length == 0)
            continue;
        if (!line.Contains("error TS", StringComparison.OrdinalIgnoreCase)
            && !line.Contains("Cannot find module", StringComparison.OrdinalIgnoreCase)
            && !line.Contains("not found", StringComparison.OrdinalIgnoreCase))
            continue;

        var codeMatch = System.Text.RegularExpressions.Regex.Match(line, @"\bTS\d+\b");
        var code = codeMatch.Success ? codeMatch.Value : "ts-runtime";
        var category = ClassifyTypeScriptDiagnostic(line, code);
        yield return new TypeScriptVerifyDiagnostic(line, code, "error", category.Category, category.LikelyCause, category.SuggestedAction);
    }
}

static (string Category, string LikelyCause, string SuggestedAction) ClassifyTypeScriptDiagnostic(string line, string code)
{
    if (line.Contains("Cannot find module", StringComparison.OrdinalIgnoreCase) || code == "TS2307")
        return ("missing-module-or-import", "Generated TS code imports a helper/module that the TS project cannot resolve.", "Check TS profile imports, tsconfig paths and package/project dependencies.");
    if (line.Contains("Cannot find name", StringComparison.OrdinalIgnoreCase) || code == "TS2304")
        return ("unknown-identifier", "Generated TS code references an identifier that is not declared/imported.", "Add TS-specific mapping/import or keep source-only code as TODO.");
    if (line.Contains("Property", StringComparison.OrdinalIgnoreCase) && line.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        return ("missing-member", "Generated TS code calls a property/method that does not exist on the target type.", "Check mapping target API and TS fixture/helper types.");
    if (line.Contains("is not assignable", StringComparison.OrdinalIgnoreCase) || code == "TS2322" || code == "TS2345")
        return ("type-mismatch", "Generated TS expression has incompatible type/signature.", "Adjust TS mapping, helper signature or generated expression.");
    return ("typescript-compile", "TypeScript compiler reported an error.", "Inspect diagnostic and update TS profile/mapping or project references.");
}

static void WriteTypeScriptVerifyArtifacts(TypeScriptVerifyReport report, string outPath, string format)
{
    Directory.CreateDirectory(outPath);
    if (format is "json" or "both")
        File.WriteAllText(Path.Combine(outPath, "ts-project-verify-report.json"), System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    if (format is "text" or "both")
    {
        File.WriteAllText(Path.Combine(outPath, "ts-project-verify-report.md"), BuildTypeScriptVerifyMarkdown(report));
        File.WriteAllText(Path.Combine(outPath, "agent-ts-verify-next-task.md"), BuildAgentTypeScriptVerifyNextTask(report));
    }
}

static string BuildTypeScriptVerifyMarkdown(TypeScriptVerifyReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# TypeScript Project Verification Report");
    sb.AppendLine();
    sb.AppendLine($"Status: **{report.Status}**");
    sb.AppendLine($"TS project: `{report.TsProjectPath}`");
    sb.AppendLine($"Input: `{report.InputPath}`");
    sb.AppendLine($"Generated files: {report.GeneratedFiles.Length}");
    sb.AppendLine($"Exit code: {report.ExitCode}");
    sb.AppendLine();
    sb.AppendLine("## Diagnostics");
    if (report.ClassifiedDiagnostics.Length == 0)
    {
        sb.AppendLine("No TypeScript diagnostics captured.");
    }
    else
    {
        foreach (var group in report.ClassifiedDiagnostics.GroupBy(x => x.Category).OrderByDescending(x => x.Count()))
        {
            sb.AppendLine($"### {group.Key} ({group.Count()})");
            foreach (var item in group.Take(10))
            {
                sb.AppendLine($"- `{item.Code}` {item.Raw}");
                sb.AppendLine($"  - Cause: {item.LikelyCause}");
                sb.AppendLine($"  - Next: {item.SuggestedAction}");
            }
            sb.AppendLine();
        }
    }
    return sb.ToString();
}

static string BuildAgentTypeScriptVerifyNextTask(TypeScriptVerifyReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Agent next task: TypeScript verification");
    sb.AppendLine();
    sb.AppendLine($"Status: **{report.Status}**");
    sb.AppendLine();
    if (report.Status == "passed")
    {
        sb.AppendLine("TS compile verification passed. Next: run smoke-plan/runtime readiness or execute one selected Playwright test in the real TS project.");
        return sb.ToString();
    }
    sb.AppendLine("## What to do");
    sb.AppendLine("- Do not edit generated .spec.ts manually.");
    sb.AppendLine("- Prefer TS-specific profile/config mappings and imports.");
    sb.AppendLine("- If a missing identifier is source-only Selenium/C# helper, keep it TODO or map it to a real TS helper.");
    sb.AppendLine("- If a module/import is missing, check TS project paths, package deps and profile imports.");
    sb.AppendLine();
    sb.AppendLine("## Top categories");
    foreach (var group in report.ClassifiedDiagnostics.GroupBy(x => x.Category).OrderByDescending(x => x.Count()).Take(5))
        sb.AppendLine($"- {group.Key}: {group.Count()}");
    return sb.ToString();
}

static string QuoteForShell(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

static CliOptions? ParseArgs(string[] args)
{
    string mode = "migrate";
    string? input = null;
    string? outDir = null;
    string? config = null;
    var configs = new List<string>();
    string format = "both";
    string workspace = "migration";
    bool failOnUnsupported = false;
    bool failOnTodo = false;
    string? before = null;
    string? after = null;
    string target = "dotnet";
    string source = "auto";
    bool sourceExplicit = false;
    string? tsProject = null;
    bool recursiveArtifacts = false;
    string irVersion = "legacy";
    string renderIr = "legacy";
    string validationMode = "warn";
    string? targetTestFramework = null;
    bool wizard = false;
    bool? installAgentKit = null;
    bool? targetProjectExists = null;
    string? targetProjectPath = null;
    string? defaultTestIdAttribute = null;
    string? targetNamespace = null;
    string? targetBaseClass = null;
    bool fix = false;
    bool apply = false;
    bool dryRun = false;
    int port = 0;
    bool staticOnly = false;
    var initModeRequested = IsInitModeRequest(args);

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--mode":
                if (i + 1 < args.Length)
                    mode = args[++i];
                else
                {
                    Console.Error.WriteLine($"--mode requires a value: {CliCommandCatalog.FormatModeList()}");
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
                {
                    config = args[++i];
                    configs.Add(config);
                }
                else
                {
                    Console.Error.WriteLine("--config requires a value");
                    return null;
                }
                break;
            case "--before":
                if (i + 1 < args.Length)
                    before = args[++i];
                else
                {
                    Console.Error.WriteLine("--before requires a value");
                    return null;
                }
                break;
            case "--after":
                if (i + 1 < args.Length)
                    after = args[++i];
                else
                {
                    Console.Error.WriteLine("--after requires a value");
                    return null;
                }
                break;
            case "--workspace":
                if (i + 1 < args.Length)
                    workspace = args[++i];
                else
                {
                    Console.Error.WriteLine("--workspace requires a value");
                    return null;
                }
                break;
            case "--target":
                if (i + 1 < args.Length)
                    target = args[++i];
                else
                {
                    Console.Error.WriteLine("--target requires a value: dotnet|playwright-dotnet|ts|playwright-typescript");
                    return null;
                }
                break;
            case "--target-test-framework":
                if (i + 1 < args.Length)
                    targetTestFramework = args[++i];
                else
                {
                    Console.Error.WriteLine("--target-test-framework requires a value: nunit|xunit");
                    return null;
                }
                break;
            case "--fix":
                fix = true;
                break;
            case "--apply":
                fix = true;
                apply = true;
                break;
            case "--dry-run":
                fix = true;
                dryRun = true;
                break;
            case "--port":
                if (i + 1 < args.Length && int.TryParse(args[++i], out var parsedPort) && parsedPort >= 0)
                    port = parsedPort;
                else
                {
                    Console.Error.WriteLine("--port requires a non-negative integer. Use 0 for static dashboard only.");
                    return null;
                }
                break;
            case "--static-only":
            case "--no-server":
                staticOnly = true;
                port = 0;
                break;
            case "--wizard":
                wizard = true;
                break;
            case "--install-kit":
                installAgentKit = true;
                break;
            case "--no-install-kit":
                installAgentKit = false;
                break;
            case "--target-project-exists":
            case "--existing-target":
                targetProjectExists = true;
                break;
            case "--no-target-project":
            case "--generate-scaffold":
                targetProjectExists = false;
                break;
            case "--target-project":
                if (i + 1 < args.Length)
                {
                    targetProjectExists = true;
                    targetProjectPath = args[++i];
                }
                else
                {
                    Console.Error.WriteLine("--target-project requires a value");
                    return null;
                }
                break;
            case "--test-id-attribute":
                if (i + 1 < args.Length)
                    defaultTestIdAttribute = args[++i];
                else
                {
                    Console.Error.WriteLine("--test-id-attribute requires a value, e.g. data-testid or data-tid");
                    return null;
                }
                break;
            case "--target-namespace":
                if (i + 1 < args.Length)
                    targetNamespace = args[++i];
                else
                {
                    Console.Error.WriteLine("--target-namespace requires a value");
                    return null;
                }
                break;
            case "--target-base-class":
                if (i + 1 < args.Length)
                    targetBaseClass = args[++i];
                else
                {
                    Console.Error.WriteLine("--target-base-class requires a value");
                    return null;
                }
                break;
            case "--source":
                if (i + 1 < args.Length)
                {
                    if (initModeRequested)
                    {
                        input = args[++i];
                    }
                    else
                    {
                        source = args[++i];
                        sourceExplicit = true;
                    }
                }
                else
                {
                    Console.Error.WriteLine(initModeRequested
                        ? "--source requires a source path for init --wizard"
                        : "--source requires a value: auto|csharp-selenium|java-selenium|python-selenium");
                    return null;
                }
                break;
            case "--ts-project":
                if (i + 1 < args.Length)
                    tsProject = args[++i];
                else
                {
                    Console.Error.WriteLine("--ts-project requires a value");
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
            case "--ir-version":
                if (i + 1 < args.Length)
                    irVersion = args[++i].Trim().ToLowerInvariant();
                else
                {
                    Console.Error.WriteLine("--ir-version requires a value: legacy|v2|both");
                    return null;
                }
                break;
            case "--render-ir":
                if (i + 1 < args.Length)
                    renderIr = args[++i].Trim().ToLowerInvariant();
                else
                {
                    Console.Error.WriteLine("--render-ir requires a value: legacy|v2");
                    return null;
                }
                break;
            case "--validation-mode":
                if (i + 1 < args.Length)
                    validationMode = args[++i].Trim().ToLowerInvariant();
                else
                {
                    Console.Error.WriteLine("--validation-mode requires a value: warn|strict|production");
                    return null;
                }
                break;
            case "--help":
            case "-h":
                CliCommandCatalog.WriteGlobalHelp();
                return null;
            case "--fail-on-unsupported":
                failOnUnsupported = true;
                break;
            case "--fail-on-todo":
                failOnTodo = true;
                break;
            case "--recursive-artifacts":
            case "--recursive":
                recursiveArtifacts = true;
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

    if (!CliCommandCatalog.IsValidMode(mode))
    {
        Console.Error.WriteLine($"Invalid mode: {mode}. Use: {CliCommandCatalog.FormatModeList()}");
        return null;
    }

    if ((mode == "config-validate" || mode == "config-normalize") && string.IsNullOrEmpty(input) && configs.Count > 0)
        input = configs[^1];

    if ((mode == "config-diff" || mode == "guard") && (string.IsNullOrEmpty(before) || string.IsNullOrEmpty(after)))
    {
        Console.Error.WriteLine($"--before and --after are required for {mode}");
        PrintHelp();
        return null;
    }

    if (CliCommandCatalog.RequiresInput(mode) && string.IsNullOrEmpty(input))
    {
        Console.Error.WriteLine("--input is required");
        CliCommandCatalog.WriteCommandHelp(mode);
        return null;
    }

    if ((mode == "config-validate" || mode == "config-normalize") && string.IsNullOrEmpty(input) && configs.Count == 0)
    {
        Console.Error.WriteLine("--config or --input is required");
        CliCommandCatalog.WriteCommandHelp(mode);
        return null;
    }

    ITargetBackend parsedTargetBackend;
    try
    {
        parsedTargetBackend = CreateBuiltInTargetBackendRegistry().Resolve(target);
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return null;
    }

    targetTestFramework = NormalizeTargetTestFramework(targetTestFramework);
    if (targetTestFramework == null && args.Any(arg => string.Equals(arg, "--target-test-framework", StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine("Invalid --target-test-framework. Use: nunit|xunit");
        return null;
    }

    if (!string.Equals(source, "auto", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            _ = CreateBuiltInSourceFrontendRegistry(null).Resolve(source);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return null;
        }
    }

    if (string.IsNullOrEmpty(workspace))
    {
        Console.Error.WriteLine("--workspace must not be empty");
        return null;
    }

    if (string.IsNullOrEmpty(outDir))
        outDir = mode.Equals("init", StringComparison.OrdinalIgnoreCase)
            ? workspace
            : CliCommandCatalog.Get(mode).DefaultOut;

    outDir = ResolveOutputDirectory(outDir, workspace);

    if (format != "text" && format != "json" && format != "both")
    {
        Console.Error.WriteLine($"Invalid format: {format}. Use: text|json|both");
        return null;
    }

    if (irVersion != "legacy" && irVersion != "v2" && irVersion != "both")
    {
        Console.Error.WriteLine($"Invalid IR version: {irVersion}. Use: legacy|v2|both");
        return null;
    }

    if (renderIr != "legacy" && renderIr != "v2")
    {
        Console.Error.WriteLine($"Invalid render IR mode: {renderIr}. Use: legacy|v2");
        return null;
    }

    if (validationMode != "warn" && validationMode != "strict" && validationMode != "production")
    {
        Console.Error.WriteLine($"Invalid validation mode: {validationMode}. Use: warn|strict|production");
        return null;
    }

    if (renderIr == "v2" && mode == "orchestrate")
    {
        Console.Error.WriteLine("--render-ir v2 is experimental and not supported with orchestrate yet. Use --mode migrate or verify first.");
        return null;
    }

    if (apply && dryRun)
    {
        Console.Error.WriteLine("Use either --apply or --dry-run with doctor --fix, not both.");
        return null;
    }

    if (fix && !mode.Equals("doctor", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("--fix/--apply/--dry-run are only supported for --mode doctor.");
        return null;
    }

    if (port > 0 && !mode.Equals("report-serve", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("--port is only supported for report serve / --mode report-serve.");
        return null;
    }

    return new CliOptions(mode, input ?? "", outDir, config, configs.ToArray(), format, failOnUnsupported, failOnTodo, workspace, before, after, target, source, sourceExplicit, tsProject, recursiveArtifacts, irVersion, renderIr, validationMode, targetTestFramework, wizard, installAgentKit, targetProjectExists, targetProjectPath, defaultTestIdAttribute, targetNamespace, targetBaseClass, fix, apply, dryRun, port, staticOnly);
}

static string[] NormalizeDirectCommand(string[] args)
{
    if (args.Length == 0)
        return args;

    if (string.Equals(args[0], "init", StringComparison.OrdinalIgnoreCase))
        return new[] { "--mode", "init" }.Concat(args.Skip(1)).ToArray();

    if (string.Equals(args[0], "report", StringComparison.OrdinalIgnoreCase)
        && args.Length > 1
        && string.Equals(args[1], "serve", StringComparison.OrdinalIgnoreCase))
    {
        return new[] { "--mode", "report-serve" }.Concat(args.Skip(2)).ToArray();
    }

    return args;
}

static bool IsInitModeRequest(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], "--mode", StringComparison.OrdinalIgnoreCase)
            && string.Equals(args[i + 1], "init", StringComparison.OrdinalIgnoreCase))
            return true;
    }

    return false;
}

static string ResolveOutputDirectory(string outDir, string workspace)
{
    if (Path.IsPathRooted(outDir) || LooksLikeWindowsRootedPath(outDir))
        return outDir;

    var normalizedOut = NormalizeRelativeCliPath(outDir);
    var normalizedWorkspace = NormalizeRelativeCliPath(workspace);

    if (normalizedOut == ".")
        return workspace;

    if (normalizedWorkspace == ".")
        return outDir;

    if (normalizedOut.Equals(normalizedWorkspace, StringComparison.OrdinalIgnoreCase)
        || normalizedOut.StartsWith(normalizedWorkspace + "/", StringComparison.OrdinalIgnoreCase))
        return outDir;

    if (normalizedOut.StartsWith("../", StringComparison.Ordinal)
        || normalizedOut.Contains("/../", StringComparison.Ordinal))
    {
        var safeName = normalizedOut
            .Replace("../", "up-", StringComparison.Ordinal)
            .Replace("/../", "/up-", StringComparison.Ordinal)
            .Replace("..", "up", StringComparison.Ordinal);
        return Path.Combine(workspace, safeName);
    }

    return Path.Combine(workspace, outDir);
}

static string NormalizeRelativeCliPath(string path)
{
    var normalized = path.Replace('\\', '/').Trim();
    while (normalized.StartsWith("./", StringComparison.Ordinal))
        normalized = normalized[2..];
    return string.IsNullOrWhiteSpace(normalized) ? "." : normalized.TrimEnd('/');
}

static bool LooksLikeWindowsRootedPath(string path)
{
    return path.Length >= 3
        && char.IsLetter(path[0])
        && path[1] == ':'
        && (path[2] == '\\' || path[2] == '/');
}


// --- Config schema mode ---











static void PrintHelp()
{
    CliCommandCatalog.WriteGlobalHelp();
}

static bool IsHelpRequest(string[] args) => args.Any(arg => arg is "--help" or "-h" or "help");

static string? FindOptionValue(string[] args, string optionName)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }

    return null;
}


sealed class NormalizedTodoGroupBuilder
{
    public string Category { get; }
    public string GroupKey { get; }
    public string DisplayName { get; }
    public string SuggestedAction { get; }
    public int Count { get; set; }
    public string ExampleFile { get; set; } = "";
    public int ExampleLine { get; set; }
    public HashSet<string> RepresentativeFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Evidence { get; } = new();

    public NormalizedTodoGroupBuilder(string category, string groupKey, string displayName, string suggestedAction)
    {
        Category = category;
        GroupKey = groupKey;
        DisplayName = displayName;
        SuggestedAction = suggestedAction;
    }

    public NormalizedTodoGroup ToGroup() => new(
        Category,
        GroupKey,
        DisplayName,
        Count,
        ExampleFile,
        ExampleLine,
        SuggestedAction,
        RepresentativeFiles.Take(5).ToArray(),
        Evidence.Take(5).ToArray());
}

sealed class TableMappingCandidateBuilder
{
    public string GroupKey { get; }
    public string SourceRoot { get; }
    public string AccessorKind { get; }
    public string AssertionKind { get; }
    public string SuggestedUiTargetRoot { get; }
    public string SuggestedConfigHint { get; }
    public int Count { get; set; }
    public string ExampleFile { get; set; } = "";
    public int ExampleLine { get; set; }
    public string SourceExpression { get; set; } = "";
    public List<string> Evidence { get; } = new();

    public TableMappingCandidateBuilder(string groupKey, string sourceRoot, string accessorKind, string assertionKind, string suggestedUiTargetRoot, string suggestedConfigHint)
    {
        GroupKey = groupKey;
        SourceRoot = sourceRoot;
        AccessorKind = accessorKind;
        AssertionKind = assertionKind;
        SuggestedUiTargetRoot = suggestedUiTargetRoot;
        SuggestedConfigHint = suggestedConfigHint;
    }

    public TableMappingCandidate ToCandidate() => new(
        GroupKey,
        SourceRoot,
        AccessorKind,
        AssertionKind,
        Count,
        ExampleFile,
        ExampleLine,
        SourceExpression,
        SuggestedUiTargetRoot,
        SuggestedConfigHint,
        Evidence.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray());
}

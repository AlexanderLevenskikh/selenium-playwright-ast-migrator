using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
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
string[] configPaths = opts.Configs;
string? primaryConfigPath = configPaths.Length > 0 ? configPaths[^1] : null;
ProjectAdapterConfig? loadedConfig = null;
string format = opts.Format;
bool failOnUnsupported = opts.FailOnUnsupported;
bool failOnTodo = opts.FailOnTodo;
string? beforePath = opts.Before;
string? afterPath = opts.After;

// Agent-safety modes operate on config/report artifacts and do not process source files.
if (mode == "config-validate")
{
    var validateInputs = configPaths.Length > 0
        ? configPaths
        : (!string.IsNullOrWhiteSpace(inputPath) ? new[] { inputPath } : Array.Empty<string>());
    var validateExitCode = RunConfigValidate(validateInputs, outPath, format);
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

if (mode != "discover-target" && mode != "scaffold" && mode != "bootstrap-project" && mode != "config-validate" && mode != "config-diff" && mode != "guard" && !File.Exists(inputPath) && !Directory.Exists(inputPath))
{
    Console.Error.WriteLine($"Input not found: {inputPath}");
    return 1;
}

if (mode == "discover-target" && !Directory.Exists(inputPath))
{
    Console.Error.WriteLine($"Discover-target mode expects a directory (target Playwright project): {inputPath}");
    return 2;
}

var parser = new RoslynTestFileParser();
var renderer = new PlaywrightDotNetRenderer();

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

// Handle explain-todo mode — explains migration TODO/root causes from existing artifacts.
if (mode == "explain-todo")
{
    var explainExitCode = RunExplainTodo(inputPath, outPath, format);
    return explainExitCode;
}

// Handle smoke-plan mode — ranks generated tests by runtime readiness and writes checklists.
if (mode == "smoke-plan")
{
    var smokeExitCode = RunSmokePlan(inputPath, outPath, format);
    return smokeExitCode;
}

// Handle scaffold mode — generates a minimal, compile-ready Playwright .NET test project
if (mode == "scaffold")
{
    var scaffoldResult = new ScaffoldWriter(new ScaffoldOptions { OutPath = outPath, Format = format }).Write();
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
    var orchestrateExitCode = RunOrchestrate(inputPath, outPath, primaryConfigPath, format, parser, renderer, adapter, loadedConfig);
    return orchestrateExitCode;
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
        RunAnalyze(summary, outPath, format, loadedConfig, resultsList, allUnmapped);
        break;
    case "migrate":
        RunMigrate(summary, outPath, format, loadedConfig, resultsList, allUnmapped);
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

static void RunMigrate(MigrationSummaryReport summary, string outPath, string format, ProjectAdapterConfig? config, List<PipelineResult> results, IReadOnlyDictionary<string, (int Count, string File, int Line)> allUnmapped)
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
    GenerateDraftConfig(allUnmapped, outPath, config);
    WriteExplainTodoArtifacts(summaryWithGenerated, outPath, format, allUnmapped, allUnsupported, null);
    WriteSmokePlanArtifacts(outPath, outPath, format);

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
    var packageReferences = BuildPackageReferences(verification, projectReferences).ToList();
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

static IEnumerable<PackageReferenceConfig> BuildPackageReferences(VerificationConfig verification, IReadOnlyList<string> projectReferences)
{
    var result = new List<PackageReferenceConfig>();
    if (verification.DisableDefaultPackageReferences != true)
    {
        result.Add(new PackageReferenceConfig { Include = "Microsoft.Playwright.NUnit", Version = "1.52.0" });
        result.Add(new PackageReferenceConfig { Include = "NUnit", Version = "3.14.0" });
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

    return refs.Values;
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


static int RunExplainTodo(string inputPath, string outPath, string format)
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

    var report = BuildExplainTodoReportFromArtifacts(artifactDir);
    WriteExplainTodoReport(report, outPath, format);

    Console.WriteLine("=== Explain TODO ===");
    Console.WriteLine($"Input artifacts: {artifactDir}");
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
        File.WriteAllText(Path.Combine(outPath, "agent-next-task.md"), WriteAgentNextTaskMarkdown(report));
    }
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
        NextBestAction: ordered.FirstOrDefault()?.SuggestedAction ?? "No TODO/actionable issue found. Move to project verify or runtime smoke.");
}

static TodoExplanationReport BuildExplainTodoReportFromArtifacts(string artifactDir)
{
    var reportPath = FindFirstExisting(artifactDir, "report.json");
    var unmappedPath = FindFirstExisting(artifactDir, "unmapped-targets.json");
    var unsupportedPath = FindFirstExisting(artifactDir, "unsupported-actions.json");
    var verifyPath = FindFirstExisting(artifactDir, "verify-report.json");
    var projectVerifyPath = FindFirstExisting(artifactDir, "project-verify-report.json");

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

    var fakeSummary = new MigrationSummaryReport(
        FilesProcessed: summary.FilesProcessed,
        TestsFound: summary.TestsFound,
        ActionsFound: summary.ActionsFound,
        SemanticActions: summary.SemanticActions,
        SyntaxFallbackActions: summary.SyntaxFallbackActions,
        UnsupportedActions: summary.UnsupportedActions,
        MappedTargets: summary.MappedTargets,
        UnmappedTargets: summary.UnmappedTargets,
        TodoComments: summary.TodoComments,
        FilesWithWarnings: 0,
        GeneratedFiles: 0,
        ProcessedFiles: Array.Empty<string>(),
        TopUnmappedTargets: Array.Empty<UnmappedTargetInfo>(),
        TopUnsupportedActions: Array.Empty<UnsupportedMethodInfo>(),
        PerFileReports: Array.Empty<MigrationReport>());

    var built = BuildExplainTodoReport(fakeSummary, unmapped, unsupported, projectVerify);
    return built with
    {
        Source = artifactDir,
        SyntaxErrors = Math.Max(built.SyntaxErrors, summary.SyntaxErrors),
        ProjectVerifyStatus = projectVerify?.Status ?? summary.VerifyStatus
    };
}

static string? FindFirstExisting(string dir, string fileName)
{
    var direct = Path.Combine(dir, fileName);
    if (File.Exists(direct))
        return direct;

    return Directory.EnumerateFiles(dir, fileName, SearchOption.AllDirectories)
        .OrderBy(p => p.Length)
        .FirstOrDefault();
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

static string WriteAgentNextTaskMarkdown(TodoExplanationReport report)
{
    var first = report.Insights.FirstOrDefault();
    var sb = new StringBuilder();
    sb.AppendLine("# Agent Next Task");
    sb.AppendLine();
    sb.AppendLine("Ты продолжаешь миграцию Selenium C# → Playwright .NET через AST Migrator.");
    sb.AppendLine();
    sb.AppendLine("## Текущий статус");
    sb.AppendLine();
    sb.AppendLine($"- TODO: `{report.TodoComments}`");
    sb.AppendLine($"- Unmapped targets: `{report.UnmappedTargets}`");
    sb.AppendLine($"- Unsupported actions: `{report.UnsupportedActions}`");
    sb.AppendLine($"- Project verify: `{report.ProjectVerifyStatus ?? "not-run"}`");
    sb.AppendLine();
    sb.AppendLine("## Следующий рекомендуемый шаг");
    sb.AppendLine();
    if (first == null)
    {
        sb.AppendLine("Явных TODO/root-cause проблем не найдено. Запусти verify-project или выбери runtime smoke candidates.");
    }
    else
    {
        sb.AppendLine($"Категория: `{first.Category}`");
        sb.AppendLine();
        sb.AppendLine($"Задача: **{first.Title}**");
        sb.AppendLine();
        sb.AppendLine($"Почему: {first.Reason}");
        sb.AppendLine();
        sb.AppendLine($"Что сделать: {first.SuggestedAction}");
        if (!string.IsNullOrWhiteSpace(first.ExampleFile))
            sb.AppendLine($"\nПример: `{PathRedaction.Redact(first.ExampleFile)}:{first.ExampleLine}`");
    }
    sb.AppendLine();
    sb.AppendLine("## Ограничения");
    sb.AppendLine();
    sb.AppendLine("- Не меняй исходный проект.");
    sb.AppendLine("- Не редактируй generated `.cs` вручную.");
    sb.AppendLine("- По умолчанию меняй только `adapter-config.json`.");
    sb.AppendLine("- Если нужен C# fix мигратора — остановись и сформируй escalation report.");
    sb.AppendLine("- Если нужен selector/POM — найди source truth; не угадывай молча.");
    sb.AppendLine();
    sb.AppendLine("## Формат ответа");
    sb.AppendLine();
    sb.AppendLine("Отвечай на русском. После этапа покажи метрики до/после и спроси: `Продолжить?`");
    return sb.ToString();
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

static int RunSmokePlan(string inputPath, string outPath, string format)
{
    if (!Directory.Exists(inputPath))
    {
        Console.Error.WriteLine($"Smoke-plan mode expects a directory with migration artifacts: {inputPath}");
        return 1;
    }

    Directory.CreateDirectory(outPath);
    var report = BuildSmokePlanReportFromArtifacts(inputPath);
    WriteSmokePlanReport(report, outPath, format);

    Console.WriteLine("=== Smoke Plan Summary ===");
    Console.WriteLine($"Source: {inputPath}");
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

static SmokePlanReport BuildSmokePlanReportFromArtifacts(string artifactDir)
{
    var projectVerifyPath = FindFirstExisting(artifactDir, "project-verify-report.json");
    var projectVerify = projectVerifyPath != null ? ReadProjectVerifyReport(projectVerifyPath) : null;
    var explainPath = FindFirstExisting(artifactDir, "explain-todo.json");
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
            NextBestAction: ReadString(root, "NextBestAction") ?? "Run smoke-plan or explain-todo.");
    }
    catch
    {
        return null;
    }
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
    sb.AppendLine("- Если runtime failure связан с мигратором — сформируй escalation report.");
    sb.AppendLine("- После этапа дай отчёт на русском и спроси: `Продолжить?`");
    return sb.ToString();
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

static int GetLineNumber(string text, int index)
{
    var line = 1;
    for (int i = 0; i < Math.Min(index, text.Length); i++)
        if (text[i] == '\n') line++;
    return line;
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
        SourceOnlyIdentifiers: new[] { "page", "pagef", "Driver", "WebDriver" },
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
    sb.AppendLine("- `docs/agent-safety.md`");
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
    sb.AppendLine("- остановись и спроси `Продолжить?`.");
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

static int RunOrchestrate(string inputPath, string outPath, string? configPath, string format, ITestFileParser parser, IRenderer renderer, IProjectAdapter? adapter, ProjectAdapterConfig? config)
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
                    string baseName = $"{result.SourceModel.ClassName}Playwright.cs";
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

    // Write orchestration reports
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


static int RunConfigValidate(string[] configPaths, string outPath, string format)
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
        issues.AddRange(AnalyzeConfigSafety(config));

    var configPathLabel = string.Join(" -> ", configPaths.Select(Path.GetFullPath));
    var report = new ConfigSafetyReport(
        GeneratedAtUtc: DateTimeOffset.UtcNow,
        ConfigPath: configPathLabel,
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
        Console.Error.WriteLine("config-diff requires --before <old-adapter-config.json> --after <new-adapter-config.json>.");
        return 2;
    }

    if (!File.Exists(beforePath) || !File.Exists(afterPath))
    {
        Console.Error.WriteLine($"Config diff input not found. before={beforePath}, after={afterPath}");
        return 2;
    }

    Directory.CreateDirectory(outPath);
    var before = ConfigValidator.ValidateJson(File.ReadAllText(beforePath), beforePath);
    var after = ConfigValidator.ValidateJson(File.ReadAllText(afterPath), afterPath);

    var changes = BuildConfigChanges(before, after).ToArray();
    var risks = BuildConfigDiffRisks(before, after).ToArray();
    var report = new ConfigDiffReport(
        GeneratedAtUtc: DateTimeOffset.UtcNow,
        BeforePath: Path.GetFullPath(beforePath),
        AfterPath: Path.GetFullPath(afterPath),
        Changes: changes,
        Risks: risks,
        Summary: new[]
        {
            $"UiTargets: {before.UiTargets.Length} → {after.UiTargets.Length}",
            $"Methods: {before.Methods.Length} → {after.Methods.Length}",
            $"ParameterizedMethods: {before.ParameterizedMethods.Length} → {after.ParameterizedMethods.Length}",
            $"SourceOnlyIdentifiers: {before.SourceOnlyIdentifiers.Length} → {after.SourceOnlyIdentifiers.Length}",
            $"TargetKnownTypes: {before.TargetKnownTypes.Length} → {after.TargetKnownTypes.Length}",
            $"TargetKnownIdentifiers: {before.TargetKnownIdentifiers.Length} → {after.TargetKnownIdentifiers.Length}"
        });

    WriteConfigDiffReport(report, outPath, format);

    Console.WriteLine("=== Config Diff ===");
    Console.WriteLine($"Changes: {report.Changes.Length}");
    Console.WriteLine($"Risks: {report.Risks.Length}");
    foreach (var risk in report.Risks.Take(20))
        Console.WriteLine($"[RISK] {risk.Code}: {risk.Message}" + (string.IsNullOrWhiteSpace(risk.Location) ? "" : $" ({risk.Location})"));
    Console.WriteLine($"Reports written to: {Path.GetFullPath(outPath)}");
    return risks.Any(r => r.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)) ? 2 : 0;
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
        new ConfigMetric("TargetKnownIdentifiers", config.TargetKnownIdentifiers.Length)
    };
}

static IEnumerable<ConfigSafetyIssue> AnalyzeConfigSafety(ProjectAdapterConfig config)
{
    var issues = new List<ConfigSafetyIssue>();
    var forbiddenTargetKnown = new HashSet<string>(new[] { "page", "pagef", "driver", "webdriver" }, StringComparer.OrdinalIgnoreCase);
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
            "Keep page/pagef/Driver/WebDriver source-only, or map the whole expression through adapter-config."));
    }

    AddDuplicateIssues(config.UiTargets.Select(x => x.SourceExpression), "UiTargets.SourceExpression", "DUPLICATE_UI_TARGET", issues);
    AddDuplicateIssues(config.Methods.Select(x => x.SourceMethod), "Methods.SourceMethod", "DUPLICATE_METHOD_MAPPING", issues);
    AddDuplicateIssues(config.ParameterizedMethods.Select(x => x.SourceMethodPattern), "ParameterizedMethods.SourceMethodPattern", "DUPLICATE_PARAMETERIZED_METHOD_MAPPING", issues);
    AddDuplicateIssues(config.SourceOnlyIdentifiers, "SourceOnlyIdentifiers", "DUPLICATE_SOURCE_ONLY_IDENTIFIER", issues);
    AddDuplicateIssues(config.TargetKnownTypes, "TargetKnownTypes", "DUPLICATE_TARGET_KNOWN_TYPE", issues);
    AddDuplicateIssues(config.TargetKnownIdentifiers, "TargetKnownIdentifiers", "DUPLICATE_TARGET_KNOWN_IDENTIFIER", issues);

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
    var forbiddenTargetKnown = new HashSet<string>(new[] { "page", "pagef", "driver", "webdriver" }, StringComparer.OrdinalIgnoreCase);
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
        sb.AppendLine($"- **{risk.Severity.ToUpperInvariant()} {risk.Code}**: {risk.Message} ({risk.Location})");
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

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--mode":
                if (i + 1 < args.Length)
                    mode = args[++i];
                else
                {
                    Console.Error.WriteLine("--mode requires a value: analyze|migrate|verify|verify-project|propose");
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

    if (mode != "analyze" && mode != "migrate" && mode != "verify" && mode != "verify-project" && mode != "explain-todo" && mode != "smoke-plan" && mode != "config-validate" && mode != "config-diff" && mode != "guard" && mode != "propose" && mode != "discover-target" && mode != "index-pom" && mode != "orchestrate" && mode != "scaffold" && mode != "bootstrap-project")
    {
        Console.Error.WriteLine($"Invalid mode: {mode}. Use: analyze|migrate|verify|verify-project|explain-todo|smoke-plan|config-validate|config-diff|guard|propose|discover-target|index-pom|orchestrate|scaffold|bootstrap-project");
        return null;
    }

    if (mode == "config-validate" && string.IsNullOrEmpty(input) && configs.Count > 0)
        input = configs[^1];

    if ((mode == "config-diff" || mode == "guard") && (string.IsNullOrEmpty(before) || string.IsNullOrEmpty(after)))
    {
        Console.Error.WriteLine($"--before and --after are required for {mode}");
        PrintHelp();
        return null;
    }

    if (mode != "scaffold" && mode != "bootstrap-project" && mode != "config-diff" && mode != "guard" && string.IsNullOrEmpty(input))
    {
        Console.Error.WriteLine(mode == "config-validate" ? "--config or --input is required" : "--input is required");
        PrintHelp();
        return null;
    }

    if (string.IsNullOrEmpty(workspace))
    {
        Console.Error.WriteLine("--workspace must not be empty");
        return null;
    }

    if (string.IsNullOrEmpty(outDir))
    {
        outDir = mode switch
        {
            "analyze" => "analysis",
            "migrate" => "generated-tests",
            "verify" => "verify",
            "verify-project" => "verify-project",
            "explain-todo" => "explain-todo",
            "smoke-plan" => "smoke-plan",
            "config-validate" => "config-validate",
            "config-diff" => "config-diff",
            "guard" => "guard",
            "propose" => "mapping-proposals",
            "discover-target" => "target-discovery",
            "index-pom" => "pom-index",
            "orchestrate" => "orchestration",
            "scaffold" => "generated-scaffold",
            "bootstrap-project" => "project-bootstrap",
            _ => "output"
        };
    }

    outDir = ResolveOutputDirectory(outDir, workspace);

    if (format != "text" && format != "json" && format != "both")
    {
        Console.Error.WriteLine($"Invalid format: {format}. Use: text|json|both");
        return null;
    }

    return new CliOptions(mode, input ?? "", outDir, config, configs.ToArray(), format, failOnUnsupported, failOnTodo, workspace, before, after);
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

static void PrintHelp()
{
    Console.WriteLine(@"
Usage: Migrator.Cli --mode <mode> --input <path> [options]

Modes:
  analyze         Parse and analyze Selenium tests without generating output files.
                    Produces reports and draft adapter-config.
  migrate         Parse, adapt, and generate Playwright C# files. Produces reports.
  verify          Validate generated code quality. Runs Roslyn syntax check,
                    TODO/placeholder detection, config validation, scope matching,
                    and quality gate evaluation. Outputs verify-report.json and
                    verify-report.txt.
  verify-project  Project-aware verification. Generates files into --out/generated,
                    creates a temporary verification .csproj, adds project/package
                    references from adapter-config Verification, auto-discovers nearest
                    .csproj/transitive ProjectReference/build props when enabled,
                    runs dotnet build, and classifies build diagnostics.
                    Does NOT modify source project files.
  explain-todo    Explain remaining TODO/root causes from existing migration artifacts.
                    Reads report.json, unmapped-targets.json, unsupported-actions.json,
                    verify-report.json, project-verify-report.json when present. Outputs
                    explain-todo.md/json and agent-next-task.md. Does NOT modify config.
  smoke-plan      Rank generated tests by runtime readiness. Reads generated .cs files,
                    report.json, explain-todo.json, and project-verify-report.json when
                    present. Outputs smoke-plan.md/json, runtime-checklist.md, and
                    agent-runtime-next-task.md. Does NOT run tests.
  config-validate Validate adapter-config structure and agent-safety rules. Outputs
                    config-validate-report.md/json. Fails on dangerous config.
  config-diff     Compare two adapter-config files and highlight risky agent changes.
                    Requires --before and --after. Outputs config-diff-report.md/json.
  guard           Compare two migration artifact directories and fail on regressions
                    such as increased TODO/syntax errors. Requires --before and --after.
  propose         Analyze migration artifacts (reports, generated output) and
                    generate mapping proposals. Reads report.json, unmapped-targets.json,
                    unsupported-actions.json, verify-report.json. Outputs
                    mapping-proposals.md and mapping-proposals.json. Does NOT modify config.
  discover-target Scan a target Playwright .NET project and collect infrastructure
                    facts. Outputs target-inventory.json, target-style-notes.md,
                    adapter-config.draft.json, and discovery-warnings.txt.
                    Does NOT modify config. Collects facts only.
  index-pom      Scan Selenium PageObjects/source files and collect source-truth facts.
                    Outputs pom-index.generated.json/md, inferred-pom-candidates.json,
                    and adapter-config.pom-draft.json. Does NOT modify config.
                    Missing POMs are emitted as inferred candidates requiring review.
  orchestrate     Dry-run orchestration mode. Runs analyze → migrate → verify → propose
                     in sequence, writes stage artifacts into subdirectories, and produces
                     orchestration-report.md and orchestration-report.json. Does NOT modify
                     adapter config, does NOT auto-apply proposals, does NOT run runtime tests.
  scaffold        Generate a minimal, compile-ready Playwright .NET test project scaffold
                     with draft adapter config. Creates .csproj, GeneratedTestBase,
                     TestSettings, ExampleSmokeTest, adapter-config.draft.json, README,
                     and .gitignore. Does NOT require --input. Outputs scaffold-report.
  bootstrap-project
                  Create reusable migration profile skeletons for a new project:
                     profiles/infrastructure-base.adapter.json,
                     profiles/projects/<project>.adapter.json, migration-profile-plan.md,
                     and agent-next-task.md. Does NOT modify source project files.

Options:
    --mode <mode>                 Operation mode (required)
                                    analyze|migrate|verify|verify-project|explain-todo|smoke-plan|config-validate|config-diff|guard|propose|discover-target|index-pom|orchestrate|scaffold|bootstrap-project
    --input <file-or-directory>   Input .cs file or directory (required).
                                    For propose/explain-todo: directory with report files.
                                    For discover-target: target Playwright project root.
                                    For index-pom: Selenium project/PageObject directory.
                                    For orchestrate: source Selenium tests directory.
                                    For verify-project: source Selenium tests directory;
                                      project refs come from adapter-config Verification,
                                      nearest .csproj auto-discovery, and transitive
                                      ProjectReference discovery when enabled.
                                    For scaffold/bootstrap-project: not required.
   --out <output-directory>      Output directory inside --workspace by default.
                                  Relative paths like orchestration-7 become migration/orchestration-7.
                                  Use an absolute path to write outside workspace.
   --workspace <directory>        Migration artifacts root (default: migration).
                                  All relative --out paths are kept under this root.
   --config <adapter-config.json>  Adapter config layer. Can be repeated.
                                  Layers are merged left-to-right; later/project configs override base profiles.
   --before <path>                Previous config/artifact path for config-diff/guard
   --after <path>                 New config/artifact path for config-diff/guard
   --format <text|json|both>     Report format (default: both)
   --fail-on-unsupported         Exit code 2 if unsupported actions exist
   --fail-on-todo                Exit code 3 if TODO comments exist
   --help, -h                    Show this help

Exit codes:
  0  Success (all stages passed, verify passed)
  1  Orchestration completed but verify/quality gates failed
  2  --fail-on-unsupported triggered / invalid config / input not found
  3  analyze/migrate stage failed / --fail-on-todo triggered
  4  Generated syntax errors detected

Examples:
  Migrator.Cli --mode analyze --input ./OldTests --out analysis --format both
  Migrator.Cli --mode migrate --input ./OldTests --out generated --config ./profiles/infrastructure-base.adapter.json --config ./profiles/projects/oldtests.adapter.json
  Migrator.Cli --mode verify-project --input ./OldTests --out verify-project --config ./profiles/infrastructure-base.adapter.json --config ./profiles/projects/oldtests.adapter.json
  Migrator.Cli --mode explain-todo --input migration/verify-project --out explain-todo --format both
  Migrator.Cli --mode smoke-plan --input migration/verify-project --out smoke-plan --format both
  Migrator.Cli --mode config-validate --config ./adapter-config.json --out config-validate
  Migrator.Cli --mode config-diff --before adapter.old.json --after adapter-config.json --out config-diff
  Migrator.Cli --mode guard --before migration/baseline --after migration/current --out guard
  Migrator.Cli --mode propose --input migration/generated --config ./adapter-config.json --format both
   Migrator.Cli --mode discover-target --input ./team-playwright-tests --out target-discovery
   Migrator.Cli --mode index-pom --input ./OldTests --out pom-index --format both
   Migrator.Cli --mode orchestrate --input ./OldTests --config ./adapter-config.json --out orchestration --format both
   Migrator.Cli --mode scaffold --out generated-scaffold
   Migrator.Cli --mode bootstrap-project --input ./OldTests --out bootstrap-oldtests

Output workspace examples:
  --out orchestration-7            writes to migration/orchestration-7
  --out migration/custom-run       writes to migration/custom-run
  --out C:\temp\migration-run     writes to absolute path C:\temp\migration-run
");
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

record GeneratedTestMethodStats(string File, string TestName, int StartLine, int ActiveLines, int TodoLines, int ExecutableLines, double ActiveRatio, int AwaitCount, int ExpectOrAssertCount, int LocatorCount);
record SmokePlanReport(DateTimeOffset GeneratedAtUtc, string Source, string? ProjectVerifyStatus, int GeneratedFiles, int TestsFound, int RuntimeReadyCandidates, int SmokeCandidates, SmokeCandidate[] Candidates, string[] RecommendedNextActions);
record SmokeCandidate(string File, string TestName, int StartLine, int ActiveLines, int TodoLines, double ActiveRatio, int CompileErrors, int CompileWarnings, int AwaitCount, int ExpectOrAssertCount, int LocatorCount, double Score, string ReadinessLevel, string[] Checklist);

record PomIndexReport(DateTimeOffset GeneratedAtUtc, string InputPath, int FilesScanned, PomFact[] Facts, PomUsageCandidate[] InferredCandidates, string[] Warnings);
record PomFact(string SourceExpression, string OwnerType, string MemberName, string MemberKind, string Selector, string SelectorKind, string TargetKindSuggestion, string TargetExpressionSuggestion, string SourceFile, int SourceLine, string Confidence, bool RequiresReview, string Notes);
record PomUsageCandidate(string SourceExpression, string SuggestedTargetExpression, string SuggestedTargetKind, int Usages, string ExampleFile, int ExampleLine, string Confidence, bool RequiresSourceTruth, string Notes);

record ConfigSafetyReport(DateTimeOffset GeneratedAtUtc, string ConfigPath, string Status, ConfigSafetyIssue[] Issues, ConfigMetric[] Metrics);
record ConfigSafetyIssue(string Severity, string Code, string Message, string? Location, string SuggestedAction);
record ConfigMetric(string Name, int Value);
record ConfigDiffReport(DateTimeOffset GeneratedAtUtc, string BeforePath, string AfterPath, ConfigDiffChange[] Changes, ConfigSafetyIssue[] Risks, string[] Summary);
record ConfigDiffChange(string Section, string ChangeType, string Key);
record GuardReport(DateTimeOffset GeneratedAtUtc, string BeforePath, string AfterPath, string Status, GuardCheck[] Checks, string[] Summary);
record GuardCheck(string Name, string Status, string Message, int? Before, int? After);

record CliOptions(string Mode, string Input, string Out, string? Config, string[] Configs, string Format, bool FailOnUnsupported, bool FailOnTodo, string Workspace, string? Before, string? After);

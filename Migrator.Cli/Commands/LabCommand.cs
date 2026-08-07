using System.Net.Sockets;
using System.Reflection;
using Migrator.Lab;
using Migrator.Lab.Contracts;
using Migrator.Lab.Execution;
using Migrator.Lab.Generator;
using Migrator.Lab.LabApp;
using Migrator.Lab.Reports;
using Migrator.Lab.Triage;

internal static class LabCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteHelp();
            return LabExitCodes.Accepted;
        }

        var subcommand = args[0].Trim().ToLowerInvariant();
        if (subcommand == "app")
            return RunApp(args.Skip(1).ToArray());
        if (subcommand == "run")
            return RunSuite(args.Skip(1).ToArray());
        if (subcommand == "replay")
            return RunReplay(args.Skip(1).ToArray());
        if (subcommand == "baseline")
            return RunBaseline(args.Skip(1).ToArray());
        if (subcommand == "diff")
            return RunDiff(args.Skip(1).ToArray());
        if (subcommand == "generate")
            return RunGenerate(args.Skip(1).ToArray());
        if (subcommand == "metamorphic")
            return RunMetamorphic(args.Skip(1).ToArray());
        if (subcommand == "reduce")
            return RunReduce(args.Skip(1).ToArray());
        if (subcommand == "triage")
            return RunTriage(args.Skip(1).ToArray());
        if (subcommand == "promote")
            return RunPromote(args.Skip(1).ToArray());
        if (subcommand == "release-gate")
            return RunReleaseGate(args.Skip(1).ToArray());

        if (args.Skip(1).Any(IsHelp))
        {
            WriteHelp();
            return LabExitCodes.Accepted;
        }

        var options = ParseCatalogOptions(args.Skip(1).ToArray());
        if (options == null)
            return LabExitCodes.LabError;

        return subcommand switch
        {
            "validate" => RunValidate(options),
            "list" => RunList(options),
            _ => UnknownSubcommand(subcommand)
        };
    }

    static int RunSuite(string[] args)
    {
        if (args.Any(IsHelp))
        {
            WriteRunHelp();
            return LabExitCodes.Accepted;
        }

        var options = ParseRunOptions(args);
        return options == null ? LabExitCodes.LabError : ExecuteRun(options, "run");
    }

    static int RunReplay(string[] args)
    {
        if (args.Any(IsHelp))
        {
            WriteReplayHelp();
            return LabExitCodes.Accepted;
        }

        var options = ParseRunOptions(args, allowSuiteOption: false, defaultArtifactsRoot: Path.Combine("artifacts", "lab", "replay"));
        if (options == null)
            return LabExitCodes.LabError;
        if (options.ProjectIds.Length != 1)
        {
            Console.Error.WriteLine("lab replay requires exactly one --project <id>.");
            return LabExitCodes.LabError;
        }
        if (!string.IsNullOrWhiteSpace(options.Tag))
        {
            Console.Error.WriteLine("lab replay does not accept --tag; select exactly one --project.");
            return LabExitCodes.LabError;
        }

        var explicitOut = args.Contains("--out", StringComparer.Ordinal);
        options = options with
        {
            Suite = "replay",
            ArtifactsRoot = explicitOut
                ? options.ArtifactsRoot
                : Path.Combine(options.ArtifactsRoot, options.ProjectIds[0])
        };
        return ExecuteRun(options, "replay");
    }

    static int ExecuteRun(LabRunOptions options, string operation)
    {
        try
        {
            var coordinator = new LabRunCoordinator();
            var result = coordinator.RunAsync(options).GetAwaiter().GetResult();

            Console.WriteLine($"Migrator Lab {operation}: {result.Summary.Projects} project(s).");
            foreach (var project in result.Projects)
            {
                Console.WriteLine($"  {project.Id}: {ToContractName(project.ActualStatus)} " +
                                  $"(expected {ToContractName(project.ExpectedStatus)}, source {project.SourceTests.Passed}/{project.SourceTests.ExpectedPassed})");
            }
            Console.WriteLine($"Reports: {result.ArtifactsRoot}");
            return LabRunStatusPolicy.GetSuiteExitCode(result.Projects);
        }
        catch (LabRunConfigurationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return LabExitCodes.LabError;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Lab {operation} failed before a suite report could be completed: {ex.Message}");
            return LabExitCodes.LabError;
        }
    }

    static int RunBaseline(string[] args)
    {
        if (args.Any(IsHelp))
        {
            WriteBaselineHelp();
            return LabExitCodes.Accepted;
        }

        string? input = null;
        var output = Path.Combine("artifacts", "lab", "baselines", "main");
        var label = "main";
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--input":
                    if (!TryReadValue(args, ref index, out input))
                        return LabExitCodes.LabError;
                    break;
                case "--out":
                    if (!TryReadValue(args, ref index, out output))
                        return LabExitCodes.LabError;
                    break;
                case "--label":
                    if (!TryReadValue(args, ref index, out label))
                        return LabExitCodes.LabError;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown lab baseline option: {args[index]}");
                    return LabExitCodes.LabError;
            }
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            Console.Error.WriteLine("lab baseline requires --input <lab run directory|lab-summary.json>.");
            return LabExitCodes.LabError;
        }

        try
        {
            var run = LabRunArtifactLoader.LoadRun(input);
            var unexpected = run.Projects
                .Where(project => project.ActualStatus != project.ExpectedStatus)
                .Select(project => $"{project.Id}: expected {ToContractName(project.ExpectedStatus)}, actual {ToContractName(project.ActualStatus)}")
                .ToArray();
            if (unexpected.Length > 0)
            {
                Console.Error.WriteLine("Refusing to create a baseline from a run with unexpected scenario outcomes:");
                foreach (var item in unexpected)
                    Console.Error.WriteLine($"  {item}");
                return LabExitCodes.Regression;
            }

            var baseline = LabBaselineService.Create(run, label);
            LabBaselineReportWriter.Write(baseline, output);
            Console.WriteLine($"Migrator Lab baseline: {baseline.Projects.Length} project(s), label '{baseline.Label}'.");
            Console.WriteLine($"Baseline: {Path.GetFullPath(output)}");
            return LabExitCodes.Accepted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Lab baseline failed: {ex.Message}");
            return LabExitCodes.LabError;
        }
    }

    static int RunDiff(string[] args)
    {
        if (args.Any(IsHelp))
        {
            WriteDiffHelp();
            return LabExitCodes.Accepted;
        }

        string? baselinePath = null;
        string? currentPath = null;
        var output = Path.Combine("artifacts", "lab", "diff");
        var durationPercent = 20d;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--baseline":
                    if (!TryReadValue(args, ref index, out baselinePath))
                        return LabExitCodes.LabError;
                    break;
                case "--current":
                    if (!TryReadValue(args, ref index, out currentPath))
                        return LabExitCodes.LabError;
                    break;
                case "--out":
                    if (!TryReadValue(args, ref index, out output))
                        return LabExitCodes.LabError;
                    break;
                case "--duration-regression-percent":
                    if (!TryReadValue(args, ref index, out var rawPercent)
                        || !double.TryParse(rawPercent, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out durationPercent)
                        || durationPercent < 0)
                    {
                        Console.Error.WriteLine("--duration-regression-percent requires a non-negative number using '.' as decimal separator.");
                        return LabExitCodes.LabError;
                    }
                    break;
                default:
                    Console.Error.WriteLine($"Unknown lab diff option: {args[index]}");
                    return LabExitCodes.LabError;
            }
        }

        if (string.IsNullOrWhiteSpace(baselinePath) || string.IsNullOrWhiteSpace(currentPath))
        {
            Console.Error.WriteLine("lab diff requires --baseline <path> and --current <path>.");
            return LabExitCodes.LabError;
        }

        try
        {
            var baseline = LabRunArtifactLoader.LoadBaseline(baselinePath);
            var current = LabRunArtifactLoader.LoadRun(currentPath);
            var diff = LabDiffEngine.Compare(baseline, current, baselinePath, currentPath, durationPercent);
            LabDiffReportWriter.Write(diff, output);
            Console.WriteLine($"Migrator Lab diff: {diff.Summary.Regressions} regression(s), {diff.Summary.Improvements} improvement(s), {diff.Summary.Unchanged} unchanged.");
            foreach (var project in diff.Projects.Where(project => project.Kind != LabDiffKind.Unchanged))
                Console.WriteLine($"  {project.Id}: {ToContractName(project.Kind)} — {string.Join(" ", project.Reasons)}");
            Console.WriteLine($"Reports: {Path.GetFullPath(output)}");
            return diff.Summary.Regressions > 0 ? LabExitCodes.Regression : LabExitCodes.Accepted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Lab diff failed: {ex.Message}");
            return LabExitCodes.LabError;
        }
    }

    static int RunGenerate(string[] args)
    {
        if (args.Any(IsHelp))
        {
            WriteGenerateHelp();
            return LabExitCodes.Accepted;
        }

        var corpus = Path.Combine("corpus", "stable", "vertical-slice");
        var baseScenario = "p01-basic-id-login";
        var output = Path.Combine("artifacts", "lab", "generated");
        var seed = 73001;
        var count = 6;
        var force = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--corpus":
                    if (!TryReadValue(args, ref index, out corpus))
                        return LabExitCodes.LabError;
                    break;
                case "--base":
                    if (!TryReadValue(args, ref index, out baseScenario))
                        return LabExitCodes.LabError;
                    break;
                case "--out":
                    if (!TryReadValue(args, ref index, out output))
                        return LabExitCodes.LabError;
                    break;
                case "--seed":
                    if (!TryReadValue(args, ref index, out var rawSeed)
                        || !int.TryParse(rawSeed, out seed)
                        || seed < 0)
                    {
                        Console.Error.WriteLine("--seed requires a non-negative integer.");
                        return LabExitCodes.LabError;
                    }
                    break;
                case "--count":
                    if (!TryReadValue(args, ref index, out var rawCount)
                        || !int.TryParse(rawCount, out count)
                        || count is < 6 or > 32)
                    {
                        Console.Error.WriteLine("--count requires an integer in range 6-32.");
                        return LabExitCodes.LabError;
                    }
                    break;
                case "--force":
                    force = true;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown lab generate option: {args[index]}");
                    return LabExitCodes.LabError;
            }
        }

        try
        {
            var manifest = new SeededVariantGenerator().Generate(new SeededVariantGenerationOptions
            {
                CorpusRoot = corpus,
                BaseScenarioId = baseScenario,
                OutputRoot = output,
                Seed = seed,
                Count = count,
                Force = force
            });
            Console.WriteLine($"Migrator Lab generate: {manifest.Variants.Length} pairwise variant(s), seed {manifest.Seed}.");
            Console.WriteLine($"Corpus fingerprint: {manifest.CorpusFingerprint}");
            Console.WriteLine($"Generated corpus: {Path.GetFullPath(output)}");
            return LabExitCodes.Accepted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Lab generate failed: {ex.Message}");
            return LabExitCodes.LabError;
        }
    }

    static int RunMetamorphic(string[] args)
    {
        if (args.Any(IsHelp))
        {
            WriteMetamorphicHelp();
            return LabExitCodes.Accepted;
        }

        string? manifestPath = null;
        string? runPath = null;
        var output = Path.Combine("artifacts", "lab", "metamorphic");
        string? candidateRoot = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--manifest":
                    if (!TryReadValue(args, ref index, out manifestPath))
                        return LabExitCodes.LabError;
                    break;
                case "--run":
                    if (!TryReadValue(args, ref index, out runPath))
                        return LabExitCodes.LabError;
                    break;
                case "--out":
                    if (!TryReadValue(args, ref index, out output))
                        return LabExitCodes.LabError;
                    break;
                case "--save-candidates":
                    if (!TryReadValue(args, ref index, out candidateRoot))
                        return LabExitCodes.LabError;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown lab metamorphic option: {args[index]}");
                    return LabExitCodes.LabError;
            }
        }

        if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(runPath))
        {
            Console.Error.WriteLine("lab metamorphic requires --manifest <generation-manifest> and --run <lab run>.");
            return LabExitCodes.LabError;
        }

        candidateRoot ??= Path.Combine(output, "candidates");
        try
        {
            var run = LabRunArtifactLoader.LoadRun(runPath);
            var report = new LabMetamorphicAnalyzer().Analyze(manifestPath, run, candidateRoot);
            LabMetamorphicReportWriter.Write(report, output);
            Console.WriteLine($"Migrator Lab metamorphic: {report.Summary.Passed}/{report.Summary.Variants} invariant variant(s) accepted; {report.Summary.SavedCandidates} seed candidate(s) saved.");
            foreach (var variant in report.Variants.Where(item => !item.Passed))
                Console.WriteLine($"  {variant.Id}: {string.Join(" ", variant.Reasons)}");
            Console.WriteLine($"Reports: {Path.GetFullPath(output)}");
            return report.Summary.Regressions == 0 ? LabExitCodes.Accepted : LabExitCodes.Regression;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or InvalidDataException)
        {
            Console.Error.WriteLine($"Lab metamorphic failed: {ex.Message}");
            return LabExitCodes.LabError;
        }
    }

    static int RunReduce(string[] args)
    {
        if (args.Any(IsHelp))
        {
            WriteReduceHelp();
            return LabExitCodes.Accepted;
        }

        string? candidate = null;
        var output = Path.Combine("artifacts", "lab", "reduced");
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--candidate":
                case "--scenario":
                    if (!TryReadValue(args, ref index, out candidate))
                        return LabExitCodes.LabError;
                    break;
                case "--out":
                    if (!TryReadValue(args, ref index, out output))
                        return LabExitCodes.LabError;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown lab reduce option: {args[index]}");
                    return LabExitCodes.LabError;
            }
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            Console.Error.WriteLine("lab reduce requires --candidate <candidate|scenario directory>.");
            return LabExitCodes.LabError;
        }

        try
        {
            var report = new LabCandidateReducer().Reduce(candidate, output);
            Console.WriteLine($"Migrator Lab reduce: {report.ScenarioId}, {report.BeforeFiles} -> {report.AfterFiles} file(s), {report.BeforeBytes} -> {report.AfterBytes} bytes.");
            Console.WriteLine($"Reduced repro: {Path.GetFullPath(output)}");
            return LabExitCodes.Accepted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            Console.Error.WriteLine($"Lab reduce failed: {ex.Message}");
            return LabExitCodes.LabError;
        }
    }

    static int RunTriage(string[] args)
    {
        if (args.Any(IsHelp))
        {
            WriteTriageHelp();
            return LabExitCodes.Accepted;
        }

        string? runPath = null;
        var corpus = Path.Combine("corpus", "stable", "vertical-slice");
        var repository = Directory.GetCurrentDirectory();
        var output = Path.Combine("artifacts", "lab", "triage");
        var taskPacks = true;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--run":
                    if (!TryReadValue(args, ref index, out runPath))
                        return LabExitCodes.LabError;
                    break;
                case "--corpus":
                    if (!TryReadValue(args, ref index, out corpus))
                        return LabExitCodes.LabError;
                    break;
                case "--repo":
                    if (!TryReadValue(args, ref index, out repository))
                        return LabExitCodes.LabError;
                    break;
                case "--out":
                    if (!TryReadValue(args, ref index, out output))
                        return LabExitCodes.LabError;
                    break;
                case "--no-task-packs":
                    taskPacks = false;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown lab triage option: {args[index]}");
                    return LabExitCodes.LabError;
            }
        }

        if (string.IsNullOrWhiteSpace(runPath))
        {
            Console.Error.WriteLine("lab triage requires --run <lab run directory|lab-summary.json>.");
            return LabExitCodes.LabError;
        }

        try
        {
            var run = LabRunArtifactLoader.LoadRun(runPath);
            var report = new LabFailureTriageService().Analyze(
                run,
                runPath,
                corpus,
                repository,
                taskPacks ? Path.Combine(output, "task-packs") : null);
            LabTriageReportWriter.Write(report, output);
            Console.WriteLine($"Migrator Lab triage: {report.Summary.Findings} finding(s) -> {report.Summary.Clusters} cluster(s), {report.Summary.TaskPacks} task pack(s).");
            Console.WriteLine($"Automation: {report.Summary.AutoFixEligible} auto-fix eligible, {report.Summary.ManualReview} manual review.");
            Console.WriteLine($"Reports: {Path.GetFullPath(output)}");
            return LabExitCodes.Accepted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            Console.Error.WriteLine($"Lab triage failed: {ex.Message}");
            return LabExitCodes.LabError;
        }
    }

    static int RunPromote(string[] args)
    {
        if (args.Any(IsHelp))
        {
            WritePromoteHelp();
            return LabExitCodes.Accepted;
        }

        string? repro = null;
        var output = Path.Combine("artifacts", "lab", "promoted-regressions");
        LabRegressionLevel? level = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--repro":
                case "--candidate":
                    if (!TryReadValue(args, ref index, out repro))
                        return LabExitCodes.LabError;
                    break;
                case "--out":
                    if (!TryReadValue(args, ref index, out output))
                        return LabExitCodes.LabError;
                    break;
                case "--level":
                    if (!TryReadValue(args, ref index, out var rawLevel) || !TryParseRegressionLevel(rawLevel, out var parsedLevel))
                    {
                        Console.Error.WriteLine("--level requires unit-test|project-fixture|saved-seed.");
                        return LabExitCodes.LabError;
                    }
                    level = parsedLevel;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown lab promote option: {args[index]}");
                    return LabExitCodes.LabError;
            }
        }

        if (string.IsNullOrWhiteSpace(repro) || level == null)
        {
            Console.Error.WriteLine("lab promote requires --repro <scenario|task-pack> and --level unit-test|project-fixture|saved-seed.");
            return LabExitCodes.LabError;
        }

        try
        {
            var manifest = new LabRegressionPromotionService().Promote(repro, level.Value, output);
            Console.WriteLine($"Migrator Lab promote: {manifest.ScenarioId} -> {ToContractName(manifest.Level)}.");
            Console.WriteLine($"Promotion artifact: {manifest.DestinationDirectory}");
            return LabExitCodes.Accepted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            Console.Error.WriteLine($"Lab promote failed: {ex.Message}");
            return LabExitCodes.LabError;
        }
    }

    static int RunReleaseGate(string[] args)
    {
        if (args.Any(IsHelp))
        {
            WriteReleaseGateHelp();
            return LabExitCodes.Accepted;
        }

        string? stableRunPath = null;
        string? realEvidencePath = null;
        var output = Path.Combine("artifacts", "lab", "release-gate");
        var maxAgeDays = 14;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--stable-run":
                    if (!TryReadValue(args, ref index, out stableRunPath))
                        return LabExitCodes.LabError;
                    break;
                case "--real-evidence":
                    if (!TryReadValue(args, ref index, out realEvidencePath))
                        return LabExitCodes.LabError;
                    break;
                case "--out":
                    if (!TryReadValue(args, ref index, out output))
                        return LabExitCodes.LabError;
                    break;
                case "--max-age-days":
                    if (!TryReadValue(args, ref index, out var rawDays) || !int.TryParse(rawDays, out maxAgeDays) || maxAgeDays <= 0)
                    {
                        Console.Error.WriteLine("--max-age-days requires a positive integer.");
                        return LabExitCodes.LabError;
                    }
                    break;
                default:
                    Console.Error.WriteLine($"Unknown lab release-gate option: {args[index]}");
                    return LabExitCodes.LabError;
            }
        }

        if (string.IsNullOrWhiteSpace(stableRunPath) || string.IsNullOrWhiteSpace(realEvidencePath))
        {
            Console.Error.WriteLine("lab release-gate requires --stable-run <run> and --real-evidence <json>.");
            return LabExitCodes.LabError;
        }

        try
        {
            var stable = LabRunArtifactLoader.LoadRun(stableRunPath);
            var report = new LabReleaseGateService().Evaluate(stable, stableRunPath, realEvidencePath, maxAgeDays);
            LabReleaseGateReportWriter.Write(report, output);
            Console.WriteLine($"Migrator Lab release gate: {(report.Passed ? "PASS" : "FAIL")} — stable unexpected={report.StableUnexpectedOutcomes}, real={report.RealStatus}, evidence age={report.RealEvidenceAgeHours}h.");
            foreach (var issue in report.Issues)
                Console.WriteLine($"  {issue}");
            Console.WriteLine($"Reports: {Path.GetFullPath(output)}");
            return report.Passed ? LabExitCodes.Accepted : LabExitCodes.Regression;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            Console.Error.WriteLine($"Lab release-gate failed: {ex.Message}");
            return LabExitCodes.LabError;
        }
    }

    static bool TryParseRegressionLevel(string value, out LabRegressionLevel level)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "unit-test":
            case "unittest":
                level = LabRegressionLevel.UnitTest;
                return true;
            case "project-fixture":
            case "projectfixture":
                level = LabRegressionLevel.ProjectFixture;
                return true;
            case "saved-seed":
            case "savedseed":
                level = LabRegressionLevel.SavedSeed;
                return true;
            default:
                level = default;
                return false;
        }
    }

    static LabRunOptions? ParseRunOptions(
        string[] args,
        bool allowSuiteOption = true,
        string? defaultArtifactsRoot = null)
    {
        var suite = "vertical";
        var corpus = Path.Combine("corpus", "stable", "vertical-slice");
        var outDirectory = defaultArtifactsRoot ?? Path.Combine("artifacts", "lab", "run");
        var projectIds = new List<string>();
        var features = new List<string>();
        string? tag = null;
        var timeout = TimeSpan.FromMinutes(10);
        var keepWorkspaces = false;
        var dotnetExecutable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var configuration = "Release";

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--suite":
                    if (!allowSuiteOption)
                    {
                        Console.Error.WriteLine("--suite is not valid for lab replay.");
                        return null;
                    }
                    if (!TryReadValue(args, ref index, out suite))
                        return null;
                    suite = suite.Trim().ToLowerInvariant();
                    if (suite is not ("vertical" or "smoke" or "pr" or "nightly"))
                    {
                        Console.Error.WriteLine("--suite requires: vertical|smoke|pr|nightly");
                        return null;
                    }
                    break;
                case "--corpus":
                    if (!TryReadValue(args, ref index, out corpus))
                        return null;
                    break;
                case "--out":
                    if (!TryReadValue(args, ref index, out outDirectory))
                        return null;
                    break;
                case "--project":
                case "--projects":
                    if (!TryReadValue(args, ref index, out var rawProjects))
                        return null;
                    projectIds.AddRange(rawProjects.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--tag":
                    if (!TryReadValue(args, ref index, out tag))
                        return null;
                    tag = tag.Trim();
                    break;
                case "--feature":
                case "--features":
                    if (!TryReadValue(args, ref index, out var rawFeatures))
                        return null;
                    features.AddRange(rawFeatures.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--timeout-seconds":
                    if (!TryReadValue(args, ref index, out var rawTimeout)
                        || !int.TryParse(rawTimeout, out var timeoutSeconds)
                        || timeoutSeconds < 1)
                    {
                        Console.Error.WriteLine("--timeout-seconds requires a positive integer");
                        return null;
                    }
                    timeout = TimeSpan.FromSeconds(timeoutSeconds);
                    break;
                case "--keep-workspaces":
                    keepWorkspaces = true;
                    break;
                case "--dotnet":
                    if (!TryReadValue(args, ref index, out dotnetExecutable))
                        return null;
                    break;
                case "--configuration":
                    if (!TryReadValue(args, ref index, out configuration))
                        return null;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown lab run option: {args[index]}");
                    return null;
            }
        }

        if (!string.IsNullOrWhiteSpace(tag) && suite != "vertical")
        {
            Console.Error.WriteLine("Use either --suite smoke|pr|nightly or --tag, not both.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(tag) && suite is "smoke" or "pr" or "nightly")
            tag = suite;

        return new LabRunOptions
        {
            Suite = suite,
            CorpusRoot = corpus,
            ArtifactsRoot = outDirectory,
            ProjectIds = projectIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Tag = tag,
            Features = features.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            DotNetExecutable = dotnetExecutable,
            Configuration = configuration,
            CommandTimeout = timeout,
            KeepWorkspaces = keepWorkspaces,
            MigratorCommand = BuildCurrentMigratorCommand()
        };
    }

    static LabProcessCommand BuildCurrentMigratorCommand()
    {
        var processPath = Environment.ProcessPath;
        var entryAssembly = Assembly.GetEntryAssembly()?.Location;

        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var processName = Path.GetFileNameWithoutExtension(processPath);
            if (string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(entryAssembly))
            {
                return LabProcessCommand.Create(processPath, entryAssembly);
            }

            return LabProcessCommand.Create(processPath);
        }

        if (!string.IsNullOrWhiteSpace(entryAssembly))
            return LabProcessCommand.Create("dotnet", entryAssembly);

        throw new InvalidOperationException("Could not resolve the current Migrator CLI executable for lab orchestration.");
    }

    static int RunValidate(LabCatalogCommandOptions options)
    {
        ScenarioCatalogResult result;
        try
        {
            result = ScenarioCatalog.Load(options.Corpus);
            LabValidationReportWriter.Write(result, options.Out, options.Format);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Lab validation could not read the corpus or write reports: {ex.Message}");
            return LabExitCodes.LabError;
        }

        Console.WriteLine($"Migrator Lab contract validation: {result.ValidCount} valid, {result.InvalidCount} invalid, {result.ReadyCount} ready, {result.PlannedCount} planned.");
        Console.WriteLine($"Reports: {Path.GetFullPath(options.Out)}");

        if (result.HasErrors)
        {
            Console.Error.WriteLine("Lab contract validation failed. See lab-contract-validation.md/json.");
            return LabExitCodes.LabError;
        }

        if (options.FailOnPlanned && result.PlannedCount > 0)
        {
            Console.Error.WriteLine($"Lab contract validation found {result.PlannedCount} planned scenario(s); --fail-on-planned requires all scenarios to be READY.");
            return LabExitCodes.LabError;
        }

        return LabExitCodes.Accepted;
    }

    static int RunList(LabCatalogCommandOptions options)
    {
        ScenarioCatalogResult result;
        try
        {
            result = ScenarioCatalog.Load(options.Corpus);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Lab list could not read the corpus: {ex.Message}");
            return LabExitCodes.LabError;
        }

        var entries = result.Entries
            .Where(entry => entry.Scenario != null)
            .Where(entry => options.Tag == null || entry.Scenario!.Tags.Contains(options.Tag, StringComparer.OrdinalIgnoreCase))
            .Where(entry => options.State == null || entry.Scenario!.Implementation.State == options.State)
            .OrderBy(entry => entry.Scenario!.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Console.WriteLine($"Corpus: {result.CorpusRoot}");
        Console.WriteLine("ID | implementation | expected | tags | contract");
        foreach (var entry in entries)
        {
            var scenario = entry.Scenario!;
            Console.WriteLine($"{scenario.Id} | {ToContractName(scenario.Implementation.State)} | {ToContractName(scenario.Expected.Status)} | {string.Join(',', scenario.Tags)} | {(entry.IsValid ? "VALID" : "INVALID")}");
        }

        if (entries.Length == 0)
            Console.WriteLine("No scenarios matched the selected filters.");

        foreach (var issue in result.CatalogIssues)
            Console.Error.WriteLine($"{issue.Severity} {issue.Code}: {issue.Message}");

        return result.HasErrors ? LabExitCodes.LabError : LabExitCodes.Accepted;
    }

    static int RunApp(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteAppHelp();
            return LabExitCodes.Accepted;
        }

        var action = args[0].Trim().ToLowerInvariant();
        if (action != "serve")
        {
            Console.Error.WriteLine($"Unknown lab app action: {action}");
            WriteAppHelp();
            return LabExitCodes.LabError;
        }

        var port = 5057;
        string? readyFile = null;
        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--port":
                    if (!TryReadValue(args, ref index, out var rawPort) || !int.TryParse(rawPort, out port) || port is < 0 or > 65535)
                    {
                        Console.Error.WriteLine("--port requires an integer in range 0-65535");
                        return LabExitCodes.LabError;
                    }
                    break;
                case "--ready-file":
                    if (!TryReadValue(args, ref index, out readyFile))
                        return LabExitCodes.LabError;
                    break;
                case "--help":
                case "-h":
                    WriteAppHelp();
                    return LabExitCodes.Accepted;
                default:
                    Console.Error.WriteLine($"Unknown lab app option: {args[index]}");
                    return LabExitCodes.LabError;
            }
        }

        try
        {
            return LabAppServeRunner.Run(port, readyFile);
        }
        catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"LabApp could not start: {ex.Message}");
            return LabExitCodes.LabError;
        }
    }

    static LabCatalogCommandOptions? ParseCatalogOptions(string[] args)
    {
        var corpus = Path.Combine("corpus", "stable", "vertical-slice");
        var outDirectory = Path.Combine("artifacts", "lab", "contract-validation");
        var format = "both";
        string? tag = null;
        ScenarioImplementationState? state = null;
        var failOnPlanned = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--corpus":
                    if (!TryReadValue(args, ref index, out corpus))
                        return null;
                    break;
                case "--out":
                    if (!TryReadValue(args, ref index, out outDirectory))
                        return null;
                    break;
                case "--format":
                    if (!TryReadValue(args, ref index, out format))
                        return null;
                    format = format.Trim().ToLowerInvariant();
                    if (format is not ("text" or "json" or "both"))
                    {
                        Console.Error.WriteLine("--format requires: text|json|both");
                        return null;
                    }
                    break;
                case "--tag":
                    if (!TryReadValue(args, ref index, out tag))
                        return null;
                    tag = tag.Trim().ToLowerInvariant();
                    break;
                case "--state":
                    if (!TryReadValue(args, ref index, out var rawState))
                        return null;
                    if (!Enum.TryParse<ScenarioImplementationState>(rawState, ignoreCase: true, out var parsedState))
                    {
                        Console.Error.WriteLine("--state requires: planned|ready");
                        return null;
                    }
                    state = parsedState;
                    break;
                case "--fail-on-planned":
                    failOnPlanned = true;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown lab option: {args[index]}");
                    return null;
            }
        }

        return new LabCatalogCommandOptions(corpus, outDirectory, format, tag, state, failOnPlanned);
    }

    static bool TryReadValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
        {
            Console.Error.WriteLine($"{args[index]} requires a value");
            value = "";
            return false;
        }

        value = args[++index];
        return true;
    }

    static int UnknownSubcommand(string subcommand)
    {
        Console.Error.WriteLine($"Unknown lab subcommand: {subcommand}");
        WriteHelp();
        return LabExitCodes.LabError;
    }

    static bool IsHelp(string value) => value is "help" or "--help" or "-h";

    static string ToContractName<T>(T value) where T : struct, Enum
    {
        var text = value.ToString();
        var result = new System.Text.StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(text[index - 1]))
                result.Append('_');
            result.Append(char.ToUpperInvariant(character));
        }
        return result.ToString();
    }

    static void WriteHelp()
    {
        Console.WriteLine("Migrator Lab — deterministic Selenium → Playwright migration test corpus.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  selenium-pw-migrator lab run [options]");
        Console.WriteLine("  selenium-pw-migrator lab replay --project <id> [options]");
        Console.WriteLine("  selenium-pw-migrator lab baseline --input <run> [options]");
        Console.WriteLine("  selenium-pw-migrator lab diff --baseline <path> --current <run> [options]");
        Console.WriteLine("  selenium-pw-migrator lab generate --seed <n> [options]");
        Console.WriteLine("  selenium-pw-migrator lab metamorphic --manifest <path> --run <run> [options]");
        Console.WriteLine("  selenium-pw-migrator lab reduce --candidate <path> [options]");
        Console.WriteLine("  selenium-pw-migrator lab triage --run <run> [options]");
        Console.WriteLine("  selenium-pw-migrator lab promote --repro <path> --level <level> [options]");
        Console.WriteLine("  selenium-pw-migrator lab release-gate --stable-run <run> --real-evidence <json> [options]");
        Console.WriteLine("  selenium-pw-migrator lab validate [options]");
        Console.WriteLine("  selenium-pw-migrator lab list [options]");
        Console.WriteLine("  selenium-pw-migrator lab app serve [options]");
        Console.WriteLine();
        Console.WriteLine("Catalog options:");
        Console.WriteLine("  --corpus <path>       Corpus root (default: corpus/stable/vertical-slice).");
        Console.WriteLine("  --out <path>          Validation report directory.");
        Console.WriteLine("  --format <value>      text|json|both (default: both).");
        Console.WriteLine("  --tag <tag>           Filter `lab list` by tag.");
        Console.WriteLine("  --state <state>       Filter `lab list` by planned|ready.");
        Console.WriteLine("  --fail-on-planned     Make validation fail until every scenario is READY.");
        Console.WriteLine();
        Console.WriteLine("Run exit codes: 0=accepted, 10=regression, 11=migrator failure, 12=source invalid, 13=infrastructure, 14=non-deterministic, 15=lab error.");
    }

    static void WriteRunHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  selenium-pw-migrator lab run [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --suite <name>           vertical|smoke|pr|nightly (default: vertical).");
        Console.WriteLine("  --corpus <path>          Corpus root (default: corpus/stable/vertical-slice).");
        Console.WriteLine("  --out <path>             Suite artifact directory (default: artifacts/lab/run).");
        Console.WriteLine("  --project <id[,id]>      Run only selected scenario ids; may be repeated.");
        Console.WriteLine("  --tag <tag>              Run READY scenarios carrying the tag.");
        Console.WriteLine("  --feature <name[,name]>  Run scenarios whose source feature list contains any requested feature; may be repeated.");
        Console.WriteLine("  --timeout-seconds <n>    Timeout for each restore/build/test/migration/verify command.");
        Console.WriteLine("  --configuration <name>   Source and target build configuration (default: Release).");
        Console.WriteLine("  --dotnet <path>          dotnet host used for source, verify-project harness, and target runtime.");
        Console.WriteLine("  --keep-workspaces        Preserve copied source workspaces for diagnostics.");
        Console.WriteLine("  Runtime output includes JSON/Markdown/HTML suite reports, project-verify reports, target TRX, semantic diff, quality budgets, and failure-only trace/screenshot artifacts.");
    }

    static void WriteReplayHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  selenium-pw-migrator lab replay --project <id> [options]");
        Console.WriteLine();
        Console.WriteLine("Runs exactly one READY scenario through the same source, migration, verify-project, Playwright runtime, quality, and oracle pipeline as lab run.");
        Console.WriteLine("Options are the same as lab run except --suite and --tag. Default output: artifacts/lab/replay/<id>.");
    }

    static void WriteBaselineHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  selenium-pw-migrator lab baseline --input <run directory|lab-summary.json> [--out <directory>] [--label <name>]");
        Console.WriteLine();
        Console.WriteLine("Creates a normalized baseline containing statuses, diagnostics, quality metrics, semantic evidence, duration, and generated-code fingerprints.");
    }

    static void WriteDiffHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  selenium-pw-migrator lab diff --baseline <baseline directory|json> --current <run directory|lab-summary.json> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --out <directory>                         Diff report directory (default: artifacts/lab/diff).");
        Console.WriteLine("  --duration-regression-percent <number>    Performance regression threshold (default: 20).");
        Console.WriteLine("Produces lab-diff.json, lab-diff.md, and lab-diff.html. Exit code 10 means at least one regression.");
    }

    static void WriteGenerateHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  selenium-pw-migrator lab generate --seed <n> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --corpus <path>    Stable base corpus (default: corpus/stable/vertical-slice).");
        Console.WriteLine("  --base <id>        READY PASS scenario used as the bounded template (default: p01-basic-id-login).");
        Console.WriteLine("  --out <path>       Generated corpus directory (default: artifacts/lab/generated).");
        Console.WriteLine("  --seed <n>         Non-negative reproducibility seed (default: 73001).");
        Console.WriteLine("  --count <n>        Variant count, 6-32; six variants provide pairwise coverage of five binary dimensions.");
        Console.WriteLine("  --force            Replace a non-empty output directory.");
    }

    static void WriteMetamorphicHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  selenium-pw-migrator lab metamorphic --manifest <generation-manifest> --run <lab run> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --out <path>                Metamorphic JSON/Markdown report directory.");
        Console.WriteLine("  --save-candidates <path>    Copy useful failing seeds with evidence for later promotion.");
        Console.WriteLine("Exit code 10 means a semantics-preserving variant changed status, diagnostics, quality, or oracle outcome.");
    }

    static void WriteReduceHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  selenium-pw-migrator lab reduce --candidate <candidate|scenario directory> [--out <directory>]");
        Console.WriteLine();
        Console.WriteLine("Produces a deterministic feature-aware repro containing only scenario.json, declared project files, migration files, and adapter config.");
    }

    static void WriteTriageHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  selenium-pw-migrator lab triage --run <lab run> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --corpus <path>       Scenario corpus used to recover features and repro files.");
        Console.WriteLine("  --repo <path>         Migrator repository root used to copy bounded relevant code.");
        Console.WriteLine("  --out <path>          Triage report/task-pack directory.");
        Console.WriteLine("  --no-task-packs       Produce clustering only.");
        Console.WriteLine("Clusters by stage, diagnostics, semantic diff, and normalized feature tags.");
    }

    static void WritePromoteHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  selenium-pw-migrator lab promote --repro <scenario|task-pack> --level unit-test|project-fixture|saved-seed [--out <directory>]");
        Console.WriteLine();
        Console.WriteLine("Creates a reviewed promotion artifact and verification plan. Unit-test promotion deliberately keeps a minimal repro and requires the agent to encode the focused assertion rather than generating a fake test.");
    }

    static void WriteReleaseGateHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  selenium-pw-migrator lab release-gate --stable-run <run> --real-evidence <json> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --out <path>          Release-gate report directory.");
        Console.WriteLine("  --max-age-days <n>    Maximum accepted age of real-project evidence (default: 14).");
        Console.WriteLine("This command is intended for rare release validation, not every PR.");
    }

    static void WriteAppHelp()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  selenium-pw-migrator lab app serve [--port <0-65535>] [--ready-file <path>]");
        Console.WriteLine();
        Console.WriteLine("The default port is 5057. Port 0 asks the OS to choose a free port.");
    }

    sealed record LabCatalogCommandOptions(
        string Corpus,
        string Out,
        string Format,
        string? Tag,
        ScenarioImplementationState? State,
        bool FailOnPlanned);
}

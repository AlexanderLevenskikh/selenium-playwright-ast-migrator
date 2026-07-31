using System.Net.Sockets;
using System.Reflection;
using Migrator.Lab;
using Migrator.Lab.Contracts;
using Migrator.Lab.Execution;
using Migrator.Lab.LabApp;
using Migrator.Lab.Reports;

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

    static LabRunOptions? ParseRunOptions(
        string[] args,
        bool allowSuiteOption = true,
        string? defaultArtifactsRoot = null)
    {
        var suite = "vertical";
        var corpus = Path.Combine("corpus", "stable", "vertical-slice");
        var outDirectory = defaultArtifactsRoot ?? Path.Combine("artifacts", "lab", "run");
        var projectIds = new List<string>();
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
                    if (suite is not ("vertical" or "smoke" or "pr"))
                    {
                        Console.Error.WriteLine("--suite requires: vertical|smoke|pr");
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
            Console.Error.WriteLine("Use either --suite smoke|pr or --tag, not both.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(tag) && suite is "smoke" or "pr")
            tag = suite;

        return new LabRunOptions
        {
            Suite = suite,
            CorpusRoot = corpus,
            ArtifactsRoot = outDirectory,
            ProjectIds = projectIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Tag = tag,
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
        Console.WriteLine("  --suite <name>           vertical|smoke|pr (default: vertical).");
        Console.WriteLine("  --corpus <path>          Corpus root (default: corpus/stable/vertical-slice).");
        Console.WriteLine("  --out <path>             Suite artifact directory (default: artifacts/lab/run).");
        Console.WriteLine("  --project <id[,id]>      Run only selected scenario ids; may be repeated.");
        Console.WriteLine("  --tag <tag>              Run READY scenarios carrying the tag.");
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

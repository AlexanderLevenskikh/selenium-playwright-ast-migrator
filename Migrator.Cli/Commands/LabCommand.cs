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
            return 0;
        }

        var subcommand = args[0].Trim().ToLowerInvariant();
        if (subcommand == "app")
            return RunApp(args.Skip(1).ToArray());
        if (subcommand == "run")
            return RunSuite(args.Skip(1).ToArray());

        if (args.Skip(1).Any(IsHelp))
        {
            WriteHelp();
            return 0;
        }

        var options = ParseCatalogOptions(args.Skip(1).ToArray());
        if (options == null)
            return 15;

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
            return 0;
        }

        var options = ParseRunOptions(args);
        if (options == null)
            return 15;

        try
        {
            var coordinator = new LabRunCoordinator();
            var result = coordinator.RunAsync(options).GetAwaiter().GetResult();

            Console.WriteLine($"Migrator Lab run: {result.Summary.Projects} project(s).");
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
            return 15;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Lab run failed before a suite report could be completed: {ex.Message}");
            return 15;
        }
    }

    static LabRunOptions? ParseRunOptions(string[] args)
    {
        var suite = "vertical";
        var corpus = Path.Combine("corpus", "stable", "vertical-slice");
        var outDirectory = Path.Combine("artifacts", "lab", "run");
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
            return 15;
        }

        Console.WriteLine($"Migrator Lab contract validation: {result.ValidCount} valid, {result.InvalidCount} invalid, {result.ReadyCount} ready, {result.PlannedCount} planned.");
        Console.WriteLine($"Reports: {Path.GetFullPath(options.Out)}");

        if (result.HasErrors)
        {
            Console.Error.WriteLine("Lab contract validation failed. See lab-contract-validation.md/json.");
            return 15;
        }

        if (options.FailOnPlanned && result.PlannedCount > 0)
        {
            Console.Error.WriteLine($"Lab contract validation found {result.PlannedCount} planned scenario(s); --fail-on-planned requires all scenarios to be READY.");
            return 15;
        }

        return 0;
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
            return 15;
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

        return result.HasErrors ? 15 : 0;
    }

    static int RunApp(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteAppHelp();
            return 0;
        }

        var action = args[0].Trim().ToLowerInvariant();
        if (action != "serve")
        {
            Console.Error.WriteLine($"Unknown lab app action: {action}");
            WriteAppHelp();
            return 15;
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
                        return 15;
                    }
                    break;
                case "--ready-file":
                    if (!TryReadValue(args, ref index, out readyFile))
                        return 15;
                    break;
                case "--help":
                case "-h":
                    WriteAppHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown lab app option: {args[index]}");
                    return 15;
            }
        }

        try
        {
            return LabAppServeRunner.Run(port, readyFile);
        }
        catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"LabApp could not start: {ex.Message}");
            return 15;
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
        return 15;
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
        Console.WriteLine("  Runtime output includes project-verify reports, target TRX, semantic diff, quality budgets, and failure-only trace/screenshot artifacts.");
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

using System.Text.Json;
using Migrator.Core;

internal static class RemediationCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "evaluate" => RunEvaluate(args.Skip(1).ToArray()),
            "guard" => RunGuard(args.Skip(1).ToArray()),
            "rebaseline" => RunRebaseline(args.Skip(1).ToArray()),
            _ => UnknownCommand(args[0])
        };
    }

    static int RunEvaluate(string[] args)
    {
        string? beforeRun = null;
        string? afterRun = null;
        string? candidate = null;
        string? autonomyState = null;
        var outPath = "remediation-evaluation.json";

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--before-run":
                    beforeRun = ReadValue(args, ref i, "--before-run");
                    break;
                case "--after-run":
                    afterRun = ReadValue(args, ref i, "--after-run");
                    break;
                case "--candidate":
                    candidate = ReadValue(args, ref i, "--candidate");
                    break;
                case "--autonomy-state":
                    autonomyState = ReadValue(args, ref i, "--autonomy-state");
                    break;
                case "--out":
                    outPath = ReadValue(args, ref i, "--out");
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown remediation evaluate option: {args[i]}");
                    return 2;
            }
        }

        if (string.IsNullOrWhiteSpace(beforeRun) || string.IsNullOrWhiteSpace(afterRun) || string.IsNullOrWhiteSpace(candidate))
        {
            Console.Error.WriteLine("remediation evaluate requires --before-run, --after-run, and --candidate.");
            return 2;
        }

        try
        {
            var visited = LoadVisitedStateHashes(autonomyState);
            var before = RemediationStateEvaluator.LoadRunState(beforeRun);
            var after = RemediationStateEvaluator.LoadRunState(afterRun);
            var evaluation = RemediationStateEvaluator.Evaluate(before, after, candidate, visited);

            WriteJson(outPath, evaluation);

            Console.WriteLine("=== Remediation Evaluation ===");
            Console.WriteLine($"Decision: {evaluation.Decision}");
            Console.WriteLine($"Reason: {evaluation.Reason}");
            Console.WriteLine($"Before state: {evaluation.Before.StateHash}");
            Console.WriteLine($"After state:  {evaluation.After.StateHash}");
            Console.WriteLine($"Candidate: {evaluation.CandidateFingerprint}");
            Console.WriteLine($"Rollback required: {evaluation.RollbackRequired}");
            foreach (var item in evaluation.Improvements)
                Console.WriteLine($"  + {item}");
            foreach (var item in evaluation.Regressions)
                Console.WriteLine($"  - {item}");
            Console.WriteLine($"Evaluation: {Path.GetFullPath(outPath)}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 4;
        }
    }

    static int RunRebaseline(string[] args)
    {
        string? beforeRun = null;
        string? afterRun = null;
        var outPath = "remediation-rebaseline.json";

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--before-run":
                    beforeRun = ReadValue(args, ref i, "--before-run");
                    break;
                case "--after-run":
                    afterRun = ReadValue(args, ref i, "--after-run");
                    break;
                case "--out":
                    outPath = ReadValue(args, ref i, "--out");
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown remediation rebaseline option: {args[i]}");
                    return 2;
            }
        }

        if (string.IsNullOrWhiteSpace(beforeRun) || string.IsNullOrWhiteSpace(afterRun))
        {
            Console.Error.WriteLine("remediation rebaseline requires --before-run and --after-run.");
            return 2;
        }

        try
        {
            var before = RemediationStateEvaluator.LoadRunState(beforeRun);
            var after = RemediationStateEvaluator.LoadRunState(afterRun);
            var evidence = RemediationRebaselineEvaluator.Evaluate(before, after);

            WriteJson(outPath, evidence);

            Console.WriteLine("=== Remediation Rebaseline ===");
            Console.WriteLine($"Decision: {evidence.Decision}");
            Console.WriteLine($"Reason: {evidence.Reason}");
            Console.WriteLine($"Before state: {evidence.Before.StateHash}");
            Console.WriteLine($"After state:  {evidence.After.StateHash}");
            Console.WriteLine($"Before tool:  {evidence.Before.ToolSha256}");
            Console.WriteLine($"After tool:   {evidence.After.ToolSha256}");
            foreach (var item in evidence.Improvements)
                Console.WriteLine($"  + {item}");
            foreach (var item in evidence.Regressions)
                Console.WriteLine($"  - {item}");
            Console.WriteLine($"Rebaseline evidence: {Path.GetFullPath(outPath)}");

            // A rejected rebaseline is deterministic evidence, not a CLI failure.
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 4;
        }
    }

    static int RunGuard(string[] args)
    {
        string? acceptedRun = null;
        string? inputPath = null;
        string? autonomyState = null;
        var configPaths = new List<string>();
        var outPath = "remediation-cycle-guard.json";

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--accepted-run":
                    acceptedRun = ReadValue(args, ref i, "--accepted-run");
                    break;
                case "--input":
                    inputPath = ReadValue(args, ref i, "--input");
                    break;
                case "--config":
                    configPaths.Add(ReadValue(args, ref i, "--config"));
                    break;
                case "--autonomy-state":
                    autonomyState = ReadValue(args, ref i, "--autonomy-state");
                    break;
                case "--out":
                    outPath = ReadValue(args, ref i, "--out");
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown remediation guard option: {args[i]}");
                    return 2;
            }
        }

        if (string.IsNullOrWhiteSpace(acceptedRun)
            || string.IsNullOrWhiteSpace(inputPath)
            || string.IsNullOrWhiteSpace(autonomyState))
        {
            Console.Error.WriteLine("remediation guard requires --accepted-run, --input, and --autonomy-state.");
            return 2;
        }

        try
        {
            var autonomy = LoadAutonomyGuardState(autonomyState);
            var accepted = RemediationStateEvaluator.LoadRunState(acceptedRun);
            var workspaceRoot = ResolveWorkspaceRoot(autonomyState);
            var source = SourceInputIdentityCapture.Capture(inputPath, workspaceRoot);
            var config = configPaths.Count == 0
                ? new ProjectAdapterConfig()
                : ProjectAdapterConfigMerger.LoadAndMerge(configPaths);
            var configSha256 = CanonicalJsonHasher.ComputeSha256(config);
            var guard = RemediationCycleGuardEvaluator.Evaluate(
                accepted,
                source.Hash,
                configSha256,
                autonomy.CurrentStateHash,
                autonomy.RollbackRequired,
                autonomy.CycleInProgress,
                autonomy.Status);

            WriteJson(outPath, guard);

            Console.WriteLine("=== Remediation Cycle Guard ===");
            Console.WriteLine($"Decision: {guard.Decision}");
            Console.WriteLine($"Reason: {guard.Reason}");
            Console.WriteLine($"Accepted state: {guard.AcceptedStateHash}");
            Console.WriteLine($"Workspace identity: {guard.WorkspaceIdentitySha256}");
            Console.WriteLine($"Rollback confirmed: {guard.RollbackConfirmed}");
            Console.WriteLine($"Ready to start cycle: {guard.ReadyToStartCycle}");
            Console.WriteLine($"Guard: {Path.GetFullPath(outPath)}");

            // A blocker is a valid deterministic guard result, not a CLI/infrastructure crash.
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 4;
        }
    }

    static IReadOnlyCollection<string> LoadVisitedStateHashes(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Array.Empty<string>();

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("visitedStateHashes", out var values) || values.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return values.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    static AutonomyGuardState LoadAutonomyGuardState(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new InvalidOperationException($"REMEDIATION_AUTONOMY_STATE_MISSING: {fullPath}");

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        var root = document.RootElement;
        var schema = ReadString(root, "schemaVersion");
        if (!string.Equals(schema, "standard-migration-autonomy/v2", StringComparison.Ordinal)
            && !string.Equals(schema, "standard-migration-autonomy/v3", StringComparison.Ordinal))
            throw new InvalidOperationException($"REMEDIATION_AUTONOMY_STATE_SCHEMA_INVALID: {schema}");

        var status = ReadString(root, "status");
        var currentStateHash = ReadString(root, "currentStateHash");
        var rollbackRequired = root.TryGetProperty("rollbackRequired", out var rollback) && rollback.ValueKind == JsonValueKind.True;
        var cycleInProgress = root.TryGetProperty("cycleInProgress", out var activeCycle) && activeCycle.ValueKind == JsonValueKind.True;
        return new AutonomyGuardState(status, currentStateHash, rollbackRequired, cycleInProgress);
    }

    static string ResolveWorkspaceRoot(string autonomyStatePath)
    {
        var stateFile = new FileInfo(Path.GetFullPath(autonomyStatePath));
        var stateDirectory = stateFile.Directory
            ?? throw new InvalidOperationException("REMEDIATION_AUTONOMY_STATE_PARENT_MISSING");
        return stateDirectory.Parent?.FullName ?? stateDirectory.FullName;
    }

    static string ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    static void WriteJson<T>(string path, T value)
    {
        var fullOutPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutPath) ?? Directory.GetCurrentDirectory());
        File.WriteAllText(fullOutPath, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown remediation command: {command}");
        PrintHelp();
        return 2;
    }

    static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{option} requires a value");
        return args[++index];
    }

    static bool IsHelp(string value) => value is "--help" or "-h" or "help";

    static void PrintHelp()
    {
        Console.WriteLine("Remediation commands:");
        Console.WriteLine("  selenium-pw-migrator remediation guard --accepted-run <run> --input <source> [--config <json> ...] --autonomy-state <json> --out <json>");
        Console.WriteLine("  selenium-pw-migrator remediation evaluate --before-run <run> --after-run <run> --candidate <stable-description> [--autonomy-state <json>] --out <json>");
        Console.WriteLine("  selenium-pw-migrator remediation rebaseline --before-run <old-tool-run> --after-run <new-tool-run> --out <json>");
        Console.WriteLine();
        Console.WriteLine("guard proves that current source/config identity matches the accepted baseline before a cycle starts; after REJECT_* it is also the rollback proof, and an abandoned active cycle can receive ABORT_CONFIRMED after exact baseline restoration.");
        Console.WriteLine("evaluate computes ACCEPT / REJECT_NO_PROGRESS / REJECT_REGRESSION / REJECT_CYCLE from exact run artifacts. The agent does not classify progress.");
        Console.WriteLine("rebaseline is the explicit tool-upgrade boundary: source/config/environment must match and the new tool run must introduce no deterministic regression.");
    }

    sealed record AutonomyGuardState(string Status, string CurrentStateHash, bool RollbackRequired, bool CycleInProgress);
}

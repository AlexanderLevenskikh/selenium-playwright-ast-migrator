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

        if (!string.Equals(args[0], "evaluate", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Unknown remediation command: {args[0]}");
            PrintHelp();
            return 2;
        }

        string? beforeRun = null;
        string? afterRun = null;
        string? candidate = null;
        string? autonomyState = null;
        var outPath = "remediation-evaluation.json";

        for (var i = 1; i < args.Length; i++)
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

            var fullOutPath = Path.GetFullPath(outPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutPath) ?? Directory.GetCurrentDirectory());
            File.WriteAllText(fullOutPath, JsonSerializer.Serialize(evaluation, new JsonSerializerOptions { WriteIndented = true }));

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
            Console.WriteLine($"Evaluation: {fullOutPath}");

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
        Console.WriteLine("  selenium-pw-migrator remediation evaluate --before-run <run> --after-run <run> --candidate <stable-description> [--autonomy-state <json>] --out <json>");
        Console.WriteLine();
        Console.WriteLine("Core computes ACCEPT / REJECT_NO_PROGRESS / REJECT_REGRESSION / REJECT_CYCLE from exact run artifacts. The agent does not classify progress.");
    }
}

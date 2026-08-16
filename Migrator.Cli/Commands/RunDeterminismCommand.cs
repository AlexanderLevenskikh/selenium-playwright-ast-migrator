using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Migrator.Core;

internal static class RunDeterminismCommand
{
    const int DeterminismMismatchExitCode = 6;

    public static bool IsRunTwiceRequest(string[] args)
        => args.Any(arg =>
            string.Equals(arg, "--twice", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--assert-identical", StringComparison.OrdinalIgnoreCase));

    public static int RunTwice(string[] args)
    {
        var twice = args.Any(arg => string.Equals(arg, "--twice", StringComparison.OrdinalIgnoreCase));
        var assertIdentical = args.Any(arg => string.Equals(arg, "--assert-identical", StringComparison.OrdinalIgnoreCase));

        if (!twice)
        {
            Console.Error.WriteLine("run --assert-identical requires --twice.");
            return 2;
        }

        var childArgs = args
            .Where(arg =>
                !string.Equals(arg, "--twice", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(arg, "--assert-identical", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var outIndexes = childArgs
            .Select((value, index) => (value, index))
            .Where(item => string.Equals(item.value, "--out", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index)
            .ToArray();

        if (outIndexes.Length != 1 || outIndexes[0] + 1 >= childArgs.Length)
        {
            Console.Error.WriteLine("run --twice requires exactly one '--out <directory>'.");
            return 2;
        }

        var outIndex = outIndexes[0];
        var root = Path.GetFullPath(childArgs[outIndex + 1]);
        if (File.Exists(root))
        {
            Console.Error.WriteLine($"DETERMINISM_OUTPUT_CONFLICT: output path is a file: {root}");
            return 2;
        }

        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            Console.Error.WriteLine(
                $"DETERMINISM_OUTPUT_NOT_EMPTY: refusing to delete or reuse non-empty output root: {root}");
            return 2;
        }

        Directory.CreateDirectory(root);

        var candidate = Path.Combine(root, "_candidate");
        var runAPath = Path.Combine(root, "run-a");
        var runBPath = Path.Combine(root, "run-b");

        foreach (var path in new[] { candidate, runAPath, runBPath })
        {
            if (Directory.Exists(path) || File.Exists(path))
            {
                Console.Error.WriteLine($"DETERMINISM_OUTPUT_CONFLICT: {path}");
                return 2;
            }
        }

        var identityArgs = childArgs.ToArray();
        identityArgs[outIndex + 1] = "<RUN_OUTPUT>";
        var invocationSha256 = CanonicalJsonHasher.ComputeSha256(new
        {
            command = "run",
            arguments = identityArgs
        });

        var startedAtUtc = DateTimeOffset.UtcNow;

        Console.WriteLine("=== Determinism Run A ===");
        var runAExitCode = RunChild(childArgs, outIndex, candidate);
        if (!Directory.Exists(candidate))
        {
            Console.Error.WriteLine("DETERMINISM_RUN_A_OUTPUT_MISSING");
            return runAExitCode != 0 ? runAExitCode : 4;
        }
        Directory.Move(candidate, runAPath);

        Console.WriteLine();
        Console.WriteLine("=== Determinism Run B ===");
        var runBExitCode = RunChild(childArgs, outIndex, candidate);
        if (!Directory.Exists(candidate))
        {
            Console.Error.WriteLine("DETERMINISM_RUN_B_OUTPUT_MISSING");
            return runBExitCode != 0 ? runBExitCode : 4;
        }
        Directory.Move(candidate, runBPath);

        RunDigestSnapshot runA;
        RunDigestSnapshot runB;
        try
        {
            runA = RunDigest.ComputeDirectory(runAPath);
            runB = RunDigest.ComputeDirectory(runBPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"DETERMINISM_DIGEST_FAILED: {ex.Message}");
            return 4;
        }

        WriteJson(Path.Combine(runAPath, "run-digest.json"), runA);
        WriteJson(Path.Combine(runBPath, "run-digest.json"), runB);

        var comparison = RunDigest.Compare(
            runA,
            runB,
            runAExitCode,
            runBExitCode,
            invocationSha256);

        WriteJson(Path.Combine(root, "determinism-result.json"), comparison);
        WriteJson(Path.Combine(root, "run-metadata.json"), new
        {
            SchemaVersion = "migrator-run-determinism-metadata/v1",
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            InvocationSha256 = invocationSha256
        });

        Console.WriteLine();
        Console.WriteLine("=== Determinism Result ===");
        Console.WriteLine($"Decision: {comparison.Decision}");
        Console.WriteLine($"Invocation SHA-256: {comparison.InvocationSha256}");
        Console.WriteLine($"Run A digest: {comparison.RunADigestSha256}");
        Console.WriteLine($"Run B digest: {comparison.RunBDigestSha256}");
        Console.WriteLine($"Run A exit: {comparison.RunAExitCode}");
        Console.WriteLine($"Run B exit: {comparison.RunBExitCode}");
        foreach (var difference in comparison.Differences.Take(50))
            Console.WriteLine($"  != {difference}");
        Console.WriteLine($"Evidence: {Path.Combine(root, "determinism-result.json")}");

        if (assertIdentical && !string.Equals(comparison.Decision, "IDENTICAL", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("RUN_DETERMINISM_ASSERTION_FAILED");
            return DeterminismMismatchExitCode;
        }

        // Determinism must never turn a reproducibly failing migration into process success.
        if (runAExitCode != 0)
            return runAExitCode;
        if (runBExitCode != 0)
            return runBExitCode;

        return 0;
    }

    static int RunChild(string[] args, int outIndex, string outputPath)
    {
        var childArgs = args.ToArray();
        childArgs[outIndex + 1] = outputPath;

        var invocation = ResolveSelfInvocation();
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.Executable,
            UseShellExecute = false
        };

        foreach (var prefix in invocation.PrefixArguments)
            startInfo.ArgumentList.Add(prefix);

        startInfo.ArgumentList.Add("run");
        foreach (var arg in childArgs)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("DETERMINISM_CHILD_PROCESS_START_FAILED");
        process.WaitForExit();
        return process.ExitCode;
    }

    static SelfInvocation ResolveSelfInvocation()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("DETERMINISM_SELF_EXECUTABLE_MISSING");

        var fileName = Path.GetFileNameWithoutExtension(executable);
        if (string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var entryAssembly = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entryAssembly))
                throw new InvalidOperationException("DETERMINISM_ENTRY_ASSEMBLY_MISSING");
            return new SelfInvocation(executable, new[] { entryAssembly });
        }

        return new SelfInvocation(executable, Array.Empty<string>());
    }

    static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    sealed record SelfInvocation(string Executable, IReadOnlyList<string> PrefixArguments);
}
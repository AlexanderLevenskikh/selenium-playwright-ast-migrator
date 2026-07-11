using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Migrator.Core;

internal static class MigrationValidationHost
{
    internal const string ResultSchema = "migration-validation-host-result/v1";
    internal const string ProfileSchema = "migration-validation-host-profile/v1";

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Run(
        string outPath,
        string validationProfile,
        string validationProject,
        string customCommand,
        int timeoutSeconds,
        bool dryRun,
        bool checkpointOnPass,
        bool forceValidation,
        TextWriter output,
        TextWriter error,
        IFileSystem? fileSystem = null,
        IProcessRunner? processRunner = null,
        IClock? clock = null)
    {
        fileSystem ??= PhysicalFileSystem.Instance;
        clock ??= SystemClock.Instance;
        processRunner ??= new SystemProcessRunner(clock);
        outPath = fileSystem.GetFullPath(outPath);
        var started = clock.GetTimestamp();
        var generatedAt = clock.UtcNow;
        var invocationId = BuildInvocationId(generatedAt);
        var profile = ResolveProfile(outPath, validationProfile, fileSystem, out var profileError);
        if (profileError != null)
        {
            error.WriteLine(profileError);
            return 2;
        }

        var planStdout = new StringWriter();
        var planStderr = new StringWriter();
        var planExit = MigrationIncrementalPipeline.PlanValidation(outPath, forceValidation, planStdout, planStderr);
        if (planExit != 0)
        {
            error.Write(planStderr.ToString());
            WriteResult(fileSystem, outPath, new HostResult(
                "PLAN_FAILED", profile, generatedAt, clock.GetElapsedTime(started), null, null, false, dryRun,
                Array.Empty<HostCheck>(), Array.Empty<HostProcessStep>(), planStderr.ToString().Trim(), false, false));
            return planExit;
        }

        var planPath = fileSystem.Combine(outPath, "validation-plan.json");
        using var plan = JsonDocument.Parse(fileSystem.ReadAllText(planPath));
        var planRoot = plan.RootElement;
        var inputFingerprint = RequiredString(planRoot, "inputFingerprint");
        var plannedImpact = RequiredString(planRoot, "validationScope");
        var baseCachePath = RequiredString(planRoot, "cachePath");

        var checks = new List<HostCheck>();
        checks.Add(RunWaveContract(outPath, clock));
        checks.Add(RunArtifactSchemaCheck(outPath, fileSystem, clock));
        checks.Add(RunGeneratedSourceCheck(outPath, fileSystem, clock));
        var internalPass = checks.All(check => check.Status == "PASS");

        List<ValidationProcessStep> processSteps;
        try
        {
            var resolvedProject = ResolveValidationProject(outPath, validationProject, fileSystem);
            processSteps = BuildProcessSteps(resolvedProject, customCommand, timeoutSeconds, fileSystem);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or ArgumentException)
        {
            var message = "VALIDATION_HOST_CONFIGURATION_REQUIRED: " + ex.Message;
            error.WriteLine(message);
            WriteResult(fileSystem, outPath, new HostResult(
                "CONFIGURATION_REQUIRED", profile, generatedAt, clock.GetElapsedTime(started), inputFingerprint, null, false, dryRun,
                checks, Array.Empty<HostProcessStep>(), message, false, false));
            return 2;
        }
        var externalRequired = plannedImpact is "changed-dotnet-files" or "changed-typescript-files" or "full-project";
        if (internalPass && externalRequired && processSteps.Count == 0)
        {
            var message = "VALIDATION_HOST_CONFIGURATION_REQUIRED: code/project changes require --validation-project or --validation-command; refusing to record an under-validated PASS.";
            error.WriteLine(message);
            WriteResult(fileSystem, outPath, new HostResult(
                "CONFIGURATION_REQUIRED", profile, generatedAt, clock.GetElapsedTime(started), inputFingerprint, null, false, dryRun,
                checks, Array.Empty<HostProcessStep>(), message, false, false));
            return 2;
        }

        var validationContractFingerprint = ComputeValidationContractFingerprint(profile, plannedImpact, processSteps);
        var contractCachePath = BuildContractCachePath(baseCachePath, inputFingerprint, validationContractFingerprint, fileSystem);
        var cacheHit = !forceValidation && IsCompatibleHostCache(contractCachePath, inputFingerprint, validationContractFingerprint, profile, fileSystem);
        if (cacheHit && internalPass)
        {
            fileSystem.WriteAllText(fileSystem.Combine(outPath, "validation-result.json"), fileSystem.ReadAllText(contractCachePath));
            WriteResult(fileSystem, outPath, new HostResult(
                "CACHE_HIT", profile, generatedAt, clock.GetElapsedTime(started), inputFingerprint, validationContractFingerprint, true, dryRun,
                checks, Array.Empty<HostProcessStep>(), "Cheap internal checks passed; exact-input and exact-validation-contract PASS was materialized without creating a duplicate checkpoint.", true, false));
            output.WriteLine("MIGRATION_VALIDATION_HOST_CACHE_HIT");
            output.WriteLine("Profile: " + profile);
            output.WriteLine("Input fingerprint: " + inputFingerprint);
            output.WriteLine("Validation contract: " + validationContractFingerprint);
            return 0;
        }

        if (dryRun)
        {
            var planned = processSteps.Select(step => HostProcessStep.Planned(step.Id, FormatRequest(step.Request))).ToArray();
            WriteResult(fileSystem, outPath, new HostResult(
                internalPass ? "DRY_RUN" : "FAIL", profile, generatedAt, clock.GetElapsedTime(started), inputFingerprint, validationContractFingerprint, false, true,
                checks, planned, internalPass ? "Validation plan resolved without executing external processes." : "An internal validation check failed.", false, false));
            output.WriteLine(internalPass ? "MIGRATION_VALIDATION_HOST_DRY_RUN" : "MIGRATION_VALIDATION_HOST_FAIL");
            foreach (var step in planned)
                output.WriteLine("Planned: " + step.CommandLine);
            return internalPass ? 0 : 1;
        }

        var processResults = new List<HostProcessStep>();
        if (internalPass && processSteps.Count > 0)
        {
            var executor = new ValidationProcessExecutor(processRunner);
            var executed = executor.Execute(processSteps);
            foreach (var result in executed)
            {
                var stepResult = WriteProcessEvidence(outPath, invocationId, result, fileSystem);
                processResults.Add(stepResult);
            }
        }

        var externalPass = processResults.All(step => step.Status == "PASS")
            && (!externalRequired || processResults.Count > 0);
        var pass = internalPass && externalPass;
        var evidenceCommands = checks.Select(check => "internal:" + check.Id)
            .Concat(processResults.Select(step => step.CommandLine))
            .ToArray();
        var executedScope = MapExecutedScope(plannedImpact);
        var recordOutput = new StringWriter();
        var recordError = new StringWriter();
        var recordExit = MigrationIncrementalPipeline.RecordValidation(
            outPath,
            "validation-host",
            pass ? 0 : 1,
            string.Join(" && ", evidenceCommands),
            executedScope,
            recordOutput,
            recordError,
            contractCachePath,
            validationContractFingerprint,
            profile);

        var checkpointWrittenOnPass = false;
        if (pass && recordExit == 0 && checkpointOnPass)
            checkpointWrittenOnPass = WriteCheckpoint(outPath, fileSystem, output, error) == 0;

        var status = pass && recordExit == 0 ? "PASS" : "FAIL";
        var detail = pass
            ? "All required internal and external checks passed; exact-input validation evidence was recorded."
            : string.Join(" ", new[] { recordError.ToString().Trim(), "One or more required checks failed." }.Where(value => value.Length > 0));
        WriteResult(fileSystem, outPath, new HostResult(
            status, profile, generatedAt, clock.GetElapsedTime(started), inputFingerprint, validationContractFingerprint, false, false,
            checks, processResults, detail, pass && recordExit == 0, checkpointWrittenOnPass));

        output.WriteLine(status == "PASS" ? "MIGRATION_VALIDATION_HOST_PASS" : "MIGRATION_VALIDATION_HOST_FAIL");
        output.WriteLine("Profile: " + profile);
        output.WriteLine("Impact: " + plannedImpact);
        output.WriteLine("Validation contract: " + validationContractFingerprint);
        output.WriteLine("Internal checks: " + checks.Count);
        output.WriteLine("Processes: " + processResults.Count);
        output.WriteLine("Result: " + fileSystem.Combine(outPath, "validation-host-result.json"));
        if (!string.IsNullOrWhiteSpace(recordError.ToString()))
            error.Write(recordError.ToString());
        return status == "PASS" ? 0 : 1;
    }

    static string ResolveProfile(string outPath, string requested, IFileSystem fileSystem, out string? error)
    {
        error = null;
        var normalized = string.IsNullOrWhiteSpace(requested) ? "auto" : requested.Trim().ToLowerInvariant();
        if (normalized == "auto")
        {
            var policyPath = fileSystem.Combine(outPath, "execution-policy.json");
            if (!fileSystem.FileExists(policyPath))
            {
                error = "VALIDATION_PROFILE_INVALID: execution-policy.json is required when --validation-profile auto is used.";
                return string.Empty;
            }
            using var policy = JsonDocument.Parse(fileSystem.ReadAllText(policyPath));
            normalized = OptionalString(policy.RootElement, "profile") ?? string.Empty;
        }
        if (normalized is not ("fast" or "standard" or "audit"))
        {
            error = "VALIDATION_PROFILE_INVALID: --validation-profile must be auto, fast, standard, or audit.";
            return string.Empty;
        }
        return normalized;
    }

    static HostCheck RunWaveContract(string outPath, IClock clock)
    {
        var started = clock.GetTimestamp();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = MigrationFastPath.ValidateWave(outPath, stdout, stderr);
        return new HostCheck(
            "wave-contract",
            exitCode == 0 ? "PASS" : "FAIL",
            clock.GetElapsedTime(started).TotalMilliseconds,
            exitCode == 0 ? stdout.ToString().Trim() : stderr.ToString().Trim());
    }

    static HostCheck RunArtifactSchemaCheck(string outPath, IFileSystem fileSystem, IClock clock)
    {
        var started = clock.GetTimestamp();
        var failures = new List<string>();
        var checkedFiles = 0;
        foreach (var path in fileSystem.EnumerateFiles(outPath, "*", SearchOption.AllDirectories))
        {
            var relative = NormalizeSlashes(fileSystem.GetRelativePath(outPath, path));
            if (relative.StartsWith("generated/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("source-scope/", StringComparison.OrdinalIgnoreCase)
                || relative.Contains("/.cache/", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    using var _ = JsonDocument.Parse(fileSystem.ReadAllText(path));
                    checkedFiles++;
                }
                else if (path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                {
                    var lineNumber = 0;
                    foreach (var line in fileSystem.ReadAllText(path).Split('\n'))
                    {
                        lineNumber++;
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        using var _ = JsonDocument.Parse(line);
                    }
                    checkedFiles++;
                }
            }
            catch (JsonException ex)
            {
                failures.Add($"{relative}: {ex.Message}");
            }
        }
        return new HostCheck(
            "artifact-schema",
            failures.Count == 0 ? "PASS" : "FAIL",
            clock.GetElapsedTime(started).TotalMilliseconds,
            failures.Count == 0 ? $"Parsed {checkedFiles} JSON/JSONL artifacts." : string.Join(" | ", failures.Take(10)));
    }

    static HostCheck RunGeneratedSourceCheck(string outPath, IFileSystem fileSystem, IClock clock)
    {
        var started = clock.GetTimestamp();
        var generatedRoot = ReadRunContextString(outPath, "generatedOutputPath", fileSystem);
        var failures = new List<string>();
        var checkedFiles = 0;
        foreach (var path in fileSystem.EnumerateFiles(generatedRoot, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".cs")
            {
                var tree = CSharpSyntaxTree.ParseText(fileSystem.ReadAllText(path), path: path);
                var errors = tree.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error).Take(10).ToArray();
                if (errors.Length > 0)
                    failures.AddRange(errors.Select(item => NormalizeSlashes(fileSystem.GetRelativePath(generatedRoot, path)) + ": " + item));
                checkedFiles++;
            }
            else if (extension is ".ts" or ".tsx" or ".js" or ".jsx")
            {
                var text = fileSystem.ReadAllText(path);
                if (text.Contains('\0') || text.Contains("<<<<<<<", StringComparison.Ordinal) || text.Contains(">>>>>>>", StringComparison.Ordinal))
                    failures.Add(NormalizeSlashes(fileSystem.GetRelativePath(generatedRoot, path)) + ": contains NUL or unresolved merge markers");
                checkedFiles++;
            }
        }
        return new HostCheck(
            "generated-source-sanity",
            failures.Count == 0 ? "PASS" : "FAIL",
            clock.GetElapsedTime(started).TotalMilliseconds,
            failures.Count == 0 ? $"Checked {checkedFiles} generated source files." : string.Join(" | ", failures.Take(10)));
    }

    static List<ValidationProcessStep> BuildProcessSteps(
        string? validationProject,
        string customCommand,
        int timeoutSeconds,
        IFileSystem fileSystem)
    {
        var steps = new List<ValidationProcessStep>();
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 7200));
        if (!string.IsNullOrWhiteSpace(validationProject))
        {
            var request = BuildProjectRequest(validationProject, timeout, fileSystem);
            steps.Add(new ValidationProcessStep("target-project-build", request));
        }
        if (!string.IsNullOrWhiteSpace(customCommand))
            steps.Add(new ValidationProcessStep("custom-validation", BuildShellRequest(customCommand, Directory.GetCurrentDirectory(), timeout)));
        return steps;
    }

    static ProcessRequest BuildProjectRequest(string projectPath, TimeSpan timeout, IFileSystem fileSystem)
    {
        var fullPath = fileSystem.GetFullPath(projectPath);
        if (fileSystem.DirectoryExists(fullPath))
        {
            var candidate = fileSystem.EnumerateFiles(fullPath, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault()
                ?? fileSystem.EnumerateFiles(fullPath, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault()
                ?? fileSystem.EnumerateFiles(fullPath, "tsconfig.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (candidate == null)
                throw new InvalidOperationException($"No .sln, .csproj, or tsconfig.json was found in validation project directory: {fullPath}");
            fullPath = candidate;
        }
        if (!fileSystem.FileExists(fullPath))
            throw new FileNotFoundException("Validation project was not found.", fullPath);

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is ".sln" or ".slnx" or ".csproj")
        {
            return new ProcessRequest(
                "dotnet",
                new[] { "build", fullPath, "--no-restore", "--nologo", "--verbosity", "minimal" },
                fileSystem.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory(),
                timeout,
                new Dictionary<string, string?>
                {
                    ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                    ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
                },
                "target project build");
        }
        if (Path.GetFileName(fullPath).Equals("tsconfig.json", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessRequest(
                "npx",
                new[] { "--no-install", "tsc", "--noEmit", "-p", fullPath },
                fileSystem.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory(),
                timeout,
                DisplayName: "TypeScript project check");
        }
        throw new InvalidOperationException("--validation-project must point to a .sln, .slnx, .csproj, tsconfig.json, or a directory containing one.");
    }

    static ProcessRequest BuildShellRequest(string command, string workingDirectory, TimeSpan timeout) =>
        OperatingSystem.IsWindows()
            ? new ProcessRequest(ResolveWindowsPowerShellExecutable(), new[] { "-NoProfile", "-NonInteractive", "-Command", command }, workingDirectory, timeout, DisplayName: "custom validation")
            : new ProcessRequest("/bin/bash", new[] { "-c", command }, workingDirectory, timeout, DisplayName: "custom validation");

    static string ResolveWindowsPowerShellExecutable()
    {
        foreach (var candidate in new[] { "pwsh.exe", "pwsh", "powershell.exe", "powershell" })
        {
            var resolved = ResolveExecutableOnPath(candidate);
            if (resolved != null)
                return resolved;
        }

        throw new InvalidOperationException(
            "PowerShell was not found. Install PowerShell 7 (`pwsh`) or ensure Windows PowerShell is available on PATH.");
    }

    static string? ResolveExecutableOnPath(string candidate)
    {
        if (Path.IsPathRooted(candidate))
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var hasExtension = Path.HasExtension(candidate);
        var extensions = hasExtension
            ? new[] { string.Empty }
            : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Prepend(string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalizedDirectory = directory.Trim('"');
            foreach (var extension in extensions)
            {
                var path = Path.Combine(normalizedDirectory, candidate + extension);
                if (File.Exists(path))
                    return Path.GetFullPath(path);
            }
        }

        return null;
    }

    static string? ResolveValidationProject(string outPath, string requested, IFileSystem fileSystem)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return fileSystem.GetFullPath(requested);
        var generated = ReadRunContextString(outPath, "generatedOutputPath", fileSystem);
        return fileSystem.EnumerateFiles(generated, "*.sln", SearchOption.AllDirectories).FirstOrDefault()
            ?? fileSystem.EnumerateFiles(generated, "*.csproj", SearchOption.AllDirectories).FirstOrDefault()
            ?? fileSystem.EnumerateFiles(generated, "tsconfig.json", SearchOption.AllDirectories).FirstOrDefault();
    }

    static string ComputeValidationContractFingerprint(
        string profile,
        string plannedImpact,
        IReadOnlyList<ValidationProcessStep> processSteps)
    {
        var canonical = new StringBuilder()
            .Append(ResultSchema).Append('\n')
            .Append(ProfileSchema).Append('\n')
            .Append(profile).Append('\n')
            .Append("internal:wave-contract\ninternal:artifact-schema\ninternal:generated-source-sanity\n");
        foreach (var step in processSteps)
        {
            canonical.Append(step.Id).Append('|')
                .Append(step.Required).Append('|')
                .Append(step.Request.FileName).Append('|')
                .Append(step.Request.WorkingDirectory).Append('|')
                .Append(step.Request.Timeout.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .AppendJoin("\u001f", step.Request.Arguments)
                .Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    static string BuildContractCachePath(
        string baseCachePath,
        string inputFingerprint,
        string validationContractFingerprint,
        IFileSystem fileSystem)
    {
        var root = fileSystem.GetDirectoryName(baseCachePath)
            ?? throw new InvalidOperationException("Validation cache path has no parent directory.");
        return fileSystem.Combine(root, inputFingerprint + "." + validationContractFingerprint + ".json");
    }

    static bool IsCompatibleHostCache(
        string path,
        string inputFingerprint,
        string validationContractFingerprint,
        string profile,
        IFileSystem fileSystem)
    {
        if (!fileSystem.FileExists(path)) return false;
        try
        {
            using var document = JsonDocument.Parse(fileSystem.ReadAllText(path));
            var root = document.RootElement;
            return OptionalString(root, "schemaVersion") == MigrationIncrementalPipeline.ValidationResultSchema
                && string.Equals(OptionalString(root, "status"), "PASS", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("exitCode", out var exitCode) && exitCode.TryGetInt32(out var code) && code == 0
                && !string.IsNullOrWhiteSpace(OptionalString(root, "command"))
                && root.TryGetProperty("scopeCoversPlannedImpact", out var covers) && covers.ValueKind == JsonValueKind.True
                && root.TryGetProperty("reusable", out var reusable) && reusable.ValueKind == JsonValueKind.True
                && string.Equals(OptionalString(root, "inputFingerprint"), inputFingerprint, StringComparison.OrdinalIgnoreCase)
                && string.Equals(OptionalString(root, "validationContractFingerprint"), validationContractFingerprint, StringComparison.OrdinalIgnoreCase)
                && string.Equals(OptionalString(root, "validationProfile"), profile, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return false;
        }
    }

    static HostProcessStep WriteProcessEvidence(string outPath, string invocationId, ValidationProcessStepResult result, IFileSystem fileSystem)
    {
        var evidenceRoot = fileSystem.Combine(outPath, "validation", "processes", invocationId);
        fileSystem.CreateDirectory(evidenceRoot);
        var safeId = new string(result.Id.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray());
        var stdoutPath = fileSystem.Combine(evidenceRoot, safeId + ".stdout.log");
        var stderrPath = fileSystem.Combine(evidenceRoot, safeId + ".stderr.log");
        fileSystem.WriteAllText(stdoutPath, result.Execution.StandardOutput);
        fileSystem.WriteAllText(stderrPath, result.Execution.StandardError);
        return new HostProcessStep(
            result.Id,
            result.Status,
            result.Execution.ExitCode,
            result.Execution.TimedOut,
            result.Execution.Duration.TotalMilliseconds,
            result.Execution.PeakWorkingSetBytes,
            result.Execution.CommandLine,
            NormalizeSlashes(fileSystem.GetRelativePath(outPath, stdoutPath)),
            NormalizeSlashes(fileSystem.GetRelativePath(outPath, stderrPath)));
    }

    static int WriteCheckpoint(string outPath, IFileSystem fileSystem, TextWriter output, TextWriter error)
    {
        var checkpointOutput = new StringWriter();
        var checkpointError = new StringWriter();
        var exit = MigrationIncrementalPipeline.CreateCheckpoint(outPath, "validation-host-pass", "validation", checkpointOutput, checkpointError);
        if (exit == 0)
            output.Write(checkpointOutput.ToString());
        else
            error.Write(checkpointError.ToString());
        return exit;
    }

    static string MapExecutedScope(string plannedImpact) => plannedImpact switch
    {
        "full-project" => "project",
        "changed-dotnet-files" or "changed-typescript-files" => "changed-files",
        _ => "artifacts"
    };

    static string ReadRunContextString(string outPath, string property, IFileSystem fileSystem)
    {
        using var context = JsonDocument.Parse(fileSystem.ReadAllText(fileSystem.Combine(outPath, "run-context.json")));
        return RequiredString(context.RootElement, property);
    }

    static void WriteResult(IFileSystem fileSystem, string outPath, HostResult result)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = ResultSchema,
            ["profileSchemaVersion"] = ProfileSchema,
            ["generatedAtUtc"] = result.GeneratedAtUtc.ToString("O"),
            ["invocationId"] = BuildInvocationId(result.GeneratedAtUtc),
            ["status"] = result.Status,
            ["profile"] = result.Profile,
            ["durationMilliseconds"] = Math.Round(result.Duration.TotalMilliseconds, 3),
            ["inputFingerprint"] = result.InputFingerprint,
            ["validationContractFingerprint"] = result.ValidationContractFingerprint,
            ["cacheHit"] = result.CacheHit,
            ["dryRun"] = result.DryRun,
            ["checks"] = result.Checks.Select(check => new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = check.Id,
                ["status"] = check.Status,
                ["durationMilliseconds"] = Math.Round(check.DurationMilliseconds, 3),
                ["detail"] = check.Detail
            }).ToArray(),
            ["processes"] = result.Processes.Select(step => new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = step.Id,
                ["status"] = step.Status,
                ["exitCode"] = step.ExitCode,
                ["timedOut"] = step.TimedOut,
                ["durationMilliseconds"] = Math.Round(step.DurationMilliseconds, 3),
                ["peakWorkingSetBytes"] = step.PeakWorkingSetBytes,
                ["commandLine"] = step.CommandLine,
                ["stdoutPath"] = step.StdoutPath,
                ["stderrPath"] = step.StderrPath
            }).ToArray(),
            ["detail"] = result.Detail,
            ["validationRecorded"] = result.ValidationRecorded,
            ["checkpointWritten"] = result.CheckpointWritten,
            ["invariants"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["singleHostOwnsPlanExecuteRecord"] = true,
                ["underScopedPassRejected"] = true,
                ["failedProcessNeverCached"] = true,
                ["cacheRequiresExactInputPass"] = true,
                ["cacheRequiresExactValidationContract"] = true,
                ["checkpointDoesNotMeanDone"] = true
            }
        };
        var serialized = JsonSerializer.Serialize(payload, JsonOptions);
        fileSystem.WriteAllText(fileSystem.Combine(outPath, "validation-host-result.json"), serialized);
        var historyRoot = fileSystem.Combine(outPath, "validation", "host-runs");
        fileSystem.CreateDirectory(historyRoot);
        fileSystem.WriteAllText(fileSystem.Combine(historyRoot, BuildInvocationId(result.GeneratedAtUtc) + ".json"), serialized);
    }

    static string BuildInvocationId(DateTimeOffset generatedAtUtc) =>
        generatedAtUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfffffff'Z'");

    static string FormatRequest(ProcessRequest request) =>
        string.Join(" ", new[] { request.FileName }.Concat(request.Arguments.Select(argument => argument.Any(char.IsWhiteSpace) ? '"' + argument + '"' : argument)));

    static string RequiredString(JsonElement root, string property)
    {
        var value = OptionalString(root, property);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Required property '{property}' is missing.");
        return value;
    }

    static string? OptionalString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static string NormalizeSlashes(string path) => path.Replace('\\', '/');

    sealed record HostResult(
        string Status,
        string Profile,
        DateTimeOffset GeneratedAtUtc,
        TimeSpan Duration,
        string? InputFingerprint,
        string? ValidationContractFingerprint,
        bool CacheHit,
        bool DryRun,
        IReadOnlyList<HostCheck> Checks,
        IReadOnlyList<HostProcessStep> Processes,
        string Detail,
        bool ValidationRecorded,
        bool CheckpointWritten);

    sealed record HostCheck(string Id, string Status, double DurationMilliseconds, string Detail);

    sealed record HostProcessStep(
        string Id,
        string Status,
        int? ExitCode,
        bool TimedOut,
        double DurationMilliseconds,
        long PeakWorkingSetBytes,
        string CommandLine,
        string? StdoutPath,
        string? StderrPath)
    {
        internal static HostProcessStep Planned(string id, string commandLine) =>
            new(id, "PLANNED", null, false, 0, 0, commandLine, null, null);
    }
}

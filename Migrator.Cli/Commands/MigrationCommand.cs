using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;

internal static class MigrationCommand
{
    const string InventorySchema = "migration-inventory/v1";
    const string ClustersSchema = "migration-clusters/v1";
    const string WavePlanSchema = "migration-wave-plan/v1";
    const string WaveTuningSchema = "migration-wave-tuning/v1";
    const string SourceScopeSchema = "migration-source-scope/v1";

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static readonly string[] IgnoredSegments = { "bin", "obj", ".git", "node_modules", "migration", "playwright-report", "TestResults", ".vs" };

    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        if (command == "plan" && args.Length > 1 && string.Equals(args[1], "show", StringComparison.OrdinalIgnoreCase))
        {
            var showOptions = MigrationOptions.Parse(args.Skip(2).ToArray(), out var showError);
            if (showOptions == null)
            {
                Console.Error.WriteLine(showError);
                PrintHelp();
                return 2;
            }

            return RunPlanShow(showOptions);
        }

        if (args.Skip(1).Any(IsHelp))
        {
            PrintHelp();
            return 0;
        }

        var options = MigrationOptions.Parse(args.Skip(1).ToArray(), out var error);
        if (options == null)
        {
            Console.Error.WriteLine(error);
            PrintHelp();
            return 2;
        }

        return command switch
        {
            "inventory" => RunInventory(options),
            "cluster" => RunCluster(options),
            "tune-wave-plan" => RunTuneWavePlan(options),
            "tune" => RunTuneWavePlan(options),
            "plan" => RunPlan(options),
            "run-wave" => RunWave(options),
            "refresh-wave-status" => RunRefreshWaveStatus(options),
            "validate-wave" => RunValidateWave(options),
            "check-progress" => RunCheckProgress(options),
            "validation-plan" => RunValidationPlan(options),
            "validate" => RunValidationHost(options),
            "validation-host" => RunValidationHost(options),
            "record-validation" => RunRecordValidation(options),
            "checkpoint-wave" => RunCheckpointWave(options),
            "resume-wave" => RunResumeWave(options),
            "build-review-bundle" => RunBuildReviewBundle(options),
            "perf-report" => RunPerformanceReport(options),
            _ => UnknownCommand(command)
        };
    }

    static int RunInventory(MigrationOptions options)
    {
        if (!ValidateInput(options.Input, out var fullInput))
            return 2;

        var inventory = BuildInventory(fullInput);
        Directory.CreateDirectory(options.Out);
        WriteInventoryArtifacts(options.Out, options.Format, inventory);
        Console.WriteLine("MIGRATION_INVENTORY_READY");
        Console.WriteLine($"Input: {inventory.InputPath}");
        Console.WriteLine($"Files scanned: {inventory.FilesScanned}");
        Console.WriteLine($"Test files: {inventory.TestFiles}");
        Console.WriteLine($"Test cases: {inventory.Tests.Length}");
        Console.WriteLine($"Artifacts: {Path.GetFullPath(options.Out)}");
        return 0;
    }

    static int RunCluster(MigrationOptions options)
    {
        MigrationInventoryReport inventory;
        if (!string.IsNullOrWhiteSpace(options.Inventory))
        {
            if (!TryReadInventory(options.Inventory, out inventory!, out var readError))
            {
                Console.Error.WriteLine(readError);
                return 2;
            }
        }
        else
        {
            if (!ValidateInput(options.Input, out var fullInput))
                return 2;
            inventory = BuildInventory(fullInput);
        }

        var clusters = BuildClusters(inventory);
        Directory.CreateDirectory(options.Out);
        WriteClusterArtifacts(options.Out, options.Format, clusters);
        Console.WriteLine("MIGRATION_CLUSTERS_READY");
        Console.WriteLine($"Clusters: {clusters.Clusters.Length}");
        Console.WriteLine($"Tests: {clusters.Tests.Length}");
        Console.WriteLine($"Artifacts: {Path.GetFullPath(options.Out)}");
        return 0;
    }

    static int RunPlan(MigrationOptions options)
    {
        var repoRoot = ResolveRepositoryRoot();
        options = NormalizeProjectPaths(options, repoRoot, normalizeInput: false);

        if (!string.Equals(options.Strategy, "wavefront", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("migration plan currently supports --strategy wavefront only.");
            return 2;
        }

        if (!TryResolvePlanInput(options, repoRoot, out var resolvedInput, out var inputError))
        {
            Console.Error.WriteLine(inputError);
            return 2;
        }

        if (!ValidateInput(resolvedInput, out var fullInput))
            return 2;

        options = options with { Input = fullInput };
        var inventory = BuildInventory(fullInput);
        if (inventory.Tests.Length == 0)
        {
            Console.Error.WriteLine($"No Selenium-like test methods found under: {fullInput}");
            return 2;
        }

        var clusters = BuildClusters(inventory);
        WaveTuningReport? tuning = null;
        if (string.Equals(options.WaveProfile, "auto", StringComparison.OrdinalIgnoreCase))
        {
            tuning = TuneWavePlan(inventory, clusters, options);
            options = ApplyTuningRecommendation(options, tuning.Recommended);
        }

        var plan = BuildWavePlan(inventory, clusters, options);
        Directory.CreateDirectory(options.Out);
        WriteInventoryArtifacts(options.Out, "json", inventory);
        WriteClusterArtifacts(options.Out, "json", clusters);
        if (tuning != null)
            WriteWaveTuningArtifacts(options.Out, tuning);
        WritePlanArtifacts(options.Out, options.Format, plan);
        WriteMemoryRecallGuide(options.Out, options.Workspace, plan);
        WriteNextCommands(options.Out, options);

        Console.WriteLine("MIGRATION_WAVE_PLAN_READY");
        Console.WriteLine($"Input: {inventory.InputPath}");
        Console.WriteLine($"Tests: {inventory.Tests.Length}");
        Console.WriteLine($"Clusters: {clusters.Clusters.Length}");
        Console.WriteLine($"Waves: {plan.Waves.Length}");
        Console.WriteLine($"Wave profile: {options.WaveProfile}");
        if (tuning != null)
        {
            Console.WriteLine($"Auto-tuned reference: {tuning.TargetWaveCount} wave(s); selected {tuning.Recommended.EstimatedWaveCount}; confidence {tuning.Confidence}");
            Console.WriteLine($"Recommended budgets: tests {options.MaxWaveSize}, files {options.MaxWaveFiles}, actions soft/hard {options.MaxWaveActions}/{options.HardWaveActions}, effective complexity soft/hard {options.MaxWaveComplexity}/{options.HardWaveComplexity}, same-file marginal cost {options.SameFileMarginalCostPercent}%");
        }
        Console.WriteLine($"Artifacts: {Path.GetFullPath(options.Out)}");
        Console.WriteLine("Next: selenium-pw-migrator migration plan show --plan " + QuoteForShell(options.Out));
        return 0;
    }

    static int RunTuneWavePlan(MigrationOptions options)
    {
        var repoRoot = ResolveRepositoryRoot();
        options = NormalizeProjectPaths(options, repoRoot, normalizeInput: false);

        if (!TryResolvePlanInput(options, repoRoot, out var resolvedInput, out var inputError))
        {
            Console.Error.WriteLine(inputError);
            return 2;
        }

        if (!ValidateInput(resolvedInput, out var fullInput))
            return 2;

        options = options with { Input = fullInput };
        var inventory = BuildInventory(fullInput);
        if (inventory.Tests.Length == 0)
        {
            Console.Error.WriteLine($"No Selenium-like test methods found under: {fullInput}");
            return 2;
        }

        var clusters = BuildClusters(inventory);
        var tuning = TuneWavePlan(inventory, clusters, options);
        Directory.CreateDirectory(options.Out);
        WriteInventoryArtifacts(options.Out, "json", inventory);
        WriteClusterArtifacts(options.Out, "json", clusters);
        WriteWaveTuningArtifacts(options.Out, tuning);

        var recommended = ApplyTuningRecommendation(options, tuning.Recommended);
        var preview = BuildWavePlan(inventory, clusters, recommended);
        WritePlanArtifacts(Path.Combine(options.Out, "recommended-preview"), options.Format, preview);

        Console.WriteLine("MIGRATION_WAVE_TUNING_READY");
        Console.WriteLine($"Input: {inventory.InputPath}");
        Console.WriteLine($"Tests: {inventory.Tests.Length}");
        Console.WriteLine($"Reference waves: {tuning.TargetWaveCount}");
        Console.WriteLine($"Recommended waves: {tuning.Recommended.EstimatedWaveCount}");
        Console.WriteLine($"Recommendation confidence: {tuning.Confidence}; score gap {tuning.ScoreGapPercent}%");
        Console.WriteLine($"Recommended budgets: tests {tuning.Recommended.MaxWaveSize}, files {tuning.Recommended.MaxWaveFiles}, actions soft/hard {tuning.Recommended.MaxWaveActions}/{tuning.Recommended.HardWaveActions}, effective complexity soft/hard {tuning.Recommended.MaxWaveComplexity}/{tuning.Recommended.HardWaveComplexity}, same-file marginal cost {tuning.Recommended.SameFileMarginalCostPercent}%");
        Console.WriteLine($"Artifacts: {Path.GetFullPath(options.Out)}");
        Console.WriteLine("No agents or migration execution were started.");
        return 0;
    }

    static int RunPlanShow(MigrationOptions options)
    {
        var repoRoot = ResolveRepositoryRoot();
        var rawPlanRoot = string.IsNullOrWhiteSpace(options.Plan) ? options.Out : options.Plan;
        if (string.IsNullOrWhiteSpace(rawPlanRoot))
            rawPlanRoot = "migration/plan";
        var planRoot = ResolveProjectArtifactPath(rawPlanRoot, repoRoot);

        var planMd = Directory.Exists(planRoot) ? Path.Combine(planRoot, "plan.md") : planRoot;
        if (!File.Exists(planMd))
        {
            Console.Error.WriteLine($"Wave plan not found: {planMd}");
            return 2;
        }

        var text = File.ReadAllText(planMd);
        Console.Write(text);
        if (!text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            Console.WriteLine();

        var outPath = ResolveProjectArtifactPath(options.Out, repoRoot);
        if (!string.IsNullOrWhiteSpace(outPath) && !Path.GetFullPath(outPath).Equals(Path.GetFullPath(planRoot), StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(outPath);
            if (options.Format is "text" or "both")
                File.WriteAllText(Path.Combine(outPath, "wave-plan-show.md"), text);
            if (options.Format is "json" or "both")
            {
                var sourceJson = Directory.Exists(planRoot) ? Path.Combine(planRoot, "waves.json") : Path.ChangeExtension(planRoot, ".json");
                if (File.Exists(sourceJson))
                    File.Copy(sourceJson, Path.Combine(outPath, "waves.json"), overwrite: true);
            }
        }

        return 0;
    }


    static int RunWave(MigrationOptions options)
    {
        var trace = MigrationFastPath.StartTrace("run-wave", options.ExecutionProfile);
        var repoRoot = ResolveRepositoryRoot();
        var outWasDefault = IsDefaultPlanOut(options.Out);
        var rawPlanRoot = string.IsNullOrWhiteSpace(options.Plan) ? "migration/plan" : options.Plan;
        options = NormalizeProjectPaths(options with { Plan = rawPlanRoot }, repoRoot, normalizeInput: false);
        var planRoot = options.Plan;
        if (!EnsureProjectWorkspaceBoundary(options.Workspace, options.Out, outWasDefault, repoRoot, out var boundaryError))
        {
            Console.Error.WriteLine(boundaryError);
            return 2;
        }

        if (!TryReadWavePlan(planRoot, out var plan, out var planError))
        {
            Console.Error.WriteLine(planError);
            return 2;
        }

        if (plan!.Waves.Length == 0)
        {
            Console.Error.WriteLine("Wave plan contains no waves.");
            return 2;
        }

        var requestedWave = string.IsNullOrWhiteSpace(options.Wave) ? plan.Waves[0].Id : options.Wave.Trim();
        var wave = plan.Waves.FirstOrDefault(w => w.Id.Equals(requestedWave, StringComparison.OrdinalIgnoreCase)
            || w.Index.ToString().Equals(requestedWave, StringComparison.OrdinalIgnoreCase)
            || $"wave-{w.Index:000}".Equals(requestedWave, StringComparison.OrdinalIgnoreCase));
        if (wave == null)
        {
            Console.Error.WriteLine($"Wave not found: {requestedWave}");
            Console.Error.WriteLine("Available waves: " + string.Join(", ", plan.Waves.Select(w => w.Id)));
            return 2;
        }

        trace.Next("resolve-plan-and-wave");
        var sourceRoot = ResolveSourceRoot(plan.InputPath);
        if (!ValidateWaveSourceScope(plan, wave, sourceRoot, options.Workspace, repoRoot, out var waveScopeError))
        {
            Console.Error.WriteLine(waveScopeError);
            return 2;
        }

        var outPath = outWasDefault ? Path.Combine(options.Workspace, "runs", wave.Id) : options.Out;
        var existingManifestPath = Path.Combine(outPath, "wave-manifest.json");
        if (File.Exists(existingManifestPath))
        {
            var existingValidation = MigrationFastPath.ValidateWave(outPath, Console.Out, Console.Error);
            if (existingValidation != 0)
                return existingValidation;

            var requestedTests = wave.Tests.Select(test => test.File + "::" + test.TestId).ToArray();
            if (!MigrationFastPath.MatchesRequestedWave(outPath, planRoot, wave.Id, wave.Files, requestedTests, options.ExecutionProfile, out var reuseError))
            {
                Console.Error.WriteLine(reuseError);
                return 2;
            }

            if (options.ExecuteMigrate)
            {
                Console.Error.WriteLine("WAVE_MANIFEST_ALREADY_EXISTS: immutable run workspaces are not rematerialized. Execute the existing run-migrate.sh or run-migrate.ps1 wrapper instead.");
                return 2;
            }

            Console.WriteLine("MIGRATION_WAVE_ALREADY_MATERIALIZED");
            Console.WriteLine($"Run workspace: {Path.GetFullPath(outPath)}");
            return 0;
        }

        Directory.CreateDirectory(outPath);
        var sourceScopeDir = Path.Combine(outPath, "source-scope");
        var generatedDir = Path.Combine(outPath, "generated");
        var evidenceDir = Path.Combine(outPath, "evidence");
        Directory.CreateDirectory(sourceScopeDir);
        Directory.CreateDirectory(generatedDir);
        Directory.CreateDirectory(evidenceDir);

        trace.Next("materialize-source-scope");
        var copiedFiles = new List<string>();
        var missingFiles = new List<string>();
        foreach (var file in wave.Files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var sourceFile = ResolveWaveSourceFile(sourceRoot, plan.InputPath, file);
            var targetFile = Path.Combine(sourceScopeDir, file.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(sourceFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                File.Copy(sourceFile, targetFile, overwrite: true);
                copiedFiles.Add(file);
            }
            else
            {
                missingFiles.Add(file);
            }
        }

        var scope = new MigrationWaveInputScope(
            SchemaVersion: "migration-wave-input-scope/v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            PlanPath: Path.GetFullPath(planRoot),
            WaveId: wave.Id,
            WaveIndex: wave.Index,
            Phase: wave.Phase,
            Cluster: wave.Cluster,
            SourceRoot: sourceRoot,
            SourceScopePath: Path.GetFullPath(sourceScopeDir),
            GeneratedOutputPath: Path.GetFullPath(generatedDir),
            ConfigPath: string.IsNullOrWhiteSpace(options.Config) ? null : Path.GetFullPath(options.Config),
            Tests: wave.Tests,
            Files: wave.Files,
            CopiedFiles: copiedFiles.ToArray(),
            MissingFiles: missingFiles.ToArray());

        File.WriteAllText(Path.Combine(outPath, "input-scope.json"), JsonSerializer.Serialize(scope, JsonOptions));
        var selectedTestsPath = Path.Combine(outPath, "selected-tests.txt");
        var selectedTests = wave.Tests.Select(t => t.File + "::" + t.TestId).ToArray();
        File.WriteAllLines(selectedTestsPath, selectedTests);
        trace.SetMetric("selectedTests", selectedTests.Length);
        trace.SetMetric("sourceFilesMaterialized", copiedFiles.Count);
        trace.SetMetric("sourceFilesMissing", missingFiles.Count);
        trace.SetMetric("processInvocations", options.ExecuteMigrate ? 1 : 0);
        MigrationFastPath.WriteExecutionPolicy(outPath, options.ExecutionProfile, wave.DominantRisk, wave.BudgetStatus);
        if (!MigrationFastPath.WriteImmutableManifest(
                outPath,
                planRoot,
                wave.Id,
                wave.Index,
                wave.Phase,
                wave.Cluster,
                sourceRoot,
                sourceScopeDir,
                generatedDir,
                selectedTestsPath,
                wave.Files,
                selectedTests,
                options.ExecutionProfile,
                out var manifestError))
        {
            Console.Error.WriteLine(manifestError);
            trace.Write(outPath, "manifest-failed");
            return 2;
        }
        if (!MigrationIncrementalPipeline.WriteRunContext(
                outPath,
                options.Workspace,
                options.Config,
                options.Target,
                options.TargetTestFramework,
                options.GenerationPolicy,
                out var runContextError))
        {
            Console.Error.WriteLine(runContextError);
            trace.Write(outPath, "run-context-failed");
            return 2;
        }
        trace.Next("write-run-contract");
        WriteWaveConfigDelta(outPath, options.Workspace, wave, options);
        WriteWaveMemoryDelta(outPath, wave, copiedFiles.Count, missingFiles);
        WriteWaveCommands(outPath, sourceScopeDir, generatedDir, options, selectedTestsPath);
        WriteWavePreflightBudget(outPath, plan, wave);
        trace.Next("execute-migration");

        var automaticExecutionBlocked = options.ExecuteMigrate && string.Equals(wave.BudgetStatus, "BLOCKED", StringComparison.OrdinalIgnoreCase);
        var execution = automaticExecutionBlocked
            ? new WaveMigrateExecution("blocked-by-complexity-budget", "Wave exceeds the hard planning ceiling. Split it or explicitly revise the plan before migration; automatic execution was not started.", 2)
            : options.ExecuteMigrate
                ? TryExecuteMigrate(outPath, sourceScopeDir, generatedDir, options, selectedTestsPath)
                : new WaveMigrateExecution("prepared", "--execute-migrate false; generated/ contains a README placeholder until migrate is run.", 0);
        trace.Next("finalize-run-artifacts");
        WriteWaveSummary(outPath, wave, scope, options, copiedFiles.Count, missingFiles, selectedTestsPath, execution);
        WriteWaveStatus(outPath, wave, execution, missingFiles);
        var validationExitCode = MigrationFastPath.ValidateWave(outPath, TextWriter.Null, Console.Error);
        trace.Write(outPath, missingFiles.Count > 0 ? "missing-source-files" : validationExitCode == 0 ? "ready" : "validation-failed");
        if (validationExitCode != 0 && missingFiles.Count == 0)
            return validationExitCode;

        Console.WriteLine("MIGRATION_WAVE_RUN_READY");
        Console.WriteLine($"Wave: {wave.Id}");
        Console.WriteLine($"Tests: {wave.Tests.Length}");
        Console.WriteLine($"Execution profile: {options.ExecutionProfile}");
        Console.WriteLine($"Files copied: {copiedFiles.Count}");
        if (missingFiles.Count > 0)
            Console.WriteLine($"Missing files: {missingFiles.Count}");
        Console.WriteLine($"Run workspace: {Path.GetFullPath(outPath)}");
        Console.WriteLine($"Generated output: {Path.GetFullPath(generatedDir)}");
        Console.WriteLine("Next: run `migration validate-wave`, then `migration check-progress` after each bounded fix cycle; invoke watchdog/sentinel only when execution-policy.json requires them or before final handoff.");
        if (missingFiles.Count > 0)
            return 1;
        if (options.ExecuteMigrate && (execution.Status.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || execution.Status.Equals("blocked-by-complexity-budget", StringComparison.OrdinalIgnoreCase)))
            return execution.ExitCode == 0 ? 1 : execution.ExitCode;
        return 0;
    }

    static int RunValidateWave(MigrationOptions options)
    {
        var repoRoot = ResolveRepositoryRoot();
        var outPath = ResolveProjectArtifactPath(options.Out, repoRoot);
        return MigrationFastPath.ValidateWave(outPath, Console.Out, Console.Error);
    }

    static int RunCheckProgress(MigrationOptions options)
    {
        var repoRoot = ResolveRepositoryRoot();
        var outPath = ResolveProjectArtifactPath(options.Out, repoRoot);
        return MigrationFastPath.CheckProgress(outPath, options.MaxIdenticalSnapshots, Console.Out, Console.Error);
    }

    static int RunValidationPlan(MigrationOptions options)
    {
        var repoRoot = ResolveRepositoryRoot();
        var outPath = ResolveProjectArtifactPath(options.Out, repoRoot);
        return MigrationIncrementalPipeline.PlanValidation(outPath, options.ForceValidation, Console.Out, Console.Error);
    }

    static int RunValidationHost(MigrationOptions options)
    {
        var repoRoot = ResolveRepositoryRoot();
        var outPath = ResolveProjectArtifactPath(options.Out, repoRoot);
        var validationProject = string.IsNullOrWhiteSpace(options.ValidationProject)
            ? string.Empty
            : ResolveProjectArtifactPath(options.ValidationProject, repoRoot);
        return MigrationValidationHost.Run(
            outPath,
            options.ValidationProfile,
            validationProject,
            options.ValidationCommand,
            options.ValidationTimeoutSeconds,
            options.ValidationDryRun,
            options.CheckpointOnPass,
            options.ForceValidation,
            Console.Out,
            Console.Error);
    }

    static int RunRecordValidation(MigrationOptions options)
    {
        var repoRoot = ResolveRepositoryRoot();
        var outPath = ResolveProjectArtifactPath(options.Out, repoRoot);
        return MigrationIncrementalPipeline.RecordValidation(
            outPath,
            options.ValidationId,
            options.ValidationExitCode,
            options.ValidationCommand,
            options.ValidationScope,
            Console.Out,
            Console.Error);
    }

    static int RunCheckpointWave(MigrationOptions options)
    {
        var repoRoot = ResolveRepositoryRoot();
        var outPath = ResolveProjectArtifactPath(options.Out, repoRoot);
        return MigrationIncrementalPipeline.CreateCheckpoint(outPath, options.CheckpointLabel, options.CheckpointStage, Console.Out, Console.Error);
    }

    static int RunResumeWave(MigrationOptions options)
    {
        var repoRoot = ResolveRepositoryRoot();
        var outPath = ResolveProjectArtifactPath(options.Out, repoRoot);
        return MigrationIncrementalPipeline.ResumeWave(outPath, Console.Out, Console.Error);
    }

    static int RunBuildReviewBundle(MigrationOptions options)
    {
        var repoRoot = ResolveRepositoryRoot();
        var outPath = ResolveProjectArtifactPath(options.Out, repoRoot);
        return MigrationIncrementalPipeline.BuildReviewBundle(outPath, Console.Out, Console.Error);
    }

    static int RunPerformanceReport(MigrationOptions options)
    {
        var repoRoot = ResolveRepositoryRoot();
        var outPath = ResolveProjectArtifactPath(options.Out, repoRoot);
        return MigrationFastPath.PrintPerformanceReport(outPath, Console.Out, Console.Error);
    }

    static int RunRefreshWaveStatus(MigrationOptions options)
    {
        var repoRoot = ResolveRepositoryRoot();
        var outPath = ResolveProjectArtifactPath(options.Out, repoRoot);
        var scopePath = Path.Combine(outPath, "input-scope.json");
        if (!File.Exists(scopePath))
        {
            Console.Error.WriteLine($"Wave input scope not found: {scopePath}");
            return 2;
        }

        MigrationWaveInputScope? scope;
        try
        {
            scope = JsonSerializer.Deserialize<MigrationWaveInputScope>(File.ReadAllText(scopePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Invalid wave input scope: {ex.Message}");
            return 2;
        }
        if (scope == null)
        {
            Console.Error.WriteLine("Wave input scope deserialized to null.");
            return 2;
        }

        var generatedDir = Path.Combine(outPath, "generated");
        var generatedFiles = Directory.Exists(generatedDir)
            ? Directory.EnumerateFiles(generatedDir, "*", SearchOption.AllDirectories)
                .Select(path => NormalizeSlashes(Path.GetRelativePath(generatedDir, path)))
                .Where(path => !path.Equals("README.md", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
        var generatedSourceFiles = generatedFiles.Count(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase));
        var placeholderOnly = generatedFiles.Length == 0;
        var exitCode = options.MigrateExitCode;
        var status = exitCode > 0 ? "failed" : placeholderOnly ? "prepared" : "migrated";
        var message = exitCode > 0
            ? $"migrate exited with code {exitCode}"
            : placeholderOnly
                ? "generated/ still contains only the placeholder; run the wave-local migrate script."
                : $"generated output detected ({generatedFiles.Length} file(s), {generatedSourceFiles} source file(s)).";

        var statusPath = Path.Combine(outPath, "wave-status.json");
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        if (File.Exists(statusPath))
        {
            try
            {
                using var existing = JsonDocument.Parse(File.ReadAllText(statusPath));
                if (existing.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in existing.RootElement.EnumerateObject())
                        payload[property.Name] = JsonSerializer.Deserialize<object?>(property.Value.GetRawText());
                }
            }
            catch
            {
                // Replace malformed status with a valid status payload below.
            }
        }
        payload["schemaVersion"] = "migration-wave-status/v2";
        payload["waveId"] = scope.WaveId;
        payload["status"] = status;
        payload["message"] = message;
        payload["migrateExitCode"] = exitCode < 0 ? null : exitCode;
        payload["placeholderOnly"] = placeholderOnly;
        payload["generatedFiles"] = generatedFiles;
        payload["generatedFileCount"] = generatedFiles.Length;
        payload["generatedSourceFileCount"] = generatedSourceFiles;
        payload["updatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
        if (!payload.ContainsKey("generatedAtUtc"))
            payload["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
        WriteJsonAtomic(statusPath, payload);

        Console.WriteLine("MIGRATION_WAVE_STATUS_REFRESHED");
        Console.WriteLine($"Wave: {scope.WaveId}");
        Console.WriteLine($"Status: {status}");
        Console.WriteLine($"Generated files: {generatedFiles.Length}");
        return exitCode > 0 ? exitCode : 0;
    }

    static bool IsDefaultPlanOut(string outPath) => string.IsNullOrWhiteSpace(outPath) || NormalizeSlashes(outPath).Equals("migration/plan", StringComparison.OrdinalIgnoreCase);

    static MigrationOptions NormalizeProjectPaths(MigrationOptions options, string repoRoot, bool normalizeInput)
    {
        return options with
        {
            Input = normalizeInput ? ResolveInputPath(options.Input, repoRoot) : options.Input,
            Workspace = ResolveProjectArtifactPath(options.Workspace, repoRoot),
            Out = ResolveProjectArtifactPath(options.Out, repoRoot),
            Plan = string.IsNullOrWhiteSpace(options.Plan) ? options.Plan : ResolveProjectArtifactPath(options.Plan, repoRoot),
            Config = string.IsNullOrWhiteSpace(options.Config) ? options.Config : ResolveProjectArtifactPath(options.Config, repoRoot)
        };
    }

    static bool TryResolvePlanInput(MigrationOptions options, string repoRoot, out string resolvedInput, out string error)
    {
        resolvedInput = string.Empty;
        error = string.Empty;
        var hasExplicitInput = !string.IsNullOrWhiteSpace(options.Input);
        var hasConfiguredSource = TryReadConfiguredSourceRoot(options.Workspace, repoRoot, out var configuredSourceRoot, out var configuredSourceHint);

        if (!hasExplicitInput)
        {
            if (hasConfiguredSource)
            {
                resolvedInput = configuredSourceRoot;
                return true;
            }

            error = $"migration command needs --input <selenium-tests>. No configured bootstrap source was found in {SourceScopeSchema} state (migration/state/source-scope.json or migration/.migration-kit/version.json).";
            return false;
        }

        resolvedInput = ResolveInputPath(options.Input, repoRoot);
        if (hasConfiguredSource && !IsSameOrDescendantPath(ResolveSourceRoot(resolvedInput), configuredSourceRoot))
        {
            error = $"WAVE_SOURCE_SCOPE_VIOLATION: --input '{options.Input}' resolves outside the configured bootstrap source '{configuredSourceHint}'. Re-run kit bootstrap-opencode with the intended --source, or plan within the configured source root.";
            return false;
        }

        return true;
    }

    static bool TryReadConfiguredSourceRoot(string workspacePath, string repoRoot, out string sourceRoot, out string sourceHint)
    {
        sourceRoot = string.Empty;
        sourceHint = string.Empty;
        var workspaceFull = Path.GetFullPath(workspacePath);
        var candidates = new[]
        {
            Path.Combine(workspaceFull, "state", "source-scope.json"),
            Path.Combine(workspaceFull, ".migration-kit", "version.json"),
            Path.Combine(workspaceFull, "state", "memory", "project-profile.json")
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
                continue;

            if (TryReadSourceCandidate(path, repoRoot, out sourceRoot, out sourceHint))
                return true;
        }

        return false;
    }

    static bool TryReadSourceCandidate(string jsonPath, string repoRoot, out string sourceRoot, out string sourceHint)
    {
        sourceRoot = string.Empty;
        sourceHint = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var propertyName in new[] { "source", "sourceRoot", "configuredSource", "sourcePath", "sourceFullPath", "configuredSourceFullPath" })
            {
                if (!doc.RootElement.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
                    continue;

                var value = property.GetString();
                if (IsPlaceholderSource(value))
                    continue;

                var full = ResolveConfiguredSourcePath(value!, repoRoot);
                sourceRoot = ResolveSourceRoot(full);
                sourceHint = value!;
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }

        return false;
    }

    static string ResolveConfiguredSourcePath(string source, string repoRoot)
        => Path.IsPathRooted(source) ? Path.GetFullPath(source) : Path.GetFullPath(Path.Combine(repoRoot, source));

    static bool IsPlaceholderSource(string? source)
        => string.IsNullOrWhiteSpace(source)
            || source.Contains("<SOURCE", StringComparison.OrdinalIgnoreCase)
            || source.Contains("SOURCE_SELENIUM_PROJECT_PATH", StringComparison.OrdinalIgnoreCase);

    static bool ContainsParentTraversal(string relativePath)
        => NormalizeSlashes(relativePath).Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == "..");

    static bool ValidateWaveSourceScope(MigrationWavePlan plan, MigrationWave wave, string sourceRoot, string workspacePath, string repoRoot, out string error)
    {
        if (TryReadConfiguredSourceRoot(workspacePath, repoRoot, out var configuredSourceRoot, out var configuredSourceHint)
            && !IsSameOrDescendantPath(sourceRoot, configuredSourceRoot))
        {
            error = $"WAVE_SOURCE_SCOPE_VIOLATION: wave plan input '{plan.InputPath}' is outside the configured bootstrap source '{configuredSourceHint}'. Rebuild the wave plan with --input set to the configured source root.";
            return false;
        }

        foreach (var relativeFile in wave.Files.Concat(wave.Tests.Select(t => t.File)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(relativeFile))
                continue;

            if (Path.IsPathRooted(relativeFile))
            {
                error = $"WAVE_SOURCE_SCOPE_VIOLATION: wave {wave.Id} contains an absolute file path: {relativeFile}";
                return false;
            }

            if (ContainsParentTraversal(relativeFile))
            {
                error = $"WAVE_SOURCE_SCOPE_VIOLATION: wave {wave.Id} contains a parent-traversal file path: {relativeFile}";
                return false;
            }

            var sourceFile = ResolveWaveSourceFile(sourceRoot, plan.InputPath, relativeFile);
            if (!IsSameOrDescendantPath(sourceFile, sourceRoot))
            {
                error = $"WAVE_SOURCE_SCOPE_VIOLATION: wave {wave.Id} contains an out-of-scope file path: {relativeFile}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    static string ResolveRepositoryRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    static string ResolveInputPath(string path, string repoRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            return path;

        var currentRelative = Path.GetFullPath(path);
        if (File.Exists(currentRelative) || Directory.Exists(currentRelative))
            return currentRelative;

        var repoRelative = Path.GetFullPath(Path.Combine(repoRoot, path));
        if (File.Exists(repoRelative) || Directory.Exists(repoRelative))
            return repoRelative;

        return currentRelative;
    }

    static string ResolveProjectArtifactPath(string path, string repoRoot)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            return string.IsNullOrWhiteSpace(path) ? path : Path.GetFullPath(path);

        // Migration workspaces and wave artifacts are project-level state.
        // Even when the agent accidentally runs from Web/<project> or another source subdirectory,
        // `migration/**` must resolve to <repo-root>/migration/**, never to <cwd>/migration/**.
        if (IsMigrationRelativePath(path))
            return Path.GetFullPath(Path.Combine(repoRoot, path));

        return Path.GetFullPath(path);
    }

    static bool EnsureProjectWorkspaceBoundary(string workspacePath, string outPath, bool outWasDefault, string repoRoot, out string error)
    {
        var workspaceFull = Path.GetFullPath(workspacePath);
        var expectedWorkspace = Path.GetFullPath(Path.Combine(repoRoot, "migration"));
        if (Path.GetFileName(workspaceFull).Equals("migration", StringComparison.OrdinalIgnoreCase)
            && !workspaceFull.Equals(expectedWorkspace, StringComparison.OrdinalIgnoreCase))
        {
            error = "NESTED_MIGRATION_WORKSPACE_BLOCKED: --workspace migration resolved outside the repository-root migration directory. Run migration commands from the repository root or pass an absolute repo-root workspace.";
            return false;
        }

        var outFull = Path.GetFullPath(outPath);
        if (outWasDefault && !IsPathWithin(outFull, expectedWorkspace))
        {
            error = "NESTED_MIGRATION_WORKSPACE_BLOCKED: default wave output must be under the repository-root migration/runs directory.";
            return false;
        }

        if (!IsPathWithin(outFull, expectedWorkspace) && ContainsMigrationSegment(outFull))
        {
            error = "NESTED_MIGRATION_WORKSPACE_BLOCKED: explicit wave output points at a migration directory outside the repository-root migration workspace.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    static bool IsMigrationRelativePath(string path)
    {
        var normalized = NormalizeSlashes(path).TrimStart('.', '/');
        return normalized.Equals("migration", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("migration/", StringComparison.OrdinalIgnoreCase);
    }

    static bool ContainsMigrationSegment(string path)
        => NormalizeSlashes(path).Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment.Equals("migration", StringComparison.OrdinalIgnoreCase));

    static bool IsPathWithin(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsSameOrDescendantPath(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static string NormalizeSlashes(string path) => path.Replace('\\', '/');

    static string ResolveSourceRoot(string inputPath)
    {
        if (Directory.Exists(inputPath))
            return Path.GetFullPath(inputPath);
        if (File.Exists(inputPath))
            return Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(inputPath);
    }

    static string ResolveWaveSourceFile(string sourceRoot, string inputPath, string relativeFile)
    {
        if (File.Exists(inputPath) && Path.GetFileName(inputPath).Equals(relativeFile, StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(inputPath);
        return Path.GetFullPath(Path.Combine(sourceRoot, relativeFile.Replace('/', Path.DirectorySeparatorChar)));
    }

    static void WriteWaveConfigDelta(string outPath, string workspace, MigrationWave wave, MigrationOptions options)
    {
        var delta = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "migration-config-delta/v1",
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["updatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["waveId"] = wave.Id,
            ["phase"] = wave.Phase,
            ["cluster"] = wave.Cluster,
            ["status"] = "observed",
            ["baseConfig"] = string.IsNullOrWhiteSpace(options.Config) ? null : options.Config,
            ["trust"] = "observed",
            ["safety"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["assertionSuppressionAllowed"] = false,
                ["overSuppressionAllowed"] = false,
                ["autoPromotionAllowed"] = false,
                ["requiresReviewerBeforeMerge"] = true
            },
            ["changes"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["methodSemantics"] = Array.Empty<object>(),
                ["waitPolicies"] = Array.Empty<object>(),
                ["suppressedMethodPatterns"] = Array.Empty<object>(),
                ["uiTargets"] = Array.Empty<object>(),
                ["sourceOnlyIdentifiers"] = Array.Empty<object>()
            },
            ["evidence"] = new[]
            {
                "input-scope.json",
                "run-summary.md"
            },
            ["notes"] = new[]
            {
                "This config delta is a reviewable placeholder. Add concrete config changes only after inspecting generated output and reviewer/watchdog findings.",
                "Do not merge this delta directly into adapter-config.json without validate-merge/reviewer approval."
            }
        };

        var deltaJson = JsonSerializer.Serialize(delta, JsonOptions);
        File.WriteAllText(Path.Combine(outPath, "config-delta.json"), deltaJson);

        var memoryDeltaDir = Path.Combine(workspace, "state", "memory", "config-deltas");
        try
        {
            Directory.CreateDirectory(memoryDeltaDir);
            File.WriteAllText(Path.Combine(memoryDeltaDir, wave.Id + ".config-delta.json"), deltaJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            File.AppendAllText(Path.Combine(outPath, "warnings.log"), $"Could not mirror config delta into project memory: {ex.Message}{Environment.NewLine}");
        }
    }

    static void WriteWaveMemoryDelta(string outPath, MigrationWave wave, int copiedFiles, IReadOnlyList<string> missingFiles)
    {
        var records = new List<SortedDictionary<string, object?>>()
        {
            new(StringComparer.Ordinal)
            {
                ["id"] = $"wave-{wave.Index:000}-prepared",
                ["kind"] = "final-gate-lesson",
                ["scope"] = wave.Cluster,
                ["status"] = "observed",
                ["source"] = "migration run-wave",
                ["text"] = $"{wave.Id} prepared a bounded source scope with {copiedFiles} copied file(s). Treat results as wave-local until reviewer/watchdog/final-gate evidence exists.",
                ["evidence"] = new[] { "input-scope.json", "run-summary.md" },
                ["createdAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
            },
            new(StringComparer.Ordinal)
            {
                ["id"] = $"wave-{wave.Index:000}-safety-boundary",
                ["kind"] = "warning",
                ["scope"] = wave.Cluster,
                ["status"] = "active",
                ["source"] = "migration run-wave",
                ["text"] = "Wave memory is guidance, not authority. Do not suppress assertions, over-suppress user interactions, or promote config changes without reviewer evidence.",
                ["evidence"] = new[] { "config-delta.json" },
                ["createdAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
            }
        };

        if (missingFiles.Count > 0)
        {
            records.Add(new(StringComparer.Ordinal)
            {
                ["id"] = $"wave-{wave.Index:000}-missing-files",
                ["kind"] = "warning",
                ["scope"] = wave.Cluster,
                ["status"] = "active",
                ["source"] = "migration run-wave",
                ["text"] = "Some wave files were missing while materializing source-scope. Rebuild the plan from the current source tree before executing this wave.",
                ["files"] = missingFiles.ToArray(),
                ["createdAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
            });
        }

        var lines = records.Select(r => JsonSerializer.Serialize(r, JsonLineOptions()));
        File.WriteAllLines(Path.Combine(outPath, "memory-delta.jsonl"), lines);
    }

    static void WriteWaveCommands(string outPath, string sourceScopeDir, string generatedDir, MigrationOptions options, string selectedTestsPath)
    {
        var migrateArgs = BuildMigrateCommand(sourceScopeDir, generatedDir, options, selectedTestsPath);
        var refreshArgs = $"selenium-pw-migrator migration refresh-wave-status --out {QuoteForShell(Path.GetFullPath(outPath))} --migrate-exit-code";
        var validationPlanArgs = $"selenium-pw-migrator migration validation-plan --out {QuoteForShell(Path.GetFullPath(outPath))}";
        var preflightPath = Path.GetFullPath(Path.Combine(outPath, "preflight-budget.json"));
        var sh = new StringBuilder();
        sh.AppendLine("#!/usr/bin/env bash");
        sh.AppendLine("set -uo pipefail");
        sh.AppendLine($"preflight={QuoteForShell(preflightPath)}");
        sh.AppendLine("if grep -Eq '\"status\"[[:space:]]*:[[:space:]]*\"BLOCKED\"' \"$preflight\"; then");
        sh.AppendLine("  echo 'WAVE_PREFLIGHT_BUDGET_BLOCKED: split or replan this wave before migration.' >&2");
        sh.AppendLine("  exit 2");
        sh.AppendLine("fi");
        sh.AppendLine("migrate_exit_code=0");
        sh.AppendLine(migrateArgs + " || migrate_exit_code=$?");
        sh.AppendLine(refreshArgs + " \"$migrate_exit_code\"");
        sh.AppendLine(validationPlanArgs);
        sh.AppendLine("exit \"$migrate_exit_code\"");
        File.WriteAllText(Path.Combine(outPath, "run-migrate.sh"), sh.ToString());

        var ps = new StringBuilder();
        ps.AppendLine("$ErrorActionPreference = 'Continue'");
        ps.AppendLine($"$preflight = Get-Content -Raw -Path '{preflightPath.Replace("'", "''")}' | ConvertFrom-Json");
        ps.AppendLine("if ([string]$preflight.status -eq 'BLOCKED') { Write-Error 'WAVE_PREFLIGHT_BUDGET_BLOCKED: split or replan this wave before migration.'; exit 2 }");
        ps.AppendLine("$migrateExitCode = 0");
        ps.AppendLine(migrateArgs);
        ps.AppendLine("if ($null -ne $LASTEXITCODE) { $migrateExitCode = $LASTEXITCODE }");
        ps.AppendLine(refreshArgs + " $migrateExitCode");
        ps.AppendLine(validationPlanArgs);
        ps.AppendLine("exit $migrateExitCode");
        File.WriteAllText(Path.Combine(outPath, "run-migrate.ps1"), ps.ToString());
        File.WriteAllText(Path.Combine(generatedDir, "README.md"), "# Generated output placeholder\n\nRun `../run-migrate.sh` or `../run-migrate.ps1` to generate this already-materialized wave. Do not rerun `migration run-wave` for the same output directory; the wrapper refreshes `wave-status.json` and creates an incremental `validation-plan.json` after migration.\n");
    }

    static string BuildMigrateCommand(string sourceScopeDir, string generatedDir, MigrationOptions options, string? selectedTestsPath = null)
    {
        var parts = new List<string>
        {
            "selenium-pw-migrator",
            "--mode", "migrate",
            "--input", QuoteForShell(Path.GetFullPath(sourceScopeDir)),
            "--out", QuoteForShell(Path.GetFullPath(generatedDir)),
            "--format", "both",
            "--target", options.Target
        };
        if (!string.IsNullOrWhiteSpace(options.Config))
        {
            parts.Add("--config");
            parts.Add(QuoteForShell(Path.GetFullPath(options.Config)));
        }
        if (!string.IsNullOrWhiteSpace(options.TargetTestFramework))
        {
            parts.Add("--target-test-framework");
            parts.Add(options.TargetTestFramework);
        }
        if (!string.IsNullOrWhiteSpace(options.GenerationPolicy))
        {
            parts.Add("--generation-policy");
            parts.Add(options.GenerationPolicy);
        }
        if (!string.IsNullOrWhiteSpace(selectedTestsPath))
        {
            parts.Add("--selected-tests");
            parts.Add(QuoteForShell(Path.GetFullPath(selectedTestsPath)));
        }
        return string.Join(" ", parts);
    }

    static void WriteWavePreflightBudget(string outPath, MigrationWavePlan plan, MigrationWave wave)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "migration-wave-preflight-budget/v1",
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["waveId"] = wave.Id,
            ["status"] = wave.BudgetStatus,
            ["metrics"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["testCount"] = wave.Tests.Length,
                ["sourceFileCount"] = wave.SourceFileCount,
                ["estimatedActions"] = wave.EstimatedActions,
                ["rawComplexity"] = wave.RawComplexity,
                ["estimatedComplexity"] = wave.EstimatedComplexity,
                ["effectiveComplexity"] = wave.EstimatedComplexity
            },
            ["budgets"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["maxWaveSize"] = plan.MaxWaveSize,
                ["maxWaveFiles"] = plan.MaxWaveFiles,
                ["maxWaveActions"] = plan.MaxWaveActions,
                ["maxWaveComplexity"] = plan.MaxWaveComplexity,
                ["softWaveActions"] = plan.MaxWaveActions,
                ["hardWaveActions"] = plan.HardWaveActions,
                ["softWaveComplexity"] = plan.MaxWaveComplexity,
                ["hardWaveComplexity"] = plan.HardWaveComplexity,
                ["sameFileMarginalCostPercent"] = plan.SameFileMarginalCostPercent
            },
            ["violations"] = wave.BudgetViolations ?? Array.Empty<string>(),
            ["automaticExecutionAllowed"] = !string.Equals(wave.BudgetStatus, "BLOCKED", StringComparison.OrdinalIgnoreCase)
        };
        WriteJsonAtomic(Path.Combine(outPath, "preflight-budget.json"), payload);
    }

    static void WriteWaveSummary(string outPath, MigrationWave wave, MigrationWaveInputScope scope, MigrationOptions options, int copiedFiles, IReadOnlyList<string> missingFiles, string selectedTestsPath, WaveMigrateExecution execution)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration wave run");
        sb.AppendLine();
        sb.AppendLine($"Wave: `{wave.Id}`");
        sb.AppendLine($"Phase: `{wave.Phase}`");
        sb.AppendLine($"Cluster: `{wave.Cluster}`");
        sb.AppendLine($"Risk: **{wave.DominantRisk}**");
        sb.AppendLine($"Execution profile: **{options.ExecutionProfile}**");
        sb.AppendLine($"Tests: {wave.Tests.Length}");
        sb.AppendLine($"Preflight budget: **{wave.BudgetStatus}** (files {wave.SourceFileCount}, actions {wave.EstimatedActions}, effective/raw complexity {wave.EstimatedComplexity}/{wave.RawComplexity})");
        sb.AppendLine($"Files copied: {copiedFiles}");
        sb.AppendLine($"Missing files: {missingFiles.Count}");
        sb.AppendLine();
        sb.AppendLine("## Safety boundary");
        sb.AppendLine();
        sb.AppendLine("- `wave-manifest.json` is immutable for this run directory and freezes the selected files/tests.");
        sb.AppendLine("- This wave is bounded to `source-scope/` and `generated/`.");
        sb.AppendLine("- `run-migrate` passes `--selected-tests selected-tests.txt`, so files copied into `source-scope/` cannot silently expand the wave to every test in those files.");
        sb.AppendLine("- `config-delta.json` is observed/reviewable only; do not merge it directly.");
        sb.AppendLine("- `memory-delta.jsonl` is guidance, not authority.");
        sb.AppendLine("- Assertions must not be suppressed.");
        sb.AppendLine("- POM uncertainty must remain reviewable until target mapping exists.");
        sb.AppendLine();
        sb.AppendLine("## Execute migration for this wave");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine(BuildMigrateCommand(scope.SourceScopePath, scope.GeneratedOutputPath, options, selectedTestsPath));
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Or rerun the wave with execution enabled:");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine($"selenium-pw-migrator migration run-wave --plan {QuoteForShell(scope.PlanPath)} --wave {wave.Id} --workspace {QuoteForShell(options.Workspace)} --out {QuoteForShell(outPath)} --execute-migrate true");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Tests in scope");
        sb.AppendLine();
        foreach (var test in wave.Tests)
            sb.AppendLine($"- `{test.File}::{test.TestId}` ({test.Risk}; {string.Join(", ", test.Tags)})");
        if (missingFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Missing files");
            foreach (var file in missingFiles)
                sb.AppendLine($"- `{file}`");
        }
        sb.AppendLine();
        sb.AppendLine("## Review checklist");
        sb.AppendLine();
        sb.AppendLine("- Compare generated tests with source tests in `source-scope/`.");
        sb.AppendLine("- Keep any new config as a delta with evidence.");
        sb.AppendLine("- Run `memory summarize --run <this-run>` only after reviewer/watchdog findings exist.");
        sb.AppendLine("- Run `selenium-pw-migrator migration validate-wave --out <run-dir>` before review.");
        sb.AppendLine("- Run `selenium-pw-migrator migration check-progress --out <run-dir>` after each bounded fix cycle; `NO_PROGRESS_DETECTED` requires watchdog/strategy change.");
        sb.AppendLine("- Follow `execution-policy.json`: fast/standard profiles use event-driven watchdog/sentinel; final gate remains mandatory.");
        sb.AppendLine("- Run final gate before promoting any wave-local learning.");
        File.WriteAllText(Path.Combine(outPath, "run-summary.md"), sb.ToString());

        var json = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "migration-wave-run/v1",
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["updatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["waveId"] = wave.Id,
            ["phase"] = wave.Phase,
            ["cluster"] = wave.Cluster,
            ["risk"] = wave.DominantRisk,
            ["tests"] = wave.Tests.Length,
            ["estimatedActions"] = wave.EstimatedActions,
            ["estimatedComplexity"] = wave.EstimatedComplexity,
            ["preflightBudgetStatus"] = wave.BudgetStatus,
            ["executionProfile"] = options.ExecutionProfile,
            ["filesCopied"] = copiedFiles,
            ["missingFiles"] = missingFiles.ToArray(),
            ["placeholderOnly"] = execution.Status.Equals("prepared", StringComparison.OrdinalIgnoreCase),
            ["artifacts"] = new[] { "wave-manifest.json", "execution-policy.json", "input-scope.json", "preflight-budget.json", "config-delta.json", "memory-delta.jsonl", "wave-validation.json", "performance-trace.json", "run-summary.md", "run-migrate.sh", "run-migrate.ps1" }
        };
        File.WriteAllText(Path.Combine(outPath, "run-summary.json"), JsonSerializer.Serialize(json, JsonOptions));
    }

    static WaveMigrateExecution TryExecuteMigrate(string outPath, string sourceScopeDir, string generatedDir, MigrationOptions options, string selectedTestsPath)
    {
        try
        {
            var entryAssembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
                return new("not-run", "Environment.ProcessPath is empty; run run-migrate.sh/ps1 manually.", 0);

            var args = new List<string>();
            if (Path.GetFileName(processPath).StartsWith("dotnet", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entryAssembly))
                args.Add(entryAssembly!);
            args.AddRange(new[] { "--mode", "migrate", "--input", Path.GetFullPath(sourceScopeDir), "--out", Path.GetFullPath(generatedDir), "--format", "both", "--target", options.Target });
            if (!string.IsNullOrWhiteSpace(options.Config))
                args.AddRange(new[] { "--config", Path.GetFullPath(options.Config) });
            if (!string.IsNullOrWhiteSpace(options.TargetTestFramework))
                args.AddRange(new[] { "--target-test-framework", options.TargetTestFramework });
            if (!string.IsNullOrWhiteSpace(options.GenerationPolicy))
                args.AddRange(new[] { "--generation-policy", options.GenerationPolicy });
            if (!string.IsNullOrWhiteSpace(selectedTestsPath))
                args.AddRange(new[] { "--selected-tests", Path.GetFullPath(selectedTestsPath) });

            var psi = new ProcessStartInfo(processPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process == null)
                return new("not-run", "Could not start migrate child process.", 0);

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            File.WriteAllText(Path.Combine(outPath, "migrate.stdout.log"), stdout);
            File.WriteAllText(Path.Combine(outPath, "migrate.stderr.log"), stderr);
            return new(process.ExitCode == 0 ? "completed" : "failed", $"migrate exited with code {process.ExitCode}", process.ExitCode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new("not-run", ex.Message, 0);
        }
    }

    static void WriteWaveStatus(string outPath, MigrationWave wave, WaveMigrateExecution execution, IReadOnlyList<string> missingFiles)
    {
        var status = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "migration-wave-status/v2",
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["updatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["waveId"] = wave.Id,
            ["preflightBudgetStatus"] = wave.BudgetStatus,
            ["estimatedActions"] = wave.EstimatedActions,
            ["estimatedComplexity"] = wave.EstimatedComplexity,
            ["sourceFileCount"] = wave.SourceFileCount,
            ["status"] = missingFiles.Count > 0 ? "incomplete" : execution.Status,
            ["message"] = execution.Message,
            ["migrateExitCode"] = execution.ExitCode,
            ["missingFiles"] = missingFiles.ToArray(),
            ["placeholderOnly"] = execution.Status.Equals("prepared", StringComparison.OrdinalIgnoreCase),
            ["next"] = new[]
            {
                "Inspect wave-manifest.json, execution-policy.json, run-summary.md, and input-scope.json.",
                "Run migration validate-wave before review and migration check-progress after each fix cycle.",
                "Run run-migrate.sh or run-migrate.ps1 if generated output has not been produced.",
                "Add concrete config-delta entries only with evidence.",
                "Run reviewer/watchdog/final-gate before promoting wave-local learning."
            }
        };
        WriteJsonAtomic(Path.Combine(outPath, "wave-status.json"), status);
    }

    static bool ValidateInput(string input, out string fullInput)
    {
        fullInput = string.IsNullOrWhiteSpace(input) ? string.Empty : Path.GetFullPath(input);
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.Error.WriteLine("migration command needs --input <selenium-tests>.");
            return false;
        }

        if (!File.Exists(fullInput) && !Directory.Exists(fullInput))
        {
            Console.Error.WriteLine($"Input not found: {input}");
            return false;
        }

        return true;
    }

    static MigrationInventoryReport BuildInventory(string fullInput)
    {
        var baseDir = Directory.Exists(fullInput)
            ? fullInput
            : Path.GetDirectoryName(fullInput) ?? Directory.GetCurrentDirectory();
        var files = File.Exists(fullInput)
            ? new[] { fullInput }
            : Directory.EnumerateFiles(fullInput, "*.cs", SearchOption.AllDirectories)
                .Where(IsRelevantPath)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var scanned = 0;
        var testFiles = 0;
        var parseWarnings = new List<string>();
        var tests = new List<MigrationTestInventoryItem>();

        foreach (var file in files)
        {
            scanned++;
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                parseWarnings.Add($"{Path.GetRelativePath(baseDir, file)}: could not read file: {ex.Message}");
                continue;
            }

            if (!LooksLikeSeleniumOrTestFile(text))
                continue;

            var extracted = ExtractTests(file, baseDir, text).ToArray();
            if (extracted.Length == 0)
                continue;

            testFiles++;
            tests.AddRange(extracted);
        }

        var distinctTags = tests.SelectMany(t => t.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        return new MigrationInventoryReport(
            SchemaVersion: InventorySchema,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            InputPath: fullInput,
            FilesScanned: scanned,
            TestFiles: testFiles,
            TestsFound: tests.Count,
            DistinctTags: distinctTags,
            Tests: tests.OrderBy(t => t.File, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Line).ToArray(),
            Warnings: parseWarnings.ToArray());
    }

    static IEnumerable<MigrationTestInventoryItem> ExtractTests(string file, string baseDir, string text)
    {
        var relative = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
        var matches = FindAttributedTests(text).ToArray();
        if (matches.Length == 0 && LooksLikeSeleniumOrTestFile(text))
            matches = FindFallbackPublicTestMethods(text).ToArray();

        foreach (var match in matches)
        {
            var methodName = match.MethodName;
            if (IsLifecycleMethod(methodName))
                continue;

            var className = FindNearestClassName(text, match.Index) ?? Path.GetFileNameWithoutExtension(file);
            var line = CountLine(text, match.Index);
            var snippet = SliceAround(text, match.Index, 3500);
            var tags = DetectTags(relative, className, methodName, text, snippet).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            var cluster = ChooseCluster(relative, className, methodName, tags);
            var metrics = CountMetrics(snippet.Length > 0 ? snippet : text);
            var risk = DetermineRisk(tags, metrics);
            var score = RepresentativeScore(tags, metrics, risk);
            var reasons = BuildReasons(tags, metrics, risk).ToArray();

            yield return new MigrationTestInventoryItem(
                TestId: $"{className}.{methodName}",
                File: relative,
                ClassName: className,
                MethodName: methodName,
                Line: line,
                Cluster: cluster,
                Tags: tags,
                Risk: risk,
                RepresentativeScore: score,
                SeleniumActions: metrics.SeleniumActions,
                Assertions: metrics.Assertions,
                Waits: metrics.Waits,
                Helpers: metrics.Helpers,
                Reasons: reasons);
        }
    }

    static IEnumerable<TestMethodMatch> FindAttributedTests(string text)
    {
        var regex = new Regex(@"(?ms)(?:^\s*\[[^\]\r\n]*(?:Test|Fact|Theory|TestCase|TestCaseSource|InlineData)[^\]\r\n]*\]\s*)+\s*(?:public|internal|protected|private)?\s*(?:async\s+)?(?:Task|ValueTask|void)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Multiline);
        foreach (Match match in regex.Matches(text))
            yield return new TestMethodMatch(match.Groups["name"].Value, match.Index);
    }

    static IEnumerable<TestMethodMatch> FindFallbackPublicTestMethods(string text)
    {
        var regex = new Regex(@"(?m)^\s*public\s+(?:async\s+)?(?:Task|ValueTask|void)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(");
        foreach (Match match in regex.Matches(text))
        {
            var name = match.Groups["name"].Value;
            if (name.Contains("Test", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Should", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Can", StringComparison.OrdinalIgnoreCase))
            {
                yield return new TestMethodMatch(name, match.Index);
            }
        }
    }

    static string? FindNearestClassName(string text, int beforeIndex)
    {
        var prefix = beforeIndex > 0 && beforeIndex < text.Length ? text[..beforeIndex] : text;
        var matches = Regex.Matches(prefix, @"\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Multiline);
        return matches.Count == 0 ? null : matches[matches.Count - 1].Groups["name"].Value;
    }

    static int CountLine(string text, int index)
    {
        var capped = Math.Min(Math.Max(index, 0), text.Length);
        var line = 1;
        for (var i = 0; i < capped; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }

    static string SliceAround(string text, int index, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        var start = Math.Max(0, index - maxLength / 3);
        var length = Math.Min(maxLength, text.Length - start);
        return text.Substring(start, length);
    }

    static bool IsRelevantPath(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !parts.Any(p => IgnoredSegments.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)))
            && !path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksLikeSeleniumOrTestFile(string text) =>
        text.Contains("OpenQA.Selenium", StringComparison.Ordinal)
        || text.Contains("IWebDriver", StringComparison.Ordinal)
        || text.Contains("IWebElement", StringComparison.Ordinal)
        || text.Contains("FindElement", StringComparison.Ordinal)
        || text.Contains("By.CssSelector", StringComparison.Ordinal)
        || text.Contains("By.XPath", StringComparison.Ordinal)
        || text.Contains("[Test", StringComparison.Ordinal)
        || text.Contains("[Fact", StringComparison.Ordinal)
        || text.Contains("[Theory", StringComparison.Ordinal);

    static bool IsLifecycleMethod(string methodName) =>
        methodName.Equals("SetUp", StringComparison.OrdinalIgnoreCase)
        || methodName.Equals("TearDown", StringComparison.OrdinalIgnoreCase)
        || methodName.Equals("OneTimeSetUp", StringComparison.OrdinalIgnoreCase)
        || methodName.Equals("OneTimeTearDown", StringComparison.OrdinalIgnoreCase)
        || methodName.Equals("Dispose", StringComparison.OrdinalIgnoreCase);

    static IEnumerable<string> DetectTags(string relativeFile, string className, string methodName, string fileText, string snippet)
    {
        var combined = string.Join(" ", relativeFile, className, methodName, snippet);
        if (Regex.IsMatch(combined, @"Login|Logout|Auth|User|Password|Session", RegexOptions.IgnoreCase))
            yield return "Auth";
        if (Regex.IsMatch(combined, @"Table|Grid|Row|List", RegexOptions.IgnoreCase))
            yield return "Table";
        if (Regex.IsMatch(combined, @"Search|Filter|Find", RegexOptions.IgnoreCase))
            yield return "SearchFilter";
        if (Regex.IsMatch(combined, @"Modal|Dialog|Popup|Confirm", RegexOptions.IgnoreCase))
            yield return "Modal";
        if (Regex.IsMatch(combined, @"Document|File|Upload|Download|Registry|Register", RegexOptions.IgnoreCase))
            yield return "Documents";
        if (Regex.IsMatch(combined, @"Order|Cart|Checkout|Catalog|Product", RegexOptions.IgnoreCase))
            yield return "Commerce";
        if (Regex.IsMatch(fileText, @"class\s+\w+Page\b|PageObject|Pages?\.|\.Page\b", RegexOptions.IgnoreCase))
            yield return "POM";
        if (Regex.IsMatch(snippet, @"Assert\.|Should\s*\(|CollectionAssert|FluentAssertions", RegexOptions.IgnoreCase))
            yield return "Assertions";
        if (Regex.IsMatch(snippet, @"WebDriverWait|Wait|Until|Thread\.Sleep", RegexOptions.IgnoreCase))
            yield return "Wait";
        if (Regex.IsMatch(snippet, @"By\.XPath|//|contains\(|following-sibling", RegexOptions.IgnoreCase))
            yield return "XPath";
        if (Regex.IsMatch(snippet, @"By\.CssSelector|\.CssSelector", RegexOptions.IgnoreCase))
            yield return "CssSelector";
        if (Regex.IsMatch(snippet, @"TestCase|TestCaseSource|Theory|InlineData", RegexOptions.IgnoreCase))
            yield return "DataDriven";
        if (CountPotentialHelpers(snippet) > 0)
            yield return "CustomHelper";
        if (CountMatches(snippet, @"FindElement|FindElements|Click\s*\(|SendKeys\s*\(|Clear\s*\(") <= 3)
            yield return "SimpleSmoke";
    }

    static string ChooseCluster(string relativeFile, string className, string methodName, string[] tags)
    {
        var preferred = new[] { "Auth", "Commerce", "Documents", "Table", "SearchFilter", "Modal" };
        foreach (var tag in preferred)
        {
            if (tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                return tag;
        }

        if (tags.Contains("POM", StringComparer.OrdinalIgnoreCase))
            return "POM-heavy";
        if (tags.Contains("Wait", StringComparer.OrdinalIgnoreCase))
            return "Wait-heavy";
        if (tags.Contains("Assertions", StringComparer.OrdinalIgnoreCase))
            return "Assertion-heavy";

        var pathFirst = relativeFile.Replace('\\', '/').Split('/').FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return string.IsNullOrWhiteSpace(pathFirst) ? "General" : NormalizeClusterName(pathFirst);
    }

    static string NormalizeClusterName(string value)
    {
        var cleaned = Regex.Replace(value, @"[^A-Za-z0-9_-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "General" : cleaned;
    }

    static TestMetrics CountMetrics(string text) => new(
        SeleniumActions: CountMatches(text, @"FindElement|FindElements|Click\s*\(|SendKeys\s*\(|Clear\s*\(|Submit\s*\("),
        Assertions: CountMatches(text, @"Assert\.|Should\s*\(|CollectionAssert|ClassicAssert"),
        Waits: CountMatches(text, @"WebDriverWait|Wait|Until|Thread\.Sleep"),
        Helpers: CountPotentialHelpers(text));

    static int CountPotentialHelpers(string text)
    {
        var invocations = Regex.Matches(text, @"\b(?:[A-Za-z_][A-Za-z0-9_]*(?:Page|Steps|Helper|Control|Table|Filter)|[A-Z][A-Za-z0-9_]*)\.[A-Za-z_][A-Za-z0-9_]*\s*\(");
        return invocations.Count;
    }

    static string DetermineRisk(string[] tags, TestMetrics metrics)
    {
        if (tags.Contains("POM", StringComparer.OrdinalIgnoreCase) && tags.Contains("CustomHelper", StringComparer.OrdinalIgnoreCase))
            return "high";
        if (tags.Contains("XPath", StringComparer.OrdinalIgnoreCase) && metrics.SeleniumActions >= 4)
            return "high";
        if (metrics.Helpers >= 5 || metrics.Waits >= 3)
            return "high";
        if (tags.Contains("POM", StringComparer.OrdinalIgnoreCase) || tags.Contains("Wait", StringComparer.OrdinalIgnoreCase) || tags.Contains("XPath", StringComparer.OrdinalIgnoreCase))
            return "medium";
        return "low";
    }

    static double RepresentativeScore(string[] tags, TestMetrics metrics, string risk)
    {
        var score = tags.Length * 10
            + metrics.Assertions * 5
            + metrics.Waits * 4
            + metrics.Helpers * 3
            + metrics.SeleniumActions * 2;
        score += risk switch
        {
            "low" => 8,
            "medium" => 14,
            "high" => 18,
            _ => 0
        };
        return Math.Round(score / 10.0, 2);
    }

    static IEnumerable<string> BuildReasons(string[] tags, TestMetrics metrics, string risk)
    {
        yield return $"{risk} risk representative candidate";
        if (tags.Contains("POM", StringComparer.OrdinalIgnoreCase))
            yield return "PageObject usage can reveal reusable mapping or review boundaries.";
        if (tags.Contains("Wait", StringComparer.OrdinalIgnoreCase))
            yield return "Wait usage can reveal synchronization policy needs.";
        if (tags.Contains("Assertions", StringComparer.OrdinalIgnoreCase))
            yield return "Assertions help detect semantic loss during migration.";
        if (tags.Contains("XPath", StringComparer.OrdinalIgnoreCase))
            yield return "XPath selectors need early source-backed review.";
        if (metrics.Helpers > 0)
            yield return "Custom helper calls may become MethodSemantics or reviewable TODOs.";
    }

    static int CountMatches(string text, string pattern) => Regex.Matches(text, pattern, RegexOptions.IgnoreCase).Count;

    static MigrationClusterReport BuildClusters(MigrationInventoryReport inventory)
    {
        var tests = inventory.Tests
            .OrderBy(t => t.Cluster, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(t => t.RepresentativeScore)
            .ToArray();

        var clusters = tests
            .GroupBy(t => t.Cluster, StringComparer.OrdinalIgnoreCase)
            .Select(g => new MigrationCluster(
                Name: g.Key,
                Tests: g.Count(),
                Files: g.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                DominantRisk: SelectDominantRisk(g.Select(t => t.Risk)),
                Tags: g.SelectMany(t => t.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                RepresentativeTests: g.OrderByDescending(t => t.RepresentativeScore).ThenBy(t => t.TestId, StringComparer.OrdinalIgnoreCase).Take(5).Select(t => t.TestId).ToArray()))
            .OrderByDescending(c => RiskWeight(c.DominantRisk))
            .ThenByDescending(c => c.Tests)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MigrationClusterReport(
            SchemaVersion: ClustersSchema,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            InputPath: inventory.InputPath,
            Tests: tests,
            Clusters: clusters);
    }

    static string SelectDominantRisk(IEnumerable<string> risks)
    {
        var ordered = risks.OrderByDescending(RiskWeight).ToArray();
        return ordered.FirstOrDefault() ?? "low";
    }

    static int RiskWeight(string risk) => risk.ToLowerInvariant() switch
    {
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0
    };

    static MigrationOptions ApplyTuningRecommendation(MigrationOptions options, WaveTuningCandidate recommendation)
        => options with
        {
            WaveProfile = "auto-tuned",
            MaxWaveSize = recommendation.MaxWaveSize,
            MaxWaveFiles = recommendation.MaxWaveFiles,
            MaxWaveActions = recommendation.MaxWaveActions,
            HardWaveActions = recommendation.HardWaveActions,
            MaxWaveComplexity = recommendation.MaxWaveComplexity,
            HardWaveComplexity = recommendation.HardWaveComplexity,
            SameFileMarginalCostPercent = recommendation.SameFileMarginalCostPercent
        };

    static WaveTuningReport TuneWavePlan(MigrationInventoryReport inventory, MigrationClusterReport clusters, MigrationOptions options)
    {
        var totalTests = inventory.Tests.Length;
        var distinctFiles = inventory.Tests.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var smokeSize = Math.Max(1, Math.Min(options.SmokeWaveSize, totalTests));
        var remainingTests = Math.Max(0, totalTests - smokeSize);
        var referenceWaves = options.TargetWaveCount > 0
            ? options.TargetWaveCount
            : DeriveReferenceWaveCount(remainingTests, Math.Max(0, distinctFiles - 1));

        if (remainingTests == 0)
        {
            var single = new WaveTuningCandidate(
                MaxWaveSize: 1,
                MaxWaveFiles: 1,
                MaxWaveActions: Math.Max(1, inventory.Tests.Sum(t => t.SeleniumActions)),
                HardWaveActions: Math.Max(1, inventory.Tests.Sum(t => t.SeleniumActions)),
                MaxWaveComplexity: Math.Max(1, inventory.Tests.Sum(EstimateTestComplexity)),
                HardWaveComplexity: Math.Max(1, inventory.Tests.Sum(EstimateTestComplexity)),
                SameFileMarginalCostPercent: 100,
                EstimatedWaveCount: 1,
                NonSmokeSingletons: 0,
                FileFragmentation: 0,
                HeavySingleTests: 0,
                SoftOverrun: 0,
                LoadImbalance: 0,
                EstimatedWorkCost: Math.Max(1, inventory.Tests.Sum(EstimateTestComplexity)),
                OrchestrationCost: Math.Max(1, options.RoleOverhead),
                CoordinationRiskCost: 0,
                Score: Math.Max(1, inventory.Tests.Sum(EstimateTestComplexity)) + Math.Max(1, options.RoleOverhead));

            return new WaveTuningReport(
                SchemaVersion: WaveTuningSchema,
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                InputPath: inventory.InputPath,
                Tests: totalTests,
                Files: distinctFiles,
                Clusters: clusters.Clusters.Length,
                TargetWaveCount: 1,
                RoleOverhead: Math.Max(1, options.RoleOverhead),
                SearchCandidatesEvaluated: 1,
                Recommended: single,
                TopCandidates: new[] { single },
                Confidence: "high",
                ScoreGapPercent: 100,
                Notes: BuildTuningNotes(options.TargetWaveCount > 0));
        }

        var smokeIds = inventory.Tests
            .OrderBy(t => RiskWeight(t.Risk))
            .ThenBy(EstimateTestComplexity)
            .ThenBy(t => t.SeleniumActions)
            .ThenBy(t => t.TestId, StringComparer.OrdinalIgnoreCase)
            .Take(smokeSize)
            .Select(t => t.TestId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var workload = inventory.Tests.Where(t => !smokeIds.Contains(t.TestId)).ToArray();
        var workloadFiles = workload.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var testComplexities = workload.Select(EstimateTestComplexity).OrderBy(x => x).ToArray();
        var actionCounts = workload.Select(t => t.SeleniumActions).OrderBy(x => x).ToArray();
        var medianComplexity = Math.Max(1, Quantile(testComplexities, 0.50));
        var p75Complexity = Math.Max(medianComplexity, Quantile(testComplexities, 0.75));
        var p95Complexity = Math.Max(p75Complexity, Quantile(testComplexities, 0.95));
        var p75Actions = Math.Max(1, Quantile(actionCounts, 0.75));
        var p95Actions = Math.Max(p75Actions, Quantile(actionCounts, 0.95));

        var maxSizes = BuildWaveSizeCandidates(remainingTests);
        var maxFiles = BuildWaveFileCandidates(workloadFiles);
        if (remainingTests > 500)
            maxSizes = SelectSpread(maxSizes, 10);
        if (workloadFiles > 100)
            maxFiles = SelectSpread(maxFiles, 6);
        var marginalCosts = workloadFiles >= remainingTests
            ? new[] { 75, 100 }
            : remainingTests > 1000 ? new[] { 30, 50 } : new[] { 25, 35, 45, 60 };
        var pressureFactors = remainingTests > 1000 ? new[] { 100, 125 } : new[] { 90, 110, 130 };
        var referenceNonSmokeWaves = Math.Max(1, referenceWaves - 1);
        var comfortableTestsPerWave = Math.Clamp((int)Math.Round(Math.Sqrt(remainingTests) * 1.25), 4, 14);
        var comfortableFilesPerWave = Math.Clamp((int)Math.Round(Math.Sqrt(Math.Max(1, workloadFiles))), 2, 5);
        var candidates = new List<WaveTuningCandidate>();

        foreach (var maxSize in maxSizes)
        foreach (var fileLimit in maxFiles)
        foreach (var marginal in marginalCosts)
        {
            var lowerBoundWaves = Math.Max(
                1,
                Math.Max(
                    (int)Math.Ceiling(remainingTests / (double)Math.Max(1, maxSize)),
                    (int)Math.Ceiling(workloadFiles / (double)Math.Max(1, fileLimit))));
            var totalEffectiveComplexity = ComputeEffectiveComplexity(workload, marginal);
            var totalActions = workload.Sum(t => t.SeleniumActions);
            var averageComplexity = Math.Max(p75Complexity, (int)Math.Ceiling(totalEffectiveComplexity / (double)lowerBoundWaves));
            var averageActions = Math.Max(p75Actions, (int)Math.Ceiling(totalActions / (double)lowerBoundWaves));
            var comfortableComplexityPerWave = Math.Max(p75Complexity, (int)Math.Ceiling(totalEffectiveComplexity / (double)referenceNonSmokeWaves));
            var comfortableActionsPerWave = Math.Max(p75Actions, (int)Math.Ceiling(totalActions / (double)referenceNonSmokeWaves));

            foreach (var pressure in pressureFactors)
            {
                var softComplexity = Math.Max(p75Complexity, (int)Math.Round(averageComplexity * pressure / 100.0));
                var hardComplexity = Math.Max(
                    Math.Max(p95Complexity + medianComplexity, (int)Math.Ceiling(softComplexity * 1.65)),
                    softComplexity + medianComplexity);
                var softActions = Math.Max(p75Actions, (int)Math.Round(averageActions * pressure / 100.0));
                var hardActions = Math.Max(
                    Math.Max(p95Actions + Math.Max(1, p75Actions), (int)Math.Ceiling(softActions * 1.75)),
                    softActions + Math.Max(1, p75Actions));

                var candidateOptions = options with
                {
                    WaveProfile = "experiment",
                    MaxWaveSize = Math.Min(maxSize, remainingTests),
                    MaxWaveFiles = Math.Min(fileLimit, Math.Max(1, workloadFiles)),
                    MaxWaveActions = Math.Max(1, softActions),
                    HardWaveActions = Math.Max(1, hardActions),
                    MaxWaveComplexity = Math.Max(1, softComplexity),
                    HardWaveComplexity = Math.Max(1, hardComplexity),
                    SameFileMarginalCostPercent = marginal
                };

                var plan = BuildWavePlan(inventory, clusters, candidateOptions);
                var blocked = plan.Waves.Count(w => string.Equals(w.BudgetStatus, "BLOCKED", StringComparison.OrdinalIgnoreCase));
                if (blocked > 0)
                    continue;

                var nonSmokeWaves = plan.Waves.Skip(1).ToArray();
                var nonSmokeSingletons = nonSmokeWaves.Count(w => w.Tests.Length == 1);
                var fileFragmentation = nonSmokeWaves
                    .SelectMany(w => w.Files.Select(file => new { file, w.Id }))
                    .GroupBy(x => x.file, StringComparer.OrdinalIgnoreCase)
                    .Sum(g => Math.Max(0, g.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() - 1));
                var softOverrun = nonSmokeWaves.Sum(w =>
                    Math.Max(0, w.EstimatedComplexity - candidateOptions.MaxWaveComplexity) / (double)Math.Max(1, candidateOptions.MaxWaveComplexity)
                    + Math.Max(0, w.EstimatedActions - candidateOptions.MaxWaveActions) / (double)Math.Max(1, candidateOptions.MaxWaveActions));
                var loads = nonSmokeWaves.Select(w => (double)w.EstimatedComplexity).ToArray();
                var imbalance = loads.Length <= 1 || loads.Average() <= 0
                    ? 0
                    : Math.Sqrt(loads.Select(x => Math.Pow(x - loads.Average(), 2)).Average()) / loads.Average();
                var heavySingles = plan.Waves.Count(w => string.Equals(w.BudgetStatus, "HEAVY_SINGLE_TEST", StringComparison.OrdinalIgnoreCase));
                var roleOverhead = Math.Max(1, options.RoleOverhead);
                var estimatedWorkCost = plan.Waves.Sum(w => (double)w.EstimatedComplexity);
                var orchestrationCost = plan.Waves.Length * (double)roleOverhead;
                var coordinationRiskCost = nonSmokeWaves.Sum(w =>
                {
                    // Candidate ceilings are allowed to be broad. Risk is measured against an
                    // inventory-derived comfortable scope, otherwise a huge candidate would make
                    // its own oversized waves appear artificially cheap.
                    var testPressure = w.Tests.Length / (double)Math.Max(1, comfortableTestsPerWave);
                    var filePressure = w.SourceFileCount / (double)Math.Max(1, comfortableFilesPerWave);
                    var complexityPressure = w.EstimatedComplexity / (double)Math.Max(1, comfortableComplexityPerWave);
                    var actionPressure = w.EstimatedActions / (double)Math.Max(1, comfortableActionsPerWave);
                    return roleOverhead * (
                        (Math.Pow(Math.Max(0, testPressure - 0.70), 2.4) * 0.50)
                        + (Math.Pow(Math.Max(0, filePressure - 0.70), 2.2) * 0.30)
                        + (Math.Pow(Math.Max(0, complexityPressure - 0.70), 2.2) * 0.45)
                        + (Math.Pow(Math.Max(0, actionPressure - 0.80), 2.0) * 0.10));
                });
                coordinationRiskCost += nonSmokeSingletons * roleOverhead * 0.65;
                coordinationRiskCost += fileFragmentation * Math.Max(medianComplexity, roleOverhead * 0.20);
                coordinationRiskCost += softOverrun * roleOverhead * 0.60;
                coordinationRiskCost += imbalance * roleOverhead * 0.40;
                coordinationRiskCost += heavySingles * roleOverhead * 0.35;
                if (options.TargetWaveCount > 0)
                    coordinationRiskCost += Math.Abs(plan.Waves.Length - options.TargetWaveCount) * roleOverhead * 1.5;

                var totalCost = estimatedWorkCost + orchestrationCost + coordinationRiskCost;
                candidates.Add(new WaveTuningCandidate(
                    MaxWaveSize: candidateOptions.MaxWaveSize,
                    MaxWaveFiles: candidateOptions.MaxWaveFiles,
                    MaxWaveActions: candidateOptions.MaxWaveActions,
                    HardWaveActions: candidateOptions.HardWaveActions,
                    MaxWaveComplexity: candidateOptions.MaxWaveComplexity,
                    HardWaveComplexity: candidateOptions.HardWaveComplexity,
                    SameFileMarginalCostPercent: candidateOptions.SameFileMarginalCostPercent,
                    EstimatedWaveCount: plan.Waves.Length,
                    NonSmokeSingletons: nonSmokeSingletons,
                    FileFragmentation: fileFragmentation,
                    HeavySingleTests: heavySingles,
                    SoftOverrun: Math.Round(softOverrun, 3),
                    LoadImbalance: Math.Round(imbalance, 3),
                    EstimatedWorkCost: Math.Round(estimatedWorkCost, 3),
                    OrchestrationCost: Math.Round(orchestrationCost, 3),
                    CoordinationRiskCost: Math.Round(coordinationRiskCost, 3),
                    Score: Math.Round(totalCost, 3)));
            }
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException("Wave tuning produced no feasible candidate profiles.");

        var ranked = candidates
            .OrderBy(c => c.Score)
            .ThenBy(c => c.EstimatedWaveCount)
            .ThenBy(c => c.FileFragmentation)
            .ThenBy(c => c.NonSmokeSingletons)
            .ThenBy(c => c.LoadImbalance)
            .Take(25)
            .ToArray();
        var scoreGapPercent = ranked.Length <= 1 || ranked[0].Score <= 0
            ? 100
            : Math.Round((ranked[1].Score - ranked[0].Score) / ranked[0].Score * 100.0, 2);
        var confidence = scoreGapPercent >= 8 ? "high" : scoreGapPercent >= 2 ? "medium" : "low";

        return new WaveTuningReport(
            SchemaVersion: WaveTuningSchema,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            InputPath: inventory.InputPath,
            Tests: totalTests,
            Files: distinctFiles,
            Clusters: clusters.Clusters.Length,
            TargetWaveCount: referenceWaves,
            RoleOverhead: Math.Max(1, options.RoleOverhead),
            SearchCandidatesEvaluated: candidates.Count,
            Recommended: ranked[0],
            TopCandidates: ranked,
            Confidence: confidence,
            ScoreGapPercent: scoreGapPercent,
            Notes: BuildTuningNotes(options.TargetWaveCount > 0));
    }

    static string[] BuildTuningNotes(bool explicitTarget)
        => new[]
        {
            "This is a deterministic static experiment: it does not invoke agents or run migration.",
            "Candidate limits are derived from the current inventory size and complexity distribution rather than a project-specific constant.",
            "The score estimates migration work, full role-cycle overhead, source-file fragmentation, load imbalance, and coordination risk.",
            "Same-file tests use marginal complexity instead of paying the full file/POM discovery cost repeatedly.",
            "Soft action/complexity limits guide packing; only the broader dynamically derived hard ceiling blocks a multi-test wave.",
            explicitTarget
                ? "The explicit --target-waves value is treated as an optimization constraint."
                : "The reported target wave count is a diagnostic reference; the recommendation is selected by estimated total cost, not forced to that number.",
            "Actual wall-clock performance can be calibrated later by passing a measured --role-overhead value."
        };

    static int DeriveReferenceWaveCount(int remainingTests, int remainingFiles)
    {
        if (remainingTests <= 0)
            return 1;
        var adaptiveTestsPerWave = Math.Clamp((int)Math.Round(Math.Sqrt(remainingTests) * 1.5), 4, 16);
        var adaptiveFilesPerWave = Math.Clamp((int)Math.Round(Math.Sqrt(Math.Max(1, remainingFiles))), 2, 6);
        return 1 + Math.Max(
            (int)Math.Ceiling(remainingTests / (double)adaptiveTestsPerWave),
            (int)Math.Ceiling(Math.Max(1, remainingFiles) / (double)adaptiveFilesPerWave));
    }

    static int[] BuildWaveSizeCandidates(int remainingTests)
    {
        var values = new SortedSet<int>();
        foreach (var value in new[] { 2, 4, 6, 8, 10, 12, 16, 24, 32, 48, 64 })
            if (value <= remainingTests)
                values.Add(value);
        foreach (var desiredWaves in new[] { 2, 3, 4, 6, 8, 10, 12, 16, 24, 32 })
        {
            var value = (int)Math.Ceiling(remainingTests / (double)desiredWaves);
            if (value >= 2 && value <= Math.Min(64, remainingTests))
                values.Add(value);
        }
        values.Add(Math.Max(1, Math.Min(remainingTests, 64)));
        return SelectSpread(values.ToArray(), 16);
    }

    static int[] BuildWaveFileCandidates(int distinctFiles)
    {
        var values = new SortedSet<int>();
        foreach (var value in new[] { 1, 2, 3, 4, 5, 6, 8, 10, 12 })
            if (value <= Math.Max(1, distinctFiles))
                values.Add(value);
        values.Add(Math.Max(1, Math.Min(distinctFiles, 12)));
        return SelectSpread(values.ToArray(), 8);
    }

    static int[] SelectSpread(int[] values, int maxCount)
    {
        if (values.Length <= maxCount)
            return values;
        var selected = new SortedSet<int> { values[0], values[^1] };
        for (var index = 1; index < maxCount - 1; index++)
        {
            var position = (int)Math.Round(index * (values.Length - 1) / (double)(maxCount - 1));
            selected.Add(values[Math.Clamp(position, 0, values.Length - 1)]);
        }
        return selected.ToArray();
    }

    static int Quantile(int[] sortedValues, double quantile)
    {
        if (sortedValues.Length == 0)
            return 0;
        var position = Math.Clamp(quantile, 0, 1) * (sortedValues.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sortedValues[lower];
        var weight = position - lower;
        return (int)Math.Round(sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * weight));
    }

    static MigrationWavePlan BuildWavePlan(MigrationInventoryReport inventory, MigrationClusterReport clusters, MigrationOptions options)
    {
        var maxWaveSize = Math.Max(1, options.MaxWaveSize);
        var repsPerCluster = Math.Max(1, options.RepresentativesPerCluster);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var waves = new List<MigrationWave>();

        // Only the first wave is intentionally a singleton. It proves the lifecycle before the
        // planner starts amortizing role overhead across file/POM-affine batches.
        var smokeCandidates = clusters.Tests
            .OrderBy(t => RiskWeight(t.Risk))
            .ThenBy(EstimateTestComplexity)
            .ThenBy(t => t.SeleniumActions)
            .ThenBy(t => t.TestId, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, options.SmokeWaveSize))
            .ToArray();
        AddWave(waves, used, "smoke-validation", "mixed", smokeCandidates, options);

        var remaining = clusters.Tests
            .Where(t => !used.Contains(t.TestId))
            .ToArray();
        foreach (var chunk in PackByBudget(remaining, options))
        {
            var dominantCluster = chunk
                .GroupBy(t => t.Cluster, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Key)
                .FirstOrDefault() ?? "mixed";
            AddWave(waves, used, "adaptive-batch", dominantCluster, chunk, options);
        }

        return new MigrationWavePlan(
            SchemaVersion: WavePlanSchema,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Strategy: "wavefront",
            InputPath: inventory.InputPath,
            Workspace: options.Workspace,
            MaxWaveSize: maxWaveSize,
            MaxWaveFiles: Math.Max(1, options.MaxWaveFiles),
            MaxWaveActions: Math.Max(1, options.MaxWaveActions),
            MaxWaveComplexity: Math.Max(1, options.MaxWaveComplexity),
            SmokeWaveSize: Math.Max(1, options.SmokeWaveSize),
            RepresentativesPerCluster: repsPerCluster,
            PreferLowRiskFirst: options.PreferLowRiskFirst,
            TotalTests: inventory.Tests.Length,
            TotalClusters: clusters.Clusters.Length,
            Waves: waves.ToArray(),
            WaveProfile: options.WaveProfile,
            HardWaveActions: Math.Max(options.MaxWaveActions, options.HardWaveActions),
            HardWaveComplexity: Math.Max(options.MaxWaveComplexity, options.HardWaveComplexity),
            SameFileMarginalCostPercent: Math.Clamp(options.SameFileMarginalCostPercent, 0, 100));
    }

    static IReadOnlyList<IReadOnlyList<MigrationTestInventoryItem>> PackByBudget(
        IReadOnlyList<MigrationTestInventoryItem> items,
        MigrationOptions options)
    {
        var chunks = BuildFileChunks(items, options)
            .OrderByDescending(chunk => ComputeEffectiveComplexity(chunk, options.SameFileMarginalCostPercent))
            .ThenByDescending(chunk => chunk.Count)
            .ThenBy(chunk => chunk[0].File, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var bins = new List<List<MigrationTestInventoryItem>>();

        foreach (var chunk in chunks)
        {
            var bestIndex = -1;
            var bestScore = double.MaxValue;
            for (var index = 0; index < bins.Count; index++)
            {
                var combined = bins[index].Concat(chunk).ToArray();
                if (!FitsHardWaveBudget(combined, options))
                    continue;

                var effective = ComputeEffectiveComplexity(combined, options.SameFileMarginalCostPercent);
                var files = combined.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var clusters = combined.Select(t => t.Cluster).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var sameFileOverlap = bins[index].Select(t => t.File).Intersect(chunk.Select(t => t.File), StringComparer.OrdinalIgnoreCase).Any();
                var targetTests = Math.Max(2, (int)Math.Round(options.MaxWaveSize * 0.85));
                var score =
                    Math.Abs(options.MaxWaveComplexity - effective)
                    + (Math.Abs(targetTests - combined.Length) * 20)
                    + (files * 25)
                    + (clusters * 8)
                    - (sameFileOverlap ? 500 : 0);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = index;
                }
            }

            if (bestIndex < 0)
                bins.Add(chunk.ToList());
            else
                bins[bestIndex].AddRange(chunk);
        }

        return bins
            .OrderBy(bin => RiskWeight(SelectDominantRisk(bin.Select(t => t.Risk))))
            .ThenBy(bin => ComputeEffectiveComplexity(bin, options.SameFileMarginalCostPercent))
            .ThenBy(bin => string.Join("|", bin.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase)
            .Select(bin => (IReadOnlyList<MigrationTestInventoryItem>)bin
                .OrderBy(t => t.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Line)
                .ThenBy(t => t.TestId, StringComparer.OrdinalIgnoreCase)
                .ToArray())
            .ToArray();
    }

    static IReadOnlyList<IReadOnlyList<MigrationTestInventoryItem>> BuildFileChunks(
        IReadOnlyList<MigrationTestInventoryItem> items,
        MigrationOptions options)
    {
        var chunks = new List<IReadOnlyList<MigrationTestInventoryItem>>();
        foreach (var fileGroup in items
            .GroupBy(t => t.File, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var current = new List<MigrationTestInventoryItem>();
            foreach (var item in fileGroup
                .OrderBy(t => RiskWeight(t.Risk))
                .ThenBy(EstimateTestComplexity)
                .ThenBy(t => t.Line)
                .ThenBy(t => t.TestId, StringComparer.OrdinalIgnoreCase))
            {
                var proposed = current.Append(item).ToArray();
                if (current.Count > 0 && !FitsHardWaveBudget(proposed, options))
                {
                    chunks.Add(current.ToArray());
                    current.Clear();
                }
                current.Add(item);
            }

            if (current.Count > 0)
                chunks.Add(current.ToArray());
        }
        return chunks;
    }

    static bool FitsHardWaveBudget(IEnumerable<MigrationTestInventoryItem> tests, MigrationOptions options)
    {
        var values = tests.ToArray();
        if (values.Length == 0)
            return true;
        if (values.Length == 1)
            return true; // A heavy test remains schedulable and is labelled explicitly.

        return values.Length <= Math.Max(1, options.MaxWaveSize)
            && values.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= Math.Max(1, options.MaxWaveFiles)
            && values.Sum(t => t.SeleniumActions) <= Math.Max(options.MaxWaveActions, options.HardWaveActions)
            && ComputeEffectiveComplexity(values, options.SameFileMarginalCostPercent) <= Math.Max(options.MaxWaveComplexity, options.HardWaveComplexity);
    }

    static int EstimateTestComplexity(MigrationTestInventoryItem test)
        => test.SeleniumActions
            + (test.Assertions * 2)
            + (test.Waits * 2)
            + (test.Helpers * 3)
            + (RiskWeight(test.Risk) * 10);

    static int ComputeEffectiveComplexity(IEnumerable<MigrationTestInventoryItem> tests, int sameFileMarginalCostPercent)
    {
        var marginal = Math.Clamp(sameFileMarginalCostPercent, 0, 100) / 100.0;
        var total = 0.0;
        foreach (var file in tests.GroupBy(t => t.File, StringComparer.OrdinalIgnoreCase))
        {
            var costs = file.Select(EstimateTestComplexity).OrderByDescending(x => x).ToArray();
            if (costs.Length == 0)
                continue;
            total += costs[0] + (costs.Skip(1).Sum() * marginal);
        }
        return (int)Math.Ceiling(total);
    }

    static void AddWave(
        List<MigrationWave> waves,
        HashSet<string> used,
        string phase,
        string cluster,
        IReadOnlyList<MigrationTestInventoryItem> tests,
        MigrationOptions options)
    {
        var unique = tests.Where(t => used.Add(t.TestId)).ToArray();
        if (unique.Length == 0)
            return;

        var sourceFileCount = unique.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var estimatedActions = unique.Sum(t => t.SeleniumActions);
        var rawComplexity = unique.Sum(EstimateTestComplexity);
        var estimatedComplexity = ComputeEffectiveComplexity(unique, options.SameFileMarginalCostPercent);
        var hardViolations = new List<string>();
        if (unique.Length > options.MaxWaveSize) hardViolations.Add($"testCount {unique.Length} > hard {options.MaxWaveSize}");
        if (sourceFileCount > options.MaxWaveFiles) hardViolations.Add($"sourceFileCount {sourceFileCount} > hard {options.MaxWaveFiles}");
        if (estimatedActions > Math.Max(options.MaxWaveActions, options.HardWaveActions)) hardViolations.Add($"estimatedActions {estimatedActions} > hard {Math.Max(options.MaxWaveActions, options.HardWaveActions)}");
        if (estimatedComplexity > Math.Max(options.MaxWaveComplexity, options.HardWaveComplexity)) hardViolations.Add($"effectiveComplexity {estimatedComplexity} > hard {Math.Max(options.MaxWaveComplexity, options.HardWaveComplexity)}");
        var softWarnings = new List<string>();
        if (estimatedActions > options.MaxWaveActions) softWarnings.Add($"soft estimatedActions {estimatedActions} > {options.MaxWaveActions}");
        if (estimatedComplexity > options.MaxWaveComplexity) softWarnings.Add($"soft effectiveComplexity {estimatedComplexity} > {options.MaxWaveComplexity}");
        var budgetStatus = hardViolations.Count > 0
            ? unique.Length == 1 ? "HEAVY_SINGLE_TEST" : "BLOCKED"
            : softWarnings.Count > 0 ? "SOFT_LIMIT_EXCEEDED" : "PASS";

        var index = waves.Count + 1;
        waves.Add(new MigrationWave(
            Id: $"wave-{index:000}",
            Index: index,
            Phase: phase,
            Cluster: cluster,
            Tests: unique,
            Files: unique.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            DominantRisk: SelectDominantRisk(unique.Select(t => t.Risk)),
            EstimatedActions: estimatedActions,
            EstimatedComplexity: estimatedComplexity,
            SourceFileCount: sourceFileCount,
            BudgetStatus: budgetStatus,
            BudgetViolations: hardViolations.Concat(softWarnings).ToArray(),
            Rationale: phase switch
            {
                "smoke-validation" => "Single low-risk smoke wave validates recall, migration, review, remediation budget, and final-gate lifecycle before expansion.",
                "representatives" => "Representative ordering opens project patterns without forcing a separate singleton wave per cluster.",
                _ => $"Affinity-aware adaptive batch for {cluster}; same-file/POM discovery is charged once and subsequent tests use marginal complexity."
            },
            RawComplexity: rawComplexity));
    }

    static void WriteInventoryArtifacts(string outPath, string format, MigrationInventoryReport inventory)
    {
        Directory.CreateDirectory(outPath);
        if (format is "json" or "both")
            File.WriteAllText(Path.Combine(outPath, "inventory.json"), JsonSerializer.Serialize(inventory, JsonOptions));
        if (format is "text" or "both")
            File.WriteAllText(Path.Combine(outPath, "inventory.md"), WriteInventoryMarkdown(inventory));
    }

    static void WriteClusterArtifacts(string outPath, string format, MigrationClusterReport clusters)
    {
        Directory.CreateDirectory(outPath);
        if (format is "json" or "both")
            File.WriteAllText(Path.Combine(outPath, "clusters.json"), JsonSerializer.Serialize(clusters, JsonOptions));
        if (format is "text" or "both")
            File.WriteAllText(Path.Combine(outPath, "clusters.md"), WriteClustersMarkdown(clusters));
    }

    static void WriteWaveTuningArtifacts(string outPath, WaveTuningReport tuning)
    {
        Directory.CreateDirectory(outPath);
        File.WriteAllText(Path.Combine(outPath, "wave-tuning.json"), JsonSerializer.Serialize(tuning, JsonOptions));

        var sb = new StringBuilder();
        sb.AppendLine("# Wave-plan tuning experiment");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{tuning.SchemaVersion}`");
        sb.AppendLine($"Input: `{tuning.InputPath}`");
        sb.AppendLine($"Tests/files/clusters: {tuning.Tests}/{tuning.Files}/{tuning.Clusters}");
        sb.AppendLine($"Reference waves: **{tuning.TargetWaveCount}**");
        sb.AppendLine($"Static role-overhead weight: **{tuning.RoleOverhead}**");
        sb.AppendLine($"Candidates evaluated: **{tuning.SearchCandidatesEvaluated}**");
        sb.AppendLine($"Recommendation confidence: **{tuning.Confidence}** (gap to runner-up: {tuning.ScoreGapPercent}%)");
        sb.AppendLine();
        sb.AppendLine("## Recommended profile");
        sb.AppendLine();
        sb.AppendLine($"- predicted waves: **{tuning.Recommended.EstimatedWaveCount}**");
        sb.AppendLine($"- tests/files: `{tuning.Recommended.MaxWaveSize}` / `{tuning.Recommended.MaxWaveFiles}`");
        sb.AppendLine($"- actions soft/hard: `{tuning.Recommended.MaxWaveActions}` / `{tuning.Recommended.HardWaveActions}`");
        sb.AppendLine($"- effective complexity soft/hard: `{tuning.Recommended.MaxWaveComplexity}` / `{tuning.Recommended.HardWaveComplexity}`");
        sb.AppendLine($"- same-file marginal cost: `{tuning.Recommended.SameFileMarginalCostPercent}%`");
        sb.AppendLine($"- non-smoke singleton waves: `{tuning.Recommended.NonSmokeSingletons}`");
        sb.AppendLine($"- source-file fragmentation: `{tuning.Recommended.FileFragmentation}`");
        sb.AppendLine($"- estimated work cost: `{tuning.Recommended.EstimatedWorkCost}`");
        sb.AppendLine($"- orchestration cost: `{tuning.Recommended.OrchestrationCost}`");
        sb.AppendLine($"- coordination-risk cost: `{tuning.Recommended.CoordinationRiskCost}`");
        sb.AppendLine($"- total estimated cost: `{tuning.Recommended.Score}`");
        sb.AppendLine();
        sb.AppendLine("## Top candidates");
        sb.AppendLine();
        sb.AppendLine("| Rank | Waves | Tests | Files | Complexity soft/hard | Same-file % | Singletons | Fragmentation | Work | Roles | Risk | Total |");
        sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        for (var index = 0; index < tuning.TopCandidates.Length; index++)
        {
            var candidate = tuning.TopCandidates[index];
            sb.AppendLine($"| {index + 1} | {candidate.EstimatedWaveCount} | {candidate.MaxWaveSize} | {candidate.MaxWaveFiles} | {candidate.MaxWaveComplexity}/{candidate.HardWaveComplexity} | {candidate.SameFileMarginalCostPercent} | {candidate.NonSmokeSingletons} | {candidate.FileFragmentation} | {candidate.EstimatedWorkCost} | {candidate.OrchestrationCost} | {candidate.CoordinationRiskCost} | {candidate.Score} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Interpretation");
        sb.AppendLine();
        foreach (var note in tuning.Notes)
            sb.AppendLine($"- {note}");
        sb.AppendLine();
        sb.AppendLine("The experiment is planning-only: no agent, migration, review, watchdog, or final-gate role is started.");
        File.WriteAllText(Path.Combine(outPath, "wave-tuning.md"), sb.ToString());
    }

    static void WritePlanArtifacts(string outPath, string format, MigrationWavePlan plan)
    {
        Directory.CreateDirectory(outPath);
        if (format is "json" or "both")
            File.WriteAllText(Path.Combine(outPath, "waves.json"), JsonSerializer.Serialize(plan, JsonOptions));
        if (format is "text" or "both")
            File.WriteAllText(Path.Combine(outPath, "plan.md"), WritePlanMarkdown(plan));
        File.WriteAllLines(Path.Combine(outPath, "selected-tests.txt"), plan.Waves.SelectMany(w => w.Tests).Select(t => t.File + "::" + t.TestId).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    static string WriteInventoryMarkdown(MigrationInventoryReport inventory)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration inventory");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{InventorySchema}`");
        sb.AppendLine($"Input: `{inventory.InputPath}`");
        sb.AppendLine($"Files scanned: {inventory.FilesScanned}");
        sb.AppendLine($"Test files: {inventory.TestFiles}");
        sb.AppendLine($"Tests found: {inventory.TestsFound}");
        sb.AppendLine();
        sb.AppendLine("## Tags");
        sb.AppendLine();
        sb.AppendLine(inventory.DistinctTags.Length == 0 ? "No tags detected." : string.Join(", ", inventory.DistinctTags.Select(t => $"`{t}`")));
        sb.AppendLine();
        sb.AppendLine("## Tests");
        sb.AppendLine();
        sb.AppendLine("| Test | File | Cluster | Risk | Tags |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var test in inventory.Tests)
            sb.AppendLine($"| `{test.TestId}` | `{test.File}` | {test.Cluster} | {test.Risk} | {string.Join(", ", test.Tags)} |");
        return sb.ToString();
    }

    static string WriteClustersMarkdown(MigrationClusterReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration clusters");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{ClustersSchema}`");
        sb.AppendLine($"Input: `{report.InputPath}`");
        sb.AppendLine();
        sb.AppendLine("| Cluster | Tests | Files | Dominant risk | Tags | Representatives |");
        sb.AppendLine("| --- | ---: | ---: | --- | --- | --- |");
        foreach (var cluster in report.Clusters)
        {
            sb.AppendLine($"| {cluster.Name} | {cluster.Tests} | {cluster.Files} | {cluster.DominantRisk} | {string.Join(", ", cluster.Tags)} | {string.Join("<br>", cluster.RepresentativeTests.Select(t => $"`{t}`"))} |");
        }
        return sb.ToString();
    }

    static string WritePlanMarkdown(MigrationWavePlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Divide-and-conquer migration wave plan");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{WavePlanSchema}`");
        sb.AppendLine($"Strategy: `{plan.Strategy}`");
        sb.AppendLine($"Input: `{plan.InputPath}`");
        sb.AppendLine($"Workspace: `{plan.Workspace}`");
        sb.AppendLine($"Total tests: {plan.TotalTests}");
        sb.AppendLine($"Clusters: {plan.TotalClusters}");
        sb.AppendLine($"Waves: {plan.Waves.Length}");
        sb.AppendLine($"Wave profile: `{plan.WaveProfile}`");
        sb.AppendLine($"Packing: tests <= {plan.MaxWaveSize}, files <= {plan.MaxWaveFiles}; actions soft/hard {plan.MaxWaveActions}/{plan.HardWaveActions}; effective complexity soft/hard {plan.MaxWaveComplexity}/{plan.HardWaveComplexity}; same-file marginal cost {plan.SameFileMarginalCostPercent}%; smoke wave size = {plan.SmokeWaveSize}.");
        sb.AppendLine();
        sb.AppendLine("> This plan is read-only. It does not migrate source files. Run `memory explain` and `memory doctor` before turning any wave into a bounded migration task.");
        sb.AppendLine();
        foreach (var wave in plan.Waves)
        {
            sb.AppendLine($"## {wave.Id}: {wave.Phase} / {wave.Cluster}");
            sb.AppendLine();
            sb.AppendLine($"Risk: **{wave.DominantRisk}**");
            sb.AppendLine($"Preflight budget: **{wave.BudgetStatus}**; tests {wave.Tests.Length}, files {wave.SourceFileCount}, actions {wave.EstimatedActions}, effective/raw complexity {wave.EstimatedComplexity}/{wave.RawComplexity}.");
            if ((wave.BudgetViolations?.Length ?? 0) > 0)
                sb.AppendLine($"Budget violations: {string.Join("; ", wave.BudgetViolations ?? Array.Empty<string>())}");
            sb.AppendLine($"Rationale: {wave.Rationale}");
            sb.AppendLine();
            sb.AppendLine("| Test | File | Risk | Tags | Why this test matters |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var test in wave.Tests)
            {
                sb.AppendLine($"| `{test.TestId}` | `{test.File}` | {test.Risk} | {string.Join(", ", test.Tags)} | {string.Join("<br>", test.Reasons)} |");
            }
            sb.AppendLine();
            sb.AppendLine("Suggested bounded action:");
            sb.AppendLine();
            sb.AppendLine("```text");
            sb.AppendLine($"Run {wave.Id} as a bounded migration task. Use project memory as guidance, not authority. Emit report, config-delta, memory-delta, reviewer findings, watchdog findings, and final-gate evidence. Do not suppress assertions or over-suppress user interactions.");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    static void WriteMemoryRecallGuide(string outPath, string workspace, MigrationWavePlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Project memory usage for wavefront migration");
        sb.AppendLine();
        sb.AppendLine("Before a supervised agent starts any wave, it should read project-scoped memory:");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine($"selenium-pw-migrator memory explain --workspace {QuoteForShell(workspace)}");
        sb.AppendLine($"selenium-pw-migrator memory doctor --workspace {QuoteForShell(workspace)}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("For a concrete wave, recall memory per touched file before planning:");
        sb.AppendLine();
        foreach (var wave in plan.Waves.Take(3))
        {
            sb.AppendLine($"## {wave.Id}");
            foreach (var file in wave.Files)
                sb.AppendLine($"- `selenium-pw-migrator memory recall --file {file} --workspace {workspace}`");
            sb.AppendLine();
        }
        sb.AppendLine("Memory is guidance, not authority. Reviewer/Watchdog/Final Gate can reject any memory-backed shortcut.");
        File.WriteAllText(Path.Combine(outPath, "memory-recall.md"), sb.ToString());
    }

    static void WriteNextCommands(string outPath, MigrationOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Next commands");
        sb.AppendLine();
        sb.AppendLine("Inspect the generated divide-and-conquer plan:");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine($"selenium-pw-migrator migration plan show --plan {QuoteForShell(outPath)}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Check project memory before turning the first wave into a supervised task:");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine($"selenium-pw-migrator memory explain --workspace {QuoteForShell(options.Workspace)}");
        sb.AppendLine($"selenium-pw-migrator memory doctor --workspace {QuoteForShell(options.Workspace)}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Prepare the first wave as a bounded run workspace:");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine($"selenium-pw-migrator migration run-wave --plan {QuoteForShell(outPath)} --wave wave-001 --workspace {QuoteForShell(options.Workspace)} --out {QuoteForShell(Path.Combine(options.Workspace, "runs", "wave-001"))}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("`run-wave` materializes source-scope/, config-delta.json, memory-delta.jsonl, run-summary.md, and run-migrate scripts. It does not promote config or memory automatically.");
        sb.AppendLine();
        sb.AppendLine("After one or more reviewed waves produce concrete deltas, create and validate a candidate config:");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine($"selenium-pw-migrator config merge-deltas --base {QuoteForShell(Path.Combine(options.Workspace, "adapter-config.json"))} --deltas {QuoteForShell(Path.Combine(options.Workspace, "state", "memory", "config-deltas"))} --out {QuoteForShell(Path.Combine(options.Workspace, "config-merge"))}");
        sb.AppendLine($"selenium-pw-migrator config validate-merge --base {QuoteForShell(Path.Combine(options.Workspace, "adapter-config.json"))} --candidate {QuoteForShell(Path.Combine(options.Workspace, "config-merge", "adapter-config.merged.json"))} --out {QuoteForShell(Path.Combine(options.Workspace, "config-merge"))}");
        sb.AppendLine("```");
        File.WriteAllText(Path.Combine(outPath, "next-commands.md"), sb.ToString());
    }


    static bool TryReadWavePlan(string path, out MigrationWavePlan? plan, out string error)
    {
        plan = null;
        error = string.Empty;
        var jsonPath = Directory.Exists(path) ? Path.Combine(path, "waves.json") : path;
        if (!File.Exists(jsonPath))
        {
            error = $"Wave plan not found: {jsonPath}";
            return false;
        }

        try
        {
            plan = JsonSerializer.Deserialize<MigrationWavePlan>(File.ReadAllText(jsonPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (plan == null)
            {
                error = "Wave plan deserialized to null.";
                return false;
            }
            plan = NormalizeWavePlan(plan);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static MigrationWavePlan NormalizeWavePlan(MigrationWavePlan plan)
    {
        var normalizedPlan = plan with
        {
            MaxWaveSize = plan.MaxWaveSize > 0 ? plan.MaxWaveSize : 12,
            MaxWaveFiles = plan.MaxWaveFiles > 0 ? plan.MaxWaveFiles : 4,
            MaxWaveActions = plan.MaxWaveActions > 0 ? plan.MaxWaveActions : 180,
            HardWaveActions = plan.HardWaveActions > 0 ? plan.HardWaveActions : Math.Max(plan.MaxWaveActions, 320),
            MaxWaveComplexity = plan.MaxWaveComplexity > 0 ? plan.MaxWaveComplexity : 550,
            HardWaveComplexity = plan.HardWaveComplexity > 0 ? plan.HardWaveComplexity : Math.Max(plan.MaxWaveComplexity, 850),
            SameFileMarginalCostPercent = plan.SameFileMarginalCostPercent is >= 0 and <= 100 ? plan.SameFileMarginalCostPercent : 30,
            SmokeWaveSize = plan.SmokeWaveSize > 0 ? plan.SmokeWaveSize : 1,
            WaveProfile = string.IsNullOrWhiteSpace(plan.WaveProfile) ? "legacy" : plan.WaveProfile
        };

        normalizedPlan = normalizedPlan with
        {
            Waves = (plan.Waves ?? Array.Empty<MigrationWave>()).Select(wave =>
            {
                var tests = wave.Tests ?? Array.Empty<MigrationTestInventoryItem>();
                var sourceFileCount = tests.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var estimatedActions = tests.Sum(t => t.SeleniumActions);
                var rawComplexity = tests.Sum(EstimateTestComplexity);
                var estimatedComplexity = ComputeEffectiveComplexity(tests, normalizedPlan.SameFileMarginalCostPercent);
                var hardViolations = new List<string>();
                if (tests.Length > normalizedPlan.MaxWaveSize) hardViolations.Add($"testCount {tests.Length} > hard {normalizedPlan.MaxWaveSize}");
                if (sourceFileCount > normalizedPlan.MaxWaveFiles) hardViolations.Add($"sourceFileCount {sourceFileCount} > hard {normalizedPlan.MaxWaveFiles}");
                if (estimatedActions > normalizedPlan.HardWaveActions) hardViolations.Add($"estimatedActions {estimatedActions} > hard {normalizedPlan.HardWaveActions}");
                if (estimatedComplexity > normalizedPlan.HardWaveComplexity) hardViolations.Add($"effectiveComplexity {estimatedComplexity} > hard {normalizedPlan.HardWaveComplexity}");
                var softWarnings = new List<string>();
                if (estimatedActions > normalizedPlan.MaxWaveActions) softWarnings.Add($"soft estimatedActions {estimatedActions} > {normalizedPlan.MaxWaveActions}");
                if (estimatedComplexity > normalizedPlan.MaxWaveComplexity) softWarnings.Add($"soft effectiveComplexity {estimatedComplexity} > {normalizedPlan.MaxWaveComplexity}");
                return wave with
                {
                    Tests = tests,
                    Files = wave.Files ?? tests.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    EstimatedActions = estimatedActions,
                    EstimatedComplexity = estimatedComplexity,
                    RawComplexity = rawComplexity,
                    SourceFileCount = sourceFileCount,
                    BudgetStatus = hardViolations.Count > 0
                        ? tests.Length == 1 ? "HEAVY_SINGLE_TEST" : "BLOCKED"
                        : softWarnings.Count > 0 ? "SOFT_LIMIT_EXCEEDED" : "PASS",
                    BudgetViolations = hardViolations.Concat(softWarnings).ToArray()
                };
            }).ToArray()
        };
        return normalizedPlan;
    }

    static JsonSerializerOptions JsonLineOptions() => new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    static bool TryReadInventory(string path, out MigrationInventoryReport? inventory, out string error)
    {
        inventory = null;
        error = string.Empty;
        var jsonPath = Directory.Exists(path) ? Path.Combine(path, "inventory.json") : path;
        if (!File.Exists(jsonPath))
        {
            error = $"Inventory not found: {jsonPath}";
            return false;
        }

        try
        {
            inventory = JsonSerializer.Deserialize<MigrationInventoryReport>(File.ReadAllText(jsonPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (inventory == null)
            {
                error = "Inventory file deserialized to null.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static void WriteJsonAtomic(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    static bool IsHelp(string arg) => arg is "-h" or "--help" or "help" or "/?";

    static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown migration command: {command}");
        PrintHelp();
        return 2;
    }

    static void PrintHelp()
    {
        Console.WriteLine("""
Migration planning and wave run commands:
  selenium-pw-migrator migration inventory --input ./SeleniumTests --out migration/plan
  selenium-pw-migrator migration cluster --input ./SeleniumTests --out migration/plan
  selenium-pw-migrator migration tune-wave-plan --input ./SeleniumTests --workspace migration --out migration/plan-tuning
  selenium-pw-migrator migration plan --strategy wavefront --input ./SeleniumTests --workspace migration --out migration/plan --wave-profile auto --smoke-wave-size 1
  selenium-pw-migrator migration plan --strategy wavefront --input ./SeleniumTests --workspace migration --out migration/plan --wave-profile balanced
  selenium-pw-migrator migration plan --strategy wavefront --input ./SeleniumTests --workspace migration --out migration/plan --wave-profile manual --max-wave-size 12 --max-wave-files 4 --max-wave-actions 180 --hard-wave-actions 320 --max-wave-complexity 550 --hard-wave-complexity 850 --same-file-marginal-cost 30
  selenium-pw-migrator migration plan show --plan migration/plan
  selenium-pw-migrator migration run-wave --plan migration/plan --wave wave-001 --workspace migration --out migration/runs/wave-001 --execution-profile fast
  selenium-pw-migrator migration validate-wave --out migration/runs/wave-001
  selenium-pw-migrator migration check-progress --out migration/runs/wave-001 --max-identical-snapshots 3
  selenium-pw-migrator migration validation-plan --out migration/runs/wave-001
  selenium-pw-migrator migration validate --out migration/runs/wave-001 --validation-project ./Target.Tests/Target.Tests.csproj --checkpoint-on-pass true
  selenium-pw-migrator migration validate --out migration/runs/wave-001 --validation-command "dotnet test ./Target.Tests/Target.Tests.csproj --no-restore" --validation-timeout-seconds 900
  selenium-pw-migrator migration record-validation --out migration/runs/wave-001 --validation-id target-build --validation-exit-code 0 --validation-scope changed-files --validation-command "dotnet test Target.Tests.csproj"
  selenium-pw-migrator migration checkpoint-wave --out migration/runs/wave-001 --checkpoint-label generated --checkpoint-stage migration
  selenium-pw-migrator migration resume-wave --out migration/runs/wave-001
  selenium-pw-migrator migration build-review-bundle --out migration/runs/wave-001
  selenium-pw-migrator migration perf-report --out migration/runs/wave-001
  selenium-pw-migrator migration refresh-wave-status --out migration/runs/wave-001 --migrate-exit-code 0

Planning is read-only; tune-wave-plan also executes no agents. The auto profile
tests deterministic budget combinations and minimizes role-cycle overhead, singleton waves,
and source-file fragmentation. Same-file tests pay marginal rather than full repeated complexity.
run-wave materializes an immutable wave manifest, execution policy, run-context, bounded source-scope plus config-delta,
memory-delta, performance trace, run summary, evidence folder, and migrate scripts. `validate-wave` rejects scope drift or changed copied inputs. `check-progress` detects repeated identical generated/evidence/TODO/unmapped/validation state. `validation-plan` computes changed-file impact and exact-input cache eligibility; `migration validate` is the single validation host that plans, executes, records evidence, and materializes exact-input cache hits without an agent-managed three-command chain. `record-validation` remains available for compatibility and manual evidence import. Checkpoints, resume decisions, and review bundles preserve work without treating a checkpoint as DONE. The migrate wrappers refresh wave-status.json and validation-plan.json after execution. It never promotes config or memory automatically.
Use `selenium-pw-migrator config merge-deltas` and `config validate-merge` to create a reviewable candidate config after wave deltas are reviewed.
""");
    }

    static string QuoteForShell(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    sealed record MigrationOptions(
        string Input,
        string Out,
        string Workspace,
        string Strategy,
        string Format,
        string Plan,
        string Inventory,
        string Wave,
        string Config,
        string Target,
        string TargetTestFramework,
        string GenerationPolicy,
        bool ExecuteMigrate,
        int MigrateExitCode,
        int MaxWaveSize,
        int MaxWaveFiles,
        int MaxWaveActions,
        int HardWaveActions,
        int MaxWaveComplexity,
        int HardWaveComplexity,
        int SameFileMarginalCostPercent,
        int SmokeWaveSize,
        int RepresentativesPerCluster,
        bool PreferLowRiskFirst,
        string WaveProfile,
        int TargetWaveCount,
        int RoleOverhead,
        string ExecutionProfile,
        int MaxIdenticalSnapshots,
        string ValidationId,
        int ValidationExitCode,
        string ValidationCommand,
        string ValidationScope,
        bool ForceValidation,
        string ValidationProfile,
        string ValidationProject,
        int ValidationTimeoutSeconds,
        bool ValidationDryRun,
        bool CheckpointOnPass,
        string CheckpointLabel,
        string CheckpointStage)
    {
        public static MigrationOptions? Parse(string[] args, out string error)
        {
            var input = string.Empty;
            var outPath = "migration/plan";
            var workspace = "migration";
            var strategy = "wavefront";
            var format = "both";
            var plan = string.Empty;
            var inventory = string.Empty;
            var wave = string.Empty;
            var config = string.Empty;
            var target = "dotnet";
            var targetTestFramework = string.Empty;
            var generationPolicy = string.Empty;
            var executeMigrate = false;
            var migrateExitCode = -1;
            var maxWaveSize = 12;
            var maxWaveFiles = 4;
            var maxWaveActions = 180;
            var hardWaveActions = 320;
            var maxWaveComplexity = 550;
            var hardWaveComplexity = 850;
            var sameFileMarginalCostPercent = 30;
            var smokeWaveSize = 1;
            var representativesPerCluster = 1;
            var preferLowRiskFirst = true;
            var waveProfile = "auto";
            var targetWaveCount = 0;
            var roleOverhead = 300;
            var executionProfile = "fast";
            var maxIdenticalSnapshots = 3;
            var validationId = "wave-validation";
            var validationExitCode = 0;
            var validationCommand = string.Empty;
            var validationScope = "changed-files";
            var forceValidation = false;
            var validationProfile = "auto";
            var validationProject = string.Empty;
            var validationTimeoutSeconds = 900;
            var validationDryRun = false;
            var checkpointOnPass = true;
            var checkpointLabel = string.Empty;
            var checkpointStage = "migration";
            var profileExplicit = false;
            var budgetExplicit = false;
            error = string.Empty;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                string Next(string option)
                {
                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"{option} requires a value");
                    return args[++i];
                }

                try
                {
                    switch (arg)
                    {
                        case "--input": input = Next(arg); break;
                        case "--out": outPath = Next(arg); break;
                        case "--workspace": workspace = Next(arg); break;
                        case "--strategy": strategy = Next(arg); break;
                        case "--format": format = Next(arg).Trim().ToLowerInvariant(); break;
                        case "--plan": plan = Next(arg); break;
                        case "--inventory": inventory = Next(arg); break;
                        case "--wave": wave = Next(arg); break;
                        case "--config": config = Next(arg); break;
                        case "--target": target = Next(arg); break;
                        case "--target-test-framework": targetTestFramework = Next(arg); break;
                        case "--generation-policy": generationPolicy = Next(arg); break;
                        case "--execute-migrate": executeMigrate = ParseBool(Next(arg), arg); break;
                        case "--migrate-exit-code": migrateExitCode = ParseNonNegativeInt(Next(arg), arg); break;
                        case "--max-wave-size": maxWaveSize = ParsePositiveInt(Next(arg), arg); budgetExplicit = true; break;
                        case "--max-wave-files": maxWaveFiles = ParsePositiveInt(Next(arg), arg); budgetExplicit = true; break;
                        case "--max-wave-actions": maxWaveActions = ParsePositiveInt(Next(arg), arg); budgetExplicit = true; break;
                        case "--hard-wave-actions": hardWaveActions = ParsePositiveInt(Next(arg), arg); budgetExplicit = true; break;
                        case "--max-wave-complexity": maxWaveComplexity = ParsePositiveInt(Next(arg), arg); budgetExplicit = true; break;
                        case "--hard-wave-complexity": hardWaveComplexity = ParsePositiveInt(Next(arg), arg); budgetExplicit = true; break;
                        case "--same-file-marginal-cost": sameFileMarginalCostPercent = ParsePercentage(Next(arg), arg); budgetExplicit = true; break;
                        case "--smoke-wave-size": smokeWaveSize = ParsePositiveInt(Next(arg), arg); break;
                        case "--representatives-per-cluster": representativesPerCluster = ParsePositiveInt(Next(arg), arg); break;
                        case "--prefer-low-risk-first": preferLowRiskFirst = ParseBool(Next(arg), arg); break;
                        case "--wave-profile": waveProfile = Next(arg).Trim().ToLowerInvariant(); profileExplicit = true; break;
                        case "--target-waves": targetWaveCount = ParseNonNegativeInt(Next(arg), arg); break;
                        case "--role-overhead": roleOverhead = ParsePositiveInt(Next(arg), arg); break;
                        case "--execution-profile": executionProfile = MigrationFastPath.NormalizeExecutionProfile(Next(arg)); break;
                        case "--max-identical-snapshots": maxIdenticalSnapshots = ParsePositiveInt(Next(arg), arg); break;
                        case "--validation-id": validationId = Next(arg); break;
                        case "--validation-exit-code": validationExitCode = ParseNonNegativeInt(Next(arg), arg); break;
                        case "--validation-command": validationCommand = Next(arg); break;
                        case "--validation-scope": validationScope = Next(arg); break;
                        case "--force-validation": forceValidation = ParseBool(Next(arg), arg); break;
                        case "--validation-profile": validationProfile = Next(arg).Trim().ToLowerInvariant(); break;
                        case "--validation-project": validationProject = Next(arg); break;
                        case "--validation-timeout-seconds": validationTimeoutSeconds = ParsePositiveInt(Next(arg), arg); break;
                        case "--validation-dry-run": validationDryRun = ParseBool(Next(arg), arg); break;
                        case "--checkpoint-on-pass": checkpointOnPass = ParseBool(Next(arg), arg); break;
                        case "--checkpoint-label": checkpointLabel = Next(arg); break;
                        case "--checkpoint-stage": checkpointStage = Next(arg); break;
                        default:
                            if (!arg.StartsWith("--", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(input))
                                input = arg;
                            else
                                throw new ArgumentException($"Unknown option: {arg}");
                            break;
                    }
                }
                catch (ArgumentException ex)
                {
                    error = ex.Message;
                    return null;
                }
            }

            if (format != "text" && format != "json" && format != "both")
            {
                error = "--format must be text, json, or both.";
                return null;
            }

            if (maxIdenticalSnapshots < 2)
            {
                error = "--max-identical-snapshots must be at least 2.";
                return null;
            }

            validationScope = validationScope.Trim().ToLowerInvariant();
            if (validationScope is not ("changed-files" or "project" or "full" or "artifacts"))
            {
                error = "--validation-scope must be changed-files, project, full, or artifacts.";
                return null;
            }

            if (validationProfile is not ("auto" or "fast" or "standard" or "audit"))
            {
                error = "--validation-profile must be auto, fast, standard, or audit.";
                return null;
            }

            checkpointStage = checkpointStage.Trim().ToLowerInvariant();
            if (checkpointStage is not ("migration" or "validation" or "review" or "final"))
            {
                error = "--checkpoint-stage must be migration, validation, review, or final.";
                return null;
            }

            if (waveProfile is not ("auto" or "manual" or "compact" or "balanced" or "conservative" or "experiment" or "auto-tuned"))
            {
                error = "--wave-profile must be auto, manual, compact, balanced, or conservative.";
                return null;
            }

            // Backward compatibility: callers that explicitly pass old max-wave-* switches are
            // asking for a manual profile unless they explicitly opted into auto tuning.
            if (budgetExplicit && !profileExplicit)
                waveProfile = "manual";

            ApplyNamedWaveProfile(
                waveProfile,
                ref maxWaveSize,
                ref maxWaveFiles,
                ref maxWaveActions,
                ref hardWaveActions,
                ref maxWaveComplexity,
                ref hardWaveComplexity,
                ref sameFileMarginalCostPercent);

            return new MigrationOptions(input, outPath, workspace, strategy, format, plan, inventory, wave, config, target, targetTestFramework, generationPolicy, executeMigrate, migrateExitCode, maxWaveSize, maxWaveFiles, maxWaveActions, hardWaveActions, maxWaveComplexity, hardWaveComplexity, sameFileMarginalCostPercent, smokeWaveSize, representativesPerCluster, preferLowRiskFirst, waveProfile, targetWaveCount, roleOverhead, executionProfile, maxIdenticalSnapshots, validationId, validationExitCode, validationCommand, validationScope, forceValidation, validationProfile, validationProject, validationTimeoutSeconds, validationDryRun, checkpointOnPass, checkpointLabel, checkpointStage);
        }

        static void ApplyNamedWaveProfile(
            string profile,
            ref int maxWaveSize,
            ref int maxWaveFiles,
            ref int maxWaveActions,
            ref int hardWaveActions,
            ref int maxWaveComplexity,
            ref int hardWaveComplexity,
            ref int sameFileMarginalCostPercent)
        {
            switch (profile)
            {
                case "compact":
                    maxWaveSize = 14;
                    maxWaveFiles = 5;
                    maxWaveActions = 180;
                    hardWaveActions = 360;
                    maxWaveComplexity = 650;
                    hardWaveComplexity = 1000;
                    sameFileMarginalCostPercent = 25;
                    break;
                case "balanced":
                    maxWaveSize = 12;
                    maxWaveFiles = 4;
                    maxWaveActions = 150;
                    hardWaveActions = 300;
                    maxWaveComplexity = 550;
                    hardWaveComplexity = 850;
                    sameFileMarginalCostPercent = 30;
                    break;
                case "conservative":
                    maxWaveSize = 8;
                    maxWaveFiles = 3;
                    maxWaveActions = 100;
                    hardWaveActions = 220;
                    maxWaveComplexity = 420;
                    hardWaveComplexity = 700;
                    sameFileMarginalCostPercent = 40;
                    break;
            }
        }

        static int ParseNonNegativeInt(string value, string option)
        {
            if (!int.TryParse(value, out var parsed) || parsed < 0)
                throw new ArgumentException($"{option} requires a non-negative integer");
            return parsed;
        }

        static int ParsePositiveInt(string value, string option)
        {
            if (!int.TryParse(value, out var parsed) || parsed <= 0)
                throw new ArgumentException($"{option} requires a positive integer");
            return parsed;
        }

        static int ParsePercentage(string value, string option)
        {
            if (!int.TryParse(value, out var parsed) || parsed < 0 || parsed > 100)
                throw new ArgumentException($"{option} requires an integer from 0 to 100");
            return parsed;
        }

        static bool ParseBool(string value, string option)
        {
            if (bool.TryParse(value, out var parsed))
                return parsed;
            if (value == "1")
                return true;
            if (value == "0")
                return false;
            throw new ArgumentException($"{option} requires true or false");
        }
    }

    sealed record TestMethodMatch(string MethodName, int Index);
    sealed record TestMetrics(int SeleniumActions, int Assertions, int Waits, int Helpers);

    sealed record MigrationInventoryReport(
        string SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        string InputPath,
        int FilesScanned,
        int TestFiles,
        int TestsFound,
        string[] DistinctTags,
        MigrationTestInventoryItem[] Tests,
        string[] Warnings);

    sealed record MigrationTestInventoryItem(
        string TestId,
        string File,
        string ClassName,
        string MethodName,
        int Line,
        string Cluster,
        string[] Tags,
        string Risk,
        double RepresentativeScore,
        int SeleniumActions,
        int Assertions,
        int Waits,
        int Helpers,
        string[] Reasons);

    sealed record MigrationClusterReport(
        string SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        string InputPath,
        MigrationTestInventoryItem[] Tests,
        MigrationCluster[] Clusters);

    sealed record MigrationCluster(
        string Name,
        int Tests,
        int Files,
        string DominantRisk,
        string[] Tags,
        string[] RepresentativeTests);

    sealed record MigrationWavePlan(
        string SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        string Strategy,
        string InputPath,
        string Workspace,
        int MaxWaveSize,
        int MaxWaveFiles,
        int MaxWaveActions,
        int MaxWaveComplexity,
        int SmokeWaveSize,
        int RepresentativesPerCluster,
        bool PreferLowRiskFirst,
        int TotalTests,
        int TotalClusters,
        MigrationWave[] Waves,
        string WaveProfile = "manual",
        int HardWaveActions = 0,
        int HardWaveComplexity = 0,
        int SameFileMarginalCostPercent = 100);

    sealed record MigrationWave(
        string Id,
        int Index,
        string Phase,
        string Cluster,
        MigrationTestInventoryItem[] Tests,
        string[] Files,
        string DominantRisk,
        int EstimatedActions,
        int EstimatedComplexity,
        int SourceFileCount,
        string BudgetStatus,
        string[] BudgetViolations,
        string Rationale,
        int RawComplexity = 0);

    sealed record WaveTuningReport(
        string SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        string InputPath,
        int Tests,
        int Files,
        int Clusters,
        int TargetWaveCount,
        int RoleOverhead,
        int SearchCandidatesEvaluated,
        WaveTuningCandidate Recommended,
        WaveTuningCandidate[] TopCandidates,
        string Confidence,
        double ScoreGapPercent,
        string[] Notes);

    sealed record WaveTuningCandidate(
        int MaxWaveSize,
        int MaxWaveFiles,
        int MaxWaveActions,
        int HardWaveActions,
        int MaxWaveComplexity,
        int HardWaveComplexity,
        int SameFileMarginalCostPercent,
        int EstimatedWaveCount,
        int NonSmokeSingletons,
        int FileFragmentation,
        int HeavySingleTests,
        double SoftOverrun,
        double LoadImbalance,
        double EstimatedWorkCost,
        double OrchestrationCost,
        double CoordinationRiskCost,
        double Score);

    sealed record MigrationWaveInputScope(
        string SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        string PlanPath,
        string WaveId,
        int WaveIndex,
        string Phase,
        string Cluster,
        string SourceRoot,
        string SourceScopePath,
        string GeneratedOutputPath,
        string? ConfigPath,
        MigrationTestInventoryItem[] Tests,
        string[] Files,
        string[] CopiedFiles,
        string[] MissingFiles);

    sealed record WaveMigrateExecution(string Status, string Message, int ExitCode);
}

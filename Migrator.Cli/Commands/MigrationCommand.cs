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
            "plan" => RunPlan(options),
            "run-wave" => RunWave(options),
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
        options = NormalizeProjectPaths(options, repoRoot, normalizeInput: true);

        if (!string.Equals(options.Strategy, "wavefront", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("migration plan currently supports --strategy wavefront only.");
            return 2;
        }

        if (!ValidateInput(options.Input, out var fullInput))
            return 2;

        var inventory = BuildInventory(fullInput);
        if (inventory.Tests.Length == 0)
        {
            Console.Error.WriteLine($"No Selenium-like test methods found under: {fullInput}");
            return 2;
        }

        var clusters = BuildClusters(inventory);
        var plan = BuildWavePlan(inventory, clusters, options);
        Directory.CreateDirectory(options.Out);
        WriteInventoryArtifacts(options.Out, "json", inventory);
        WriteClusterArtifacts(options.Out, "json", clusters);
        WritePlanArtifacts(options.Out, options.Format, plan);
        WriteMemoryRecallGuide(options.Out, options.Workspace, plan);
        WriteNextCommands(options.Out, options);

        Console.WriteLine("MIGRATION_WAVE_PLAN_READY");
        Console.WriteLine($"Input: {inventory.InputPath}");
        Console.WriteLine($"Tests: {inventory.Tests.Length}");
        Console.WriteLine($"Clusters: {clusters.Clusters.Length}");
        Console.WriteLine($"Waves: {plan.Waves.Length}");
        Console.WriteLine($"Artifacts: {Path.GetFullPath(options.Out)}");
        Console.WriteLine("Next: selenium-pw-migrator migration plan show --plan " + QuoteForShell(options.Out));
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

        var outPath = outWasDefault ? Path.Combine(options.Workspace, "runs", wave.Id) : options.Out;
        Directory.CreateDirectory(outPath);
        var sourceScopeDir = Path.Combine(outPath, "source-scope");
        var generatedDir = Path.Combine(outPath, "generated");
        var evidenceDir = Path.Combine(outPath, "evidence");
        Directory.CreateDirectory(sourceScopeDir);
        Directory.CreateDirectory(generatedDir);
        Directory.CreateDirectory(evidenceDir);

        var sourceRoot = ResolveSourceRoot(plan.InputPath);
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
        File.WriteAllLines(Path.Combine(outPath, "selected-tests.txt"), wave.Tests.Select(t => t.File + "::" + t.TestId));
        WriteWaveConfigDelta(outPath, options.Workspace, wave, options);
        WriteWaveMemoryDelta(outPath, wave, copiedFiles.Count, missingFiles);
        WriteWaveCommands(outPath, sourceScopeDir, generatedDir, options);
        WriteWaveSummary(outPath, wave, scope, options, copiedFiles.Count, missingFiles);

        var execution = options.ExecuteMigrate
            ? TryExecuteMigrate(outPath, sourceScopeDir, generatedDir, options)
            : new WaveMigrateExecution("prepared", "--execute-migrate false; generated/ contains a README placeholder until migrate is run.", 0);
        WriteWaveStatus(outPath, wave, execution, missingFiles);

        Console.WriteLine("MIGRATION_WAVE_RUN_READY");
        Console.WriteLine($"Wave: {wave.Id}");
        Console.WriteLine($"Tests: {wave.Tests.Length}");
        Console.WriteLine($"Files copied: {copiedFiles.Count}");
        if (missingFiles.Count > 0)
            Console.WriteLine($"Missing files: {missingFiles.Count}");
        Console.WriteLine($"Run workspace: {Path.GetFullPath(outPath)}");
        Console.WriteLine($"Generated output: {Path.GetFullPath(generatedDir)}");
        Console.WriteLine("Next: review run-summary.md, then run memory summarize after migrate/review artifacts exist.");
        if (missingFiles.Count > 0)
            return 1;
        if (options.ExecuteMigrate && execution.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
            return execution.ExitCode == 0 ? 1 : execution.ExitCode;
        return 0;
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

    static void WriteWaveCommands(string outPath, string sourceScopeDir, string generatedDir, MigrationOptions options)
    {
        var migrateArgs = BuildMigrateCommand(sourceScopeDir, generatedDir, options);
        File.WriteAllText(Path.Combine(outPath, "run-migrate.sh"), "#!/usr/bin/env bash\nset -euo pipefail\n" + migrateArgs + "\n");
        File.WriteAllText(Path.Combine(outPath, "run-migrate.ps1"), "$ErrorActionPreference = 'Stop'\n" + migrateArgs + "\n");
        File.WriteAllText(Path.Combine(generatedDir, "README.md"), "# Generated output placeholder\n\nRun `../run-migrate.sh` or `../run-migrate.ps1`, or rerun `migration run-wave --execute-migrate true`, to generate this wave.\n");
    }

    static string BuildMigrateCommand(string sourceScopeDir, string generatedDir, MigrationOptions options)
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
        return string.Join(" ", parts);
    }

    static void WriteWaveSummary(string outPath, MigrationWave wave, MigrationWaveInputScope scope, MigrationOptions options, int copiedFiles, IReadOnlyList<string> missingFiles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration wave run");
        sb.AppendLine();
        sb.AppendLine($"Wave: `{wave.Id}`");
        sb.AppendLine($"Phase: `{wave.Phase}`");
        sb.AppendLine($"Cluster: `{wave.Cluster}`");
        sb.AppendLine($"Risk: **{wave.DominantRisk}**");
        sb.AppendLine($"Tests: {wave.Tests.Length}");
        sb.AppendLine($"Files copied: {copiedFiles}");
        sb.AppendLine($"Missing files: {missingFiles.Count}");
        sb.AppendLine();
        sb.AppendLine("## Safety boundary");
        sb.AppendLine();
        sb.AppendLine("- This wave is bounded to `source-scope/` and `generated/`.");
        sb.AppendLine("- `config-delta.json` is observed/reviewable only; do not merge it directly.");
        sb.AppendLine("- `memory-delta.jsonl` is guidance, not authority.");
        sb.AppendLine("- Assertions must not be suppressed.");
        sb.AppendLine("- POM uncertainty must remain reviewable until target mapping exists.");
        sb.AppendLine();
        sb.AppendLine("## Execute migration for this wave");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine(BuildMigrateCommand(scope.SourceScopePath, scope.GeneratedOutputPath, options));
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
        sb.AppendLine("- Run final gate before promoting any wave-local learning.");
        File.WriteAllText(Path.Combine(outPath, "run-summary.md"), sb.ToString());

        var json = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = "migration-wave-run/v1",
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["waveId"] = wave.Id,
            ["phase"] = wave.Phase,
            ["cluster"] = wave.Cluster,
            ["risk"] = wave.DominantRisk,
            ["tests"] = wave.Tests.Length,
            ["filesCopied"] = copiedFiles,
            ["missingFiles"] = missingFiles.ToArray(),
            ["artifacts"] = new[] { "input-scope.json", "config-delta.json", "memory-delta.jsonl", "run-summary.md", "run-migrate.sh", "run-migrate.ps1" }
        };
        File.WriteAllText(Path.Combine(outPath, "run-summary.json"), JsonSerializer.Serialize(json, JsonOptions));
    }

    static WaveMigrateExecution TryExecuteMigrate(string outPath, string sourceScopeDir, string generatedDir, MigrationOptions options)
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
            ["schemaVersion"] = "migration-wave-status/v1",
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["waveId"] = wave.Id,
            ["status"] = missingFiles.Count > 0 ? "incomplete" : execution.Status,
            ["message"] = execution.Message,
            ["migrateExitCode"] = execution.ExitCode,
            ["missingFiles"] = missingFiles.ToArray(),
            ["next"] = new[]
            {
                "Inspect run-summary.md and input-scope.json.",
                "Run run-migrate.sh or run-migrate.ps1 if generated output has not been produced.",
                "Add concrete config-delta entries only with evidence.",
                "Run reviewer/watchdog/final-gate before promoting wave-local learning."
            }
        };
        File.WriteAllText(Path.Combine(outPath, "wave-status.json"), JsonSerializer.Serialize(status, JsonOptions));
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
        var invocations = Regex.Matches(text, @"\b[A-Z][A-Za-z0-9_]*(?:Page|Steps|Helper|Control|Table|Filter)?\.[A-Z][A-Za-z0-9_]*\s*\(");
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

    static MigrationWavePlan BuildWavePlan(MigrationInventoryReport inventory, MigrationClusterReport clusters, MigrationOptions options)
    {
        var maxWaveSize = Math.Max(1, options.MaxWaveSize);
        var repsPerCluster = Math.Max(1, options.RepresentativesPerCluster);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var waves = new List<MigrationWave>();

        var orderedClusters = clusters.Clusters
            .OrderBy(c => options.PreferLowRiskFirst ? RiskWeight(c.DominantRisk) : -RiskWeight(c.DominantRisk))
            .ThenByDescending(c => c.Tests)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var representativeBuffer = new List<MigrationTestInventoryItem>();
        foreach (var cluster in orderedClusters)
        {
            var clusterTests = clusters.Tests
                .Where(t => t.Cluster.Equals(cluster.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.RepresentativeScore)
                .ThenBy(t => t.TestId, StringComparer.OrdinalIgnoreCase)
                .Take(repsPerCluster);
            representativeBuffer.AddRange(clusterTests);
        }

        foreach (var chunk in Chunk(representativeBuffer, maxWaveSize))
            AddWave(waves, used, "representatives", "mixed", chunk);

        foreach (var cluster in orderedClusters)
        {
            var remaining = clusters.Tests
                .Where(t => t.Cluster.Equals(cluster.Name, StringComparison.OrdinalIgnoreCase))
                .Where(t => !used.Contains(t.TestId))
                .OrderByDescending(t => t.RepresentativeScore)
                .ThenBy(t => t.TestId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var chunk in Chunk(remaining, maxWaveSize))
                AddWave(waves, used, "cluster-expansion", cluster.Name, chunk);
        }

        return new MigrationWavePlan(
            SchemaVersion: WavePlanSchema,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Strategy: "wavefront",
            InputPath: inventory.InputPath,
            Workspace: options.Workspace,
            MaxWaveSize: maxWaveSize,
            RepresentativesPerCluster: repsPerCluster,
            PreferLowRiskFirst: options.PreferLowRiskFirst,
            TotalTests: inventory.Tests.Length,
            TotalClusters: clusters.Clusters.Length,
            Waves: waves.ToArray());
    }

    static void AddWave(List<MigrationWave> waves, HashSet<string> used, string phase, string cluster, IReadOnlyList<MigrationTestInventoryItem> tests)
    {
        var unique = tests.Where(t => used.Add(t.TestId)).ToArray();
        if (unique.Length == 0)
            return;

        var index = waves.Count + 1;
        waves.Add(new MigrationWave(
            Id: $"wave-{index:000}",
            Index: index,
            Phase: phase,
            Cluster: cluster,
            Tests: unique,
            Files: unique.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            DominantRisk: SelectDominantRisk(unique.Select(t => t.Risk)),
            Rationale: phase == "representatives"
                ? "Representative wave opens project patterns before scaling the scope."
                : $"Cluster expansion wave for {cluster}."));
    }

    static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
            yield return items.Skip(i).Take(size).ToArray();
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
        sb.AppendLine();
        sb.AppendLine("> This plan is read-only. It does not migrate source files. Run `memory explain` and `memory doctor` before turning any wave into a bounded migration task.");
        sb.AppendLine();
        foreach (var wave in plan.Waves)
        {
            sb.AppendLine($"## {wave.Id}: {wave.Phase} / {wave.Cluster}");
            sb.AppendLine();
            sb.AppendLine($"Risk: **{wave.DominantRisk}**");
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
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
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
  selenium-pw-migrator migration plan --strategy wavefront --input ./SeleniumTests --workspace migration --out migration/plan
  selenium-pw-migrator migration plan show --plan migration/plan
  selenium-pw-migrator migration run-wave --plan migration/plan --wave wave-001 --workspace migration --out migration/runs/wave-001

Planning is read-only. run-wave materializes a bounded source-scope plus config-delta,
memory-delta, run summary, evidence folder, and migrate scripts. It never promotes config or memory automatically.
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
        int MaxWaveSize,
        int RepresentativesPerCluster,
        bool PreferLowRiskFirst)
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
            var maxWaveSize = 8;
            var representativesPerCluster = 1;
            var preferLowRiskFirst = true;
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
                        case "--max-wave-size": maxWaveSize = ParsePositiveInt(Next(arg), arg); break;
                        case "--representatives-per-cluster": representativesPerCluster = ParsePositiveInt(Next(arg), arg); break;
                        case "--prefer-low-risk-first": preferLowRiskFirst = ParseBool(Next(arg), arg); break;
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

            return new MigrationOptions(input, outPath, workspace, strategy, format, plan, inventory, wave, config, target, targetTestFramework, generationPolicy, executeMigrate, maxWaveSize, representativesPerCluster, preferLowRiskFirst);
        }

        static int ParsePositiveInt(string value, string option)
        {
            if (!int.TryParse(value, out var parsed) || parsed <= 0)
                throw new ArgumentException($"{option} requires a positive integer");
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
        int RepresentativesPerCluster,
        bool PreferLowRiskFirst,
        int TotalTests,
        int TotalClusters,
        MigrationWave[] Waves);

    sealed record MigrationWave(
        string Id,
        int Index,
        string Phase,
        string Cluster,
        MigrationTestInventoryItem[] Tests,
        string[] Files,
        string DominantRisk,
        string Rationale);

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

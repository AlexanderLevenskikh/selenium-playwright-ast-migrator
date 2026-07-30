using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Migrator.Lab.Contracts;
using Migrator.Lab.LabApp;
using Migrator.Lab.Reports;

namespace Migrator.Lab.Execution;

public sealed class LabRunCoordinator
{
    readonly ILabProcessRunner processRunner;

    public LabRunCoordinator(ILabProcessRunner? processRunner = null)
    {
        this.processRunner = processRunner ?? new SystemLabProcessRunner();
    }

    public async Task<LabSuiteRunResult> RunAsync(
        LabRunOptions options,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var corpus = ScenarioCatalog.Load(options.CorpusRoot);
        var selected = SelectScenarios(corpus, options);
        var artifactsRoot = Path.GetFullPath(options.ArtifactsRoot);
        Directory.CreateDirectory(artifactsRoot);
        Directory.CreateDirectory(Path.Combine(artifactsRoot, "projects"));
        Directory.CreateDirectory(Path.Combine(artifactsRoot, ".workspaces"));

        await using var app = await LabAppHost.StartAsync(0, cancellationToken).ConfigureAwait(false);
        var projects = new List<LabScenarioRunResult>();
        foreach (var entry in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await RunScenarioAsync(entry, options, artifactsRoot, app, cancellationToken).ConfigureAwait(false);
            projects.Add(result);
        }

        var completedAt = DateTimeOffset.UtcNow;
        var suite = new LabSuiteRunResult
        {
            StartedAtUtc = startedAt,
            CompletedAtUtc = completedAt,
            Suite = options.Suite,
            CorpusRoot = corpus.CorpusRoot,
            ArtifactsRoot = artifactsRoot,
            AppBaseUrl = app.BaseUri.AbsoluteUri,
            Summary = BuildSummary(projects),
            Projects = projects.ToArray()
        };
        LabRunReportWriter.Write(suite);
        return suite;
    }

    static ScenarioCatalogEntry[] SelectScenarios(ScenarioCatalogResult catalog, LabRunOptions options)
    {
        if (catalog.HasErrors)
        {
            var issues = catalog.CatalogIssues
                .Concat(catalog.Entries.SelectMany(entry => entry.Issues))
                .Select(issue => $"{issue.Code}: {issue.Message}");
            throw new LabRunConfigurationException(
                "Lab corpus is invalid:" + Environment.NewLine + string.Join(Environment.NewLine, issues));
        }

        var entries = catalog.Entries
            .Where(entry => entry.Scenario?.Implementation.State == ScenarioImplementationState.Ready)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(options.Tag))
        {
            entries = entries
                .Where(entry => entry.Scenario!.Tags.Contains(options.Tag, StringComparer.OrdinalIgnoreCase))
                .ToArray();
        }

        if (options.ProjectIds.Length > 0)
        {
            var requested = options.ProjectIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var known = catalog.Entries
                .Where(entry => entry.Scenario != null)
                .Select(entry => entry.Scenario!.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = requested.Where(id => !known.Contains(id)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
            if (unknown.Length > 0)
                throw new LabRunConfigurationException($"Unknown lab scenario id(s): {string.Join(", ", unknown)}");

            entries = entries.Where(entry => requested.Contains(entry.Scenario!.Id)).ToArray();
        }

        if (entries.Length == 0)
            throw new LabRunConfigurationException("No READY lab scenarios matched the selected filters.");

        return entries.OrderBy(entry => entry.Scenario!.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    async Task<LabScenarioRunResult> RunScenarioAsync(
        ScenarioCatalogEntry entry,
        LabRunOptions options,
        string artifactsRoot,
        LabAppHost app,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var scenario = entry.Scenario!;
        var scenarioArtifacts = Path.Combine(artifactsRoot, "projects", scenario.Id);
        RecreateDirectory(scenarioArtifacts);
        File.Copy(entry.ScenarioFile, Path.Combine(scenarioArtifacts, "scenario.json"), overwrite: true);

        var workspace = Path.Combine(
            artifactsRoot,
            ".workspaces",
            scenario.Id + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        CopyDeclaredProject(entry.ScenarioDirectory, workspace, scenario.Project.Files);

        var stages = new List<LabStageResult>();
        var issues = new List<string>();
        var sourceSummary = new LabSourceTestSummary { ExpectedPassed = ReadExpectedPassCount(scenario.Oracle.Source) };
        var targetSummary = new LabSourceTestSummary { ExpectedPassed = ReadExpectedPassCount(scenario.Oracle.Target) };
        var migrationSummary = new LabMigrationSummary();
        var projectVerifySummary = new LabProjectVerifySummary();
        var quality = new LabQualityEvaluation();
        var oracle = new LabSemanticOracleSummary();
        string? runtimeArtifactsDirectory = null;
        var initialHash = ScenarioContentHasher.Compute(workspace, scenario.Project.Files);
        var sourceDirectory = Path.Combine(scenarioArtifacts, "source");
        Directory.CreateDirectory(sourceDirectory);
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [scenario.App.BaseUrlEnvironmentVariable] = app.BaseUri.AbsoluteUri,
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
        };

        try
        {
            var entryProject = Path.Combine(workspace, ToPlatformPath(scenario.Project.EntryProject));
            var restore = await RunProcessStageAsync(
                LabRunStage.SourceRestore,
                options.DotNetExecutable,
                new[] { "restore", entryProject, "--nologo" },
                workspace,
                sourceDirectory,
                "restore",
                environment,
                options.CommandTimeout,
                cancellationToken,
                LabProcessClassification.Source).ConfigureAwait(false);
            stages.Add(restore);

            if (restore.Outcome == LabStageOutcome.Passed)
            {
                stages.Add(await RunProcessStageAsync(
                    LabRunStage.SourceBuild,
                    options.DotNetExecutable,
                    new[] { "build", entryProject, "--configuration", options.Configuration, "--no-restore", "--nologo" },
                    workspace,
                    sourceDirectory,
                    "build",
                    environment,
                    options.CommandTimeout,
                    cancellationToken,
                    LabProcessClassification.Source).ConfigureAwait(false));
            }
            else
            {
                stages.Add(Skipped(LabRunStage.SourceBuild, "Skipped because source restore did not pass."));
            }

            if (GetStage(stages, LabRunStage.SourceBuild).Outcome == LabStageOutcome.Passed)
            {
                var trxDirectory = Path.Combine(sourceDirectory, "test-results");
                Directory.CreateDirectory(trxDirectory);
                var test = await RunProcessStageAsync(
                    LabRunStage.SourceTest,
                    options.DotNetExecutable,
                    new[]
                    {
                        "test", entryProject,
                        "--configuration", options.Configuration,
                        "--no-build", "--no-restore", "--nologo",
                        "--logger", "trx;LogFileName=source-tests.trx",
                        "--results-directory", trxDirectory
                    },
                    workspace,
                    sourceDirectory,
                    "test",
                    environment,
                    options.CommandTimeout,
                    cancellationToken,
                    LabProcessClassification.Source).ConfigureAwait(false);

                var trxPath = Path.Combine(trxDirectory, "source-tests.trx");
                sourceSummary = TrxResultReader.Read(trxPath, sourceSummary.ExpectedPassed);
                test = ApplyTestCountContract(test, sourceSummary, "Source");
                stages.Add(test);
            }
            else
            {
                stages.Add(Skipped(LabRunStage.SourceTest, "Skipped because source build did not pass."));
            }

            var migrationInput = Path.Combine(workspace, ".migration-input");
            var migrationDirectory = Path.Combine(scenarioArtifacts, "migration");
            if (GetStage(stages, LabRunStage.SourceTest).Outcome == LabStageOutcome.Passed)
            {
                Directory.CreateDirectory(migrationInput);
                CopyDeclaredProject(workspace, migrationInput, scenario.Source.MigrationFiles);
                var adapterConfigPath = ResolveScenarioAdapterConfigPath(workspace, scenario);
                var commandArguments = options.MigratorCommand.PrefixArguments
                    .Concat(new[]
                    {
                        "run",
                        "--input", migrationInput,
                        "--out", migrationDirectory,
                        "--format", "both",
                        "--source", "selenium-csharp",
                        "--target", "dotnet",
                        "--target-test-framework", "nunit"
                    })
                    .ToList();
                if (adapterConfigPath != null)
                {
                    commandArguments.Add("--config");
                    commandArguments.Add(adapterConfigPath);
                }

                stages.Add(await RunProcessStageAsync(
                    LabRunStage.Migration,
                    options.MigratorCommand.FileName,
                    commandArguments.ToArray(),
                    workspace,
                    scenarioArtifacts,
                    "migration-process",
                    environment,
                    options.CommandTimeout,
                    cancellationToken,
                    LabProcessClassification.Migration).ConfigureAwait(false));
                migrationSummary = LabMigrationArtifactReader.Read(migrationDirectory);
                issues.AddRange(migrationSummary.Issues);
            }
            else
            {
                stages.Add(Skipped(LabRunStage.Migration, "Skipped because source validation did not pass."));
            }

            if (CanContinueAfterMigration(stages, migrationSummary))
            {
                quality = LabQualityEvaluator.Evaluate(scenario, migrationSummary);
                stages.Add(new LabStageResult
                {
                    Stage = LabRunStage.QualityEvaluation,
                    Outcome = quality.Passed ? LabStageOutcome.Passed : LabStageOutcome.Failed,
                    Message = quality.Passed ? "Migration quality budgets passed." : string.Join(" ", quality.Issues)
                });
                issues.AddRange(quality.Issues);

                var projectVerifyDirectory = Path.Combine(scenarioArtifacts, "project-verify");
                var verifyConfigPath = WriteProjectVerifyConfig(workspace, entryProject, scenario, options, scenarioArtifacts);
                var verifyArguments = options.MigratorCommand.PrefixArguments
                    .Concat(new[]
                    {
                        "verify-project",
                        "--input", migrationInput,
                        "--config", verifyConfigPath,
                        "--out", projectVerifyDirectory,
                        "--format", "both",
                        "--source", "selenium-csharp",
                        "--target", "dotnet",
                        "--target-test-framework", "nunit"
                    })
                    .ToArray();
                stages.Add(await RunProcessStageAsync(
                    LabRunStage.ProjectVerify,
                    options.MigratorCommand.FileName,
                    verifyArguments,
                    workspace,
                    scenarioArtifacts,
                    "project-verify-process",
                    environment,
                    options.CommandTimeout,
                    cancellationToken,
                    LabProcessClassification.ProjectVerify).ConfigureAwait(false));
                projectVerifySummary = LabProjectVerifyArtifactReader.Read(projectVerifyDirectory);
                issues.AddRange(projectVerifySummary.Issues);
            }
            else
            {
                stages.Add(Skipped(LabRunStage.QualityEvaluation, "Skipped because migration did not complete."));
                stages.Add(Skipped(LabRunStage.ProjectVerify, "Skipped because migration did not complete."));
            }

            if (GetStage(stages, LabRunStage.ProjectVerify).Outcome == LabStageOutcome.Passed
                && projectVerifySummary.ReportPresent
                && string.Equals(projectVerifySummary.Status, "passed", StringComparison.OrdinalIgnoreCase))
            {
                var targetRoot = Path.Combine(scenarioArtifacts, "target");
                var targetProject = LabTargetProjectBuilder.Prepare(
                    migrationDirectory,
                    targetRoot,
                    ReadScenarioRoute(scenario));
                runtimeArtifactsDirectory = targetProject.RuntimeArtifactsDirectory;
                var targetEnvironment = new Dictionary<string, string?>(environment, StringComparer.OrdinalIgnoreCase)
                {
                    ["MIGRATOR_LAB_TARGET_ROUTE"] = targetProject.Route,
                    ["MIGRATOR_LAB_RUNTIME_ARTIFACTS"] = targetProject.RuntimeArtifactsDirectory
                };

                stages.Add(await RunProcessStageAsync(
                    LabRunStage.TargetBuild,
                    options.DotNetExecutable,
                    new[] { "build", targetProject.ProjectPath, "--configuration", options.Configuration, "--nologo" },
                    targetProject.RootDirectory,
                    targetRoot,
                    "target-build",
                    targetEnvironment,
                    options.CommandTimeout,
                    cancellationToken,
                    LabProcessClassification.Target).ConfigureAwait(false));

                if (GetStage(stages, LabRunStage.TargetBuild).Outcome == LabStageOutcome.Passed)
                {
                    app.ResetObservations();
                    var targetResultsDirectory = Path.Combine(targetRoot, "test-results");
                    Directory.CreateDirectory(targetResultsDirectory);
                    var targetTest = await RunProcessStageAsync(
                        LabRunStage.TargetTest,
                        options.DotNetExecutable,
                        new[]
                        {
                            "test", targetProject.ProjectPath,
                            "--configuration", options.Configuration,
                            "--no-build", "--no-restore", "--nologo",
                            "--logger", "trx;LogFileName=target-tests.trx",
                            "--results-directory", targetResultsDirectory
                        },
                        targetProject.RootDirectory,
                        targetRoot,
                        "target-test",
                        targetEnvironment,
                        options.CommandTimeout,
                        cancellationToken,
                        LabProcessClassification.Target).ConfigureAwait(false);
                    var targetTrxPath = Path.Combine(targetResultsDirectory, "target-tests.trx");
                    targetSummary = TrxResultReader.Read(targetTrxPath, targetSummary.ExpectedPassed);
                    targetTest = ApplyTestCountContract(targetTest, targetSummary, "Target");
                    stages.Add(targetTest);

                    await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
                    var observations = app.SnapshotObservations();
                    File.WriteAllText(
                        Path.Combine(targetRoot, "runtime-observations.json"),
                        JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
                    oracle = LabSemanticOracle.Evaluate(scenario, targetSummary, migrationSummary, projectVerifySummary, observations);
                    stages.Add(new LabStageResult
                    {
                        Stage = LabRunStage.SemanticOracle,
                        Outcome = oracle.Passed ? LabStageOutcome.Passed : LabStageOutcome.Failed,
                        Message = oracle.Passed ? "Semantic oracle passed." : string.Join(" ", oracle.Issues)
                    });
                    issues.AddRange(oracle.Issues);
                }
                else
                {
                    stages.Add(Skipped(LabRunStage.TargetTest, "Skipped because target runtime project did not build."));
                    stages.Add(Skipped(LabRunStage.SemanticOracle, "Skipped because target runtime project did not build."));
                }
            }
            else
            {
                stages.Add(Skipped(LabRunStage.TargetBuild, "Skipped because verify-project did not pass."));
                stages.Add(Skipped(LabRunStage.TargetTest, "Skipped because verify-project did not pass."));
                stages.Add(Skipped(LabRunStage.SemanticOracle, "Skipped because verify-project did not pass."));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException)
        {
            issues.Add($"Lab runner error: {ex.Message}");
            AddMissingFailureStage(stages, ex.Message);
        }

        var sourceContentPreserved = false;
        try
        {
            var finalHash = ScenarioContentHasher.Compute(workspace, scenario.Project.Files);
            sourceContentPreserved = string.Equals(initialHash, finalHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add($"Could not verify source fixture integrity: {ex.Message}");
        }

        if (!sourceContentPreserved)
            issues.Add("The migrator or lab runner modified or removed declared source fixture files.");

        var actualStatus = LabRunStatusPolicy.ClassifyScenario(
            scenario.Expected.Status,
            stages,
            migrationSummary,
            projectVerifySummary,
            quality,
            oracle,
            sourceContentPreserved);
        var result = new LabScenarioRunResult
        {
            Id = scenario.Id,
            ExpectedStatus = scenario.Expected.Status,
            ActualStatus = actualStatus,
            ScenarioDirectory = entry.ScenarioDirectory,
            ArtifactsDirectory = scenarioArtifacts,
            WorkspaceDirectory = workspace,
            RuntimeArtifactsDirectory = runtimeArtifactsDirectory,
            DurationMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            SourceContentPreserved = sourceContentPreserved,
            SourceTests = sourceSummary,
            TargetTests = targetSummary,
            Migration = migrationSummary,
            ProjectVerify = projectVerifySummary,
            Quality = quality,
            Oracle = oracle,
            Stages = stages.ToArray(),
            Issues = issues.Distinct(StringComparer.Ordinal).ToArray()
        };

        if (!options.KeepWorkspaces)
            TryDeleteWorkspace(workspace, issues);

        File.WriteAllText(
            Path.Combine(scenarioArtifacts, "status.txt"),
            ToContractName(actualStatus) + Environment.NewLine);
        return result with { Issues = issues.Distinct(StringComparer.Ordinal).ToArray() };
    }

    async Task<LabStageResult> RunProcessStageAsync(
        LabRunStage stage,
        string fileName,
        string[] arguments,
        string workingDirectory,
        string logDirectory,
        string logPrefix,
        Dictionary<string, string?> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        LabProcessClassification classification)
    {
        var stdout = Path.Combine(logDirectory, logPrefix + ".stdout.log");
        var stderr = Path.Combine(logDirectory, logPrefix + ".stderr.log");
        var result = await processRunner.RunAsync(
            new LabProcessRequest
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                Environment = new Dictionary<string, string?>(environment, StringComparer.OrdinalIgnoreCase),
                StandardOutputPath = stdout,
                StandardErrorPath = stderr,
                Timeout = timeout
            },
            cancellationToken).ConfigureAwait(false);

        var combined = ReadProcessOutput(result);
        var outcome = classification switch
        {
            LabProcessClassification.Source => LabRunStatusPolicy.ClassifySourceProcess(stage, result, combined),
            LabProcessClassification.Migration => ClassifyMigrationProcess(result),
            LabProcessClassification.ProjectVerify => LabRunStatusPolicy.ClassifyProjectVerifyProcess(result, combined),
            LabProcessClassification.Target => LabRunStatusPolicy.ClassifyTargetProcess(stage, result, combined),
            _ => LabStageOutcome.Failed
        };
        return new LabStageResult
        {
            Stage = stage,
            Outcome = outcome,
            ExitCode = result.ExitCode,
            DurationMs = result.DurationMs,
            Command = FormatCommand(fileName, arguments),
            WorkingDirectory = Path.GetFullPath(workingDirectory),
            StandardOutputPath = result.StandardOutputPath,
            StandardErrorPath = result.StandardErrorPath,
            Message = result.FailureMessage
        };
    }

    static LabStageOutcome ClassifyMigrationProcess(LabProcessResult result)
    {
        if (result.TimedOut)
            return LabStageOutcome.TimedOut;
        if (result.StartFailed)
            return LabStageOutcome.InfrastructureFailure;
        return result.ExitCode is 0 or 1
            ? LabStageOutcome.Passed
            : LabStageOutcome.Failed;
    }

    static LabStageResult ApplyTestCountContract(LabStageResult stage, LabSourceTestSummary summary, string label)
    {
        if (stage.Outcome == LabStageOutcome.Passed
            && (summary.Passed != summary.ExpectedPassed || summary.Total != summary.ExpectedPassed))
        {
            return stage with
            {
                Outcome = LabStageOutcome.Failed,
                Message = $"{label} test count mismatch: expected {summary.ExpectedPassed} passing tests, got {summary.Passed}/{summary.Total}."
            };
        }
        return stage;
    }

    static bool CanContinueAfterMigration(IReadOnlyCollection<LabStageResult> stages, LabMigrationSummary migration)
    {
        var stage = GetStage(stages, LabRunStage.Migration);
        return stage.Outcome == LabStageOutcome.Passed
            && migration.MandatoryArtifactsPresent
            && migration.FailedStages.Length == 0
            && !string.Equals(migration.OrchestrationStatus, "Failed", StringComparison.OrdinalIgnoreCase);
    }

    static string WriteProjectVerifyConfig(
        string workspace,
        string entryProject,
        ScenarioSpec scenario,
        LabRunOptions options,
        string scenarioArtifacts)
    {
        var configPath = Path.Combine(scenarioArtifacts, "project-verify-config.json");
        var projectReferences = new[] { Path.GetFullPath(entryProject) }
            .Concat(scenario.Project.References.Select(reference =>
                Path.GetFullPath(Path.Combine(workspace, reference.Replace('/', Path.DirectorySeparatorChar)))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceConfigPath = ResolveScenarioAdapterConfigPath(workspace, scenario);
        var config = sourceConfigPath == null
            ? new JsonObject()
            : JsonNode.Parse(File.ReadAllText(sourceConfigPath)) as JsonObject
              ?? throw new InvalidOperationException($"Scenario adapter config must contain a JSON object: {sourceConfigPath}");

        config["SchemaVersion"] ??= "adapter-config/v1";
        config["SourceProjectName"] ??= "Migrator.Lab." + scenario.Id;
        config["Verification"] = JsonSerializer.SerializeToNode(new
        {
            TargetFramework = "net10.0",
            BaseDirectory = Path.GetFullPath(workspace),
            BuildWorkingDirectory = Path.GetFullPath(workspace),
            ProjectReferences = projectReferences,
            AutoDiscoverNearestProject = false,
            AutoDiscoverProjectReferences = false,
            AutoDiscoverBuildFiles = true,
            AutoDiscoverPackageReferences = false,
            NoRestore = false,
            Configuration = options.Configuration
        });

        File.WriteAllText(configPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        return Path.GetFullPath(configPath);
    }

    static string? ResolveScenarioAdapterConfigPath(string workspace, ScenarioSpec scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario.Source.AdapterConfig))
            return null;

        return Path.GetFullPath(Path.Combine(
            workspace,
            scenario.Source.AdapterConfig.Replace('/', Path.DirectorySeparatorChar)));
    }

    static string ReadScenarioRoute(ScenarioSpec scenario)
    {
        foreach (var page in scenario.App.Pages)
        {
            if (page.ValueKind == JsonValueKind.Object
                && page.TryGetProperty("path", out var path)
                && path.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(path.GetString()))
            {
                return path.GetString()!;
            }
        }
        return "/";
    }

    static string ReadProcessOutput(LabProcessResult result)
    {
        var builder = new StringBuilder();
        foreach (var path in new[] { result.StandardOutputPath, result.StandardErrorPath })
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                builder.AppendLine(File.ReadAllText(path));
        }
        if (!string.IsNullOrWhiteSpace(result.FailureMessage))
            builder.AppendLine(result.FailureMessage);
        return builder.ToString();
    }

    static int ReadExpectedPassCount(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("mustPassTests", out var count)
            && count.TryGetInt32(out var value)
            && value >= 0)
        {
            return value;
        }
        return 0;
    }

    static LabStageResult GetStage(IEnumerable<LabStageResult> stages, LabRunStage stage) =>
        stages.Last(item => item.Stage == stage);

    static LabStageResult Skipped(LabRunStage stage, string message) => new()
    {
        Stage = stage,
        Outcome = LabStageOutcome.Skipped,
        Message = message
    };

    static void AddMissingFailureStage(List<LabStageResult> stages, string message)
    {
        var ordered = new[]
        {
            LabRunStage.Migration,
            LabRunStage.ProjectVerify,
            LabRunStage.TargetBuild,
            LabRunStage.TargetTest,
            LabRunStage.SemanticOracle
        };
        var missing = ordered
            .Where(stage => stages.All(item => item.Stage != stage))
            .Cast<LabRunStage?>()
            .FirstOrDefault();
        if (!missing.HasValue)
            return;

        stages.Add(new LabStageResult
        {
            Stage = missing.Value,
            Outcome = LabStageOutcome.InfrastructureFailure,
            Message = message
        });
    }

    static LabSuiteSummary BuildSummary(IEnumerable<LabScenarioRunResult> projects)
    {
        var items = projects.ToArray();
        return new LabSuiteSummary
        {
            Projects = items.Length,
            Passed = Count(ScenarioStatus.Pass),
            PassedWithWarnings = Count(ScenarioStatus.PassWithWarnings),
            UnsupportedAsExpected = Count(ScenarioStatus.UnsupportedAsExpected),
            Regressions = Count(ScenarioStatus.Regression),
            MigratorFailures = Count(ScenarioStatus.MigratorFailure),
            SourceInvalid = Count(ScenarioStatus.SourceInvalid),
            InfrastructureFailures = Count(ScenarioStatus.InfrastructureFailure),
            NonDeterministic = Count(ScenarioStatus.NonDeterministic)
        };

        int Count(ScenarioStatus status) => items.Count(project => project.ActualStatus == status);
    }

    static void CopyDeclaredProject(string sourceRoot, string destinationRoot, IEnumerable<string> files)
    {
        foreach (var relativePath in files)
        {
            var platformPath = ToPlatformPath(relativePath);
            var source = Path.GetFullPath(Path.Combine(sourceRoot, platformPath));
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, platformPath));
            var destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);
            File.Copy(source, destination, overwrite: true);
        }
    }

    static string ToPlatformPath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
    }

    static void TryDeleteWorkspace(string workspace, List<string> issues)
    {
        try
        {
            if (Directory.Exists(workspace))
                Directory.Delete(workspace, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add($"Workspace cleanup failed: {ex.Message}");
        }
    }

    static string FormatCommand(string fileName, IEnumerable<string> arguments) =>
        string.Join(" ", new[] { Quote(fileName) }.Concat(arguments.Select(Quote)));

    static string Quote(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;

    static string ToContractName<T>(T value) where T : struct, Enum
    {
        var text = value.ToString();
        var builder = new StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            if (index > 0 && char.IsUpper(text[index]))
                builder.Append('_');
            builder.Append(char.ToUpperInvariant(text[index]));
        }
        return builder.ToString();
    }

    enum LabProcessClassification
    {
        Source,
        Migration,
        ProjectVerify,
        Target
    }
}

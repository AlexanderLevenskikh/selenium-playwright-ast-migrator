using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
            var result = await RunScenarioAsync(entry, options, artifactsRoot, app.BaseUri, cancellationToken).ConfigureAwait(false);
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
        Uri appBaseUri,
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
        var sourceSummary = new LabSourceTestSummary { ExpectedPassed = ReadExpectedSourcePassCount(scenario) };
        var migrationSummary = new LabMigrationSummary();
        var initialHash = ScenarioContentHasher.Compute(workspace, scenario.Project.Files);
        var sourceDirectory = Path.Combine(scenarioArtifacts, "source");
        Directory.CreateDirectory(sourceDirectory);
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [scenario.App.BaseUrlEnvironmentVariable] = appBaseUri.AbsoluteUri,
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
                cancellationToken).ConfigureAwait(false);
            stages.Add(restore);

            if (restore.Outcome == LabStageOutcome.Passed)
            {
                var build = await RunProcessStageAsync(
                    LabRunStage.SourceBuild,
                    options.DotNetExecutable,
                    new[] { "build", entryProject, "--configuration", options.Configuration, "--no-restore", "--nologo" },
                    workspace,
                    sourceDirectory,
                    "build",
                    environment,
                    options.CommandTimeout,
                    cancellationToken).ConfigureAwait(false);
                stages.Add(build);
            }
            else
            {
                stages.Add(Skipped(LabRunStage.SourceBuild, "Skipped because source restore did not pass."));
            }

            if (stages.Last(stage => stage.Stage == LabRunStage.SourceBuild).Outcome == LabStageOutcome.Passed)
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
                    cancellationToken).ConfigureAwait(false);

                var trxPath = Path.Combine(trxDirectory, "source-tests.trx");
                sourceSummary = TrxResultReader.Read(trxPath, sourceSummary.ExpectedPassed);
                if (test.Outcome == LabStageOutcome.Passed
                    && (sourceSummary.Passed != sourceSummary.ExpectedPassed
                        || sourceSummary.Total != sourceSummary.ExpectedPassed))
                {
                    test = test with
                    {
                        Outcome = LabStageOutcome.Failed,
                        Message = $"Source test count mismatch: expected {sourceSummary.ExpectedPassed} passing tests, got {sourceSummary.Passed}/{sourceSummary.Total}."
                    };
                }
                stages.Add(test);
            }
            else
            {
                stages.Add(Skipped(LabRunStage.SourceTest, "Skipped because source build did not pass."));
            }

            if (stages.Last(stage => stage.Stage == LabRunStage.SourceTest).Outcome == LabStageOutcome.Passed)
            {
                var migrationInput = Path.Combine(workspace, ".migration-input");
                Directory.CreateDirectory(migrationInput);
                CopyDeclaredProject(workspace, migrationInput, scenario.Source.MigrationFiles);
                var migrationDirectory = Path.Combine(scenarioArtifacts, "migration");
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
                    .ToArray();

                var migration = await RunProcessStageAsync(
                    LabRunStage.Migration,
                    options.MigratorCommand.FileName,
                    commandArguments,
                    workspace,
                    scenarioArtifacts,
                    "migration-process",
                    environment,
                    options.CommandTimeout,
                    cancellationToken,
                    migrationAware: true).ConfigureAwait(false);
                stages.Add(migration);
                migrationSummary = LabMigrationArtifactReader.Read(migrationDirectory);
                issues.AddRange(migrationSummary.Issues);
            }
            else
            {
                stages.Add(Skipped(LabRunStage.Migration, "Skipped because source validation did not pass."));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            issues.Add($"Lab runner error: {ex.Message}");
            if (stages.All(stage => stage.Stage != LabRunStage.Migration))
            {
                stages.Add(new LabStageResult
                {
                    Stage = LabRunStage.Migration,
                    Outcome = LabStageOutcome.InfrastructureFailure,
                    Message = ex.Message
                });
            }
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
            sourceContentPreserved);
        var result = new LabScenarioRunResult
        {
            Id = scenario.Id,
            ExpectedStatus = scenario.Expected.Status,
            ActualStatus = actualStatus,
            ScenarioDirectory = entry.ScenarioDirectory,
            ArtifactsDirectory = scenarioArtifacts,
            WorkspaceDirectory = workspace,
            DurationMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            SourceContentPreserved = sourceContentPreserved,
            SourceTests = sourceSummary,
            Migration = migrationSummary,
            Stages = stages.ToArray(),
            Issues = issues.ToArray()
        };

        if (!options.KeepWorkspaces)
            TryDeleteWorkspace(workspace, issues);

        File.WriteAllText(
            Path.Combine(scenarioArtifacts, "status.txt"),
            ToContractName(actualStatus) + Environment.NewLine);
        return result with { Issues = issues.ToArray() };
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
        bool migrationAware = false)
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
        var outcome = migrationAware
            ? ClassifyMigrationProcess(result)
            : LabRunStatusPolicy.ClassifySourceProcess(stage, result, combined);
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

    static int ReadExpectedSourcePassCount(ScenarioSpec scenario)
    {
        var source = scenario.Oracle.Source;
        if (source.ValueKind == JsonValueKind.Object
            && source.TryGetProperty("mustPassTests", out var count)
            && count.TryGetInt32(out var value)
            && value >= 0)
        {
            return value;
        }
        return 0;
    }

    static LabStageResult Skipped(LabRunStage stage, string message) => new()
    {
        Stage = stage,
        Outcome = LabStageOutcome.Skipped,
        Message = message
    };

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
            var character = text[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(text[index - 1]))
                builder.Append('_');
            builder.Append(char.ToUpperInvariant(character));
        }
        return builder.ToString();
    }
}

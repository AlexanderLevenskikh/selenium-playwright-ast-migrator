using Migrator.Lab.Contracts;
using Migrator.Lab.Execution;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Scenario")]
public sealed class LabRunCoordinatorTests
{
    [Fact]
    public async Task Coordinator_RunsVerticalSliceThroughSourceAndExistingMigrationStages()
    {
        var artifacts = Path.Combine(Path.GetTempPath(), "migrator-lab-run-" + Guid.NewGuid().ToString("N"));
        try
        {
            var coordinator = new LabRunCoordinator(new SuccessfulFakeProcessRunner());
            var result = await coordinator.RunAsync(new LabRunOptions
            {
                CorpusRoot = VerticalSliceRoot(),
                ArtifactsRoot = artifacts,
                ProjectIds = new[] { "p01-basic-id-login" },
                DotNetExecutable = "fake-dotnet",
                MigratorCommand = LabProcessCommand.Create("fake-migrator"),
                CommandTimeout = TimeSpan.FromSeconds(5)
            });

            var project = Assert.Single(result.Projects);
            Assert.Equal(ScenarioStatus.Pass, project.ActualStatus);
            Assert.Equal(1, project.SourceTests.Passed);
            Assert.True(project.SourceContentPreserved);
            Assert.Equal(4, project.Stages.Length);
            Assert.All(project.Stages, stage => Assert.Equal(LabStageOutcome.Passed, stage.Outcome));
            var summaryJsonPath = Path.Combine(artifacts, "lab-summary.json");
            Assert.True(File.Exists(summaryJsonPath));
            Assert.True(File.Exists(Path.Combine(artifacts, "lab-summary.md")));
            var summaryJson = File.ReadAllText(summaryJsonPath);
            Assert.Contains("\"schemaVersion\": \"migrator-lab-run/v1\"", summaryJson);
            Assert.Contains("\"summary\"", summaryJson);
            Assert.Contains("\"actualStatus\": \"PASS\"", summaryJson);
            Assert.True(File.Exists(Path.Combine(project.ArtifactsDirectory, "scenario-result.json")));
            Assert.True(File.Exists(Path.Combine(project.ArtifactsDirectory, "source", "source-validation.json")));
            Assert.False(Directory.Exists(project.WorkspaceDirectory));
        }
        finally
        {
            if (Directory.Exists(artifacts))
                Directory.Delete(artifacts, recursive: true);
        }
    }

    sealed class SuccessfulFakeProcessRunner : ILabProcessRunner
    {
        public Task<LabProcessResult> RunAsync(LabProcessRequest request, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.StandardOutputPath))!);
            File.WriteAllText(request.StandardOutputPath, "fake command passed" + Environment.NewLine);
            File.WriteAllText(request.StandardErrorPath, "");

            if (request.Arguments.Contains("test", StringComparer.Ordinal))
                WriteTrx(request.Arguments);
            if (request.Arguments.Contains("run", StringComparer.Ordinal))
                WriteMigrationArtifacts(request.Arguments);

            return Task.FromResult(new LabProcessResult
            {
                ExitCode = 0,
                DurationMs = 1,
                StandardOutputPath = Path.GetFullPath(request.StandardOutputPath),
                StandardErrorPath = Path.GetFullPath(request.StandardErrorPath)
            });
        }

        static void WriteTrx(string[] arguments)
        {
            var resultsDirectory = ReadOption(arguments, "--results-directory");
            Directory.CreateDirectory(resultsDirectory);
            File.WriteAllText(Path.Combine(resultsDirectory, "source-tests.trx"), """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <ResultSummary outcome="Completed">
                <Counters total="1" executed="1" passed="1" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" notRunnable="0" notExecuted="0" />
              </ResultSummary>
            </TestRun>
            """);
        }

        static void WriteMigrationArtifacts(string[] arguments)
        {
            var output = ReadOption(arguments, "--out");
            Directory.CreateDirectory(Path.Combine(output, "generated"));
            Directory.CreateDirectory(Path.Combine(output, "verify"));
            File.WriteAllText(Path.Combine(output, "orchestration-report.json"), """
            {
              "Status": "Passed",
              "Stages": [
                { "Name": "analyze", "Status": "Passed" },
                { "Name": "migrate", "Status": "Passed" },
                { "Name": "verify", "Status": "Passed" },
                { "Name": "propose", "Status": "Passed" }
              ]
            }
            """);
            File.WriteAllText(Path.Combine(output, "generated", "report.json"), """
            { "UnsupportedActions": 0, "TodoComments": 0, "UnmappedTargets": 0 }
            """);
            File.WriteAllText(Path.Combine(output, "generated", "unsupported-actions.json"), "[]");
            File.WriteAllText(Path.Combine(output, "verify", "verify-report.json"), "{}");
        }

        static string ReadOption(string[] arguments, string option)
        {
            var index = Array.IndexOf(arguments, option);
            Assert.True(index >= 0 && index + 1 < arguments.Length, $"Missing option {option}");
            return arguments[index + 1];
        }
    }

    static string VerticalSliceRoot() => Path.Combine(FindRepositoryRoot(), "corpus", "stable", "vertical-slice");

    static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Migrator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Migrator.sln.");
    }
}

using System.Text;
using System.Text.Json;
using Migrator.Lab.Contracts;
using Migrator.Lab.Execution;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Scenario")]
public sealed class LabRunCoordinatorTests
{
    [Fact]
    public async Task Coordinator_RunsVerticalSliceThroughProjectVerifyRuntimeAndOracle()
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
            Assert.Equal(1, project.TargetTests.Passed);
            Assert.True(project.SourceContentPreserved);
            Assert.True(project.ProjectVerify.ReportPresent);
            Assert.True(project.Quality.Passed);
            Assert.True(project.Oracle.Passed);
            Assert.Equal(9, project.Stages.Length);
            Assert.All(project.Stages, stage => Assert.Equal(LabStageOutcome.Passed, stage.Outcome));
            var summaryJsonPath = Path.Combine(artifacts, "lab-summary.json");
            Assert.True(File.Exists(summaryJsonPath));
            Assert.True(File.Exists(Path.Combine(artifacts, "lab-summary.md")));
            var summaryJson = File.ReadAllText(summaryJsonPath);
            Assert.Contains("\"schemaVersion\": \"migrator-lab-run/v2\"", summaryJson);
            Assert.Contains("\"actualStatus\": \"PASS\"", summaryJson);
            Assert.True(File.Exists(Path.Combine(project.ArtifactsDirectory, "scenario-result.json")));
            Assert.True(File.Exists(Path.Combine(project.ArtifactsDirectory, "source", "source-validation.json")));
            Assert.True(File.Exists(Path.Combine(project.ArtifactsDirectory, "target", "runtime-validation.json")));
            Assert.True(File.Exists(Path.Combine(project.ArtifactsDirectory, "target", "semantic-diff.json")));
            Assert.True(File.Exists(Path.Combine(project.ArtifactsDirectory, "target", "quality-evaluation.json")));
            Assert.False(Directory.Exists(project.WorkspaceDirectory));
        }
        finally
        {
            if (Directory.Exists(artifacts))
                Directory.Delete(artifacts, recursive: true);
        }
    }

    [Fact]
    public async Task Coordinator_PassesScenarioAdapterConfigAndMergesVerificationSettings()
    {
        var artifacts = Path.Combine(Path.GetTempPath(), "migrator-lab-adapter-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            var runner = new SuccessfulFakeProcessRunner();
            var coordinator = new LabRunCoordinator(runner);
            var result = await coordinator.RunAsync(new LabRunOptions
            {
                CorpusRoot = VerticalSliceRoot(),
                ArtifactsRoot = artifacts,
                ProjectIds = new[] { "p09-helper-extension-mapping" },
                DotNetExecutable = "fake-dotnet",
                MigratorCommand = LabProcessCommand.Create("fake-migrator"),
                CommandTimeout = TimeSpan.FromSeconds(5)
            });

            var migrationRequest = Assert.Single(runner.Requests.Where(request => request.Arguments.Contains("run", StringComparer.Ordinal)));
            var configIndex = Array.IndexOf(migrationRequest.Arguments, "--config");
            Assert.True(configIndex >= 0 && configIndex + 1 < migrationRequest.Arguments.Length);
            Assert.Equal("adapter-config.json", Path.GetFileName(migrationRequest.Arguments[configIndex + 1]));

            var project = Assert.Single(result.Projects);
            var configPath = Path.Combine(project.ArtifactsDirectory, "project-verify-config.json");
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            Assert.Equal("Migrator.Lab.P09", document.RootElement.GetProperty("SourceProjectName").GetString());
            Assert.Equal(1, document.RootElement.GetProperty("ParameterizedMethods").GetArrayLength());
            Assert.True(document.RootElement.TryGetProperty("Verification", out var verification));
            Assert.Equal("net10.0", verification.GetProperty("TargetFramework").GetString());
        }
        finally
        {
            if (Directory.Exists(artifacts))
                Directory.Delete(artifacts, recursive: true);
        }
    }

    [Fact]
    public async Task Coordinator_WritesAllDeclaredProjectReferencesDeterministically()
    {
        var artifacts = Path.Combine(Path.GetTempPath(), "migrator-lab-project-refs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var coordinator = new LabRunCoordinator(new SuccessfulFakeProcessRunner());
            var result = await coordinator.RunAsync(new LabRunOptions
            {
                CorpusRoot = VerticalSliceRoot(),
                ArtifactsRoot = artifacts,
                ProjectIds = new[] { "p24a-transitive-warning-isolated" },
                DotNetExecutable = "fake-dotnet",
                MigratorCommand = LabProcessCommand.Create("fake-migrator"),
                CommandTimeout = TimeSpan.FromSeconds(5)
            });

            var project = Assert.Single(result.Projects);
            var configPath = Path.Combine(project.ArtifactsDirectory, "project-verify-config.json");
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var verification = document.RootElement.GetProperty("Verification");
            var references = verification.GetProperty("ProjectReferences")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .Select(Path.GetFileName)
                .ToArray();

            Assert.Contains("Tests.csproj", references, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("A.csproj", references, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("B.csproj", references, StringComparer.OrdinalIgnoreCase);
            Assert.False(verification.GetProperty("AutoDiscoverProjectReferences").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(artifacts))
                Directory.Delete(artifacts, recursive: true);
        }
    }

    sealed class SuccessfulFakeProcessRunner : ILabProcessRunner
    {
        public List<LabProcessRequest> Requests { get; } = new();

        public async Task<LabProcessResult> RunAsync(LabProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.StandardOutputPath))!);
            File.WriteAllText(request.StandardOutputPath, "fake command passed" + Environment.NewLine);
            File.WriteAllText(request.StandardErrorPath, "");

            if (request.Arguments.Contains("verify-project", StringComparer.Ordinal))
            {
                WriteProjectVerifyArtifacts(request.Arguments);
            }
            else if (request.Arguments.Contains("run", StringComparer.Ordinal))
            {
                WriteMigrationArtifacts(request.Arguments);
            }
            else if (request.Arguments.Contains("test", StringComparer.Ordinal))
            {
                var target = ReadOption(request.Arguments, "--logger").Contains("target-tests.trx", StringComparison.Ordinal);
                WriteTrx(request.Arguments, target ? "target-tests.trx" : "source-tests.trx");
                if (target)
                    await PostTargetObservationsAsync(request.Environment, cancellationToken);
            }

            return new LabProcessResult
            {
                ExitCode = 0,
                DurationMs = 1,
                StandardOutputPath = Path.GetFullPath(request.StandardOutputPath),
                StandardErrorPath = Path.GetFullPath(request.StandardErrorPath)
            };
        }

        static void WriteTrx(string[] arguments, string fileName)
        {
            var resultsDirectory = ReadOption(arguments, "--results-directory");
            Directory.CreateDirectory(resultsDirectory);
            File.WriteAllText(Path.Combine(resultsDirectory, fileName), """
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
            File.WriteAllText(Path.Combine(output, "generated", "LoginTestsPlaywright.cs"), """
            using Microsoft.Playwright.NUnit;
            using NUnit.Framework;
            namespace Migrator.Lab.Corpus.P01;
            public class LoginTestsPlaywright : PageTest
            {
                [Test]
                public async Task UserCanLogin()
                {
                    await Page.Locator("#login").ClickAsync();
                }
            }
            """);
            File.WriteAllText(Path.Combine(output, "generated", "report.json"), """
            { "UnsupportedActions": 0, "TodoComments": 0, "UnmappedTargets": 0, "FilesWithWarnings": 0 }
            """);
            File.WriteAllText(Path.Combine(output, "generated", "unsupported-actions.json"), "[]");
            File.WriteAllText(Path.Combine(output, "verify", "verify-report.json"), "{\"summary\":{\"status\":\"passed\"}}");
        }

        static void WriteProjectVerifyArtifacts(string[] arguments)
        {
            var output = ReadOption(arguments, "--out");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "project-verify-report.json"), """
            {
              "Status": "passed",
              "ExitCode": 0,
              "HarnessProject": "fake.csproj",
              "ProjectReferences": [],
              "Diagnostics": [],
              "ClassifiedDiagnostics": [],
              "HarnessEvidence": {
                "SchemaVersion": "verify-project-harness/v1",
                "CentralPackageManagementDetected": false,
                "CentralPackageManagementMode": "not-detected",
                "ManagePackageVersionsCentrallyDisabled": true,
                "DirectoryPackagesPropsPathPinned": true,
                "ImportedBuildFiles": [],
                "SkippedBuildFiles": []
              }
            }
            """);
        }

        static async Task PostTargetObservationsAsync(
            IReadOnlyDictionary<string, string?> environment,
            CancellationToken cancellationToken)
        {
            var baseUrl = environment["MIGRATOR_LAB_APP_URL"]!;
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            await Post("auth:attempt", "");
            await Post("auth:success", "ok");

            async Task Post(string eventName, string resultText)
            {
                var json = JsonSerializer.Serialize(new
                {
                    @event = eventName,
                    path = "/login",
                    dom = new
                    {
                        result = new
                        {
                            text = resultText,
                            value = string.Empty,
                            visible = true,
                            enabled = true,
                            @checked = false
                        }
                    }
                });
                using var response = await client.PostAsync("__lab/events", new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
                response.EnsureSuccessStatusCode();
            }
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

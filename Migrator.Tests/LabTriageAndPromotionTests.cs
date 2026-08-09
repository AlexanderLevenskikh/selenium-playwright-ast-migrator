using System.Text.Json;
using Migrator.Lab;
using Migrator.Lab.Contracts;
using Migrator.Lab.Execution;
using Migrator.Lab.Triage;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public sealed class LabTriageAndPromotionTests
{
    [Fact]
    public void CandidateReducer_KeepsOnlyContractFilesAndDropsBuildNoise()
    {
        var root = TempRoot();
        try
        {
            var source = Path.Combine(root, "candidate");
            CopyDirectory(Path.Combine(StableCorpusRoot(), "p01-basic-id-login"), source);
            Directory.CreateDirectory(Path.Combine(source, "obj"));
            File.WriteAllText(Path.Combine(source, "obj", "noise.tmp"), "noise");
            File.WriteAllText(Path.Combine(source, "notes.txt"), "not part of the scenario contract");

            var output = Path.Combine(root, "reduced");
            var report = new LabCandidateReducer().Reduce(source, output);

            Assert.Equal("p01-basic-id-login", report.ScenarioId);
            Assert.Contains("FindElement", report.RetainedFeatures);
            Assert.Contains("notes.txt", report.RemovedFiles);
            Assert.Contains("obj/noise.tmp", report.RemovedFiles);
            Assert.True(report.BeforeFiles > report.AfterFiles);
            Assert.True(File.Exists(Path.Combine(output, "scenario", "scenario.json")));
            Assert.True(File.Exists(Path.Combine(output, "scenario", "Tests", "LoginTests.cs")));
            Assert.False(File.Exists(Path.Combine(output, "scenario", "notes.txt")));
            Assert.False(Directory.Exists(Path.Combine(output, "scenario", "obj")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Triage_BuildsBoundedTaskPackWithEvidenceCodeReproAndDefinitionOfDone()
    {
        var root = TempRoot();
        try
        {
            var run = CreateSemanticRegression("p01-basic-id-login");
            var taskRoot = Path.Combine(root, "task-packs");
            var report = new LabFailureTriageService().Analyze(
                run,
                Path.Combine(root, "lab-summary.json"),
                StableCorpusRoot(),
                FindRepositoryRoot(),
                taskRoot);

            Assert.Equal(1, report.Summary.Findings);
            Assert.Equal(1, report.Summary.Clusters);
            Assert.Equal(1, report.Summary.TaskPacks);
            var cluster = Assert.Single(report.Clusters);
            Assert.Equal("semantic-oracle", cluster.Stage);
            Assert.Equal(LabRegressionLevel.UnitTest, cluster.RecommendedRegressionLevel);
            Assert.Equal(LabAutomationDisposition.AutoFixEligible, cluster.AutomationDisposition);
            Assert.NotNull(cluster.TaskPackDirectory);

            var taskPath = Path.Combine(cluster.TaskPackDirectory!, "TASK.md");
            var manifestPath = Path.Combine(cluster.TaskPackDirectory!, "task-pack.json");
            Assert.True(File.Exists(taskPath));
            Assert.True(File.Exists(manifestPath));
            Assert.True(File.Exists(Path.Combine(cluster.TaskPackDirectory!, "repro", "scenario.json")));
            var task = File.ReadAllText(taskPath);
            Assert.Contains("Confirmed evidence", task, StringComparison.Ordinal);
            Assert.Contains("Definition of done", task, StringComparison.Ordinal);
            Assert.Contains("Relevant migrator code", task, StringComparison.Ordinal);

            var manifest = JsonSerializer.Deserialize<LabTaskPackManifest>(File.ReadAllText(manifestPath), LabJson.Options)!;
            Assert.InRange(manifest.RelevantMigratorCode.Length, 1, 3);
            Assert.NotEmpty(manifest.RelevantTests);
            Assert.NotEmpty(manifest.DefinitionOfDone);
            Assert.Contains("todo=1", manifest.QualityBaseline, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TriageEvidencePaths_AreConfinedToRunArtifactsRoot()
    {
        var root = TempRoot();
        try
        {
            var artifacts = Path.Combine(root, "artifacts");
            Directory.CreateDirectory(artifacts);
            var inside = Path.Combine(artifacts, "inside.log");
            var outside = Path.Combine(root, "outside.log");
            File.WriteAllText(inside, "inside");
            File.WriteAllText(outside, "outside");

            var project = CreateHealthyProject("p01-basic-id-login") with
            {
                Stages = new[]
                {
                    new LabStageResult
                    {
                        Stage = LabRunStage.TargetTest,
                        Outcome = LabStageOutcome.Failed,
                        StandardOutputPath = inside,
                        StandardErrorPath = outside
                    }
                }
            };

            var evidence = LabFailureTriageService.DetermineRawEvidencePaths(project, artifacts);

            Assert.Equal(new[] { Path.GetFullPath(inside) }, evidence);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Triage_RetainsRawEvidenceAndUsesEvidenceBackedCodeBeforeHeuristics()
    {
        var root = TempRoot();
        try
        {
            var scenarioId = "p01-basic-id-login";
            var projectArtifacts = Path.Combine(root, "projects", scenarioId);
            Directory.CreateDirectory(projectArtifacts);
            var stderrPath = Path.Combine(projectArtifacts, "target-test.stderr.log");
            File.WriteAllText(
                stderrPath,
                "stack: at Migrator.SeleniumCSharp\\DefaultProjectAdapter.cs:line 42");

            var original = CreateSemanticRegression(scenarioId);
            var project = original.Projects[0] with
            {
                Stages = new[]
                {
                    new LabStageResult
                    {
                        Stage = LabRunStage.TargetTest,
                        Outcome = LabStageOutcome.Passed,
                        StandardErrorPath = stderrPath
                    }
                }
            };
            var run = original with
            {
                ArtifactsRoot = root,
                Projects = new[] { project }
            };
            var taskRoot = Path.Combine(root, "task-packs");

            var report = new LabFailureTriageService().Analyze(
                run,
                Path.Combine(root, "lab-summary.json"),
                StableCorpusRoot(),
                FindRepositoryRoot(),
                taskRoot);

            var finding = Assert.Single(report.Findings);
            Assert.Contains(
                "Migrator.SeleniumCSharp/DefaultProjectAdapter.cs",
                finding.EvidenceBackedComponents);
            Assert.Equal(LabAutomationDisposition.ManualReview, finding.AutomationDisposition);

            var cluster = Assert.Single(report.Clusters);
            Assert.Equal(LabAutomationDisposition.ManualReview, cluster.AutomationDisposition);
            var manifestPath = Path.Combine(cluster.TaskPackDirectory!, "task-pack.json");
            var manifest = JsonSerializer.Deserialize<LabTaskPackManifest>(File.ReadAllText(manifestPath), LabJson.Options)!;

            var evidenceArtifact = Assert.Single(manifest.EvidenceArtifacts);
            Assert.True(File.Exists(Path.Combine(cluster.TaskPackDirectory!, evidenceArtifact.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Contains("Migrator.SeleniumCSharp/DefaultProjectAdapter.cs", manifest.EvidenceBackedMigratorCode);
            Assert.Contains("Migrator.SeleniumCSharp/DefaultProjectAdapter.cs", manifest.RelevantMigratorCode);
            Assert.InRange(manifest.RelevantMigratorCode.Length, 1, 3);

            var task = File.ReadAllText(Path.Combine(cluster.TaskPackDirectory!, "TASK.md"));
            Assert.Contains("Retained evidence artifacts", task, StringComparison.Ordinal);
            Assert.Contains("referenced by retained evidence", task, StringComparison.Ordinal);
            Assert.Contains("triage hints, not proof", task, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Triage_ClustersEquivalentFindingsByStageDiagnosticSemanticAndFeatureSignature()
    {
        var root = TempRoot();
        try
        {
            var corpus = Path.Combine(root, "corpus");
            foreach (var id in new[] { "cluster-a", "cluster-b" })
            {
                var destination = Path.Combine(corpus, id);
                CopyDirectory(Path.Combine(StableCorpusRoot(), "p01-basic-id-login"), destination);
                var scenarioPath = Path.Combine(destination, "scenario.json");
                var json = File.ReadAllText(scenarioPath)
                    .Replace("\"id\": \"p01-basic-id-login\"", $"\"id\": \"{id}\"", StringComparison.Ordinal);
                File.WriteAllText(scenarioPath, json);
            }

            LabScenarioRunResult Regressed(string id) => CreateHealthyProject(id) with
            {
                ActualStatus = ScenarioStatus.Regression,
                ProjectVerify = new LabProjectVerifySummary { ReportPresent = true, Status = "passed" },
                Quality = new LabQualityEvaluation
                {
                    Passed = false,
                    TodoActual = 1,
                    Issues = new[] { "same quality regression" }
                }
            };

            var report = new LabFailureTriageService().Analyze(
                CreateRun(Regressed("cluster-a"), Regressed("cluster-b")),
                Path.Combine(root, "run"),
                corpus,
                FindRepositoryRoot());

            Assert.Equal(2, report.Summary.Findings);
            Assert.Equal(1, report.Summary.Clusters);
            var cluster = Assert.Single(report.Clusters);
            Assert.Equal(new[] { "cluster-a", "cluster-b" }, cluster.ScenarioIds);
            Assert.Contains("QUALITY_BUDGET", cluster.DiagnosticCodes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Triage_ProjectTopologyFailure_RecommendsProjectFixture()
    {
        var root = TempRoot();
        try
        {
            var project = CreateHealthyProject("p23-cpm-isolation") with
            {
                ActualStatus = ScenarioStatus.Regression,
                ProjectVerify = new LabProjectVerifySummary
                {
                    ReportPresent = true,
                    Status = "failed",
                    DiagnosticCategories = new[] { "missing-type-or-namespace" }
                },
                Stages = new[]
                {
                    new LabStageResult { Stage = LabRunStage.ProjectVerify, Outcome = LabStageOutcome.Failed }
                }
            };
            var run = CreateRun(project);

            var report = new LabFailureTriageService().Analyze(
                run,
                Path.Combine(root, "run"),
                StableCorpusRoot(),
                FindRepositoryRoot());

            var cluster = Assert.Single(report.Clusters);
            Assert.Equal("project-verify", cluster.Stage);
            Assert.Equal(LabRegressionLevel.ProjectFixture, cluster.RecommendedRegressionLevel);
            Assert.Contains(cluster.SuspectedComponents, path => path.Contains("LabTargetProjectBuilder", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Promotion_CreatesReviewedSavedSeedArtifactWithoutMutatingStableCorpus()
    {
        var root = TempRoot();
        try
        {
            var sourceScenario = Path.Combine(StableCorpusRoot(), "p01-basic-id-login");
            var before = ScenarioContentHasher.Compute(
                sourceScenario,
                ScenarioSpecLoader.Load(Path.Combine(sourceScenario, "scenario.json")).Scenario!.Project.Files);

            var manifest = new LabRegressionPromotionService().Promote(
                sourceScenario,
                LabRegressionLevel.SavedSeed,
                Path.Combine(root, "promoted"));

            Assert.Equal(LabRegressionLevel.SavedSeed, manifest.Level);
            Assert.True(File.Exists(Path.Combine(manifest.DestinationDirectory, "promotion.json")));
            Assert.True(File.Exists(Path.Combine(manifest.DestinationDirectory, "scenario", "scenario.json")));
            Assert.Contains(manifest.NextVerificationSteps, step => step.Contains("corpus/seeds", StringComparison.Ordinal));

            var after = ScenarioContentHasher.Compute(
                sourceScenario,
                ScenarioSpecLoader.Load(Path.Combine(sourceScenario, "scenario.json")).Scenario!.Project.Files);
            Assert.Equal(before, after);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReleaseGate_RequiresTrustedContractBaselineAndRetainedRealProjectEvidence()
    {
        var root = TempRoot();
        try
        {
            var now = new DateTimeOffset(2026, 8, 7, 18, 0, 0, TimeSpan.Zero);
            var retainedArtifact = Path.Combine(root, "real-project-report.json");
            File.WriteAllText(retainedArtifact, "{\"status\":\"PASS\"}");
            var evidencePath = Path.Combine(root, "real-project-evidence.json");
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new LabRealProjectEvidence
            {
                Project = "real-release-probe",
                SourceRevision = "source-abc",
                MigratorRevision = "migrator-def",
                ExecutedAtUtc = now.AddDays(-2),
                Status = "PASS",
                EvidencePaths = new[] { retainedArtifact }
            }, LabJson.Options));

            var tempCorpus = Path.Combine(root, "corpus");
            var tempScenario = Path.Combine(tempCorpus, "p01-basic-id-login");
            CopyDirectory(Path.Combine(StableCorpusRoot(), "p01-basic-id-login"), tempScenario);
            var stable = CreateRun(CreateHealthyProject("p01-basic-id-login")) with { CorpusRoot = tempCorpus };
            var trustedBaseline = LabBaselineService.Create(stable, "trusted-main");
            var service = new LabReleaseGateService();
            var pass = service.Evaluate(
                stable,
                Path.Combine(root, "stable"),
                trustedBaseline,
                Path.Combine(root, "trusted-baseline"),
                evidencePath,
                maxAgeDays: 14,
                now: now);
            Assert.True(pass.Passed, string.Join(Environment.NewLine, pass.Issues));
            Assert.Equal(0, pass.StableContractChanges);
            Assert.Equal(1, pass.VerifiedEvidenceArtifacts);

            var scenarioPath = Path.Combine(tempScenario, "scenario.json");
            var originalScenario = File.ReadAllText(scenarioPath);
            File.WriteAllText(scenarioPath, originalScenario.Replace("\"todoMax\": 0", "\"todoMax\": 1", StringComparison.Ordinal));
            var changedAfterRun = service.Evaluate(
                stable,
                Path.Combine(root, "stable"),
                trustedBaseline,
                Path.Combine(root, "trusted-baseline"),
                evidencePath,
                maxAgeDays: 14,
                now: now);
            Assert.False(changedAfterRun.Passed);
            Assert.Equal(1, changedAfterRun.StableContractChanges);
            Assert.Contains(changedAfterRun.Issues, issue => issue.Contains("after the recorded run", StringComparison.OrdinalIgnoreCase));
            File.WriteAllText(scenarioPath, originalScenario);

            var changedContract = stable with
            {
                Projects = new[] { stable.Projects[0] with { ContractHash = "sha256:" + new string('b', 64) } }
            };
            var tampered = service.Evaluate(
                changedContract,
                Path.Combine(root, "stable"),
                trustedBaseline,
                Path.Combine(root, "trusted-baseline"),
                evidencePath,
                maxAgeDays: 14,
                now: now);
            Assert.False(tampered.Passed);
            Assert.Equal(1, tampered.StableContractChanges);
            Assert.Contains(tampered.Issues, issue => issue.Contains("contract changed", StringComparison.OrdinalIgnoreCase));

            File.Delete(retainedArtifact);
            var missingArtifact = service.Evaluate(
                stable,
                Path.Combine(root, "stable"),
                trustedBaseline,
                Path.Combine(root, "trusted-baseline"),
                evidencePath,
                maxAgeDays: 14,
                now: now);
            Assert.False(missingArtifact.Passed);
            Assert.Equal(0, missingArtifact.VerifiedEvidenceArtifacts);
            Assert.Contains(missingArtifact.Issues, issue => issue.Contains("does not exist", StringComparison.OrdinalIgnoreCase));

            File.WriteAllText(retainedArtifact, "");
            var emptyArtifact = service.Evaluate(
                stable,
                Path.Combine(root, "stable"),
                trustedBaseline,
                Path.Combine(root, "trusted-baseline"),
                evidencePath,
                maxAgeDays: 14,
                now: now);
            Assert.False(emptyArtifact.Passed);
            Assert.Equal(0, emptyArtifact.VerifiedEvidenceArtifacts);
            Assert.Contains(emptyArtifact.Issues, issue => issue.Contains("is empty", StringComparison.OrdinalIgnoreCase));

            File.WriteAllText(retainedArtifact, "{\"status\":\"PASS\"}");
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new LabRealProjectEvidence
            {
                Project = "real-release-probe",
                SourceRevision = "source-abc",
                MigratorRevision = "migrator-def",
                ExecutedAtUtc = now.AddDays(-30),
                Status = "PASS",
                EvidencePaths = new[] { retainedArtifact }
            }, LabJson.Options));
            var stale = service.Evaluate(
                stable,
                Path.Combine(root, "stable"),
                trustedBaseline,
                Path.Combine(root, "trusted-baseline"),
                evidencePath,
                maxAgeDays: 14,
                now: now);
            Assert.False(stale.Passed);
            Assert.Contains(stale.Issues, issue => issue.Contains("stale", StringComparison.OrdinalIgnoreCase));

            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new LabRealProjectEvidence
            {
                Project = "real-release-probe",
                SourceRevision = "source-abc",
                MigratorRevision = "migrator-def",
                ExecutedAtUtc = now.AddDays(-1),
                Status = "PASS",
                EvidencePaths = new[] { retainedArtifact }
            }, LabJson.Options));
            var brokenStable = stable with
            {
                Projects = new[] { stable.Projects[0] with { ActualStatus = ScenarioStatus.Regression } }
            };
            var failedStable = service.Evaluate(
                brokenStable,
                Path.Combine(root, "stable"),
                trustedBaseline,
                Path.Combine(root, "trusted-baseline"),
                evidencePath,
                maxAgeDays: 14,
                now: now);
            Assert.False(failedStable.Passed);
            Assert.Equal(1, failedStable.StableUnexpectedOutcomes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static LabSuiteRunResult CreateSemanticRegression(string id)
    {
        var project = CreateHealthyProject(id) with
        {
            ActualStatus = ScenarioStatus.Regression,
            Quality = new LabQualityEvaluation
            {
                Passed = false,
                TodoActual = 1,
                TodoMax = 0,
                Issues = new[] { "Quality budget exceeded: TODO comments = 1, maximum = 0." }
            },
            Oracle = new LabSemanticOracleSummary
            {
                Passed = false,
                Checks = new[]
                {
                    new LabSemanticCheck
                    {
                        Kind = "event-sequence",
                        Expected = "auth:attempt -> auth:success",
                        Actual = "auth:success",
                        Passed = false
                    }
                },
                Issues = new[] { "Semantic oracle failed (event-sequence)." }
            },
            ProjectVerify = new LabProjectVerifySummary { ReportPresent = true, Status = "passed" }
        };
        return CreateRun(project);
    }

    static LabScenarioRunResult CreateHealthyProject(string id) => new()
    {
        Id = id,
        ExpectedStatus = ScenarioStatus.Pass,
        ActualStatus = ScenarioStatus.Pass,
        ContractHash = ContractHashFor(id),
        SourceTests = new LabSourceTestSummary { Passed = 1, ExpectedPassed = 1, Total = 1 },
        TargetTests = new LabSourceTestSummary { Passed = 1, ExpectedPassed = 1, Total = 1 },
        ProjectVerify = new LabProjectVerifySummary { ReportPresent = true, Status = "passed" },
        Quality = new LabQualityEvaluation { Passed = true },
        Oracle = new LabSemanticOracleSummary { Passed = true }
    };

    static LabSuiteRunResult CreateRun(params LabScenarioRunResult[] projects) => new()
    {
        Suite = "test",
        CorpusRoot = StableCorpusRoot(),
        ArtifactsRoot = "artifacts/test",
        Summary = new LabSuiteSummary { Projects = projects.Length },
        Projects = projects
    };

    static string StableCorpusRoot() => Path.Combine(FindRepositoryRoot(), "corpus", "stable", "vertical-slice");

    static string ContractHashFor(string id)
    {
        var scenarioFile = Path.Combine(StableCorpusRoot(), id, "scenario.json");
        return File.Exists(scenarioFile)
            ? ScenarioContractHasher.ComputeFile(scenarioFile)
            : "sha256:" + new string('a', 64);
    }

    static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-lab-triage-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

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

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}

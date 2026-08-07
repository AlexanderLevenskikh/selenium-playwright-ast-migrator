using System.Text.Json;
using Migrator.Lab;
using Migrator.Lab.Contracts;
using Migrator.Lab.Generator;
using Migrator.Lab.Reports;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public sealed class LabSeededGenerationTests
{

    [Fact]
    public void ScenarioSpec_DefaultOptionalOracleSections_AreOmittedDuringSerialization()
    {
        var scenario = new ScenarioSpec
        {
            Id = "serialization-probe",
            Oracle = new ScenarioOracleSpec
            {
                Source = JsonDocument.Parse("{\"mustPassTests\":1}").RootElement.Clone(),
                Target = JsonDocument.Parse("{\"mustPassTests\":1}").RootElement.Clone(),
                Semantic = JsonDocument.Parse("{}").RootElement.Clone()
            }
        };

        var json = JsonSerializer.Serialize(scenario, LabJson.Options);

        Assert.DoesNotContain("\"diagnostics\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mustNot\"", json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("serialization-probe", document.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public void SameSeed_ProducesSamePairwiseCorpusAndEveryRequestedMetamorphicShape()
    {
        var root = TempRoot();
        try
        {
            var firstRoot = Path.Combine(root, "first");
            var secondRoot = Path.Combine(root, "second");
            var generator = new SeededVariantGenerator();
            var options = new SeededVariantGenerationOptions
            {
                CorpusRoot = StableCorpusRoot(),
                BaseScenarioId = "p01-basic-id-login",
                Seed = 73001,
                Count = 6
            };

            var first = generator.Generate(options with { OutputRoot = firstRoot });
            var second = generator.Generate(options with { OutputRoot = secondRoot });

            Assert.Equal(first.CorpusFingerprint, second.CorpusFingerprint);
            Assert.Equal(6, first.Variants.Length);
            Assert.Equal(first.Variants.Select(item => item.ContentHash), second.Variants.Select(item => item.ContentHash));
            Assert.True(SeededVariantGenerator.CoversEveryPair(SeededVariantGenerator.BuildPairwiseRows(73001, 6)));

            var catalog = ScenarioCatalog.Load(firstRoot);
            Assert.False(catalog.HasErrors, string.Join(Environment.NewLine, catalog.Entries.SelectMany(entry => entry.Issues).Select(issue => $"{issue.Code}: {issue.Message}")));
            Assert.Equal(6, catalog.ReadyCount);
            Assert.All(catalog.Entries, entry =>
            {
                Assert.True(entry.Scenario!.Id.StartsWith("p30-s73001-v", StringComparison.Ordinal));
                Assert.Equal(ScenarioStatus.Pass, entry.Scenario.Expected.Status);
                Assert.Contains("generated", entry.Scenario.Tags);
                Assert.Contains("metamorphic", entry.Scenario.Tags);
                Assert.Equal(
                    entry.Scenario.Implementation.ContentHash,
                    ScenarioContentHasher.Compute(entry.ScenarioDirectory, entry.Scenario.Project.Files));
            });

            Assert.Contains(first.Variants, item => item.Dimensions["local-name"] == "renamed");
            Assert.Contains(first.Variants, item => item.Dimensions["element-declaration"] == "explicit");
            Assert.Contains(first.Variants, item => item.Dimensions["namespace-shape"] == "block");
            Assert.Contains(first.Variants, item => item.Dimensions["file-layout"] == "specs");
            Assert.Contains(first.Variants, item => item.Dimensions["by-reference"] == "alias");

            var renamed = ReadMigrationSource(firstRoot, first.Variants.First(item => item.Dimensions["local-name"] == "renamed"));
            Assert.Contains("statusElement", renamed, StringComparison.Ordinal);
            var explicitType = ReadMigrationSource(firstRoot, first.Variants.First(item => item.Dimensions["element-declaration"] == "explicit"));
            Assert.Contains("IWebElement", explicitType, StringComparison.Ordinal);
            var blockNamespace = ReadMigrationSource(firstRoot, first.Variants.First(item => item.Dimensions["namespace-shape"] == "block"));
            Assert.Contains("namespace Migrator.Lab.Corpus.P01\n{", blockNamespace, StringComparison.Ordinal);
            var alias = ReadMigrationSource(firstRoot, first.Variants.First(item => item.Dimensions["by-reference"] == "alias"));
            Assert.Contains("using SeleniumBy = OpenQA.Selenium.By;", alias, StringComparison.Ordinal);
            Assert.Contains("SeleniumBy.Id", alias, StringComparison.Ordinal);
            var movedVariant = first.Variants.First(item => item.Dimensions["file-layout"] == "specs");
            var movedScenario = catalog.Entries.Single(entry => entry.Scenario!.Id == movedVariant.Id).Scenario!;
            Assert.True(Assert.Single(movedScenario.Source.MigrationFiles).StartsWith("Specs/", StringComparison.Ordinal));

            Assert.False(string.IsNullOrWhiteSpace(first.Environment.FrameworkDescription));
            Assert.False(string.IsNullOrWhiteSpace(first.Environment.OsDescription));
            Assert.True(File.Exists(Path.Combine(firstRoot, "generation-manifest.json")));
            Assert.True(File.Exists(Path.Combine(firstRoot, "generation-manifest.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DifferentSeed_ChangesGeneratedCorpusFingerprintButRemainsPairwise()
    {
        var root = TempRoot();
        try
        {
            var generator = new SeededVariantGenerator();
            var first = generator.Generate(new SeededVariantGenerationOptions
            {
                CorpusRoot = StableCorpusRoot(),
                OutputRoot = Path.Combine(root, "a"),
                Seed = 73001,
                Count = 6
            });
            var second = generator.Generate(new SeededVariantGenerationOptions
            {
                CorpusRoot = StableCorpusRoot(),
                OutputRoot = Path.Combine(root, "b"),
                Seed = 73002,
                Count = 6
            });

            Assert.NotEqual(first.CorpusFingerprint, second.CorpusFingerprint);
            Assert.True(SeededVariantGenerator.CoversEveryPair(SeededVariantGenerator.BuildPairwiseRows(73002, 6)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MetamorphicAnalyzer_SavesUsefulFailingSeedAsRegressionCandidate()
    {
        var root = TempRoot();
        try
        {
            var corpus = Path.Combine(root, "generated");
            var manifest = new SeededVariantGenerator().Generate(new SeededVariantGenerationOptions
            {
                CorpusRoot = StableCorpusRoot(),
                OutputRoot = corpus,
                Seed = 73003,
                Count = 6
            });
            var manifestPath = Path.Combine(corpus, "generation-manifest.json");
            var cleanRun = CreateRun(root, manifest);
            var candidates = Path.Combine(root, "candidates");

            var clean = new LabMetamorphicAnalyzer().Analyze(manifestPath, cleanRun, candidates);
            Assert.Equal(0, clean.Summary.Regressions);
            Assert.Equal(6, clean.Summary.Passed);
            Assert.Equal(0, clean.Summary.SavedCandidates);

            var failingId = manifest.Variants[0].Id;
            var regressedProjects = cleanRun.Projects
                .Select(project => project.Id == failingId
                    ? project with
                    {
                        ActualStatus = ScenarioStatus.Regression,
                        Quality = project.Quality with { Passed = false, TodoActual = 1 },
                        Issues = new[] { "synthetic metamorphic regression" }
                    }
                    : project)
                .ToArray();
            var regressedRun = cleanRun with { Projects = regressedProjects };

            var report = new LabMetamorphicAnalyzer().Analyze(manifestPath, regressedRun, candidates);
            Assert.Equal(1, report.Summary.Regressions);
            Assert.Equal(1, report.Summary.SavedCandidates);
            var failing = Assert.Single(report.Variants, item => item.Id == failingId);
            Assert.False(failing.Passed);
            Assert.NotNull(failing.CandidateDirectory);
            Assert.True(File.Exists(Path.Combine(failing.CandidateDirectory!, "candidate.json")));
            Assert.True(File.Exists(Path.Combine(failing.CandidateDirectory!, "scenario", "scenario.json")));

            using var candidate = JsonDocument.Parse(File.ReadAllText(Path.Combine(failing.CandidateDirectory!, "candidate.json")));
            Assert.Equal("saved-seed", candidate.RootElement.GetProperty("recommendedRegressionLevel").GetString());
            Assert.Equal(failingId, candidate.RootElement.GetProperty("scenarioId").GetString());
            Assert.Equal(SeededVariantGenerator.GeneratorVersion, candidate.RootElement.GetProperty("generatorVersion").GetString());
            Assert.Equal("p01-basic-id-login", candidate.RootElement.GetProperty("baseScenarioId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(candidate.RootElement.GetProperty("environment").GetProperty("frameworkDescription").GetString()));

            var reportRoot = Path.Combine(root, "report");
            LabMetamorphicReportWriter.Write(report, reportRoot);
            Assert.True(File.Exists(Path.Combine(reportRoot, "lab-metamorphic.json")));
            Assert.True(File.Exists(Path.Combine(reportRoot, "lab-metamorphic.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static LabSuiteRunResult CreateRun(string root, LabGenerationManifest manifest)
    {
        var projects = manifest.Variants.Select(variant => new LabScenarioRunResult
        {
            Id = variant.Id,
            ExpectedStatus = variant.ExpectedStatus,
            ActualStatus = variant.ExpectedStatus,
            ScenarioDirectory = Path.Combine(root, "generated", variant.Directory),
            ArtifactsDirectory = Path.Combine(root, "run", "projects", variant.Id),
            SourceTests = new LabSourceTestSummary { Passed = 1, ExpectedPassed = 1, Total = 1 },
            TargetTests = new LabSourceTestSummary { Passed = 1, ExpectedPassed = 1, Total = 1 },
            Migration = new LabMigrationSummary
            {
                TodoComments = 0,
                UnmappedTargets = 0,
                UnsupportedActions = 0,
                Warnings = 0
            },
            ProjectVerify = new LabProjectVerifySummary
            {
                ReportPresent = true,
                Status = "passed",
                DiagnosticCategories = Array.Empty<string>()
            },
            Quality = new LabQualityEvaluation
            {
                Passed = true,
                TodoActual = 0,
                UnmappedActual = 0,
                UnsupportedActual = 0,
                WarningsActual = 0
            },
            Oracle = new LabSemanticOracleSummary { Passed = true }
        }).ToArray();

        return new LabSuiteRunResult
        {
            Suite = "generated",
            CorpusRoot = Path.Combine(root, "generated"),
            ArtifactsRoot = Path.Combine(root, "run"),
            Summary = new LabSuiteSummary { Projects = projects.Length, Passed = projects.Length },
            Projects = projects
        };
    }

    static string ReadMigrationSource(string corpusRoot, LabGeneratedVariant variant)
    {
        var entry = ScenarioCatalog.Load(corpusRoot).Entries.Single(item => item.Scenario!.Id == variant.Id);
        var migrationFile = Assert.Single(entry.Scenario!.Source.MigrationFiles);
        return File.ReadAllText(Path.Combine(entry.ScenarioDirectory, migrationFile.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    static string StableCorpusRoot() => Path.Combine(FindRepositoryRoot(), "corpus", "stable", "vertical-slice");

    static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-lab-seed-tests-" + Guid.NewGuid().ToString("N"));
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
}

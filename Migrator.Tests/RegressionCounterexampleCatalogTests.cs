using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Migrator.Core;
using Migrator.Core.Coverage;
using Migrator.PlaywrightDotNet;
using Migrator.Roslyn;
using Migrator.SeleniumCSharp;
using Xunit;

namespace Migrator.Tests;

/// <summary>
/// Data-driven guard over corpus/regression-counterexamples/counterexamples.json.
///
/// Every entry is a minimal counterexample with a documented invariant (property) and an honest
/// current verdict (mustProduce + mustContain/mustNotContain on the generated target code).
/// This test fails when a migration change stops honoring the invariant — or when a known gap
/// is silently closed. Changing a verdict is therefore an explicit, reviewed decision, never an
/// incidental side effect.
/// </summary>
public sealed class RegressionCounterexampleCatalogTests
{
    readonly string _catalogDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "RegressionCounterexamples");
    readonly string _testFilesDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "TestFiles");

    [Fact]
    public void Catalog_IsWellFormed()
    {
        var catalog = LoadCatalog();
        Assert.Equal("migrator-counterexamples/v1", catalog.SchemaVersion);
        Assert.NotEmpty(catalog.Counterexamples);

        var ids = catalog.Counterexamples.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        foreach (var entry in catalog.Counterexamples)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.DefectClass), $"{entry.Id}: defectClass required");
            Assert.False(string.IsNullOrWhiteSpace(entry.Property), $"{entry.Id}: property required");
            Assert.True(entry.Status is "guarded" or "known-gap", $"{entry.Id}: bad status");
            Assert.True(entry.Verdict.MustProduce is "transformed" or "partial" or "requires_review" or "unsupported" or "ambiguous",
                $"{entry.Id}: bad mustProduce");
            var sourcePath = Path.Combine(_catalogDir, "samples", entry.SourceFile);
            Assert.True(File.Exists(sourcePath), $"{entry.Id}: missing source file {sourcePath}");
        }
    }

    [Theory]
    [MemberData(nameof(CounterexampleEntries))]
    public void CatalogEntry_MigrationKeepsDocumentedVerdict(string id, string sourceFile, string mustProduce, string[] mustContain, string[] mustNotContain)
    {
        var sourcePath = Path.Combine(_catalogDir, "samples", sourceFile);
        var adapterConfig = Path.Combine(_testFilesDir, "adapter-config.json");
        var adapter = new DefaultProjectAdapter(adapterConfig);
        var parser = new RoslynTestFileParser();
        var renderer = new PlaywrightDotNetRenderer();
        var pipeline = new MigrationPipeline(parser, renderer, adapter);

        var result = pipeline.ProcessFile(sourcePath);

        var report = CoverageReportBuilder.Build(
            new[] { result },
            Path.GetDirectoryName(sourcePath)!,
            backendId: "playwright-dotnet");

        var testStates = report.FilesDetail.SelectMany(f => f.Tests).Select(t => t.State).ToArray();
        Assert.NotEmpty(testStates);
        Assert.True(
            testStates.Contains(mustProduce, StringComparer.Ordinal),
            $"[{id}] expected test state '{mustProduce}', got [{string.Join(", ", testStates)}]. " +
            "The migration contract changed: update the verdict in counterexamples.json only after deliberate review.");

        foreach (var needle in mustContain)
        {
            Assert.True(
                result.GeneratedOutput.Contains(needle, StringComparison.Ordinal),
                $"[{id}] expected generated output to contain '{needle}', but it did not.");
        }

        foreach (var forbidden in mustNotContain)
        {
            Assert.False(
                result.GeneratedOutput.Contains(forbidden, StringComparison.Ordinal),
                $"[{id}] expected generated output NOT to contain '{forbidden}', but it did.");
        }
    }

    public static IEnumerable<object[]> CounterexampleEntries() =>
        LoadCatalogStatic().Counterexamples.Select(c => new object[]
        {
            c.Id,
            c.SourceFile,
            c.Verdict.MustProduce,
            c.Verdict.MustContain.ToArray(),
            c.Verdict.MustNotContain.ToArray()
        });

    Catalog LoadCatalog() => LoadCatalogStatic();

    static Catalog LoadCatalogStatic()
    {
        var root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var path = Path.Combine(root, "RegressionCounterexamples", "counterexamples.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Catalog>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    sealed class Catalog
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public List<Counterexample> Counterexamples { get; set; } = new();
    }

    sealed class Counterexample
    {
        public string Id { get; set; } = string.Empty;
        public string DefectClass { get; set; } = string.Empty;
        public string Property { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
        public Verdict Verdict { get; set; } = new();
    }

    sealed class Verdict
    {
        public string MustProduce { get; set; } = string.Empty;
        public List<string> MustContain { get; set; } = new();
        public List<string> MustNotContain { get; set; } = new();
    }
}

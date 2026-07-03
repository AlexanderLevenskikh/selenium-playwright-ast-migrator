using System.Text.Json;
using Migrator.Core;
using Xunit;

namespace Migrator.Tests;

public sealed class DoctorFixModeTests : IDisposable
{
    readonly string _root;
    readonly string _source;
    readonly string _workspace;

    public DoctorFixModeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"doctor-fix-test-{Guid.NewGuid():N}");
        _source = Path.Combine(_root, "OldTests");
        _workspace = Path.Combine(_root, "migration", "doctor-fix");
        Directory.CreateDirectory(_source);
        File.WriteAllText(Path.Combine(_source, "SamplePage.cs"), "public sealed class SamplePage { }");
        File.WriteAllText(Path.Combine(_source, "SampleTests.cs"), "using NUnit.Framework; public class SampleTests { [Test] public void Smoke() {} }");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Fact]
    public void DryRun_PlansSafeWorkspaceAndConfigFixesWithoutWritingProjectFiles()
    {
        var plan = new DoctorFixPlanner(new DoctorFixOptions
        {
            InputPath = _source,
            WorkspacePath = _workspace,
            TargetTestFramework = "xunit",
            Apply = false,
            DryRun = true
        }).BuildAndMaybeApply();

        Assert.Equal("planned", plan.Status);
        Assert.True(plan.DryRun);
        Assert.Contains(plan.Actions, action => action.Id == "create-starter-adapter-config");
        Assert.Contains(plan.Actions, action => action.Id == "create-workspace-gitignore");
        Assert.Contains(plan.ManualRecommendations, item => item.Contains("never edits Selenium source tests", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(_workspace, "profiles", "adapter-config.json")));
        Assert.False(File.Exists(Path.Combine(_workspace, ".gitignore")));
    }

    [Fact]
    public void Apply_CreatesStarterWorkspaceConfigAndGitIgnoreInsideWorkspace()
    {
        var plan = new DoctorFixPlanner(new DoctorFixOptions
        {
            InputPath = _source,
            WorkspacePath = _workspace,
            TargetTestFramework = "xunit",
            Apply = true
        }).BuildAndMaybeApply();

        var configPath = Path.Combine(_workspace, "profiles", "adapter-config.json");
        Assert.Equal("applied", plan.Status);
        Assert.Contains(configPath, plan.AppliedFiles);
        Assert.True(File.Exists(configPath));
        Assert.True(File.Exists(Path.Combine(_workspace, ".gitignore")));

        var config = ConfigValidator.ValidateJson(File.ReadAllText(configPath), configPath);
        Assert.Equal("xunit", config.TestHost?.TargetTestFramework);
        Assert.Equal("net10.0", config.Verification?.TargetFramework);
        Assert.False(config.Verification?.DisableDefaultPackageReferences);
    }

    [Fact]
    public void Apply_WritesDoctorNewConfigCandidateWithoutOverwritingOriginal()
    {
        var configPath = Path.Combine(_root, "adapter-config.json");
        var original = """
{
  "SourceProjectName": "Demo",
  "UiTargets": [],
  "PageObjects": [],
  "Methods": []
}
""";
        File.WriteAllText(configPath, original);

        var plan = new DoctorFixPlanner(new DoctorFixOptions
        {
            InputPath = _source,
            WorkspacePath = _workspace,
            ConfigPaths = new[] { configPath },
            TargetTestFramework = "nunit",
            Apply = true
        }).BuildAndMaybeApply();

        var candidatePath = configPath + ".doctor.new";
        Assert.Contains(candidatePath, plan.AppliedFiles);
        Assert.Equal(original, File.ReadAllText(configPath));
        Assert.True(File.Exists(candidatePath));

        using var doc = JsonDocument.Parse(File.ReadAllText(candidatePath));
        var root = doc.RootElement;
        Assert.Equal("adapter-config/v1", root.GetProperty("SchemaVersion").GetString());
        Assert.Equal("./adapter-config.schema.json", root.GetProperty("$schema").GetString());
        Assert.Equal("nunit", root.GetProperty("TestHost").GetProperty("TargetTestFramework").GetString());
        Assert.True(root.GetProperty("Verification").GetProperty("AutoDiscoverNearestProject").GetBoolean());
    }

    [Fact]
    public void WriteArtifacts_EmitsPlanPatchAndReport()
    {
        var outPath = Path.Combine(_root, "doctor-fix-artifacts");
        var plan = new DoctorFixPlanner(new DoctorFixOptions
        {
            InputPath = _source,
            WorkspacePath = _workspace,
            Apply = false,
            DryRun = true
        }).BuildAndMaybeApply();

        DoctorFixPlanner.WriteArtifacts(plan, outPath, "both");

        Assert.True(File.Exists(Path.Combine(outPath, "doctor-fix-plan.md")));
        Assert.True(File.Exists(Path.Combine(outPath, "doctor-fix-plan.json")));
        Assert.True(File.Exists(Path.Combine(outPath, "doctor-fix.patch")));
        Assert.True(File.Exists(Path.Combine(outPath, "doctor-fix-report.md")));
        Assert.Contains("Dry-run", File.ReadAllText(Path.Combine(outPath, "doctor-fix-report.md")));
    }
}

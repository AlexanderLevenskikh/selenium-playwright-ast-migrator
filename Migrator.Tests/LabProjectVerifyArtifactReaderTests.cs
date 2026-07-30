using Migrator.Lab.Execution;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public sealed class LabProjectVerifyArtifactReaderTests
{
    [Fact]
    public void Reader_ExtractsDiagnosticsReferencesAndHarnessEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-lab-project-verify-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "project-verify-report.json"), """
            {
              "Status": "passed",
              "ExitCode": 0,
              "HarnessProject": "project-verify/Generated.Playwright.Verify.csproj",
              "ProjectReferences": ["A/A.csproj", "B/B.csproj"],
              "Diagnostics": ["warning NU1701"],
              "ClassifiedDiagnostics": [
                { "Category": "nuget-restore" },
                { "Category": "nuget-restore" }
              ],
              "HarnessEvidence": {
                "SchemaVersion": "verify-project-harness/v1",
                "CentralPackageManagementDetected": true,
                "CentralPackageManagementMode": "isolated",
                "ManagePackageVersionsCentrallyDisabled": true,
                "DirectoryPackagesPropsPathPinned": true,
                "ImportedBuildFiles": [],
                "SkippedBuildFiles": ["Directory.Packages.props"],
                "HarnessProjectSnapshot": "project-verify-harness.csproj"
              }
            }
            """);

            var result = LabProjectVerifyArtifactReader.Read(root);

            Assert.True(result.ReportPresent);
            Assert.Equal("passed", result.Status);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(2, result.ProjectReferences.Length);
            Assert.Single(result.DiagnosticCategories);
            Assert.True(result.Harness.CentralPackageManagementDetected);
            Assert.Equal("isolated", result.Harness.CentralPackageManagementMode);
            Assert.True(result.Harness.ManagePackageVersionsCentrallyDisabled);
            Assert.True(result.Harness.DirectoryPackagesPropsPathPinned);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Reader_MissingReportIsExplicitlyIncomplete()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-lab-project-verify-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var result = LabProjectVerifyArtifactReader.Read(root);
            Assert.False(result.ReportPresent);
            Assert.Contains(result.Issues, issue => issue.Contains("missing", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

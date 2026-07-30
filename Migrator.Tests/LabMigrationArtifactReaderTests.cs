using Migrator.Lab.Execution;
using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public sealed class LabMigrationArtifactReaderTests
{
    [Fact]
    public void Reader_RequiresRunVerifyAndGeneratedArtifactsAndExtractsMetrics()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-lab-artifacts-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "generated"));
            Directory.CreateDirectory(Path.Combine(root, "verify"));
            File.WriteAllText(Path.Combine(root, "orchestration-report.json"), """
            {
              "Status": "PassedWithWarnings",
              "Stages": [
                { "Name": "analyze", "Status": "Passed" },
                { "Name": "migrate", "Status": "Passed" },
                { "Name": "verify", "Status": "PassedWithWarnings" },
                { "Name": "propose", "Status": "Failed" }
              ]
            }
            """);
            File.WriteAllText(Path.Combine(root, "generated", "report.json"), """
            { "UnsupportedActions": 2, "TodoComments": 3, "UnmappedTargets": 4, "FilesWithWarnings": 5 }
            """);
            File.WriteAllText(Path.Combine(root, "generated", "unsupported-actions.json"), "[{}, {}]");
            File.WriteAllText(Path.Combine(root, "verify", "verify-report.json"), "{\"summary\":{\"status\":\"passed_with_warnings\"}}");
            File.WriteAllText(Path.Combine(root, "generated", "ExamplePlaywright.cs"), "public class ExamplePlaywright {}");

            var result = LabMigrationArtifactReader.Read(root);

            Assert.True(result.MandatoryArtifactsPresent);
            Assert.Equal("PassedWithWarnings", result.OrchestrationStatus);
            Assert.Equal(2, result.UnsupportedActions);
            Assert.Equal(3, result.TodoComments);
            Assert.Equal(4, result.UnmappedTargets);
            Assert.Equal(5, result.Warnings);
            Assert.Equal("passed_with_warnings", result.VerifyStatus);
            Assert.Single(result.GeneratedFiles);
            Assert.Empty(result.FailedStages); // propose is intentionally non-critical
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Reader_MissingVerifyReportIsNotACompletedMigration()
    {
        var root = Path.Combine(Path.GetTempPath(), "migrator-lab-artifacts-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "generated"));
            File.WriteAllText(Path.Combine(root, "orchestration-report.json"), "{\"Status\":\"Passed\",\"Stages\":[]}");

            var result = LabMigrationArtifactReader.Read(root);

            Assert.False(result.MandatoryArtifactsPresent);
            Assert.Contains(result.Issues, issue => issue.Contains("verify/verify-report.json", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}

using Xunit;

namespace Migrator.Tests;

public class FrameworkMatrixExpansionTests
{
    [Fact]
    public void FrameworkMatrixCommand_WritesSourceFrameworkDetectionAndReadinessReports()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/FrameworkMatrixCommand.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));

        Assert.Contains("RunFrameworkMatrix", command);
        Assert.Contains("framework-matrix/v2", command);
        Assert.Contains("source-framework-detection/v1", command);
        Assert.Contains("framework-matrix.md", command);
        Assert.Contains("framework-matrix.json", command);
        Assert.Contains("source-framework-detection.md", command);
        Assert.Contains("source-framework-detection.json", command);
        Assert.Contains("C# NUnit/xUnit/MSTest", catalog);
        Assert.Contains("StableCommand(\"framework-matrix\"", catalog);
        Assert.Contains("FrameworkMatrixCommand.RunFrameworkMatrix", program);
        Assert.Contains("\"framework-matrix\"", program);
        Assert.Contains("framework matrix", program);
    }

    [Fact]
    public void FrameworkMatrixExpansion_LabelsMSTestJavaAndPythonAsDetectedUnsupportedOrPlanned()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/FrameworkMatrixCommand.cs"));
        var docs = File.ReadAllText(FindRepositoryFile("docs/framework-matrix.md"));

        Assert.Contains("mstest", command);
        Assert.Contains("detected-unsupported", command);
        Assert.Contains("MSTest is detected but target MSTest output is unsupported", command);
        Assert.Contains("junit4", command);
        Assert.Contains("junit5", command);
        Assert.Contains("testng", command);
        Assert.Contains("pytest", command);
        Assert.Contains("unittest", command);
        Assert.Contains("playwright-java", command);
        Assert.Contains("playwright-python", command);
        Assert.Contains("planned", command);
        Assert.Contains("detected/unsupported", docs);
        Assert.Contains("Java JUnit 4/JUnit 5/TestNG", docs);
        Assert.Contains("Python pytest/unittest", docs);
    }

    [Fact]
    public void FrameworkMatrixDocs_DescribeGeneratedReportsAndWizardTargetSelection()
    {
        var docs = File.ReadAllText(FindRepositoryFile("docs/framework-matrix.md"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var toolReadme = File.ReadAllText(FindRepositoryFile("Migrator.Cli/README_TOOL.md"));

        Assert.Contains("selenium-pw-migrator framework matrix", docs);
        Assert.Contains("source framework detection reports", docs);
        Assert.Contains("framework-matrix.md/json", docs);
        Assert.Contains("source-framework-detection.md/json", docs);
        Assert.Contains("Wizard target framework selection", docs);
        Assert.Contains("read-only", docs);
        Assert.Contains("generated readiness reports", readme);
        Assert.Contains("framework matrix --input", toolReadme);
    }

    static string FindRepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}

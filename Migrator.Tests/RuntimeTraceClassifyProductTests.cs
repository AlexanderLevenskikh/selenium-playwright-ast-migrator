namespace Migrator.Tests;

public class RuntimeTraceClassifyProductTests
{
    [Fact]
    public void RuntimeClassifier_IsTraceAwareAndUsesPublicRuntimeCategories()
    {
        var source = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/RuntimeFailureClassifierCommand.cs"));
        var models = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Models/CliReportModels.cs"));

        Assert.Contains("CollectRuntimeTraceArtifacts", source);
        Assert.Contains("ZipLooksLikePlaywrightTrace", source);
        Assert.Contains("playwright-trace-zip", source);
        Assert.Contains("screenshot", source);
        Assert.Contains("video", source);
        Assert.Contains("console-network-log", source);
        Assert.Contains("runtime-classification.md", source);
        Assert.Contains("runtime-classification.json", source);
        Assert.Contains("runtime-next-tickets.md", source);

        Assert.Contains("locator-not-found", source);
        Assert.Contains("strict-mode-violation", source);
        Assert.Contains("timeout-wait-state", source);
        Assert.Contains("assertion-mismatch", source);
        Assert.Contains("navigation-route-missing", source);
        Assert.Contains("auth/session-not-ready", source);
        Assert.Contains("test-data-missing", source);
        Assert.Contains("modal/dialog-state", source);
        Assert.Contains("frame/shadow-dom", source);
        Assert.Contains("environment/flaky-infra", source);

        Assert.Contains("RuntimeTraceArtifact", models);
        Assert.Contains("RuntimeContextLink", models);
        Assert.Contains("LikelyOwner", models);
        Assert.Contains("TraceArtifacts", models);
        Assert.Contains("ContextLinks", models);
    }

    [Fact]
    public void RuntimeClassifier_DoesNotTreatTraceZipAsTextLogAndDegradesGracefully()
    {
        var source = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/RuntimeFailureClassifierCommand.cs"));

        Assert.Contains("IsRuntimeTextCandidate(inputPath)", source);
        Assert.Contains("ZipFile.OpenRead(file)", source);
        Assert.Contains("catch", source);
        Assert.Contains("return false;", source);
        Assert.Contains("Binary trace/media files are indexed", File.ReadAllText(FindRepositoryFile("docs/runtime-failure-classifier.md")));
    }

    [Fact]
    public void RuntimeClassifier_LinksRuntimeFailuresToGeneratedAndSourceContextWhenAvailable()
    {
        var source = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/RuntimeFailureClassifierCommand.cs"));
        var docs = File.ReadAllText(FindRepositoryFile("docs/runtime-failure-classifier.md"));

        Assert.Contains("BuildRuntimeContextLinks", source);
        Assert.Contains("ExtractStackFrame", source);
        Assert.Contains("FindGeneratedFileForObservation", source);
        Assert.Contains("FindSourceContextForObservation", source);
        Assert.Contains("Generated/source context links", source);
        Assert.Contains("Generated/source context links", docs);
        Assert.Contains("source migration context", docs);
    }

    [Fact]
    public void ReportServe_RuntimeFailureReaderUnderstandsLikelyOwner()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));

        Assert.Contains("LikelyOwner: ReadString(group, \"LikelyOwner\") ?? \"manual triage\"", program);
        Assert.Contains("group.LikelyOwner", program);
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

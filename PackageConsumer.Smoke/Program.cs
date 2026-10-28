using Migrator.Application;

if (ContractVersions.Analysis != "migrator-analysis/v1alpha1")
    throw new InvalidOperationException("Unexpected public API version.");

var temp = Path.Combine(Path.GetTempPath(), "migrator-nuget-consumer-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    File.WriteAllText(Path.Combine(temp, "SmokeTests.cs"), """
        using NUnit.Framework;
        [TestFixture] public class PackageSmoke
        {
            [Test] public void OneTest() {}
        }
        """);
    var engine = new MigrationEngine();
    var analysis = engine.Analyze(temp);
    if (analysis.DiscoveryStatus != "complete" || analysis.DeclaredTests != 1 || analysis.ParsedTests != 1)
        throw new InvalidOperationException("NuGet consumer could not analyze the fixture.");
    var preview = engine.Preview(temp, expectedAnalysisId: analysis.AnalysisId);
    if (preview.Files.Count != 1 || string.IsNullOrWhiteSpace(preview.Files[0].Content))
        throw new InvalidOperationException("NuGet consumer could not render the preview.");
    Console.WriteLine("Independent Core NuGet consumer passed.");
}
finally { Directory.Delete(temp, recursive: true); }

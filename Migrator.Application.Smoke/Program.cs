using Migrator.Application;
var work = Path.Combine(Path.GetTempPath(), "migrator-app-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
try
{
    var multi = Path.Combine(work, "Multiple.cs");
    File.WriteAllText(multi, """
        using NUnit.Framework;
        [TestFixture] public class First {
            [Test] public void A() {}
        }
        [TestFixture] public class Second {
            [Test] public void B() {}
        }
        """);
    var engine = new MigrationEngine();
    var first = engine.Analyze(work);
    Require(first.DiscoveryStatus == "partial", "multi fixture must be partial");
    Require(first.DeclaredTests == 2, "must count both test declarations");
    Require(first.Files.Single().Disposition == "multiple_fixtures", "must reject lossy generation");
    var firstExplanation = engine.Explain(work, first.Findings.Single().Id, expectedAnalysisId: first.AnalysisId);
    Require(firstExplanation.Code == "MULTIPLE_FIXTURES", "finding explanation code");
    Require(firstExplanation.AnalysisId == first.AnalysisId, "explanation must match source snapshot");
    Require(firstExplanation.SuggestedNextAction.Contains("Split fixtures"), "missing next action");
    try
    {
        engine.Explain(work, "unknown-id", expectedAnalysisId: first.AnalysisId);
        throw new Exception("unknown finding was accepted");
    }
    catch (KeyNotFoundException ex) when (ex.Message.StartsWith("UNKNOWN_FINDING", StringComparison.Ordinal)) {}

    Require(engine.Preview(work).Files.Count == 0, "blocked files cannot generate outputs");
    File.Delete(multi);
    var xunit = Path.Combine(work, "Xunit.cs");
    File.WriteAllText(xunit, """
        using Xunit;
        public class X { [Fact] public void FactTest() {} }
        """);
    var second = engine.Analyze(work);
    Require(second.DiscoveryStatus == "partial" && second.DeclaredTests == 1, "xunit counted but blocked");
    Require(second.Findings.Any(f => f.Code == "UNSUPPORTED_SOURCE_FRAMEWORK"), "missing framework finding");
    File.Delete(xunit);
    var nunit = Path.Combine(work, "Nunit.cs");
    File.WriteAllText(nunit, """
        using NUnit.Framework;
        [TestFixture] public class SimpleFixture {
            [Test] public void SimpleTest() {}
        }
        """);
    var third = engine.Analyze(work);
    Require(third.DiscoveryStatus == "complete" && third.ParsedTests == 1, "single NUnit test must parse");
    var preview = engine.Preview(work, expectedAnalysisId: third.AnalysisId);
    Require(preview.Files.Count == 1 && preview.TargetTreeHash.Length == 64, "expected one target");
    Require(!string.IsNullOrWhiteSpace(preview.Files.Single().Content), "rendered output missing");
    File.AppendAllText(nunit, "\n// changed");
    try
    {
        engine.Preview(work, expectedAnalysisId: third.AnalysisId);
        throw new Exception("stale analysis was accepted");
    }
    catch (InvalidOperationException ex) when (ex.Message.StartsWith("STALE_ANALYSIS", StringComparison.Ordinal)) {}
    Console.WriteLine("Core alpha smoke passed.");
}
finally { Directory.Delete(work, recursive: true); }
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("Smoke failed: " + message);
}

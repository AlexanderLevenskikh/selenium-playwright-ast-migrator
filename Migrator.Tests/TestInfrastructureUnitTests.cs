using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Unit")]
public class TestInfrastructureUnitTests
{
    [Fact]
    public void CliTestRunner_TokenizesQuotedArgumentsWithoutShellRoundTrip()
    {
        var tokens = CliTestRunner.Tokenize("migration validate --out \"path with spaces/run\" --force true");

        Assert.Equal(new[]
        {
            "migration", "validate", "--out", "path with spaces/run", "--force", "true"
        }, tokens);
    }

    [Fact]
    public void OrchestratorScenarioCache_InvalidatesWhenInputContentChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), $"scenario-cache-unit-{Guid.NewGuid():N}");
        var input = Path.Combine(root, "input");
        var output1 = Path.Combine(root, "out-1");
        var output2 = Path.Combine(root, "out-2");
        Directory.CreateDirectory(input);
        var source = Path.Combine(input, "Sample.cs");
        File.WriteAllText(source, "class First {}\n");
        var executions = 0;

        CliResult Execute(string output)
        {
            executions++;
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "result.txt"), File.ReadAllText(source));
            return new CliResult(0, "ok", "");
        }

        try
        {
            var first = OrchestratorScenarioCache.Materialize(input, output1, null, Execute);
            File.WriteAllText(source, "class Second {}\n");
            var second = OrchestratorScenarioCache.Materialize(input, output2, null, Execute);

            Assert.Equal(2, executions);
            Assert.NotEqual(first.ScenarioKey, second.ScenarioKey);
            Assert.Contains("Second", File.ReadAllText(Path.Combine(output2, "result.txt")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

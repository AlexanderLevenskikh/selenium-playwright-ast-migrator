using System.Text.Json;

namespace Migrator.Tests;

[Collection("CliProcess")]
[Trait("Shard", "Cli")]
public class PomIndexTargetPomTests
{
    [Fact]
    public void IndexPom_PlaywrightKonturTargetPom_ExtractsReviewableSelectorFacts()
    {
        var temp = CreateTempDir();
        try
        {
            var pomDir = Path.Combine(temp, "TargetPom");
            Directory.CreateDirectory(pomDir);
            File.WriteAllText(Path.Combine(pomDir, "RowCostSidePage.cs"), """
namespace Demo.TargetPom;

public class RowCostSidePage
{
    public Button Save => ControlFactory.Create<Button>(this, "save-row-cost");

    public ElementsCollection<RowCostRow> Rows =>
        ControlFactory.CreateElementsCollection<RowCostRow>(this, "row-item");

    public Button Lock => ControlFactory.Create<Button>(
        WrappedItem.GetByTestId("lock-button"));

    public ElementsCollection<RowCostRow> RowCosts =>
        ControlFactory.CreateElementsCollection<RowCostRow>(
            WrappedItem.Locator("[data-tid^='row-cost-list-row-']"));

    public ILocator StrategyType => Page
        .GetByTestId("MenuItem__root")
        .GetByText("Скидки");
}
""");

            var outDir = Path.Combine(temp, "pom-index");
            var result = CliTestRunner.Run($"--mode index-pom --input \"{pomDir}\" --out \"{outDir}\" --format both");

            AssertCliSuccess(result);
            Assert.Contains("POM facts:", result.StdOut);

            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "pom-index.generated.json")));
            var facts = json.RootElement.GetProperty("Facts").EnumerateArray().ToArray();

            AssertFact(facts, "RowCostSidePage.Save", "save-row-cost", "TestId", "TargetPlaywrightPom", "high", requiresReview: false);
            AssertFact(facts, "RowCostSidePage.Rows", "row-item", "TestId", "TargetPlaywrightPom", "high", requiresReview: false);
            AssertFact(facts, "RowCostSidePage.Lock", "lock-button", "TestId", "TargetPlaywrightPom", "high", requiresReview: false);
            AssertFact(facts, "RowCostSidePage.RowCosts", "[data-tid^='row-cost-list-row-']", "CssSelector", "TargetPlaywrightPom", "medium", requiresReview: true);
            AssertFact(facts, "RowCostSidePage.StrategyType", "MenuItem__root", "TestId", "TargetPlaywrightPom", "high", requiresReview: false);
            AssertFact(facts, "RowCostSidePage.StrategyType", "Скидки", "Text", "TargetPlaywrightPom", "low", requiresReview: true);

            var markdown = File.ReadAllText(Path.Combine(outDir, "pom-index.generated.md"));
            Assert.Contains("## Source-truth Selenium POM facts", markdown);
            Assert.Contains("## Target-side Playwright/Kontur POM facts", markdown);
            Assert.Contains("No Selenium By selector facts were found, but target-side Playwright/Kontur POM facts were found", markdown);

            var draft = File.ReadAllText(Path.Combine(outDir, "adapter-config.pom-draft.json"));
            Assert.Contains("TargetPomEvidence", draft);
            Assert.Contains("TargetPlaywrightPom", draft);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void IndexPom_PlaywrightKonturTargetPom_IgnoresCommentsAndStringLiterals()
    {
        var temp = CreateTempDir();
        try
        {
            var pomDir = Path.Combine(temp, "TargetPom");
            Directory.CreateDirectory(pomDir);
            File.WriteAllText(Path.Combine(pomDir, "CommentedPom.cs"), """
public class CommentedPom
{
    // public Button Ghost => ControlFactory.Create<Button>(this, "ghost-button");
    /* public Button BlockGhost => WrappedItem.GetByTestId("block-ghost"); */
    public string Documentation => "ControlFactory.Create<Button>(this, \"string-only\")";
    public Button Real => controlFactory.Create<Button>(this, "real-button");
}
""");

            var outDir = Path.Combine(temp, "pom-index");
            var result = CliTestRunner.Run($"--mode index-pom --input \"{pomDir}\" --out \"{outDir}\" --format both");

            AssertCliSuccess(result);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "pom-index.generated.json")));
            var facts = json.RootElement.GetProperty("Facts").EnumerateArray().ToArray();

            Assert.Single(facts);
            AssertFact(facts, "CommentedPom.Real", "real-button", "TestId", "TargetPlaywrightPom", "high", requiresReview: false);
            Assert.DoesNotContain(facts, f => f.GetProperty("Selector").GetString() == "ghost-button");
            Assert.DoesNotContain(facts, f => f.GetProperty("Selector").GetString() == "block-ghost");
            Assert.DoesNotContain(facts, f => f.GetProperty("Selector").GetString() == "string-only");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    [Fact]
    public void IndexPom_PlaywrightKonturTargetPom_UsesAstForNamedArgumentsAndStaticInterpolatedStrings()
    {
        var temp = CreateTempDir();
        try
        {
            var pomDir = Path.Combine(temp, "TargetPom");
            Directory.CreateDirectory(pomDir);
            File.WriteAllText(Path.Combine(pomDir, "AstOnlyPom.cs"), """
public class AstOnlyPom
{
    public Button NamedArgument => ControlFactory.Create<Button>(root: this, tid: $"named-button");

    public Button Getter
    {
        get
        {
            return ControlFactory.Create<Button>(
                WrappedItem.GetByTestId($"getter-button"));
        }
    }

    public ILocator MethodLocator()
    {
        return Page
            .Locator($"[data-testid='method-row']");
    }
}
""");

            var outDir = Path.Combine(temp, "pom-index");
            var result = CliTestRunner.Run($"--mode index-pom --input \"{pomDir}\" --out \"{outDir}\" --format both");

            AssertCliSuccess(result);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "pom-index.generated.json")));
            var facts = json.RootElement.GetProperty("Facts").EnumerateArray().ToArray();

            AssertFact(facts, "AstOnlyPom.NamedArgument", "named-button", "TestId", "TargetPlaywrightPom", "high", requiresReview: false);
            AssertFact(facts, "AstOnlyPom.Getter", "getter-button", "TestId", "TargetPlaywrightPom", "high", requiresReview: false);
            AssertFact(facts, "AstOnlyPom.MethodLocator", "[data-testid='method-row']", "CssSelector", "TargetPlaywrightPom", "medium", requiresReview: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    static void AssertFact(JsonElement[] facts, string sourceExpression, string selector, string selectorKind, string factOrigin, string confidence, bool requiresReview)
    {
        var fact = facts.SingleOrDefault(f =>
            f.GetProperty("SourceExpression").GetString() == sourceExpression
            && f.GetProperty("Selector").GetString() == selector
            && f.GetProperty("SelectorKind").GetString() == selectorKind);

        Assert.True(fact.ValueKind != JsonValueKind.Undefined, $"Expected fact {sourceExpression} / {selector} / {selectorKind} was not found.");
        Assert.Equal(factOrigin, fact.GetProperty("FactOrigin").GetString());
        Assert.Equal("PageObjectProperty", fact.GetProperty("TargetKindSuggestion").GetString());
        Assert.Equal(sourceExpression.Split('.').Last(), fact.GetProperty("TargetExpressionSuggestion").GetString());
        Assert.Equal(confidence, fact.GetProperty("Confidence").GetString());
        Assert.Equal(requiresReview, fact.GetProperty("RequiresReview").GetBoolean());
    }

    static void AssertCliSuccess(CliResult result)
    {
        Assert.False(result.TimedOut, result.StdErr);
        Assert.True(result.ExitCode == 0, $"CLI failed with exit code {result.ExitCode}.\nCommand: {result.CommandLine}\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
    }

    static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "migrator_pom_index_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }
}

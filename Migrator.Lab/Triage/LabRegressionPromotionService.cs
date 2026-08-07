using System.Text.Json;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Triage;

public sealed class LabRegressionPromotionService
{
    public LabPromotionManifest Promote(
        string candidateOrScenarioDirectory,
        LabRegressionLevel level,
        string outputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateOrScenarioDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        var scenarioRoot = ResolveScenarioRoot(Path.GetFullPath(candidateOrScenarioDirectory));
        var entry = ScenarioSpecLoader.Load(Path.Combine(scenarioRoot, "scenario.json"));
        if (!entry.IsValid || entry.Scenario == null)
            throw new InvalidDataException("Cannot promote an invalid scenario.");

        var category = level switch
        {
            LabRegressionLevel.UnitTest => "unit-test-repros",
            LabRegressionLevel.ProjectFixture => "project-fixtures",
            LabRegressionLevel.SavedSeed => "saved-seeds",
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
        };
        var destination = Path.Combine(Path.GetFullPath(outputRoot), category, entry.Scenario.Id);
        if (Directory.Exists(destination))
            Directory.Delete(destination, recursive: true);
        Directory.CreateDirectory(destination);

        var reduction = new LabCandidateReducer().Reduce(scenarioRoot, Path.Combine(destination, "reduction"));
        Directory.Move(Path.Combine(destination, "reduction", "scenario"), Path.Combine(destination, "scenario"));

        var manifest = new LabPromotionManifest
        {
            ScenarioId = entry.Scenario.Id,
            Level = level,
            SourceDirectory = scenarioRoot,
            DestinationDirectory = destination,
            PromotedAtUtc = DateTimeOffset.UtcNow,
            NextVerificationSteps = BuildNextSteps(level, entry.Scenario.Id)
        };
        File.WriteAllText(
            Path.Combine(destination, "promotion.json"),
            JsonSerializer.Serialize(manifest, LabJson.Options) + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(destination, "README.md"),
            RenderReadme(manifest, reduction));
        return manifest;
    }

    static string[] BuildNextSteps(LabRegressionLevel level, string scenarioId)
    {
        var first = level switch
        {
            LabRegressionLevel.UnitTest => "Encode the smallest parser/renderer assertion from this repro in Migrator.Tests; the repro is evidence, not a substitute for a focused unit test.",
            LabRegressionLevel.ProjectFixture => "Move the reviewed scenario into the permanent project regression corpus and include it in the affected feature suite.",
            LabRegressionLevel.SavedSeed => "Move the reviewed scenario under corpus/seeds and preserve its seed/generator metadata when available.",
            _ => "Review the promoted repro."
        };
        return new[]
        {
            first,
            $"Replay {scenarioId} after the fix.",
            "Run the affected cluster.",
            "Run the stable corpus before merge/release."
        };
    }

    static string RenderReadme(LabPromotionManifest manifest, LabReductionReport reduction)
    {
        var lines = new List<string>
        {
            $"# Regression promotion: {manifest.ScenarioId}",
            "",
            $"Level: `{manifest.Level}`",
            $"Reduced files: {reduction.BeforeFiles} → {reduction.AfterFiles}",
            "",
            "This directory is a promotion artifact. Review it before copying it into the permanent corpus/test tree.",
            "",
            "## Next verification steps",
            ""
        };
        lines.AddRange(manifest.NextVerificationSteps.Select(step => $"- [ ] {step}"));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    static string ResolveScenarioRoot(string inputRoot)
    {
        if (File.Exists(Path.Combine(inputRoot, "scenario.json")))
            return inputRoot;
        var nested = Path.Combine(inputRoot, "scenario");
        if (File.Exists(Path.Combine(nested, "scenario.json")))
            return nested;
        var repro = Path.Combine(inputRoot, "repro");
        if (File.Exists(Path.Combine(repro, "scenario.json")))
            return repro;
        throw new FileNotFoundException($"Could not find scenario.json in '{inputRoot}', scenario/, or repro/.");
    }
}

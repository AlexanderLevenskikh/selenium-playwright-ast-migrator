using System.Text.Json;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Generator;

public sealed class LabMetamorphicAnalyzer
{
    public LabMetamorphicReport Analyze(
        string manifestPath,
        LabSuiteRunResult run,
        string? candidateRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentNullException.ThrowIfNull(run);

        var resolvedManifestPath = LabGenerationManifestLoader.ResolveManifestPath(manifestPath);
        var manifest = LabGenerationManifestLoader.Load(resolvedManifestPath);
        var corpusRoot = Path.GetDirectoryName(resolvedManifestPath)!;
        var runById = run.Projects.ToDictionary(project => project.Id, StringComparer.OrdinalIgnoreCase);

        var reference = manifest.Variants
            .Select(variant => runById.GetValueOrDefault(variant.Id))
            .FirstOrDefault(project => project != null && IsHealthy(project))
            ?? manifest.Variants.Select(variant => runById.GetValueOrDefault(variant.Id)).FirstOrDefault(project => project != null);
        var referenceSignature = reference == null ? "" : BuildSignature(reference);
        var referenceId = reference?.Id ?? "";

        var results = new List<LabMetamorphicVariantResult>(manifest.Variants.Length);
        foreach (var variant in manifest.Variants)
        {
            runById.TryGetValue(variant.Id, out var project);
            var reasons = new List<string>();
            var signature = project == null ? "missing" : BuildSignature(project);

            if (project == null)
            {
                reasons.Add("Generated scenario is missing from the lab run.");
            }
            else
            {
                if (project.ActualStatus != variant.ExpectedStatus)
                    reasons.Add($"Expected {ToContractName(variant.ExpectedStatus)}, actual {ToContractName(project.ActualStatus)}.");
                if (!project.SourceContentPreserved)
                    reasons.Add("Source fixture content changed during execution.");
                if (variant.ExpectedStatus == ScenarioStatus.Pass && !project.Quality.Passed)
                    reasons.Add("Quality budget changed under a semantics-preserving transformation.");
                if (variant.ExpectedStatus == ScenarioStatus.Pass && !project.Oracle.Passed)
                    reasons.Add("Semantic oracle changed under a semantics-preserving transformation.");
                if (reference != null && !string.Equals(signature, referenceSignature, StringComparison.Ordinal))
                    reasons.Add($"Diagnostic/outcome signature differs from reference variant {reference.Id}.");
            }

            string? savedCandidate = null;
            if (project != null
                && reasons.Count > 0
                && !string.IsNullOrWhiteSpace(candidateRoot)
                && IsUsefulCandidate(project))
            {
                savedCandidate = SaveCandidate(
                    corpusRoot,
                    Path.GetFullPath(candidateRoot),
                    manifest,
                    variant,
                    project,
                    reasons);
            }

            results.Add(new LabMetamorphicVariantResult
            {
                Id = variant.Id,
                ExpectedStatus = variant.ExpectedStatus,
                ActualStatus = project?.ActualStatus,
                Passed = reasons.Count == 0,
                Signature = signature,
                Reasons = reasons.ToArray(),
                Dimensions = new Dictionary<string, string>(variant.Dimensions, StringComparer.Ordinal),
                CandidateDirectory = savedCandidate
            });
        }

        return new LabMetamorphicReport
        {
            Family = manifest.Family,
            Seed = manifest.Seed,
            CorpusFingerprint = manifest.CorpusFingerprint,
            ReferenceVariantId = referenceId,
            ReferenceSignature = referenceSignature,
            Summary = new LabMetamorphicSummary
            {
                Variants = results.Count,
                Passed = results.Count(result => result.Passed),
                Regressions = results.Count(result => !result.Passed),
                SavedCandidates = results.Count(result => !string.IsNullOrWhiteSpace(result.CandidateDirectory))
            },
            Variants = results.ToArray()
        };
    }

    static bool IsHealthy(LabScenarioRunResult project) =>
        project.ActualStatus == project.ExpectedStatus
        && project.SourceContentPreserved
        && (project.ExpectedStatus != ScenarioStatus.Pass || (project.Quality.Passed && project.Oracle.Passed));

    static bool IsUsefulCandidate(LabScenarioRunResult project)
    {
        if (project.SourceTests.Passed < project.SourceTests.ExpectedPassed)
            return false;
        if (project.ActualStatus is ScenarioStatus.SourceInvalid or ScenarioStatus.InfrastructureFailure)
            return false;

        return project.ActualStatus is ScenarioStatus.Regression or ScenarioStatus.MigratorFailure or ScenarioStatus.NonDeterministic
               || !project.Quality.Passed
               || !project.Oracle.Passed;
    }

    static string BuildSignature(LabScenarioRunResult project)
    {
        var diagnostics = string.Join(",", project.ProjectVerify.DiagnosticCategories.OrderBy(value => value, StringComparer.Ordinal));
        return string.Join("|", new[]
        {
            ToContractName(project.ActualStatus),
            $"source={project.SourceTests.Passed}/{project.SourceTests.ExpectedPassed}",
            $"target={project.TargetTests.Passed}/{project.TargetTests.ExpectedPassed}",
            $"todo={project.Quality.TodoActual}",
            $"unmapped={project.Quality.UnmappedActual}",
            $"unsupported={project.Quality.UnsupportedActual}",
            $"warnings={project.Quality.WarningsActual}",
            $"quality={project.Quality.Passed}",
            $"oracle={project.Oracle.Passed}",
            $"diagnostics={diagnostics}"
        });
    }

    static string SaveCandidate(
        string corpusRoot,
        string candidateRoot,
        LabGenerationManifest manifest,
        LabGeneratedVariant variant,
        LabScenarioRunResult project,
        IReadOnlyCollection<string> reasons)
    {
        var sourceDirectory = Path.Combine(corpusRoot, variant.Directory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Generated seed directory is missing: {sourceDirectory}");

        var destinationRoot = Path.Combine(candidateRoot, variant.Id);
        var scenarioDestination = Path.Combine(destinationRoot, "scenario");
        if (Directory.Exists(destinationRoot))
            Directory.Delete(destinationRoot, recursive: true);
        CopyDirectory(sourceDirectory, scenarioDestination);

        var metadata = new LabSeedCandidate
        {
            ScenarioId = variant.Id,
            Family = manifest.Family,
            FamilySeed = manifest.Seed,
            VariantSeed = variant.Seed,
            CorpusFingerprint = manifest.CorpusFingerprint,
            ContentHash = variant.ContentHash,
            GeneratorVersion = manifest.GeneratorVersion,
            BaseScenarioId = manifest.BaseScenarioId,
            BaseContentHash = manifest.BaseContentHash,
            Environment = manifest.Environment,
            ExpectedStatus = variant.ExpectedStatus,
            ActualStatus = project.ActualStatus,
            Dimensions = new Dictionary<string, string>(variant.Dimensions, StringComparer.Ordinal),
            Reasons = reasons.ToArray(),
            DiagnosticCategories = project.ProjectVerify.DiagnosticCategories.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            QualityIssues = project.Quality.Issues.ToArray(),
            OracleIssues = project.Oracle.Issues.ToArray(),
            RunIssues = project.Issues.ToArray()
        };
        File.WriteAllText(
            Path.Combine(destinationRoot, "candidate.json"),
            JsonSerializer.Serialize(metadata, LabJson.Options) + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(destinationRoot, "README.md"),
            $"# Saved seed candidate: {variant.Id}{Environment.NewLine}{Environment.NewLine}" +
            $"Family seed: `{manifest.Seed}`{Environment.NewLine}{Environment.NewLine}" +
            $"Generator: `{manifest.GeneratorVersion}`{Environment.NewLine}{Environment.NewLine}" +
            $"Base scenario: `{manifest.BaseScenarioId}` (`{manifest.BaseContentHash}`){Environment.NewLine}{Environment.NewLine}" +
            $"Recommended regression level: `saved-seed`{Environment.NewLine}{Environment.NewLine}" +
            "Reasons:" + Environment.NewLine +
            string.Join(Environment.NewLine, reasons.Select(reason => $"- {reason}")) + Environment.NewLine);
        return destinationRoot;
    }

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            if (IsBuildOutput(relative))
                continue;
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (IsBuildOutput(relative))
                continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    static bool IsBuildOutput(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => part.Equals("bin", StringComparison.OrdinalIgnoreCase)
                                 || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
                                 || part.Equals("TestResults", StringComparison.OrdinalIgnoreCase));
    }

    static string ToContractName<T>(T value) where T : struct, Enum
    {
        var text = value.ToString();
        var result = new System.Text.StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(text[index - 1]))
                result.Append('_');
            result.Append(char.ToUpperInvariant(character));
        }
        return result.ToString();
    }
}

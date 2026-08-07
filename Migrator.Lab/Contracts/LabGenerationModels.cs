namespace Migrator.Lab.Contracts;

public sealed record LabGenerationEnvironment
{
    public string FrameworkDescription { get; init; } = "";
    public string RuntimeVersion { get; init; } = "";
    public string OsDescription { get; init; } = "";
    public string ProcessArchitecture { get; init; } = "";
    public string CurrentCulture { get; init; } = "";
    public string GeneratorAssemblyVersion { get; init; } = "";
}

public sealed record LabGeneratedVariant
{
    public int Index { get; init; }
    public string Id { get; init; } = "";
    public int Seed { get; init; }
    public string Directory { get; init; } = "";
    public string ContentHash { get; init; } = "";
    public ScenarioStatus ExpectedStatus { get; init; } = ScenarioStatus.Pass;
    public Dictionary<string, string> Dimensions { get; init; } = new(StringComparer.Ordinal);
}

public sealed record LabGenerationManifest
{
    public string SchemaVersion { get; init; } = "migrator-lab-generation/v1";
    public string GeneratorVersion { get; init; } = "pairwise-binary/v1";
    public string Family { get; init; } = "p30-basic-login-metamorphic";
    public string BaseScenarioId { get; init; } = "";
    public string BaseContentHash { get; init; } = "";
    public int Seed { get; init; }
    public string[] Dimensions { get; init; } = Array.Empty<string>();
    public string CorpusFingerprint { get; init; } = "";
    public LabGenerationEnvironment Environment { get; init; } = new();
    public LabGeneratedVariant[] Variants { get; init; } = Array.Empty<LabGeneratedVariant>();
}

public sealed record LabMetamorphicVariantResult
{
    public string Id { get; init; } = "";
    public ScenarioStatus ExpectedStatus { get; init; } = ScenarioStatus.Pass;
    public ScenarioStatus? ActualStatus { get; init; }
    public bool Passed { get; init; }
    public string Signature { get; init; } = "";
    public string[] Reasons { get; init; } = Array.Empty<string>();
    public Dictionary<string, string> Dimensions { get; init; } = new(StringComparer.Ordinal);
    public string? CandidateDirectory { get; init; }
}

public sealed record LabMetamorphicSummary
{
    public int Variants { get; init; }
    public int Passed { get; init; }
    public int Regressions { get; init; }
    public int SavedCandidates { get; init; }
}

public sealed record LabMetamorphicReport
{
    public string SchemaVersion { get; init; } = "migrator-lab-metamorphic/v1";
    public string Family { get; init; } = "";
    public int Seed { get; init; }
    public string CorpusFingerprint { get; init; } = "";
    public string ReferenceVariantId { get; init; } = "";
    public string ReferenceSignature { get; init; } = "";
    public LabMetamorphicSummary Summary { get; init; } = new();
    public LabMetamorphicVariantResult[] Variants { get; init; } = Array.Empty<LabMetamorphicVariantResult>();
}

public sealed record LabSeedCandidate
{
    public string SchemaVersion { get; init; } = "migrator-lab-seed-candidate/v1";
    public string ScenarioId { get; init; } = "";
    public string Family { get; init; } = "";
    public int FamilySeed { get; init; }
    public int VariantSeed { get; init; }
    public string CorpusFingerprint { get; init; } = "";
    public string ContentHash { get; init; } = "";
    public string GeneratorVersion { get; init; } = "";
    public string BaseScenarioId { get; init; } = "";
    public string BaseContentHash { get; init; } = "";
    public LabGenerationEnvironment Environment { get; init; } = new();
    public ScenarioStatus ExpectedStatus { get; init; }
    public ScenarioStatus ActualStatus { get; init; }
    public Dictionary<string, string> Dimensions { get; init; } = new(StringComparer.Ordinal);
    public string[] Reasons { get; init; } = Array.Empty<string>();
    public string[] DiagnosticCategories { get; init; } = Array.Empty<string>();
    public string[] QualityIssues { get; init; } = Array.Empty<string>();
    public string[] OracleIssues { get; init; } = Array.Empty<string>();
    public string[] RunIssues { get; init; } = Array.Empty<string>();
    public string RecommendedRegressionLevel { get; init; } = "saved-seed";
}

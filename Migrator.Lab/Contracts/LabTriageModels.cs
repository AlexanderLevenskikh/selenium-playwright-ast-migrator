namespace Migrator.Lab.Contracts;

public enum LabRegressionLevel
{
    UnitTest,
    ProjectFixture,
    SavedSeed
}

public enum LabAutomationDisposition
{
    AutoFixEligible,
    ManualReview
}

public sealed record LabFailureEvidence
{
    public string ScenarioId { get; init; } = "";
    public ScenarioStatus ExpectedStatus { get; init; }
    public ScenarioStatus ActualStatus { get; init; }
    public string Stage { get; init; } = "";
    public string[] DiagnosticCodes { get; init; } = Array.Empty<string>();
    public string[] SemanticDiffKinds { get; init; } = Array.Empty<string>();
    public string[] FeatureTags { get; init; } = Array.Empty<string>();
    public int TodoActual { get; init; }
    public int UnmappedActual { get; init; }
    public int UnsupportedActual { get; init; }
    public int WarningsActual { get; init; }
    public string[] QualityIssues { get; init; } = Array.Empty<string>();
    public string[] OracleIssues { get; init; } = Array.Empty<string>();
    public string[] RunIssues { get; init; } = Array.Empty<string>();
    public string[] RawEvidencePaths { get; init; } = Array.Empty<string>();
    public string[] EvidenceBackedComponents { get; init; } = Array.Empty<string>();
    public string[] SuspectedComponents { get; init; } = Array.Empty<string>();
    public LabRegressionLevel RecommendedRegressionLevel { get; init; }
    public LabAutomationDisposition AutomationDisposition { get; init; }
    public string ReproCommand { get; init; } = "";
    public string ScenarioDirectory { get; init; } = "";
}

public sealed record LabIssueCluster
{
    public string Id { get; init; } = "";
    public string Fingerprint { get; init; } = "";
    public string Stage { get; init; } = "";
    public string Severity { get; init; } = "";
    public string[] DiagnosticCodes { get; init; } = Array.Empty<string>();
    public string[] SemanticDiffKinds { get; init; } = Array.Empty<string>();
    public string[] FeatureTags { get; init; } = Array.Empty<string>();
    public string[] ScenarioIds { get; init; } = Array.Empty<string>();
    public string[] SuspectedComponents { get; init; } = Array.Empty<string>();
    public LabRegressionLevel RecommendedRegressionLevel { get; init; }
    public LabAutomationDisposition AutomationDisposition { get; init; }
    public string? TaskPackDirectory { get; init; }
}

public sealed record LabTriageSummary
{
    public int Findings { get; init; }
    public int Clusters { get; init; }
    public int AutoFixEligible { get; init; }
    public int ManualReview { get; init; }
    public int TaskPacks { get; init; }
}

public sealed record LabTriageReport
{
    public string SchemaVersion { get; init; } = "migrator-lab-triage/v1";
    public string RunPath { get; init; } = "";
    public string CorpusRoot { get; init; } = "";
    public string RepositoryRoot { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public LabTriageSummary Summary { get; init; } = new();
    public LabFailureEvidence[] Findings { get; init; } = Array.Empty<LabFailureEvidence>();
    public LabIssueCluster[] Clusters { get; init; } = Array.Empty<LabIssueCluster>();
}

public sealed record LabReductionReport
{
    public string SchemaVersion { get; init; } = "migrator-lab-reduction/v1";
    public string ScenarioId { get; init; } = "";
    public string SourceDirectory { get; init; } = "";
    public string ReducedDirectory { get; init; } = "";
    public string[] RetainedFeatures { get; init; } = Array.Empty<string>();
    public string[] RetainedFiles { get; init; } = Array.Empty<string>();
    public string[] RemovedFiles { get; init; } = Array.Empty<string>();
    public long BeforeBytes { get; init; }
    public long AfterBytes { get; init; }
    public int BeforeFiles { get; init; }
    public int AfterFiles { get; init; }
}

public sealed record LabTaskPackManifest
{
    public string SchemaVersion { get; init; } = "migrator-lab-task-pack/v1";
    public string ClusterId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Classification { get; init; } = "";
    public string[] ScenarioIds { get; init; } = Array.Empty<string>();
    public string[] Evidence { get; init; } = Array.Empty<string>();
    public string[] EvidenceArtifacts { get; init; } = Array.Empty<string>();
    public string[] EvidenceBackedMigratorCode { get; init; } = Array.Empty<string>();
    public string[] SuspectedMigratorComponents { get; init; } = Array.Empty<string>();
    public string[] RelevantMigratorCode { get; init; } = Array.Empty<string>();
    public string[] RelevantTests { get; init; } = Array.Empty<string>();
    public string[] Constraints { get; init; } = Array.Empty<string>();
    public string[] FilesNotToChange { get; init; } = Array.Empty<string>();
    public string[] DefinitionOfDone { get; init; } = Array.Empty<string>();
    public string ReproCommand { get; init; } = "";
    public string QualityBaseline { get; init; } = "";
    public LabRegressionLevel RecommendedRegressionLevel { get; init; }
    public LabAutomationDisposition AutomationDisposition { get; init; }
}

public sealed record LabPromotionManifest
{
    public string SchemaVersion { get; init; } = "migrator-lab-promotion/v1";
    public string ScenarioId { get; init; } = "";
    public LabRegressionLevel Level { get; init; }
    public string SourceDirectory { get; init; } = "";
    public string DestinationDirectory { get; init; } = "";
    public DateTimeOffset PromotedAtUtc { get; init; }
    public string[] NextVerificationSteps { get; init; } = Array.Empty<string>();
}

public sealed record LabRealProjectEvidence
{
    public string SchemaVersion { get; init; } = "migrator-lab-real-project-evidence/v1";
    public string Project { get; init; } = "";
    public string SourceRevision { get; init; } = "";
    public string MigratorRevision { get; init; } = "";
    public DateTimeOffset ExecutedAtUtc { get; init; }
    public string Status { get; init; } = "";
    public string[] EvidencePaths { get; init; } = Array.Empty<string>();
    public string Notes { get; init; } = "";
}

public sealed record LabReleaseGateReport
{
    public string SchemaVersion { get; init; } = "migrator-lab-release-gate/v2";
    public bool Passed { get; init; }
    public string StableRunPath { get; init; } = "";
    public string ContractBaselinePath { get; init; } = "";
    public string RealEvidencePath { get; init; } = "";
    public int StableUnexpectedOutcomes { get; init; }
    public int StableContractChanges { get; init; }
    public string RealProject { get; init; } = "";
    public string RealStatus { get; init; } = "";
    public int VerifiedEvidenceArtifacts { get; init; }
    public long RealEvidenceAgeHours { get; init; }
    public int MaxAgeDays { get; init; }
    public string[] Issues { get; init; } = Array.Empty<string>();
}

using System.Text.Json;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Triage;

public sealed class LabReleaseGateService
{
    public LabReleaseGateReport Evaluate(
        LabSuiteRunResult stableRun,
        string stableRunPath,
        string realEvidencePath,
        int maxAgeDays = 14,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(stableRun);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableRunPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(realEvidencePath);
        if (maxAgeDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxAgeDays), "maxAgeDays must be positive.");

        var resolvedEvidence = Path.GetFullPath(realEvidencePath);
        if (!File.Exists(resolvedEvidence))
            throw new FileNotFoundException($"Real-project release evidence was not found: {resolvedEvidence}", resolvedEvidence);

        LabRealProjectEvidence evidence;
        try
        {
            evidence = JsonSerializer.Deserialize<LabRealProjectEvidence>(File.ReadAllText(resolvedEvidence), LabJson.Options)
                       ?? throw new InvalidDataException($"Real-project evidence is empty: {resolvedEvidence}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Real-project evidence is invalid JSON: {resolvedEvidence}. {ex.Message}", ex);
        }

        var current = now ?? DateTimeOffset.UtcNow;
        var issues = new List<string>();
        if (!string.Equals(evidence.SchemaVersion, "migrator-lab-real-project-evidence/v1", StringComparison.Ordinal))
            issues.Add($"Unsupported real-project evidence schema: {evidence.SchemaVersion}");
        if (string.IsNullOrWhiteSpace(evidence.Project))
            issues.Add("Real-project evidence must identify the project.");
        if (string.IsNullOrWhiteSpace(evidence.SourceRevision) || string.IsNullOrWhiteSpace(evidence.MigratorRevision))
            issues.Add("Real-project evidence must record sourceRevision and migratorRevision.");
        if (!string.Equals(evidence.Status, "PASS", StringComparison.OrdinalIgnoreCase))
            issues.Add($"Real-project evidence status is {evidence.Status}; PASS is required.");
        if (evidence.EvidencePaths.Length == 0)
            issues.Add("Real-project evidence must point to at least one retained evidence artifact.");

        var age = current - evidence.ExecutedAtUtc;
        if (age < TimeSpan.Zero)
            issues.Add("Real-project evidence timestamp is in the future.");
        if (age > TimeSpan.FromDays(maxAgeDays))
            issues.Add($"Real-project evidence is stale ({Math.Floor(age.TotalDays)} days old, max {maxAgeDays}).");

        var unexpected = stableRun.Projects.Count(project => project.ActualStatus != project.ExpectedStatus);
        if (unexpected > 0)
            issues.Add($"Stable corpus contains {unexpected} unexpected outcome(s).");

        return new LabReleaseGateReport
        {
            Passed = issues.Count == 0,
            StableRunPath = Path.GetFullPath(stableRunPath),
            RealEvidencePath = resolvedEvidence,
            StableUnexpectedOutcomes = unexpected,
            RealProject = evidence.Project,
            RealStatus = evidence.Status,
            RealEvidenceAgeHours = (long)Math.Max(0, Math.Floor(age.TotalHours)),
            MaxAgeDays = maxAgeDays,
            Issues = issues.ToArray()
        };
    }
}

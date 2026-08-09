using System.Text.Json;
using Migrator.Lab;
using Migrator.Lab.Contracts;

namespace Migrator.Lab.Triage;

public sealed class LabReleaseGateService
{
    public LabReleaseGateReport Evaluate(
        LabSuiteRunResult stableRun,
        string stableRunPath,
        LabBaselineSnapshot contractBaseline,
        string contractBaselinePath,
        string realEvidencePath,
        int maxAgeDays = 14,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(stableRun);
        ArgumentNullException.ThrowIfNull(contractBaseline);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableRunPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractBaselinePath);
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
        var evidencePaths = evidence.EvidencePaths ?? Array.Empty<string>();
        if (evidencePaths.Length == 0)
            issues.Add("Real-project evidence must point to at least one retained evidence artifact.");

        var verifiedEvidenceArtifacts = ValidateEvidenceArtifacts(evidencePaths, resolvedEvidence, issues);
        if (evidencePaths.Length > 0 && verifiedEvidenceArtifacts == 0)
            issues.Add("Real-project evidence must contain at least one existing non-empty retained evidence artifact.");

        var age = current - evidence.ExecutedAtUtc;
        if (age < TimeSpan.Zero)
            issues.Add("Real-project evidence timestamp is in the future.");
        if (age > TimeSpan.FromDays(maxAgeDays))
            issues.Add($"Real-project evidence is stale ({Math.Floor(age.TotalDays)} days old, max {maxAgeDays}).");

        var unexpected = stableRun.Projects.Count(project => project.ActualStatus != project.ExpectedStatus);
        if (unexpected > 0)
            issues.Add($"Stable corpus contains {unexpected} unexpected outcome(s).");

        ValidateCurrentCorpus(stableRun.CorpusRoot, issues);
        var contractChanges = ValidateTrustedContractBaseline(stableRun, contractBaseline, issues);

        return new LabReleaseGateReport
        {
            Passed = issues.Count == 0,
            StableRunPath = Path.GetFullPath(stableRunPath),
            ContractBaselinePath = Path.GetFullPath(contractBaselinePath),
            RealEvidencePath = resolvedEvidence,
            StableUnexpectedOutcomes = unexpected,
            StableContractChanges = contractChanges,
            RealProject = evidence.Project,
            RealStatus = evidence.Status,
            VerifiedEvidenceArtifacts = verifiedEvidenceArtifacts,
            RealEvidenceAgeHours = (long)Math.Max(0, Math.Floor(age.TotalHours)),
            MaxAgeDays = maxAgeDays,
            Issues = issues.ToArray()
        };
    }


    static void ValidateCurrentCorpus(string corpusRoot, List<string> issues)
    {
        try
        {
            var catalog = ScenarioCatalog.Load(corpusRoot);
            foreach (var issue in catalog.CatalogIssues.Where(item => item.Severity == ValidationIssueSeverity.Error))
                issues.Add($"Current stable corpus is invalid: {issue.Code}: {issue.Message}");
            foreach (var entry in catalog.Entries.Where(entry => !entry.IsValid))
            {
                foreach (var issue in entry.Issues.Where(item => item.Severity == ValidationIssueSeverity.Error))
                    issues.Add($"Current stable scenario contract is invalid ({Path.GetFileName(Path.GetDirectoryName(entry.ScenarioFile))}): {issue.Code}: {issue.Message}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            issues.Add($"Current stable corpus could not be validated: {ex.Message}");
        }
    }

    static int ValidateTrustedContractBaseline(
        LabSuiteRunResult stableRun,
        LabBaselineSnapshot contractBaseline,
        List<string> issues)
    {
        var baselineById = contractBaseline.Projects
            .GroupBy(project => project.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var currentById = stableRun.Projects
            .GroupBy(project => project.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in stableRun.Projects)
        {
            if (!baselineById.TryGetValue(project.Id, out var baseline))
            {
                changed.Add(project.Id);
                issues.Add($"Stable scenario '{project.Id}' is not present in the trusted contract baseline.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(baseline.ContractHash))
            {
                changed.Add(project.Id);
                issues.Add($"Trusted baseline scenario '{project.Id}' has no contract fingerprint. Regenerate the trusted baseline with the current Lab version.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(project.ContractHash))
            {
                changed.Add(project.Id);
                issues.Add($"Stable run scenario '{project.Id}' has no captured contract fingerprint. Re-run the stable corpus with the current Lab version.");
                continue;
            }

            if (!string.Equals(baseline.ContractHash, project.ContractHash, StringComparison.OrdinalIgnoreCase))
            {
                changed.Add(project.Id);
                issues.Add($"Stable scenario contract changed for '{project.Id}'. Refresh the trusted baseline explicitly only after review.");
            }

            var scenarioFile = Path.Combine(stableRun.CorpusRoot, project.Id, "scenario.json");
            if (!File.Exists(scenarioFile))
            {
                changed.Add(project.Id);
                issues.Add($"Current stable corpus scenario file is missing for '{project.Id}': {scenarioFile}");
                continue;
            }

            try
            {
                var currentWorkingTreeHash = ScenarioContractHasher.ComputeFile(scenarioFile);
                if (!string.Equals(currentWorkingTreeHash, project.ContractHash, StringComparison.OrdinalIgnoreCase))
                {
                    changed.Add(project.Id);
                    issues.Add($"Stable scenario contract changed after the recorded run for '{project.Id}'. Re-run the stable corpus before release-gate.");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
            {
                changed.Add(project.Id);
                issues.Add($"Current stable scenario contract could not be verified for '{project.Id}': {ex.Message}");
            }
        }

        foreach (var baseline in contractBaseline.Projects)
        {
            if (currentById.ContainsKey(baseline.Id))
                continue;
            changed.Add(baseline.Id);
            issues.Add($"Trusted contract baseline scenario '{baseline.Id}' is missing from the stable run.");
        }

        return changed.Count;
    }

    static int ValidateEvidenceArtifacts(
        string[] evidencePaths,
        string evidenceFile,
        List<string> issues)
    {
        var verified = 0;
        var evidenceDirectory = Path.GetDirectoryName(evidenceFile) ?? Directory.GetCurrentDirectory();
        foreach (var rawPath in evidencePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            string resolved;
            try
            {
                resolved = ResolveEvidenceArtifact(rawPath, evidenceDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                issues.Add($"Real-project evidence artifact path is invalid: {rawPath} ({ex.Message})");
                continue;
            }

            if (!File.Exists(resolved))
            {
                issues.Add($"Real-project evidence artifact does not exist: {rawPath}");
                continue;
            }

            try
            {
                var info = new FileInfo(resolved);
                if (info.Length <= 0)
                {
                    issues.Add($"Real-project evidence artifact is empty: {rawPath}");
                    continue;
                }
                verified++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                issues.Add($"Real-project evidence artifact could not be inspected: {rawPath} ({ex.Message})");
            }
        }

        return verified;
    }

    static string ResolveEvidenceArtifact(string path, string evidenceDirectory)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        var fromWorkingDirectory = Path.GetFullPath(path);
        if (File.Exists(fromWorkingDirectory))
            return fromWorkingDirectory;

        return Path.GetFullPath(Path.Combine(evidenceDirectory, path));
    }
}

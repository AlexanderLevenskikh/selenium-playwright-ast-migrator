using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Migrator.Core;

/// <summary>
/// Writes mapping proposals to JSON and Markdown formats.
/// </summary>
public static class ProposalWriter
{
    public static string ToJson(IReadOnlyList<MappingProposal> proposals)
    {
        var output = new ProposalJsonOutput
        {
            GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            TotalProposals = proposals.Count,
            Summary = new ProposalJsonSummary
            {
                High = proposals.Count(p => p.Priority == ProposalPriority.High),
                Medium = proposals.Count(p => p.Priority == ProposalPriority.Medium),
                Low = proposals.Count(p => p.Priority == ProposalPriority.Low),
                RequiresSourceTruth = proposals.Count(p => p.RequiresSourceTruth)
            },
            TopProposals = proposals.Take(5).Select(p => ToProposalSummary(p)).ToList(),
            Proposals = proposals.Select(p => ToProposalSummary(p)).ToList()
        };

        return JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        });
    }

    public static string ToMarkdown(IReadOnlyList<MappingProposal> proposals)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Mapping Proposals");
        sb.AppendLine();

        // Summary table
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Priority | Count |");
        sb.AppendLine("|---|---:|");
        sb.AppendLine($"| High | {proposals.Count(p => p.Priority == ProposalPriority.High)} |");
        sb.AppendLine($"| Medium | {proposals.Count(p => p.Priority == ProposalPriority.Medium)} |");
        sb.AppendLine($"| Low | {proposals.Count(p => p.Priority == ProposalPriority.Low)} |");
        sb.AppendLine($"| **Total** | **{proposals.Count}** |");
        sb.AppendLine();

        // Top proposals
        if (proposals.Count > 0)
        {
            sb.AppendLine("## Top proposals");
            sb.AppendLine();
            var top = proposals.Take(5).ToList();
            for (int i = 0; i < top.Count; i++)
            {
                var p = top[i];
                sb.AppendLine($"{i + 1}. **{p.Title}** ({p.Priority}, score: {p.Score})");
            }
            sb.AppendLine();
        }

        // Group by kind
        sb.AppendLine("## Proposals by kind");
        sb.AppendLine();

        var kindGroups = proposals.GroupBy(p => p.Kind).ToList();
        foreach (var group in kindGroups)
        {
            sb.AppendLine($"### {group.Key} ({group.Count()} proposal(s))");
            sb.AppendLine();

            foreach (var p in group)
            {
                sb.AppendLine($"## {p.Title}");
                sb.AppendLine();
                sb.AppendLine($"- **Id:** `{p.Id}`");
                sb.AppendLine($"- **Kind:** {p.Kind}");
                sb.AppendLine($"- **Priority:** {p.Priority}");
                sb.AppendLine($"- **Confidence:** {p.Confidence}");
                sb.AppendLine($"- **Occurrences:** {p.Occurrences}");
                sb.AppendLine($"- **Score:** {p.Score}");
                sb.AppendLine($"- **RequiresSourceTruth:** {p.RequiresSourceTruth}");
                sb.AppendLine();
                sb.AppendLine("**Evidence:**");
                sb.AppendLine();
                sb.AppendLine(p.Evidence);
                sb.AppendLine();

                if (p.AffectedFiles.Count > 0)
                {
                    sb.AppendLine("**Affected files:**");
                    sb.AppendLine();
                    foreach (var f in p.AffectedFiles)
                        sb.AppendLine($"- {PathRedaction.Redact(f)}");
                    sb.AppendLine();
                }

                sb.AppendLine("**Reason:**");
                sb.AppendLine();
                sb.AppendLine(p.Reason);
                sb.AppendLine();

                if (!string.IsNullOrEmpty(p.SuggestedConfigSnippet))
                {
                    sb.AppendLine("**Suggested config snippet:**");
                    sb.AppendLine();
                    sb.AppendLine("```json");
                    sb.AppendLine(p.SuggestedConfigSnippet);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(p.Risks))
                {
                    sb.AppendLine("**Risks:**");
                    sb.AppendLine();
                    sb.AppendLine(p.Risks);
                    sb.AppendLine();
                }

                sb.AppendLine("**Next action:**");
                sb.AppendLine();
                sb.AppendLine(p.NextAction);
                sb.AppendLine();

                sb.AppendLine("> **Agent constraints:** Do not invent selectors. Use PageObject/source truth before applying this proposal. Add mapping to the narrowest scope. Run analyze/migrate/verify after applying.");
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
        }

        // Deferred / manual migration
        var manualProposals = proposals.Where(p => p.Kind == ProposalKind.ManualMigration).ToList();
        if (manualProposals.Any())
        {
            sb.AppendLine("## Deferred / manual migration");
            sb.AppendLine();
            foreach (var p in manualProposals)
            {
                sb.AppendLine($"- `{p.Id}`: {p.Title} ({p.Occurrences} occurrences)");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    static ProposalJsonSummaryItem ToProposalSummary(MappingProposal p)
    {
        return new ProposalJsonSummaryItem
        {
            Id = p.Id,
            Kind = p.Kind,
            Title = p.Title,
            Priority = p.Priority,
            Confidence = p.Confidence,
            Evidence = p.Evidence,
            AffectedFiles = PathRedaction.RedactAll(p.AffectedFiles),
            AffectedTests = p.AffectedTests,
            Occurrences = p.Occurrences,
            SuggestedConfigSnippet = p.SuggestedConfigSnippet,
            RequiresSourceTruth = p.RequiresSourceTruth,
            Reason = p.Reason,
            Risks = p.Risks,
            NextAction = p.NextAction,
            Score = p.Score
        };
    }
}

/// <summary>
/// JSON output structure for proposals.
/// </summary>
public sealed class ProposalJsonOutput
{
    [JsonPropertyName("generatedAt")]
    public string GeneratedAt { get; init; } = null!;

    [JsonPropertyName("totalProposals")]
    public int TotalProposals { get; init; }

    [JsonPropertyName("summary")]
    public ProposalJsonSummary Summary { get; init; } = null!;

    [JsonPropertyName("topProposals")]
    public IReadOnlyList<ProposalJsonSummaryItem> TopProposals { get; init; } = Array.Empty<ProposalJsonSummaryItem>();

    [JsonPropertyName("proposals")]
    public IReadOnlyList<ProposalJsonSummaryItem> Proposals { get; init; } = Array.Empty<ProposalJsonSummaryItem>();
}

public sealed class ProposalJsonSummary
{
    [JsonPropertyName("high")]
    public int High { get; init; }

    [JsonPropertyName("medium")]
    public int Medium { get; init; }

    [JsonPropertyName("low")]
    public int Low { get; init; }

    [JsonPropertyName("requiresSourceTruth")]
    public int RequiresSourceTruth { get; init; }
}

public sealed class ProposalJsonSummaryItem
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    [JsonPropertyName("kind")]
    public ProposalKind Kind { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = null!;

    [JsonPropertyName("priority")]
    public ProposalPriority Priority { get; init; }

    [JsonPropertyName("confidence")]
    public ProposalConfidence Confidence { get; init; }

    [JsonPropertyName("evidence")]
    public string Evidence { get; init; } = null!;

    [JsonPropertyName("affectedFiles")]
    public IReadOnlyList<string> AffectedFiles { get; init; } = Array.Empty<string>();

    [JsonPropertyName("affectedTests")]
    public IReadOnlyList<string> AffectedTests { get; init; } = Array.Empty<string>();

    [JsonPropertyName("occurrences")]
    public int Occurrences { get; init; }

    [JsonPropertyName("suggestedConfigSnippet")]
    public string? SuggestedConfigSnippet { get; init; }

    [JsonPropertyName("requiresSourceTruth")]
    public bool RequiresSourceTruth { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = null!;

    [JsonPropertyName("risks")]
    public string? Risks { get; init; }

    [JsonPropertyName("nextAction")]
    public string NextAction { get; init; } = null!;

    [JsonPropertyName("score")]
    public int Score { get; init; }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Migrator.Core;

/// <summary>
/// Writes TargetDiscovery output: target-inventory.json, target-style-notes.md, adapter-config.draft.json.
/// All paths are relative. No secrets. No auto-apply.
/// </summary>
public static class DiscoveryWriter
{
    public static string ToInventoryJson(TargetInventory inventory)
    {
        return JsonSerializer.Serialize(inventory, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        });
    }

    public static string ToStyleNotes(TargetInventory inventory)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Target Playwright Infrastructure Discovery");
        sb.AppendLine();

        // Summary table
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Item | Detected | Confidence |");
        sb.AppendLine("|---|---|---|");

        var topFramework = inventory.DetectedFrameworks.FirstOrDefault();
        sb.AppendLine($"| Framework | {(topFramework?.Name ?? "None")} | {(topFramework?.Confidence ?? "N/A")} |");

        var topHost = inventory.DetectedTestHosts.FirstOrDefault();
        sb.AppendLine($"| Base class | {(topHost?.BaseClass ?? "None")} | {(topHost?.Confidence ?? "N/A")} |");

        var topLocator = inventory.DetectedLocatorAttributes.FirstOrDefault();
        sb.AppendLine($"| Default locator attribute | {(topLocator?.Attribute ?? "None")} | {(topLocator?.Confidence ?? "N/A")} |");

        var topAuth = inventory.DetectedAuthPatterns.FirstOrDefault();
        sb.AppendLine($"| Auth setup | {(topAuth?.Pattern ?? "None")} | {(topAuth?.Evidence.Count >= 2 ? "Medium" : "Low")} |");

        sb.AppendLine();

        // TestHost candidates
        if (inventory.DetectedTestHosts.Any())
        {
            sb.AppendLine("## TestHost candidates");
            sb.AppendLine();
            foreach (var host in inventory.DetectedTestHosts)
            {
                sb.AppendLine($"### `{host.BaseClass}` (confidence: {host.Confidence}, occurrences: {host.Occurrences})");
                sb.AppendLine();
                sb.AppendLine($"- **Namespace:** `{host.Namespace}`");
                sb.AppendLine($"- **Framework:** {host.Framework ?? "Unknown"}");
                if (host.ClassAttributes.Any())
                    sb.AppendLine($"- **Class attributes:** {string.Join(", ", host.ClassAttributes.Select(a => $"`{a}`"))}");
                if (host.Usings.Any())
                    sb.AppendLine($"- **Usings:** {string.Join(", ", host.Usings.Take(10).Select(u => $"`{u}`"))}{(host.Usings.Count > 10 ? $" +{host.Usings.Count - 10} more" : "")}");
                if (host.Evidence.Any())
                    sb.AppendLine($"- **Evidence files:** {string.Join(", ", host.Evidence.Take(5).Select(f => $"`{f}`"))}");
                sb.AppendLine();
            }
        }

        // Locator conventions
        if (inventory.DetectedLocatorAttributes.Any() || inventory.DetectedLocatorMethods.Any())
        {
            sb.AppendLine("## Locator conventions");
            sb.AppendLine();

            if (inventory.DetectedLocatorAttributes.Any())
            {
                sb.AppendLine("### Locator attributes");
                sb.AppendLine();
                sb.AppendLine("| Attribute | Occurrences | Confidence |");
                sb.AppendLine("|---|---:|---|");
                foreach (var attr in inventory.DetectedLocatorAttributes)
                    sb.AppendLine($"| `{attr.Attribute}` | {attr.Occurrences} | {attr.Confidence} |");
                sb.AppendLine();
            }

            if (inventory.DetectedLocatorMethods.Any())
            {
                sb.AppendLine("### Locator methods");
                sb.AppendLine();
                sb.AppendLine("| Method | Occurrences |");
                sb.AppendLine("|---|---:|");
                foreach (var method in inventory.DetectedLocatorMethods.Take(15))
                    sb.AppendLine($"| `{method.Method}` | {method.Occurrences} |");
                sb.AppendLine();
            }
        }

        // Navigation / auth patterns
        if (inventory.DetectedNavigationPatterns.Any() || inventory.DetectedAuthPatterns.Any())
        {
            sb.AppendLine("## Navigation / auth patterns");
            sb.AppendLine();

            if (inventory.DetectedNavigationPatterns.Any())
            {
                sb.AppendLine("### Navigation");
                sb.AppendLine();
                foreach (var nav in inventory.DetectedNavigationPatterns)
                {
                    sb.AppendLine($"- **{nav.Pattern}**: `{nav.Example}` ({nav.Evidence.Count} file(s))");
                }
                sb.AppendLine();
            }

            if (inventory.DetectedAuthPatterns.Any())
            {
                sb.AppendLine("### Auth");
                sb.AppendLine();
                foreach (var auth in inventory.DetectedAuthPatterns)
                {
                    sb.AppendLine($"- **{auth.Pattern}**: placeholder `{auth.SuggestedPlaceholder}` ({auth.Evidence.Count} file(s))");
                }
                sb.AppendLine();
            }
        }

        // Helper methods
        if (inventory.DetectedHelperMethods.Any())
        {
            sb.AppendLine("## Helper methods");
            sb.AppendLine();
            sb.AppendLine("| Name | Occurrences | Files | Potential Use |");
            sb.AppendLine("|---|---:|---:|---|");
            foreach (var helper in inventory.DetectedHelperMethods.Take(20))
                sb.AppendLine($"| `{helper.Name}` | {helper.Occurrences} | {helper.Files.Count} | {helper.PotentialUse} |");
            sb.AppendLine();
        }

        // SetUp methods
        if (inventory.DetectedSetUpMethods.Any())
        {
            sb.AppendLine("## SetUp methods");
            sb.AppendLine();
            foreach (var setup in inventory.DetectedSetUpMethods)
            {
                sb.AppendLine($"### `{setup.ClassName}.{setup.MethodName}`");
                sb.AppendLine();
                if (setup.Statements.Any())
                {
                    sb.AppendLine("**Statements:**");
                    sb.AppendLine();
                    sb.AppendLine("```csharp");
                    foreach (var stmt in setup.Statements.Take(10))
                        sb.AppendLine(stmt);
                    if (setup.Statements.Count > 10)
                        sb.AppendLine($"// ... {setup.Statements.Count - 10} more statements");
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }
        }

        // Warnings
        if (inventory.Warnings.Any())
        {
            sb.AppendLine("## Warnings");
            sb.AppendLine();
            foreach (var w in inventory.Warnings)
                sb.AppendLine($"- {w}");
            if (inventory.RedactionCount > 0)
                sb.AppendLine($"- {inventory.RedactionCount} URL/secret redaction(s) applied");
            sb.AppendLine();
        }

        // Recommended next actions
        sb.AppendLine("## Recommended next actions");
        sb.AppendLine();
        sb.AppendLine("1. Review `adapter-config.draft.json`.");
        sb.AppendLine("2. Confirm TestHost (base class, attributes, usings).");
        sb.AppendLine("3. Confirm locator attribute convention.");
        sb.AppendLine("4. Fill route placeholders with source truth.");
        sb.AppendLine("5. Run `analyze`/`migrate`/`verify` with reviewed config.");
        sb.AppendLine();

        // Agent constraints
        sb.AppendLine("> **Agent constraints:** Do not use this draft without review. Do not invent missing routes. Do not copy real secrets into public config. Discovery collects facts only — it does not invent infrastructure or auto-apply config changes.");
        sb.AppendLine();

        return sb.ToString();
    }

    public static string ToAdapterConfigDraft(TargetInventory inventory)
    {
        var topFramework = inventory.DetectedFrameworks.FirstOrDefault();
        var topHost = inventory.DetectedTestHosts.FirstOrDefault();
        var topLocator = inventory.DetectedLocatorAttributes.FirstOrDefault();
        var topAuth = inventory.DetectedAuthPatterns.FirstOrDefault();

        // Build LocatorSettings
        string? defaultTestIdAttr = null;
        if (topLocator != null && topLocator.Confidence == "High")
        {
            defaultTestIdAttr = topLocator.Attribute;
        }

        var knownAttrs = inventory.DetectedLocatorAttributes
            .Where(a => a.Confidence != "Low")
            .Select(a => a.Attribute)
            .Distinct()
            .ToList();

        var draft = new DiscoveryConfigDraft
        {
            GeneratedBy = "Migrator target discovery",
            RequiresReview = true,
            Note = "This is a DRAFT config generated by --mode discover-target. Review before use. Do not commit without validation.",

            LocatorSettings = topLocator != null ? new LocatorSettings(
                defaultTestIdAttr ?? "<REVIEW_REQUIRED>",
                knownAttrs.Count > 0 ? knownAttrs.ToArray() : null
            ) : new LocatorSettings("<REVIEW_REQUIRED>", null),

            TestHost = topHost != null ? new TestHostConfig
            {
                Namespace = topHost.Namespace,
                BaseClass = topHost.BaseClass,
                ClassAttributes = topHost.ClassAttributes.ToArray(),
                Usings = topHost.Usings.ToArray(),
                SetUpStatements = BuildSetUpStatements(inventory)
            } : new TestHostConfig
            {
                BaseClass = "<REVIEW_REQUIRED>",
                SetUpStatements = new[] { "// REVIEW_REQUIRED: add login/navigation setup" }
            }
        };

        return JsonSerializer.Serialize(draft, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        });
    }

    public static string[] BuildSetUpStatements(TargetInventory inventory)
    {
        var statements = new List<string>();

        // If we have navigation patterns, use sanitized versions
        foreach (var nav in inventory.DetectedNavigationPatterns)
        {
            statements.Add($"{nav.Example};");
        }

        // If no navigation patterns but we have auth patterns, add placeholder
        if (!statements.Any() && inventory.DetectedAuthPatterns.Any())
        {
            statements.Add("await Page.GotoAsync(\"<test-login>\");");
            statements.Add("await Page.GotoAsync(\"<ROUTE_SOURCE_TRUTH_REQUIRED>\");");
        }

        // If nothing detected at all
        if (!statements.Any())
        {
            statements.Add("// REVIEW_REQUIRED: add login/navigation setup");
            statements.Add("await Page.GotoAsync(\"<test-login>\");");
            statements.Add("await Page.GotoAsync(\"<ROUTE_SOURCE_TRUTH_REQUIRED>\");");
        }

        return statements.ToArray();
    }

    public static string ToWarningsText(TargetInventory inventory)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Discovery warnings: {inventory.Warnings.Count}");
        sb.AppendLine($"Redactions applied: {inventory.RedactionCount}");
        sb.AppendLine();
        foreach (var w in inventory.Warnings)
            sb.AppendLine($"WARNING: {w}");
        if (inventory.RedactionCount > 0)
            sb.AppendLine($"WARNING: {inventory.RedactionCount} URL/secret value(s) were redacted for safety");
        return sb.ToString();
    }
}

// --- Draft config JSON model ---

public sealed class DiscoveryConfigDraft
{
    [JsonPropertyName("GeneratedBy")]
    public string GeneratedBy { get; init; } = null!;

    [JsonPropertyName("RequiresReview")]
    public bool RequiresReview { get; init; }

    [JsonPropertyName("Note")]
    public string? Note { get; init; }

    [JsonPropertyName("LocatorSettings")]
    public LocatorSettings? LocatorSettings { get; init; }

    [JsonPropertyName("TestHost")]
    public TestHostConfig? TestHost { get; init; }
}

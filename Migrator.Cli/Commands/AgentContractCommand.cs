using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Core;

internal static class AgentContractCommand
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    static readonly string[] PreferredArtifactNames =
    {
        "current-ticket.md",
        "runbook.md",
        "report-triage-decisions.md",
        "report-triage-decisions.json",
        "selector-evidence.md",
        "selector-evidence.json",
        "runtime-feedback-loop.md",
        "runtime-feedback-loop.json",
        "runtime-next-tickets.md",
        "agent-runtime-failure-next-task.md",
        "explain-todo.md",
        "agent-next-task.md",
        "migration-board.md",
        "verify-project-report.md",
        "project-verify-report.md",
        "report.json",
        "report.txt"
    };

    static readonly string[] ExcludedDirectoryNames =
    {
        ".git", ".vs", ".idea", "bin", "obj", "node_modules", ".playwright", "playwright-report", "test-results"
    };

    public static int RunAgentContract(string inputPath, string outPath, string format, string[] configPaths)
    {
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            Console.Error.WriteLine($"agent contract expects a migration ticket/workspace/artifact directory or file: {inputPath}");
            return 1;
        }

        Directory.CreateDirectory(outPath);
        var report = BuildAgentContract(inputPath, configPaths);
        WriteAgentContract(report, outPath, format);

        Console.WriteLine("=== Agent Contract Pack ===");
        Console.WriteLine($"Input: {report.InputPath}");
        Console.WriteLine($"Ticket: {report.Ticket.Title}");
        Console.WriteLine($"Allowed paths: {report.AllowedPaths.Length}");
        Console.WriteLine($"Stop rules: {report.StopPolicy.Length}");
        Console.WriteLine($"Files written to: {Path.GetFullPath(outPath)}");
        return 0;
    }

    public static AgentContractPackReport BuildAgentContract(string inputPath, string[] configPaths)
    {
        var fullInput = Path.GetFullPath(inputPath);
        var root = File.Exists(fullInput) ? Path.GetDirectoryName(fullInput) ?? Directory.GetCurrentDirectory() : fullInput;
        var files = CollectArtifactFiles(fullInput).ToArray();
        var facts = files.Select(file => BuildArtifactFact(root, file)).ToArray();
        var ticket = BuildTicket(fullInput, facts);
        var allowedPaths = BuildAllowedPaths(root, facts, configPaths).ToArray();
        var boundaries = BuildSourceEditBoundaries(root).ToArray();
        var commands = BuildExactNextCommands(root, fullInput, ticket).ToArray();
        var roles = BuildRoles().ToArray();
        var stopPolicy = BuildStopPolicy().ToArray();
        var reportFormat = BuildReportFormat().ToArray();
        var handoffChecklist = BuildHandoffChecklist().ToArray();
        var warnings = BuildWarnings(facts, configPaths).ToArray();

        return new AgentContractPackReport(
            SchemaVersion: "agent-contract-pack/v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            InputPath: PathRedaction.Redact(fullInput),
            ArtifactRoot: PathRedaction.Redact(root),
            Ticket: ticket,
            SourceArtifacts: facts.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            AllowedPaths: allowedPaths,
            SourceEditBoundaries: boundaries,
            ExactNextCommands: commands,
            StopPolicy: stopPolicy,
            ReportFormat: reportFormat,
            Roles: roles,
            HandoffChecklist: handoffChecklist,
            Warnings: warnings);
    }

    static IEnumerable<string> CollectArtifactFiles(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            yield return Path.GetFullPath(inputPath);
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories))
        {
            if (IsExcludedPath(inputPath, file))
                continue;

            var name = Path.GetFileName(file);
            var ext = Path.GetExtension(file);
            if (PreferredArtifactNames.Any(x => name.Equals(x, StringComparison.OrdinalIgnoreCase))
                || ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.GetFullPath(file);
            }
        }
    }

    static AgentContractArtifact BuildArtifactFact(string root, string file)
    {
        var relative = SafeRelativePath(root, file);
        var text = ReadSmallText(file);
        var kind = InferArtifactKind(relative, text);
        var summary = BuildArtifactSummary(kind, text);
        var suggestedUse = kind switch
        {
            "ticket" => "Use as the task source of truth and preserve its acceptance criteria.",
            "runbook" => "Use for pilot scope, first commands, risks, and acceptance checklist.",
            "triage" => "Use for create-ticket/defer/accept decisions and next work ordering.",
            "selector-evidence" => "Use before changing selector mappings; do not invent missing selector evidence.",
            "runtime-feedback" => "Use for runtime root causes, readiness score, and smoke rerun scope.",
            "verify" => "Use for compile/project diagnostics and quality gates.",
            _ => "Use as supporting evidence only. Do not treat it as permission to edit source tests."
        };

        return new AgentContractArtifact(
            RelativePath: relative,
            Kind: kind,
            Summary: summary,
            SuggestedUse: suggestedUse);
    }

    static AgentContractTicket BuildTicket(string fullInput, IReadOnlyList<AgentContractArtifact> facts)
    {
        var ticketArtifact = facts.FirstOrDefault(x => x.Kind == "ticket") ?? facts.FirstOrDefault(x => x.Kind == "runbook") ?? facts.FirstOrDefault();
        var title = ticketArtifact == null
            ? "Migration ticket"
            : ExtractTitle(Path.Combine(File.Exists(fullInput) ? Path.GetDirectoryName(fullInput) ?? "." : fullInput, ticketArtifact.RelativePath), ticketArtifact.Kind);

        if (string.IsNullOrWhiteSpace(title))
            title = Path.GetFileNameWithoutExtension(fullInput.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var objective = ticketArtifact?.Kind switch
        {
            "runbook" => "Execute the next migration step from the runbook using evidence-first, source-safe changes.",
            "triage" => "Turn triage decisions into one small, reviewable migration improvement.",
            "runtime-feedback" => "Resolve the highest-impact runtime feedback item and validate with a narrow smoke rerun.",
            _ => "Complete one bounded migration improvement using the provided artifacts as source of truth."
        };

        var acceptance = new[]
        {
            "Changes are limited to allowed paths and are explained in the final report.",
            "Generated/source evidence is cited before adding selector or helper mappings.",
            "config-validate and the relevant verify command are run or the reason they cannot run is recorded.",
            "No source tests, target project files, production POM files, or project files are edited unless the human explicitly expands the boundary.",
            "TODO reduction is not counted as progress if it comes from suppression, empty tests, weakened assertions, dummy known identifiers, or real project edits.",
            "Final success is backed by scope-clean, quality-gate, and verification evidence.",
            "If runtime work is involved, a minimal smoke rerun plan is produced or executed."
        };

        return new AgentContractTicket(
            Title: title,
            Objective: objective,
            AcceptanceCriteria: acceptance);
    }

    static IEnumerable<AgentContractAllowedPath> BuildAllowedPaths(string root, IReadOnlyList<AgentContractArtifact> facts, string[] configPaths)
    {
        yield return new AgentContractAllowedPath("migration/**", "read-write", "Generated migration workspace, reports, ledgers, and agent outputs.");
        yield return new AgentContractAllowedPath("migration/proposals/**", "read-write", "Proposal patches or source-change tickets for forbidden real project changes.");

        foreach (var config in configPaths.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            yield return new AgentContractAllowedPath(PathRedaction.Redact(config), "read-write", "Explicitly provided adapter config layer. Keep changes small and reviewable.");

        foreach (var artifact in facts.Take(12))
            yield return new AgentContractAllowedPath(artifact.RelativePath, "read-only", $"Evidence artifact: {artifact.Kind}.");
    }

    static IEnumerable<string> BuildSourceEditBoundaries(string root)
    {
        yield return "Do not edit Selenium source tests, PageObjects, or product code unless the human explicitly asks for source edits.";
        yield return "Do not edit the real target project, production POM project, Playwright test project, .csproj files, nuget.config, or root-level generated files in artifact-only mode.";
        yield return "Do not edit generated Playwright files by hand as the primary fix; prefer config/profile/generator changes.";
        yield return "Do not invent selectors. Every selector mapping must cite POM/source/HTML/selector-evidence proof.";
        yield return "Do not suppress assertions broadly. FluentAssertions, NUnit assertions, assertion-like helpers, and business checks must not be suppressed to reduce TODO.";
        yield return "Do not count TODO reduction as progress when suppressions increased, generated tests became empty, assertions weakened, dummy target-known identifiers were added, or real project files changed.";
        yield return "Do not claim FINAL unless scope guard, quality gates, and verification evidence are recorded.";
        yield return "Do not widen allowed paths during the task. Stop and ask for a new contract when scope changes.";
        yield return $"Treat `{PathRedaction.Redact(root)}` as the artifact root, not as permission to edit everything under it.";
    }

    static IEnumerable<AgentContractCommandStep> BuildExactNextCommands(string root, string fullInput, AgentContractTicket ticket)
    {
        var quotedInput = QuoteForShell(fullInput);
        yield return new AgentContractCommandStep(1, "Read the contract", "cat migration/agent-contract/agent-contract.md", "Start with the task boundary, stop policy, and allowed paths.");
        yield return new AgentContractCommandStep(2, "Validate config", "dotnet run --project Migrator.Cli -- --mode config-validate --config ./adapter-config.json --validation-mode strict --out migration/agent-contract/config-validate", "Catch unsafe config changes before migration work.");
        yield return new AgentContractCommandStep(3, "Refresh runbook", $"dotnet run --project Migrator.Cli -- runbook --input {quotedInput} --out migration/runbook --format both", "Refresh the practical plan when the source/project shape changed.");
        yield return new AgentContractCommandStep(4, "Refresh selector evidence", $"dotnet run --project Migrator.Cli -- selector evidence --input {quotedInput} --config ./adapter-config.json --out migration/selector-evidence --format both", "Selector changes need source/config/generated proof.");
        yield return new AgentContractCommandStep(5, "Generate triage dashboard", $"dotnet run --project Migrator.Cli -- report serve --input {quotedInput} --static-only --out migration/report-dashboard --format both", "Export current TODO/root-cause/runtime decisions for review.");
        yield return new AgentContractCommandStep(6, "Pack evidence", $"dotnet run --project Migrator.Cli -- evidence pack --input {quotedInput} --out evidence/agent-contract.zip", "Create a reviewable redacted evidence bundle.");
    }

    static IEnumerable<AgentContractRole> BuildRoles()
    {
        yield return new AgentContractRole(
            "coordinator",
            "Choose the single next ticket, keep scope small, update the ledger, and stop when boundaries are exceeded.",
            new[] { "Read runbook/triage/runtime artifacts", "Pick one highest-impact task", "Assign migrator/verifier responsibilities", "Write final handoff" });
        yield return new AgentContractRole(
            "migrator",
            "Implement the bounded config/generator/docs/test change using evidence-first reasoning.",
            new[] { "Use selector evidence before mapping selectors", "Prefer config/profile/generator fixes over hand edits", "Keep diffs small", "Record commands run" });
        yield return new AgentContractRole(
            "verifier",
            "Run validation and inspect artifacts before accepting the change.",
            new[] { "Run dotnet test or targeted tests when available", "Run config-validate/verify", "Check no source boundary was crossed", "Summarize residual risk" });
    }

    static IEnumerable<AgentContractStopRule> BuildStopPolicy()
    {
        yield return new AgentContractStopRule("source-edit-boundary", "Stop if the fix requires editing Selenium source tests, product app code, or generated Playwright output directly.", "Ask the human for an expanded contract or propose a config/generator alternative.");
        yield return new AgentContractStopRule("forbidden-write", "Stop if the next step or current diff touches real target/POM/project files outside the migration workspace.", "Write a proposal under migration/proposals and reject the batch as artifact-only unsafe.");
        yield return new AgentContractStopRule("selector-evidence-gap", "Stop if a selector cannot be proven from source/POM/HTML/selector-evidence.", "Create a selector-evidence TODO and request source truth instead of inventing a locator.");
        yield return new AgentContractStopRule("broad-suppression", "Stop if the proposed fix hides assertions, FluentAssertions, NUnit checks, business checks, or broad helper categories.", "Use targeted mappings or failing guards; document why suppression is safe.");
        yield return new AgentContractStopRule("metric-gaming", "Stop if TODO decreases only because suppression increased, tests became empty, assertions weakened, dummy target-known identifiers were added, or real project files changed.", "Reject or revert the batch and write metric-gaming evidence.");
        yield return new AgentContractStopRule("missing-final-evidence", "Stop if FINAL is claimed without scope guard, config-validate/quality gate, and verify/project-verify evidence.", "Mark the result NOT FINAL - INVESTIGATION RESULT ONLY.");
        yield return new AgentContractStopRule("quality-regression", "Stop if TODO/unsupported/compile errors increase without explanation.", "Revert the risky change or split the task.");
        yield return new AgentContractStopRule("missing-tooling", "Stop if required validation cannot run in the environment.", "Report the exact command and missing tool instead of claiming validation passed.");
    }

    static IEnumerable<string> BuildReportFormat()
    {
        yield return "Summary: one paragraph with what changed and why.";
        yield return "Evidence used: list source/config/generated/runtime artifacts consulted.";
        yield return "Files changed: grouped by config, generator, tests, docs, artifacts.";
        yield return "Commands run: exact commands plus pass/fail status.";
        yield return "Scope guard: exact command/result and forbidden path status.";
        yield return "TODO/suppression movement: before/after counts and why progress is real.";
        yield return "Final gate: FINAL only if scope, quality, and verification evidence are PASS; otherwise say NOT FINAL - INVESTIGATION RESULT ONLY.";
        yield return "Risks and follow-ups: residual TODOs, unsafe selectors, runtime unknowns.";
        yield return "Stop-policy status: confirm no boundary was crossed, or state where work stopped.";
    }

    static IEnumerable<string> BuildHandoffChecklist()
    {
        yield return "Allowed paths respected.";
        yield return "Scope guard passed.";
        yield return "No invented selectors or unproven mappings.";
        yield return "No broad or assertion suppressions added to reduce TODO.";
        yield return "TODO reduction did not come from empty tests, weakened assertions, dummy target-known identifiers, or real project edits.";
        yield return "Final gate evidence recorded.";
        yield return "config-validate/verify/test results recorded.";
        yield return "Triage decisions or current-ticket updated when applicable.";
        yield return "Evidence pack or relevant artifacts are linked for review.";
    }

    static IEnumerable<string> BuildWarnings(IReadOnlyList<AgentContractArtifact> facts, string[] configPaths)
    {
        if (facts.All(x => x.Kind != "runbook"))
            yield return "No runbook artifact detected. Generate one before starting a large migration ticket.";
        if (facts.All(x => x.Kind != "selector-evidence"))
            yield return "No selector-evidence artifact detected. Selector mapping work must refresh selector evidence first.";
        if (facts.All(x => x.Kind != "triage"))
            yield return "No report-triage decisions detected. Run report serve 2.0 before assigning a broad cleanup task.";
        if (configPaths.Length == 0)
            yield return "No --config layer was provided. Config edits must identify the intended adapter-config path before writing.";
    }

    static void WriteAgentContract(AgentContractPackReport report, string outPath, string format)
    {
        var writeText = format is "text" or "both";
        var writeJson = format is "json" or "both";

        if (writeText)
        {
            File.WriteAllText(Path.Combine(outPath, "agent-contract.md"), RenderMarkdown(report));
            File.WriteAllText(Path.Combine(outPath, "allowed-paths.md"), RenderAllowedPaths(report));
            File.WriteAllText(Path.Combine(outPath, "stop-policy.md"), RenderStopPolicy(report));
            File.WriteAllText(Path.Combine(outPath, "next-commands.md"), RenderCommands(report));
            File.WriteAllText(Path.Combine(outPath, "report-template.md"), RenderReportTemplate(report));
        }

        if (writeJson)
            File.WriteAllText(Path.Combine(outPath, "agent-contract.json"), JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);

        var loopsDir = Path.Combine(outPath, ".agent-loops");
        Directory.CreateDirectory(loopsDir);
        File.WriteAllText(Path.Combine(loopsDir, "coordinator.md"), RenderRolePrompt(report, "coordinator"));
        File.WriteAllText(Path.Combine(loopsDir, "migrator.md"), RenderRolePrompt(report, "migrator"));
        File.WriteAllText(Path.Combine(loopsDir, "verifier.md"), RenderRolePrompt(report, "verifier"));
        File.WriteAllText(Path.Combine(loopsDir, "README.md"), RenderLoopReadme(report));
    }

    static string RenderMarkdown(AgentContractPackReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Agent Contract Pack");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{report.SchemaVersion}`");
        sb.AppendLine($"Input: `{report.InputPath}`");
        sb.AppendLine($"Artifact root: `{report.ArtifactRoot}`");
        sb.AppendLine();
        sb.AppendLine("## Ticket");
        sb.AppendLine($"- Title: {report.Ticket.Title}");
        sb.AppendLine($"- Objective: {report.Ticket.Objective}");
        sb.AppendLine();
        sb.AppendLine("## Acceptance criteria");
        foreach (var item in report.Ticket.AcceptanceCriteria)
            sb.AppendLine($"- {item}");
        sb.AppendLine();
        sb.AppendLine("## Allowed paths");
        foreach (var path in report.AllowedPaths)
            sb.AppendLine($"- `{path.Path}` — **{path.Access}** — {path.Reason}");
        sb.AppendLine();
        sb.AppendLine("## Source-edit boundaries");
        foreach (var boundary in report.SourceEditBoundaries)
            sb.AppendLine($"- {boundary}");
        sb.AppendLine();
        sb.AppendLine("## Exact next commands");
        foreach (var command in report.ExactNextCommands)
        {
            sb.AppendLine($"{command.Order}. **{command.Name}** — {command.Why}");
            sb.AppendLine();
            sb.AppendLine("```bash");
            sb.AppendLine(command.Command);
            sb.AppendLine("```");
            sb.AppendLine();
        }
        sb.AppendLine("## Multi-agent option");
        foreach (var role in report.Roles)
            sb.AppendLine($"- **{role.Name}**: {role.Mission}");
        sb.AppendLine();
        sb.AppendLine("## Stop policy");
        foreach (var rule in report.StopPolicy)
            sb.AppendLine($"- **{rule.Code}**: {rule.Condition} Next: {rule.RequiredAction}");
        sb.AppendLine();
        sb.AppendLine("## Warnings");
        if (report.Warnings.Length == 0)
            sb.AppendLine("- None.");
        foreach (var warning in report.Warnings)
            sb.AppendLine($"- {warning}");
        return sb.ToString();
    }

    static string RenderAllowedPaths(AgentContractPackReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Allowed Paths");
        sb.AppendLine();
        foreach (var path in report.AllowedPaths)
            sb.AppendLine($"- `{path.Path}` — **{path.Access}** — {path.Reason}");
        return sb.ToString();
    }

    static string RenderStopPolicy(AgentContractPackReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Stop Policy");
        sb.AppendLine();
        foreach (var rule in report.StopPolicy)
        {
            sb.AppendLine($"## {rule.Code}");
            sb.AppendLine($"Condition: {rule.Condition}");
            sb.AppendLine($"Required action: {rule.RequiredAction}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static string RenderCommands(AgentContractPackReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Exact Next Commands");
        sb.AppendLine();
        foreach (var command in report.ExactNextCommands)
        {
            sb.AppendLine($"## {command.Order}. {command.Name}");
            sb.AppendLine(command.Why);
            sb.AppendLine();
            sb.AppendLine("```bash");
            sb.AppendLine(command.Command);
            sb.AppendLine("```");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static string RenderReportTemplate(AgentContractPackReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Agent Final Report Template");
        sb.AppendLine();
        foreach (var item in report.ReportFormat)
            sb.AppendLine($"- {item}");
        sb.AppendLine();
        sb.AppendLine("## Handoff checklist");
        foreach (var item in report.HandoffChecklist)
            sb.AppendLine($"- [ ] {item}");
        return sb.ToString();
    }

    static string RenderRolePrompt(AgentContractPackReport report, string roleName)
    {
        var role = report.Roles.First(x => x.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
        var sb = new StringBuilder();
        sb.AppendLine($"# Agent role: {role.Name}");
        sb.AppendLine();
        sb.AppendLine(role.Mission);
        sb.AppendLine();
        sb.AppendLine("## Responsibilities");
        foreach (var item in role.Responsibilities)
            sb.AppendLine($"- {item}");
        sb.AppendLine();
        sb.AppendLine("## Contract boundaries");
        foreach (var boundary in report.SourceEditBoundaries)
            sb.AppendLine($"- {boundary}");
        sb.AppendLine();
        sb.AppendLine("## Stop policy");
        foreach (var rule in report.StopPolicy)
            sb.AppendLine($"- {rule.Code}: {rule.Condition}");
        sb.AppendLine();
        sb.AppendLine("## Final report format");
        foreach (var item in report.ReportFormat)
            sb.AppendLine($"- {item}");
        return sb.ToString();
    }

    static string RenderLoopReadme(AgentContractPackReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Ticket-specific agent loops");
        sb.AppendLine();
        sb.AppendLine("Use these prompts only with the generated `agent-contract.md` in the parent folder.");
        sb.AppendLine("They do not expand allowed paths and they do not permit source-test edits.");
        sb.AppendLine();
        sb.AppendLine("- `coordinator.md` — scope and handoff.");
        sb.AppendLine("- `migrator.md` — bounded implementation.");
        sb.AppendLine("- `verifier.md` — validation and risk review.");
        return sb.ToString();
    }

    static string InferArtifactKind(string relative, string text)
    {
        var name = Path.GetFileName(relative).ToLowerInvariant();
        if (name == "current-ticket.md" || relative.Contains("ticket", StringComparison.OrdinalIgnoreCase))
            return "ticket";
        if (name.Contains("runbook", StringComparison.OrdinalIgnoreCase))
            return "runbook";
        if (name.Contains("triage", StringComparison.OrdinalIgnoreCase))
            return "triage";
        if (name.Contains("selector-evidence", StringComparison.OrdinalIgnoreCase))
            return "selector-evidence";
        if (name.Contains("runtime-feedback", StringComparison.OrdinalIgnoreCase) || text.Contains("RuntimeReadinessScore", StringComparison.OrdinalIgnoreCase))
            return "runtime-feedback";
        if (name.Contains("verify", StringComparison.OrdinalIgnoreCase))
            return "verify";
        if (name.Contains("report", StringComparison.OrdinalIgnoreCase))
            return "report";
        return "supporting";
    }

    static string BuildArtifactSummary(string kind, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return $"{kind} artifact";

        var firstHeading = text.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith("#", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(firstHeading))
            return firstHeading.TrimStart('#', ' ').Trim();

        var firstLine = text.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0);
        return firstLine == null ? $"{kind} artifact" : TrimTo(firstLine, 120);
    }

    static string ExtractTitle(string path, string kind)
    {
        var text = ReadSmallText(path);
        var heading = text.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith("#", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(heading))
            return heading.TrimStart('#', ' ').Trim();
        return CultureTitle(kind);
    }

    static string ReadSmallText(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length > 2_000_000)
                return "";
            return File.ReadAllText(file);
        }
        catch
        {
            return "";
        }
    }

    static bool IsExcludedPath(string root, string file)
    {
        var relative = SafeRelativePath(root, file).Replace('\\', '/');
        return relative.Split('/').Any(segment => ExcludedDirectoryNames.Any(excluded => segment.Equals(excluded, StringComparison.OrdinalIgnoreCase)));
    }

    static string SafeRelativePath(string root, string file)
    {
        try
        {
            return Path.GetRelativePath(root, file).Replace('\\', '/');
        }
        catch
        {
            return Path.GetFileName(file);
        }
    }

    static string QuoteForShell(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    static string TrimTo(string value, int max) => value.Length <= max ? value : value[..Math.Max(0, max - 1)] + "…";

    static string CultureTitle(string value) => Regex.Replace(value.Replace('-', ' '), @"\b\w", m => m.Value.ToUpperInvariant());
}

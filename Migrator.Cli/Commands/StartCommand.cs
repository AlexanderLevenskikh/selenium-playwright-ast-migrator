using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Migrator.Core.SourceFrontends;

internal static class StartCommand
{
    public static int RunFromOptions(string inputPath, string outPath, string format, string workspace, string agent, string target, string? targetTestFramework, string? generationPolicy, string? targetProjectPath)
    {
        var sourcePath = inputPath;
        if (string.IsNullOrWhiteSpace(sourcePath) && IsInteractiveConsole())
            sourcePath = Prompt("Where are Selenium tests?", "./Tests");

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            PrintHelp();
            Console.Error.WriteLine("start needs a Selenium tests path. Pass --input ./OldTests or --source-path ./OldTests.");
            return 1;
        }

        sourcePath = sourcePath.Trim();
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Selenium tests path not found: {sourcePath}");
            return 1;
        }

        agent = NormalizeAgent(agent);

        target = NormalizeTarget(target);
        var framework = string.IsNullOrWhiteSpace(targetTestFramework) ? "nunit" : targetTestFramework!.Trim().ToLowerInvariant();
        var policy = string.IsNullOrWhiteSpace(generationPolicy) ? "balanced" : generationPolicy!.Trim().ToLowerInvariant();
        var fullWorkspace = Path.GetFullPath(string.IsNullOrWhiteSpace(workspace) ? "migration" : workspace);
        Directory.CreateDirectory(fullWorkspace);
        Directory.CreateDirectory(outPath);
        Directory.CreateDirectory(Path.Combine(fullWorkspace, "profiles"));
        Directory.CreateDirectory(Path.Combine(fullWorkspace, "state"));

        var detection = DetectSource(sourcePath);
        var hasTargetProject = !string.IsNullOrWhiteSpace(targetProjectPath);
        var profilePath = Path.Combine(fullWorkspace, "profiles", "adapter-config.start.json");
        var nextCommands = BuildNextCommands(sourcePath, fullWorkspace, agent, target, framework, policy, targetProjectPath);
        var report = new StartWizardReport(
            SchemaVersion: "start-wizard/v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            SourcePath: Path.GetFullPath(sourcePath),
            WorkspacePath: fullWorkspace,
            DetectedSource: detection.Source,
            DetectionConfidence: detection.Confidence,
            Agent: agent,
            Target: target,
            TargetTestFramework: framework,
            GenerationPolicy: policy,
            HasTargetProject: hasTargetProject,
            TargetProjectPath: string.IsNullOrWhiteSpace(targetProjectPath) ? null : Path.GetFullPath(targetProjectPath!),
            ProfileSkeleton: profilePath,
            NextCommands: nextCommands);

        WriteProfileSkeleton(profilePath, report);
        WriteWorkspaceReadme(fullWorkspace, report);
        WriteNextCommands(Path.Combine(fullWorkspace, "next-commands.md"), report);
        WriteCurrentTicket(fullWorkspace, report);
        WriteStartDispatchState(fullWorkspace, report);
        WriteStartSummary(outPath, report, format);

        Console.WriteLine("=== Migrator Start ===");
        Console.WriteLine($"Source: {report.SourcePath}");
        Console.WriteLine($"Detected source: {report.DetectedSource} ({report.DetectionConfidence})");
        Console.WriteLine($"Workspace: {report.WorkspacePath}");
        Console.WriteLine($"Profile skeleton: {report.ProfileSkeleton}");
        Console.WriteLine($"Agent route: {report.Agent}");
        Console.WriteLine();
        Console.WriteLine("Next commands:");
        foreach (var command in report.NextCommands.Take(5))
            Console.WriteLine($"  {command}");
        Console.WriteLine();
        Console.WriteLine($"Start summary written to: {Path.GetFullPath(outPath)}");
        return 0;
    }

    static SourceDetection DetectSource(string sourcePath)
    {
        try
        {
            var detection = SourceAutoDetector.Detect(sourcePath);
            return new SourceDetection(detection.DetectedSourceId, detection.Confidence.ToString());
        }
        catch
        {
            return new SourceDetection("csharp-selenium", "unknown");
        }
    }

    static string[] BuildNextCommands(string sourcePath, string workspace, string agent, string target, string framework, string policy, string? targetProjectPath)
    {
        var source = ToCommandPath(sourcePath);
        var ws = ToCommandPath(workspace);
        var commands = new List<string>
        {
            "selenium-pw-migrator doctor install",
        };

        if (!string.Equals(agent, "manual", StringComparison.OrdinalIgnoreCase) && !string.Equals(agent, "none", StringComparison.OrdinalIgnoreCase))
            commands.Add($"selenium-pw-migrator kit bootstrap-agent --agent {agent} --workspace {Quote(ws)} --source {Quote(source)}");

        commands.Add($"selenium-pw-migrator pilot --input {Quote(source)} --max-tests 10 --out {Quote(ToCommandPath(Path.Combine(workspace, "pilot")))}");
        commands.Add($"selenium-pw-migrator --mode doctor --input {Quote(source)} --out {Quote(ToCommandPath(Path.Combine(workspace, "doctor")))}");

        if (string.Equals(agent, "manual", StringComparison.OrdinalIgnoreCase) || string.Equals(agent, "none", StringComparison.OrdinalIgnoreCase))
            commands.Add($"selenium-pw-migrator --mode migrate --input {Quote(source)} --config {Quote(ToCommandPath(Path.Combine(workspace, "profiles", "adapter-config.start.json")))} --target {target} --target-test-framework {framework} --generation-policy {policy} --out {Quote(ToCommandPath(Path.Combine(workspace, "run-001")))}");
        else
            commands.Add("# After bootstrap, open the agent environment and run /supervised-task. It should use migration/current-ticket.md instead of asking what to do.");

        if (!string.IsNullOrWhiteSpace(targetProjectPath))
            commands.Add($"selenium-pw-migrator --mode discover-target --input {Quote(ToCommandPath(targetProjectPath!))} --out {Quote(ToCommandPath(Path.Combine(workspace, "target-discovery")))}");

        commands.Add($"# After a migrate/agent run exists, open the dashboard from run artifacts:");
        commands.Add($"selenium-pw-migrator report serve --input {Quote(ToCommandPath(Path.Combine(workspace, "runs", "latest")))} --out {Quote(ToCommandPath(Path.Combine(workspace, "dashboard", "latest")))} --static-only");
        return commands.ToArray();
    }

    static void WriteProfileSkeleton(string path, StartWizardReport report)
    {
        var json = new
        {
            SchemaVersion = "adapter-config/start-skeleton/v1",
            Notes = new[]
            {
                "Generated by selenium-pw-migrator start.",
                "Keep this as a project-owned profile layer and review every mapping before using aggressive generation.",
                "Run pilot/explain-todo to replace TODO-heavy patterns with evidence-backed mappings."
            },
            Target = report.Target,
            TargetTestFramework = report.TargetTestFramework,
            GenerationPolicy = report.GenerationPolicy,
            Source = new { Path = report.SourcePath, Detected = report.DetectedSource, Confidence = report.DetectionConfidence },
            UiTargets = Array.Empty<object>(),
            Methods = Array.Empty<object>(),
            ParameterizedMethods = Array.Empty<object>(),
            Tables = Array.Empty<object>(),
            Verification = new
            {
                TargetProjectPath = report.TargetProjectPath,
                ProjectReferences = Array.Empty<object>(),
                PackageReferences = Array.Empty<object>()
            }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
    }

    static void WriteWorkspaceReadme(string workspace, StartWizardReport report)
    {
        var path = Path.Combine(workspace, "README.start.md");
        var sb = new StringBuilder();
        sb.AppendLine("# Migration start workspace");
        sb.AppendLine();
        sb.AppendLine("This workspace was created by `selenium-pw-migrator start`.");
        sb.AppendLine();
        sb.AppendLine("## What was detected");
        sb.AppendLine();
        sb.AppendLine($"- Source: `{report.SourcePath}`");
        sb.AppendLine($"- Detected source: `{report.DetectedSource}` (`{report.DetectionConfidence}`)");
        sb.AppendLine($"- Agent route: `{report.Agent}`");
        sb.AppendLine($"- Target: `{report.Target}` / `{report.TargetTestFramework}`");
        sb.AppendLine($"- Profile skeleton: `{report.ProfileSkeleton}`");
        sb.AppendLine();
        sb.AppendLine("## Next commands");
        sb.AppendLine();
        foreach (var command in report.NextCommands)
            sb.AppendLine($"```shell\n{command}\n```");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    static void WriteNextCommands(string path, StartWizardReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Next Commands");
        sb.AppendLine();
        sb.AppendLine("Run these in order. The pilot comes before full migration so you can fix high-impact mappings first.");
        sb.AppendLine();
        for (var i = 0; i < report.NextCommands.Length; i++)
        {
            sb.AppendLine($"## {i + 1}. Step");
            sb.AppendLine();
            sb.AppendLine("```shell");
            sb.AppendLine(report.NextCommands[i]);
            sb.AppendLine("```");
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    static void WriteStartSummary(string outPath, StartWizardReport report, string format)
    {
        Directory.CreateDirectory(outPath);
        if (format is "json" or "both")
            File.WriteAllText(Path.Combine(outPath, "start-summary.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
        if (format is "text" or "both")
            File.WriteAllText(Path.Combine(outPath, "start-summary.md"), BuildStartMarkdown(report), new UTF8Encoding(false));
    }

    static string BuildStartMarkdown(StartWizardReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Start Summary");
        sb.AppendLine();
        sb.AppendLine("`selenium-pw-migrator start` selected the first safe migration route.");
        sb.AppendLine();
        sb.AppendLine("## Inputs");
        sb.AppendLine();
        sb.AppendLine($"- Source: `{report.SourcePath}`");
        sb.AppendLine($"- Workspace: `{report.WorkspacePath}`");
        sb.AppendLine($"- Source detection: `{report.DetectedSource}` / `{report.DetectionConfidence}`");
        sb.AppendLine($"- Agent: `{report.Agent}`");
        sb.AppendLine($"- Target: `{report.Target}` / `{report.TargetTestFramework}`");
        if (!string.IsNullOrWhiteSpace(report.TargetProjectPath))
            sb.AppendLine($"- Target project: `{report.TargetProjectPath}`");
        sb.AppendLine();
        sb.AppendLine("## Created");
        sb.AppendLine();
        sb.AppendLine($"- Profile skeleton: `{report.ProfileSkeleton}`");
        sb.AppendLine($"- Workspace readme: `{Path.Combine(report.WorkspacePath, "README.start.md")}`");
        sb.AppendLine($"- Next commands: `{Path.Combine(report.WorkspacePath, "next-commands.md")}`");
        sb.AppendLine();
        sb.AppendLine("## Next commands");
        sb.AppendLine();
        foreach (var command in report.NextCommands)
        {
            sb.AppendLine("```shell");
            sb.AppendLine(command);
            sb.AppendLine("```");
        }
        return sb.ToString();
    }


    static void WriteCurrentTicket(string workspace, StartWizardReport report)
    {
        var path = Path.Combine(workspace, "current-ticket.md");
        if (File.Exists(path))
            return;

        var sb = new StringBuilder();
        sb.AppendLine("# Current Ticket");
        sb.AppendLine();
        sb.AppendLine("Auto-selected by `selenium-pw-migrator start`.");
        sb.AppendLine();
        sb.AppendLine("## Goal");
        sb.AppendLine();
        sb.AppendLine("Create a bounded pilot slice, inspect the first migration evidence, and fix only the highest-impact mapping/scaffold issue before scaling.");
        sb.AppendLine();
        sb.AppendLine("## First safe actions");
        sb.AppendLine();
        sb.AppendLine("1. Run `selenium-pw-migrator doctor install`.");
        if (!string.Equals(report.Agent, "manual", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine($"2. Ensure the agent handoff is bootstrapped for `{report.Agent}` with `kit bootstrap-agent` if `AGENT_CONTRACT.md` is missing.");
        sb.AppendLine("3. Run the pilot command from `next-commands.md` and use `pilot/selected-input` as the first migration input.");
        sb.AppendLine("4. Do not ask the user to choose among unrelated repository maintenance tasks unless these migration artifacts are missing or contradictory.");
        sb.AppendLine();
        sb.AppendLine("## Allowed roots");
        sb.AppendLine();
        sb.AppendLine("- `migration/**`");
        sb.AppendLine("- generated pilot/report artifacts under the configured workspace");
        sb.AppendLine();
        sb.AppendLine("## Stop conditions");
        sb.AppendLine();
        sb.AppendLine("Stop only for missing source path, denied writes, failed guard/policy checks, missing required credentials/project references, or a contradictory state file.");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    static void WriteStartDispatchState(string workspace, StartWizardReport report)
    {
        var path = Path.Combine(workspace, "state", "start-dispatch.json");
        var json = new
        {
            SchemaVersion = "start-dispatch/v1",
            CreatedBy = "selenium-pw-migrator start",
            report.Agent,
            report.SourcePath,
            report.WorkspacePath,
            NextAction = "Run doctor install, ensure agent handoff bootstrap exists, then run pilot on the selected slice.",
            DoNotAskMenu = true,
            NextCommands = report.NextCommands
        };
        File.WriteAllText(path, JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
    }

    static string NormalizeAgent(string agent)
    {
        agent = string.IsNullOrWhiteSpace(agent) ? "opencode" : agent.Trim().ToLowerInvariant();
        return agent == "none" ? "manual" : agent;
    }

    static string NormalizeTarget(string target)
    {
        target = string.IsNullOrWhiteSpace(target) ? "dotnet" : target.Trim().ToLowerInvariant();
        return target switch
        {
            "playwright-dotnet" => "dotnet",
            "playwright-typescript" => "ts",
            _ => target
        };
    }

    static bool IsInteractiveConsole() => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    static string Prompt(string label, string fallback)
    {
        Console.Write($"{label} [{fallback}]: ");
        var value = Console.ReadLine();
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    static string PromptChoice(string label, string[] choices, string fallback)
    {
        Console.WriteLine(label);
        for (var i = 0; i < choices.Length; i++)
            Console.WriteLine($"  {i + 1}. {choices[i]}");
        Console.Write($"Choose [{fallback}]: ");
        var value = Console.ReadLine();
        if (int.TryParse(value, out var index) && index >= 1 && index <= choices.Length)
            return choices[index - 1];
        return choices.Contains(value ?? "", StringComparer.OrdinalIgnoreCase) ? value!.Trim().ToLowerInvariant() : fallback;
    }

    static string ToCommandPath(string path)
    {
        if (!Path.IsPathRooted(path))
            return NormalizeSeparators(path);

        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        if (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative))
            return NormalizeSeparators(relative);

        return NormalizeSeparators(path);
    }

    static string NormalizeSeparators(string path) => path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    static void PrintHelp()
    {
        Console.WriteLine("""
Usage:
  selenium-pw-migrator start --input <selenium-tests> [--agent opencode|codex|generic|manual] [--workspace migration]

Examples:
  selenium-pw-migrator start --input ./OldTests --agent opencode
  selenium-pw-migrator start --source-path ./OldTests --agent codex --target-project ./PlaywrightTests
""");
    }

    record SourceDetection(string Source, string Confidence);
}

record StartWizardReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string SourcePath,
    string WorkspacePath,
    string DetectedSource,
    string DetectionConfidence,
    string Agent,
    string Target,
    string TargetTestFramework,
    string GenerationPolicy,
    bool HasTargetProject,
    string? TargetProjectPath,
    string ProfileSkeleton,
    string[] NextCommands);

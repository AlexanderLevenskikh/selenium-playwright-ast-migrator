using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

internal static class KitCommand
{
    const string KitVersion = "0.5.1";

    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        if (args.Skip(1).Any(IsHelp))
        {
            PrintHelp();
            return 0;
        }

        var options = KitOptions.Parse(args.Skip(1).ToArray(), out var error);
        if (options == null)
        {
            Console.Error.WriteLine(error);
            PrintHelp();
            return 2;
        }

        return command switch
        {
            "init" => RunInit(options with { Update = false }),
            "update" => RunInit(options with { Update = true }),
            "doctor" => RunDoctor(options),
            "next-ticket" => RunNextTicket(options),
            _ => UnknownCommand(command)
        };
    }

    static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown kit command: {command}");
        PrintHelp();
        return 2;
    }

    static int RunInit(KitOptions options)
    {
        var projectRoot = Directory.GetCurrentDirectory();
        var kitRoot = ResolveKitRoot();
        var templateRoot = Path.Combine(kitRoot, "templates", "migration-kit");
        if (!Directory.Exists(templateRoot))
        {
            Console.Error.WriteLine($"Migration kit templates were not found: {templateRoot}");
            return 2;
        }

        var workspacePath = ToAbsolutePath(options.Workspace, projectRoot);
        var tokens = BuildTokens(options);

        Console.WriteLine(options.Update ? "Updating migration kit workspace" : "Installing migration kit workspace");
        Console.WriteLine($"Kit version:  {KitVersion}");
        Console.WriteLine($"Kit root:     {kitRoot}");
        Console.WriteLine($"Project root: {projectRoot}");
        Console.WriteLine($"Workspace:    {workspacePath}");
        Console.WriteLine();

        if (options.Backup && Directory.Exists(workspacePath))
            CreateBackup(workspacePath, projectRoot);

        foreach (var dir in new[]
        {
            "runs", "reports", "logs", "profiles", "prompts", "schemas", "state",
            "tickets", "evidence", "proposals", "scripts", "codex", ".migration-kit"
        })
        {
            Directory.CreateDirectory(Path.Combine(workspacePath, dir));
        }

        CopyDirectoryContents(templateRoot, workspacePath, tokens, workspacePath, options);

        var schemaSource = Path.Combine(kitRoot, "schemas", "adapter-config.schema.json");
        if (File.Exists(schemaSource))
        {
            WriteTextFileSafe(
                Path.Combine(workspacePath, "schemas", "adapter-config.schema.json"),
                File.ReadAllText(schemaSource),
                workspacePath,
                options,
                neverOverwrite: false);
        }

        if (!options.NoCodexFiles)
        {
            var codexSource = Path.Combine(kitRoot, "templates", "codex");
            if (Directory.Exists(codexSource))
                CopyDirectoryContents(codexSource, Path.Combine(workspacePath, "codex"), tokens, workspacePath, options);
        }

        if (options.WithTeam)
        {
            var teamSource = Path.Combine(kitRoot, "templates", "opencode-team");
            if (Directory.Exists(teamSource))
            {
                CopyDirectoryContents(teamSource, Path.Combine(workspacePath, "opencode-team"), tokens, workspacePath, options);

                var agentsTemplate = Path.Combine(teamSource, "project-template", "AGENTS.md");
                if (File.Exists(agentsTemplate))
                {
                    WriteTemplatedFileSafe(
                        agentsTemplate,
                        Path.Combine(projectRoot, "AGENTS.md"),
                        tokens,
                        workspacePath,
                        options,
                        neverOverwrite: false);
                }
            }
        }

        if (options.WithLoopLibrary)
        {
            var loopSource = Path.Combine(kitRoot, "templates", "loops-library");
            if (Directory.Exists(loopSource))
                CopyDirectoryContents(loopSource, Path.Combine(workspacePath, "loops-library"), tokens, workspacePath, options);
        }

        if (!options.NoRootAgentFiles)
        {
            CopyRootAgentDirectory(Path.Combine(kitRoot, ".agent-loops"), Path.Combine(projectRoot, ".agent-loops"), workspacePath, options);
        }

        WriteQuickStart(workspacePath, options);
        WriteVersionFile(workspacePath, options, updateMode: options.Update);

        Console.WriteLine();
        Console.WriteLine("Migration kit workspace ready.");
        Console.WriteLine($"Open: {Path.Combine(workspacePath, "QUICKSTART.md")}");
        Console.WriteLine($"Agent kickoff: {Path.Combine(workspacePath, "prompts", "kickoff-prompt.txt")}");
        Console.WriteLine();
        Console.WriteLine("Recommended next command:");
        Console.WriteLine($"  {options.ToolCommand} kit doctor --workspace {Quote(options.Workspace)}");
        return 0;
    }

    static int RunDoctor(KitOptions options)
    {
        var projectRoot = Directory.GetCurrentDirectory();
        var workspacePath = ToAbsolutePath(options.Workspace, projectRoot);
        var checks = new List<KitDoctorCheck>();

        AddCheck(checks, "workspace", Directory.Exists(workspacePath), workspacePath, "Run `migrator kit init --workspace <path>` first.");
        AddCheck(checks, "version", File.Exists(Path.Combine(workspacePath, ".migration-kit", "version.json")), Path.Combine(workspacePath, ".migration-kit", "version.json"), "Run `migrator kit update --workspace <path>`.");
        AddCheck(checks, "adapter-config", File.Exists(ToAbsolutePath(options.Config, projectRoot)), ToAbsolutePath(options.Config, projectRoot), "Create or copy adapter-config.json into migration/profiles/.");
        AddCheck(checks, "kickoff-prompt", File.Exists(Path.Combine(workspacePath, "prompts", "kickoff-prompt.txt")), Path.Combine(workspacePath, "prompts", "kickoff-prompt.txt"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "loop-batch-prompt", File.Exists(Path.Combine(workspacePath, "prompts", "loop-batch-prompt.txt")), Path.Combine(workspacePath, "prompts", "loop-batch-prompt.txt"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "state-handoff", File.Exists(Path.Combine(workspacePath, "state", "handoff.md")), Path.Combine(workspacePath, "state", "handoff.md"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "stop-policy-checklist", File.Exists(Path.Combine(workspacePath, "state", "stop-policy-checklist.md")), Path.Combine(workspacePath, "state", "stop-policy-checklist.md"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "schema", File.Exists(Path.Combine(workspacePath, "schemas", "adapter-config.schema.json")), Path.Combine(workspacePath, "schemas", "adapter-config.schema.json"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "codex-files", File.Exists(Path.Combine(workspacePath, "codex", "CODEX.md")), Path.Combine(workspacePath, "codex", "CODEX.md"), "Run `migrator kit update --backup` without --no-codex-files.");
        AddCheck(checks, "agent-contract", File.Exists(Path.Combine(workspacePath, "AGENT_CONTRACT.md")), Path.Combine(workspacePath, "AGENT_CONTRACT.md"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "final-gate", File.Exists(Path.Combine(workspacePath, "state", "final-gate.md")), Path.Combine(workspacePath, "state", "final-gate.md"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "scope-guard", File.Exists(Path.Combine(workspacePath, "scripts", "check-scope.ps1")), Path.Combine(workspacePath, "scripts", "check-scope.ps1"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "final-gate-script", File.Exists(Path.Combine(workspacePath, "scripts", "check-final-gate.ps1")), Path.Combine(workspacePath, "scripts", "check-final-gate.ps1"), "Run `migrator kit update --backup`.");

        var dotnet = RunProcess("dotnet", "--version");
        AddCheck(checks, "dotnet", dotnet.ExitCode == 0, dotnet.ExitCode == 0 ? dotnet.StdOut.Trim() : dotnet.StdErr.Trim(), "Install .NET SDK or use a self-contained migrator bundle.");

        var status = checks.All(c => c.Ok) ? "passed" : "warning";
        var reportDir = Path.Combine(workspacePath, "reports", "kit-doctor");
        Directory.CreateDirectory(reportDir);
        File.WriteAllText(Path.Combine(reportDir, "kit-doctor.md"), WriteDoctorMarkdown(status, checks));
        File.WriteAllText(Path.Combine(reportDir, "kit-doctor.json"), JsonSerializer.Serialize(new KitDoctorReport(DateTimeOffset.UtcNow, status, checks), new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Kit doctor: {status.ToUpperInvariant()}");
        foreach (var check in checks)
            Console.WriteLine($"{(check.Ok ? "OK" : "WARN"),-5} {check.Name,-18} {check.Detail}");
        Console.WriteLine($"Report: {Path.Combine(reportDir, "kit-doctor.md")}");

        return checks.Count(c => !c.Ok && c.Name is "workspace" or "adapter-config") > 0 ? 2 : 0;
    }

    static int RunNextTicket(KitOptions options)
    {
        var projectRoot = Directory.GetCurrentDirectory();
        var workspacePath = ToAbsolutePath(options.Workspace, projectRoot);
        if (!Directory.Exists(workspacePath))
        {
            Console.Error.WriteLine($"Workspace not found: {workspacePath}");
            return 2;
        }

        var input = !string.IsNullOrWhiteSpace(options.Input)
            ? ToAbsolutePath(options.Input!, projectRoot)
            : FindLatestRunDirectory(workspacePath) ?? workspacePath;

        var prompt = BuildNextTicketPrompt(workspacePath, input, options);
        var outputPath = Path.Combine(workspacePath, "prompts", "generated-next-ticket-prompt.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, prompt);

        var currentTicket = Path.Combine(workspacePath, "current-ticket.md");
        if (!File.Exists(currentTicket) || options.Force)
        {
            File.WriteAllText(currentTicket, "# Current ticket\n\nStatus: NEEDS_TRIAGE\n\nUse `migration/prompts/generated-next-ticket-prompt.txt` to produce the next actionable ticket.\n");
        }

        Console.WriteLine("Generated next-ticket prompt:");
        Console.WriteLine(outputPath);
        Console.WriteLine();
        Console.WriteLine("Give this file to the agent, or paste its contents into Codex/OpenCode.");
        return 0;
    }

    static string BuildNextTicketPrompt(string workspacePath, string inputPath, KitOptions options)
    {
        var files = Directory.Exists(inputPath)
            ? Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories).Select(Path.GetFileName).Where(x => x != null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray()
            : Array.Empty<string>();

        var knownArtifacts = files.Where(f =>
            f.Equals("report.json", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("verify-report.json", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("project-verify-report.json", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("explain-todo.json", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("migration-quality-dashboard.json", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("migration-quality-tickets.md", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("smoke-plan.json", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("migration-board.md", StringComparison.OrdinalIgnoreCase) ||
            f.Equals("migration-board.html", StringComparison.OrdinalIgnoreCase)).ToArray();

        return $$"""
# Agent task: produce the next actionable migration ticket

Workspace:

```text
{{workspacePath}}
```

Artifacts to inspect:

```text
{{inputPath}}
```

Known artifact files detected:

{{(knownArtifacts.Length == 0 ? "- No standard artifact files detected; inspect the directory manually." : string.Join(Environment.NewLine, knownArtifacts.Select(x => "- " + x)))}}

## Goal

Do not write code yet. Analyze the latest migration artifacts and produce one bounded next ticket.

Prefer a root-cause ticket over many downstream TODO edits. Do not hide errors by adding broad `TargetKnownIdentifiers` or broad suppressions. Do not ask the user whether to continue; produce one bounded ticket or a stop-policy-backed blocker.

## Required output

### Findings

### Evidence

Include representative examples with file, line, source snippet, generated snippet, and TODO/build category.

### Root cause groups

### Recommended next ticket

Include title, root cause, expected output, whether it is CONFIG_FIX / ENGINE_FIX / TARGET_INFRA / MANUAL, and regression tests to add.

### Safety checks

State why this does not make tests falsely green and which manual-review TODOs must remain.

### Expected impact

Estimate TODO/build/runtime-readiness impact and how to verify it.

## State files to update

- `migration/current-ticket.md`
- `migration/state/handoff.md`
- `migration/state/decision-log.md`
- `migration/state/run-ledger.md`
- `migration/state/stop-policy-checklist.md`
""";
    }

    static void AddCheck(List<KitDoctorCheck> checks, string name, bool ok, string detail, string recommendation)
        => checks.Add(new KitDoctorCheck(name, ok, detail, recommendation));

    static string WriteDoctorMarkdown(string status, IReadOnlyList<KitDoctorCheck> checks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Migration Kit Doctor");
        sb.AppendLine();
        sb.AppendLine($"Status: **{status}**");
        sb.AppendLine();
        sb.AppendLine("| Check | Status | Detail | Recommendation |");
        sb.AppendLine("|---|---:|---|---|");
        foreach (var check in checks)
            sb.AppendLine($"| {EscapeMd(check.Name)} | {(check.Ok ? "OK" : "WARN")} | {EscapeMd(check.Detail)} | {EscapeMd(check.Recommendation)} |");
        return sb.ToString();
    }

    static string EscapeMd(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    static void CopyDirectoryContents(string sourceDir, string destDir, IReadOnlyDictionary<string, string> tokens, string workspacePath, KitOptions options)
    {
        if (!Directory.Exists(sourceDir))
            return;

        foreach (var sourcePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, sourcePath);
            var destPath = Path.Combine(destDir, relative);
            var neverOverwrite = IsWorkspaceMutableFile(relative);
            WriteTemplatedFileSafe(sourcePath, destPath, tokens, workspacePath, options, neverOverwrite);
        }
    }

    static void CopyRootAgentDirectory(string sourceDir, string destDir, string workspacePath, KitOptions options)
    {
        if (!Directory.Exists(sourceDir))
            return;

        foreach (var sourcePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, sourcePath);
            var destPath = Path.Combine(destDir, relative);
            WriteTextFileSafe(destPath, File.ReadAllText(sourcePath), workspacePath, options, neverOverwrite: false);
        }
    }

    static void WriteTemplatedFileSafe(string sourcePath, string destPath, IReadOnlyDictionary<string, string> tokens, string workspacePath, KitOptions options, bool neverOverwrite)
    {
        var content = File.ReadAllText(sourcePath);
        foreach (var pair in tokens)
            content = content.Replace("{{" + pair.Key + "}}", pair.Value);
        WriteTextFileSafe(destPath, content, workspacePath, options, neverOverwrite);
    }

    static void WriteTextFileSafe(string destPath, string content, string workspacePath, KitOptions options, bool neverOverwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        if (!File.Exists(destPath))
        {
            File.WriteAllText(destPath, content);
            Console.WriteLine($"write: {destPath}");
            return;
        }

        var existing = File.ReadAllText(destPath);
        if (existing == content)
        {
            Console.WriteLine($"unchanged: {destPath}");
            return;
        }

        if (options.Force && !neverOverwrite)
        {
            File.WriteAllText(destPath, content);
            Console.WriteLine($"overwrite: {destPath}");
            return;
        }

        if (options.Update)
        {
            var updatesRoot = Path.Combine(workspacePath, ".migration-kit", "updates", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            var relative = SafeRelativePath(workspacePath, destPath);
            var updatePath = Path.Combine(updatesRoot, relative + ".new");
            Directory.CreateDirectory(Path.GetDirectoryName(updatePath)!);
            File.WriteAllText(updatePath, content);
            Console.WriteLine($"conflict -> new: {updatePath}");
            return;
        }

        Console.WriteLine($"skip existing: {destPath}");
    }

    static void CreateBackup(string workspacePath, string projectRoot)
    {
        var backupRoot = Path.Combine(workspacePath, ".migration-kit", "backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backupRoot);

        if (Directory.Exists(workspacePath))
        {
            var workspaceBackup = Path.Combine(backupRoot, "workspace");
            Directory.CreateDirectory(workspaceBackup);
            foreach (var entry in Directory.EnumerateFileSystemEntries(workspacePath).Where(x => Path.GetFileName(x) != ".migration-kit"))
                CopyFileSystemEntry(entry, Path.Combine(workspaceBackup, Path.GetFileName(entry)));
        }

        foreach (var name in new[] { ".agent-loops", ".agent-state", "AGENTS.md" })
        {
            var path = Path.Combine(projectRoot, name);
            if (File.Exists(path) || Directory.Exists(path))
                CopyFileSystemEntry(path, Path.Combine(backupRoot, name));
        }

        Console.WriteLine($"backup: {backupRoot}");
    }

    static void CopyFileSystemEntry(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            Directory.CreateDirectory(destination);
            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
                CopyFileSystemEntry(entry, Path.Combine(destination, Path.GetFileName(entry)));
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    static void WriteQuickStart(string workspacePath, KitOptions options)
    {
        var quickStart = $$"""
# Quickstart

Workspace installed at: `{{options.Workspace}}`
Kit version: {{KitVersion}}

## Cross-platform CLI

Initial install/update should now work on Windows, macOS and Linux through the CLI:

```bash
{{options.ToolCommand}} kit init --workspace "{{options.Workspace}}" --source "{{options.Source}}" --config "{{options.Config}}" --out "{{options.Output}}"
{{options.ToolCommand}} kit update --workspace "{{options.Workspace}}" --backup
{{options.ToolCommand}} kit doctor --workspace "{{options.Workspace}}"
{{options.ToolCommand}} kit next-ticket --workspace "{{options.Workspace}}"
```

PowerShell wrappers are still supported, but they are optional convenience scripts.

## Agent entrypoints

Kickoff:

```text
{{Path.Combine(options.Workspace, "prompts", "kickoff-prompt.txt")}}
```

Resume:

```text
{{Path.Combine(options.Workspace, "prompts", "resume-prompt.txt")}}
```

One bounded loop batch:

```text
{{Path.Combine(options.Workspace, "prompts", "loop-batch-prompt.txt")}}
```

Stop-policy checklist before any stop/handoff:

```text
{{Path.Combine(options.Workspace, "state", "stop-policy-checklist.md")}}
```

Codex bounded ticket:

```text
Read {{Path.Combine(options.Workspace, "codex", "CODEX.md")}} and {{Path.Combine(options.Workspace, "codex", "prompts", "ticket-fix-prompt.txt")}}.
Fix only the current ticket.
```
""";
        WriteTextFileSafe(Path.Combine(workspacePath, "QUICKSTART.md"), quickStart, workspacePath, options, neverOverwrite: false);
    }

    static void WriteVersionFile(string workspacePath, KitOptions options, bool updateMode)
    {
        var path = Path.Combine(workspacePath, ".migration-kit", "version.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var previousInstalledAt = DateTimeOffset.UtcNow.ToString("o");
        if (File.Exists(path))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("installedAtUtc", out var installedAt))
                    previousInstalledAt = installedAt.GetString() ?? previousInstalledAt;
            }
            catch
            {
                // Keep a fresh installedAt if previous metadata was not valid JSON.
            }
        }

        var payload = new SortedDictionary<string, object?>
        {
            ["kitVersion"] = KitVersion,
            ["installedAtUtc"] = previousInstalledAt,
            ["updatedAtUtc"] = DateTimeOffset.UtcNow.ToString("o"),
            ["updateMode"] = updateMode,
            ["workspace"] = options.Workspace,
            ["source"] = options.Source,
            ["target"] = options.Target,
            ["config"] = options.Config,
            ["output"] = options.Output,
            ["toolCommand"] = options.ToolCommand,
            ["installer"] = "cli-kit-command"
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"write: {path}");
    }

    static string? FindLatestRunDirectory(string workspacePath)
    {
        var runs = Path.Combine(workspacePath, "runs");
        if (!Directory.Exists(runs))
            return null;

        return Directory.EnumerateDirectories(runs)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    static IReadOnlyDictionary<string, string> BuildTokens(KitOptions options) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["SOURCE"] = options.Source,
        ["TARGET"] = options.Target,
        ["CONFIG"] = options.Config,
        ["OUTPUT"] = options.Output,
        ["WORKSPACE"] = options.Workspace,
        ["TOOL"] = options.ToolCommand,
        ["KIT_VERSION"] = KitVersion
    };

    static string ResolveKitRoot()
    {
        foreach (var candidate in CandidateKitRoots())
        {
            if (Directory.Exists(Path.Combine(candidate, "templates", "migration-kit")))
                return candidate;
        }

        return Directory.GetCurrentDirectory();
    }

    static IEnumerable<string> CandidateKitRoots()
    {
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            yield return dir.FullName;
            dir = dir.Parent;
        }

        dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            yield return dir.FullName;
            dir = dir.Parent;
        }
    }

    static bool IsWorkspaceMutableFile(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized == "agent-state.md"
            || normalized == "current-ticket.md"
            || normalized == "profiles/adapter-config.json"
            || normalized.StartsWith("runs/", StringComparison.Ordinal)
            || normalized.StartsWith("reports/", StringComparison.Ordinal)
            || normalized.StartsWith("logs/", StringComparison.Ordinal)
            || normalized.StartsWith("state/run-ledger.md", StringComparison.Ordinal)
            || normalized.StartsWith("state/decision-log.md", StringComparison.Ordinal)
            || normalized.StartsWith("state/handoff.md", StringComparison.Ordinal)
            || normalized.StartsWith("state/stop-policy-checklist.md", StringComparison.Ordinal)
            || normalized.StartsWith("state/final-gate.md", StringComparison.Ordinal);
    }

    static string SafeRelativePath(string basePath, string path)
    {
        try
        {
            var relative = Path.GetRelativePath(basePath, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal))
                return relative;
        }
        catch
        {
            // Fallback below.
        }

        return Path.GetFileName(path);
    }

    static string ToAbsolutePath(string path, string basePath)
        => Path.IsPathRooted(path) || LooksLikeWindowsRootedPath(path) ? path : Path.Combine(basePath, path);

    static bool LooksLikeWindowsRootedPath(string path)
        => path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

    static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    static ProcessResult RunProcess(string fileName, string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);
            return new ProcessResult(process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return new ProcessResult(127, "", ex.Message);
        }
    }

    static bool IsHelp(string value) => value is "--help" or "-h" or "help";

    static void PrintHelp()
    {
        Console.WriteLine("""
Usage:
  selenium-pw-migrator kit init [options]
  selenium-pw-migrator kit update [options]
  selenium-pw-migrator kit doctor [options]
  selenium-pw-migrator kit next-ticket [options]

Commands:
  init          Create a migration workspace from bundled templates.
  update        Safely refresh kit-owned files. Project-owned state is preserved.
  doctor        Validate workspace health and cross-platform prerequisites.
  next-ticket   Generate a bounded prompt for deriving the next actionable ticket.

Common options:
  --workspace <path>        Migration workspace root. Default: migration
  --source <path>           Source Selenium tests/project path.
  --target-path <path>      Target project/output path metadata.
  --config <path>           Adapter config path. Default: migration/profiles/adapter-config.json
  --out <path>              Default run output path. Default: migration/runs/run-001
  --tool-command <cmd>      Command shown in generated docs. Default: selenium-pw-migrator
  --backup                  Snapshot existing workspace before update/init.
  --force                   Overwrite kit-owned files instead of writing .new conflicts.
  --with-team               Install optional OpenCode team templates.
  --with-loop-library       Install optional reusable loop library.
  --no-codex-files          Do not install migration/codex files.
  --no-root-agent-files     Do not copy .agent-loops into project root.
  --input <path>            Artifact directory for kit next-ticket.

Examples:
  selenium-pw-migrator kit init --workspace migration --source ./OldTests
  selenium-pw-migrator kit update --workspace migration --backup
  selenium-pw-migrator kit doctor --workspace migration
  selenium-pw-migrator kit next-ticket --workspace migration --input migration/runs/run-053
""");
    }

    sealed record KitOptions(
        string Workspace,
        string Source,
        string Target,
        string Config,
        string Output,
        string ToolCommand,
        bool Update,
        bool Force,
        bool Backup,
        bool NoRootAgentFiles,
        bool NoCodexFiles,
        bool WithTeam,
        bool WithLoopLibrary,
        string? Input)
    {
        public static KitOptions? Parse(string[] args, out string error)
        {
            var options = new KitOptions(
                Workspace: "migration",
                Source: "<SOURCE_SELENIUM_PROJECT_PATH>",
                Target: "<TARGET_PROJECT_OR_OUTPUT_PATH>",
                Config: "migration/profiles/adapter-config.json",
                Output: "migration/runs/run-001",
                ToolCommand: "selenium-pw-migrator",
                Update: false,
                Force: false,
                Backup: false,
                NoRootAgentFiles: false,
                NoCodexFiles: false,
                WithTeam: false,
                WithLoopLibrary: false,
                Input: null);

            error = string.Empty;
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                string ReadValue()
                {
                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"{arg} requires a value");
                    return args[++i];
                }

                try
                {
                    options = arg switch
                    {
                        "--workspace" => options with { Workspace = ReadValue() },
                        "--source" => options with { Source = ReadValue() },
                        "--target-path" => options with { Target = ReadValue() },
                        "--config" => options with { Config = ReadValue() },
                        "--out" => options with { Output = ReadValue() },
                        "--output" => options with { Output = ReadValue() },
                        "--tool-command" => options with { ToolCommand = ReadValue() },
                        "--input" => options with { Input = ReadValue() },
                        "--update" => options with { Update = true },
                        "--force" => options with { Force = true },
                        "--backup" => options with { Backup = true },
                        "--no-root-agent-files" => options with { NoRootAgentFiles = true },
                        "--no-codex-files" => options with { NoCodexFiles = true },
                        "--with-team" => options with { WithTeam = true },
                        "--with-loop-library" => options with { WithLoopLibrary = true },
                        "--help" or "-h" => options,
                        _ when arg.StartsWith("-", StringComparison.Ordinal) => throw new ArgumentException($"Unknown option: {arg}"),
                        _ => throw new ArgumentException($"Unexpected argument: {arg}")
                    };
                }
                catch (ArgumentException ex)
                {
                    error = ex.Message;
                    return null;
                }
            }

            if (string.IsNullOrWhiteSpace(options.Workspace))
            {
                error = "--workspace must not be empty";
                return null;
            }

            return options;
        }
    }

    sealed record KitDoctorCheck(string Name, bool Ok, string Detail, string Recommendation);
    sealed record KitDoctorReport(DateTimeOffset GeneratedAtUtc, string Status, IReadOnlyList<KitDoctorCheck> Checks);
    sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}

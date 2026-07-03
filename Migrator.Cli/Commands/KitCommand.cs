using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class KitCommand
{
    const string KitVersion = "0.0.0-preview.1";

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
            "bootstrap-opencode" => RunBootstrapOpenCode(options),
            _ => UnknownCommand(command)
        };
    }

    static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown kit command: {command}");
        PrintHelp();
        return 2;
    }


    static int RunBootstrapOpenCode(KitOptions options)
    {
        var projectRoot = Directory.GetCurrentDirectory();
        var workspacePath = ToAbsolutePath(options.Workspace, projectRoot);
        var updateMode = Directory.Exists(workspacePath);
        var bootstrapOptions = options with
        {
            Update = updateMode,
            Backup = options.Backup || updateMode,
            WithTeam = true
        };

        Console.WriteLine("Bootstrapping guarded OpenCode migration workspace");
        Console.WriteLine("This command installs/updates the migration kit, installs OpenCode team templates, runs kit doctor, and can install an OpenCode config for Windows Desktop, Unix/WSL CLI, CI, or manual agent handoff.");
        Console.WriteLine();

        var initExitCode = RunInit(bootstrapOptions);
        if (initExitCode != 0)
            return initExitCode;

        Console.WriteLine();
        Console.WriteLine("Running kit doctor after bootstrap...");
        var doctorExitCode = RunDoctor(bootstrapOptions);
        if (doctorExitCode != 0)
        {
            Console.Error.WriteLine("bootstrap-opencode stopped because kit doctor reported a blocking issue.");
            return doctorExitCode;
        }

        var installExitCode = RunOpenCodeInstall(workspacePath, projectRoot, options);
        if (installExitCode != 0)
            return installExitCode;

        Console.WriteLine();
        Console.WriteLine("BOOTSTRAP_OPENCODE_READY");
        Console.WriteLine("Next:");
        Console.WriteLine("  1. Start the selected agent environment using the instructions printed above or migration/QUICKSTART.md.");
        Console.WriteLine("  2. Run /supervised-task or give the agent migration/prompts/kickoff-prompt.txt.");
        Console.WriteLine("  3. Let the orchestrator create or resume the active harness run; do not hand-create migration/runs/<run-id>.");
        return 0;
    }

    static int RunOpenCodeInstall(string workspacePath, string projectRoot, KitOptions options)
    {
        var mode = ResolveOpenCodeInstallMode(options);
        Console.WriteLine();
        Console.WriteLine($"OpenCode install mode: {mode}");

        if (mode is "none" or "manual" or "ci")
        {
            PrintManualAgentBootstrapInstructions(options, mode);
            return 0;
        }

        if (mode == "global" && !options.Force)
        {
            Console.Error.WriteLine("Refusing global OpenCode install without --force. Global mode affects every OpenCode session for this user.");
            Console.Error.WriteLine("Use --opencode-install project-local for a safer portable config, or add --force if global install is intentional.");
            return 2;
        }

        var installMode = mode switch
        {
            "project-desktop" => "ProjectDesktop",
            "project-local" => "ProjectLocal",
            "global" => "Global",
            _ => throw new InvalidOperationException($"Unsupported OpenCode install mode: {mode}")
        };

        var target = mode switch
        {
            "project-desktop" => projectRoot,
            "project-local" => Path.Combine(projectRoot, ".opencode-migrator"),
            _ => ""
        };

        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? RunWindowsOpenCodeInstall(workspacePath, projectRoot, installMode, target)
            : RunUnixOpenCodeInstall(workspacePath, projectRoot, installMode, target);
    }

    static string ResolveOpenCodeInstallMode(KitOptions options)
    {
        if (options.ProjectDesktop)
            return "project-desktop";

        var mode = string.IsNullOrWhiteSpace(options.OpenCodeInstall)
            ? "manual"
            : options.OpenCodeInstall.Trim().ToLowerInvariant();

        return mode switch
        {
            "auto" => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "project-desktop" : "project-local",
            "none" or "manual" or "ci" or "project-local" or "project-desktop" or "global" => mode,
            _ => throw new InvalidOperationException($"Unsupported OpenCode install mode: {options.OpenCodeInstall}")
        };
    }

    static int RunWindowsOpenCodeInstall(string workspacePath, string projectRoot, string mode, string target)
    {
        var scriptPath = Path.Combine(workspacePath, "opencode-team", "scripts", "install-windows.ps1");
        if (!File.Exists(scriptPath))
        {
            Console.Error.WriteLine($"OpenCode Windows installer was not found: {scriptPath}");
            Console.Error.WriteLine("Run bootstrap-opencode without --no-team support, or install the kit with --with-team first.");
            return 2;
        }

        var powershell = ResolvePowerShellExecutable();
        if (powershell == null)
        {
            Console.Error.WriteLine("PowerShell was not found. Install PowerShell 7 (`pwsh`) or Windows PowerShell, then rerun bootstrap-opencode.");
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine($"Installing OpenCode config on Windows ({mode})...");
        Console.WriteLine($"Installer: {scriptPath}");
        if (!string.IsNullOrWhiteSpace(target))
            Console.WriteLine($"Target:    {target}");

        var args = $"-NoProfile -ExecutionPolicy Bypass -File {ProcessQuote(scriptPath)} -Mode {mode}";
        if (!string.IsNullOrWhiteSpace(target))
            args += $" -Target {ProcessQuote(target)}";

        var result = RunProcess(powershell, args, timeoutMs: 60000);
        WriteProcessOutput(result);

        if (result.ExitCode != 0)
        {
            Console.Error.WriteLine($"OpenCode Windows install failed with exit code {result.ExitCode}.");
            return result.ExitCode;
        }

        Console.WriteLine(mode == "ProjectDesktop" ? "OPENCODE_PROJECT_DESKTOP_READY" : "OPENCODE_PROJECT_LOCAL_READY");
        PrintPostInstallAgentInstructions(mode, target);
        return 0;
    }

    static int RunUnixOpenCodeInstall(string workspacePath, string projectRoot, string mode, string target)
    {
        if (mode == "ProjectDesktop")
        {
            Console.Error.WriteLine("ProjectDesktop mode is Windows/OpenCode Desktop only. Use --opencode-install project-local on macOS/Linux/WSL, or --opencode-install auto.");
            return 2;
        }

        var scriptPath = Path.Combine(workspacePath, "opencode-team", "scripts", "install-unix.sh");
        if (!File.Exists(scriptPath))
        {
            Console.Error.WriteLine($"OpenCode Unix installer was not found: {scriptPath}");
            Console.Error.WriteLine("Run bootstrap-opencode without --no-team support, or install the kit with --with-team first.");
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine($"Installing OpenCode config on Unix-like environment ({mode})...");
        Console.WriteLine($"Installer: {scriptPath}");
        if (!string.IsNullOrWhiteSpace(target))
            Console.WriteLine($"Target:    {target}");

        var args = $"{ProcessQuote(scriptPath)} --mode {mode}";
        if (!string.IsNullOrWhiteSpace(target))
            args += $" --target {ProcessQuote(target)}";

        var result = RunProcess("bash", args, timeoutMs: 60000);
        WriteProcessOutput(result);

        if (result.ExitCode != 0)
        {
            Console.Error.WriteLine($"OpenCode Unix install failed with exit code {result.ExitCode}.");
            return result.ExitCode;
        }

        Console.WriteLine(mode == "Global" ? "OPENCODE_GLOBAL_READY" : "OPENCODE_PROJECT_LOCAL_READY");
        PrintPostInstallAgentInstructions(mode, target);
        return 0;
    }

    static void WriteProcessOutput(ProcessResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.StdOut))
            Console.Write(result.StdOut);
        if (!string.IsNullOrWhiteSpace(result.StdErr))
            Console.Error.Write(result.StdErr);
    }

    static void PrintManualAgentBootstrapInstructions(KitOptions options, string mode)
    {
        Console.WriteLine(mode == "ci"
            ? "CI/manual-agent mode selected: no OpenCode config was installed."
            : "No OpenCode config install was requested.");
        Console.WriteLine("Use the installed migration workspace with any agent by giving it these entrypoints:");
        Console.WriteLine($"  Contract: {Path.Combine(options.Workspace, "AGENT_CONTRACT.md")}");
        Console.WriteLine($"  Kickoff:  {Path.Combine(options.Workspace, "prompts", "kickoff-prompt.txt")}");
        Console.WriteLine($"  Harness:  {Path.Combine(options.Workspace, "harness", "README.md")}");
        Console.WriteLine("For OpenCode CLI on macOS/Linux/WSL, rerun with:");
        Console.WriteLine($"  {options.ToolCommand} kit bootstrap-opencode --workspace {Quote(options.Workspace)} --opencode-install project-local");
        Console.WriteLine("For Windows OpenCode Desktop, rerun with:");
        Console.WriteLine($"  {options.ToolCommand} kit bootstrap-opencode --workspace {Quote(options.Workspace)} --project-desktop");
    }

    static void PrintPostInstallAgentInstructions(string mode, string target)
    {
        Console.WriteLine();
        Console.WriteLine("Agent start:");
        if (mode == "ProjectDesktop")
        {
            Console.WriteLine("  Open the repository root in OpenCode Desktop.");
            Console.WriteLine("  Run /supervised-task or /harness-run.");
        }
        else if (mode == "ProjectLocal")
        {
            Console.WriteLine("  Start OpenCode CLI with this project-local config:");
            Console.WriteLine(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? $"  $env:OPENCODE_CONFIG = {ProcessQuote(Path.Combine(target, "opencode.jsonc"))}; opencode"
                : $"  OPENCODE_CONFIG={ProcessQuote(Path.Combine(target, "opencode.jsonc"))} opencode");
            Console.WriteLine("  Then run /supervised-task or /harness-run.");
        }
        else
        {
            Console.WriteLine("  Open OpenCode and run /supervised-task or /harness-run.");
        }
    }

    static string? ResolvePowerShellExecutable()
    {
        foreach (var candidate in new[] { "pwsh", "powershell" })
        {
            var result = RunProcess(candidate, "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"", timeoutMs: 5000);
            if (result.ExitCode == 0)
                return candidate;
        }

        return null;
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
            "tickets", "evidence", "proposals", "scripts", "codex", "harness", "dashboard", ".migration-kit"
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


        WriteQuickStart(workspacePath, options);
        WriteVersionFile(workspacePath, options, updateMode: options.Update);
        WriteGuardChecksums(workspacePath);

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
        AddCheck(checks, "scope-guard-shell", File.Exists(Path.Combine(workspacePath, "scripts", "check-scope.sh")), Path.Combine(workspacePath, "scripts", "check-scope.sh"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "final-gate-shell", File.Exists(Path.Combine(workspacePath, "scripts", "check-final-gate.sh")), Path.Combine(workspacePath, "scripts", "check-final-gate.sh"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-reference", File.Exists(Path.Combine(workspacePath, "harness", "README.md")), Path.Combine(workspacePath, "harness", "README.md"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-policy", File.Exists(Path.Combine(workspacePath, "state", "harness-policy.json")), Path.Combine(workspacePath, "state", "harness-policy.json"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-run-template", File.Exists(Path.Combine(workspacePath, "state", "harness-run-template.json")), Path.Combine(workspacePath, "state", "harness-run-template.json"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-policy-script", File.Exists(Path.Combine(workspacePath, "scripts", "check-harness-policy.ps1")), Path.Combine(workspacePath, "scripts", "check-harness-policy.ps1"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-run-script", File.Exists(Path.Combine(workspacePath, "scripts", "new-harness-run.ps1")), Path.Combine(workspacePath, "scripts", "new-harness-run.ps1"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-event-script", File.Exists(Path.Combine(workspacePath, "scripts", "write-harness-event.ps1")), Path.Combine(workspacePath, "scripts", "write-harness-event.ps1"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-dashboard-script", File.Exists(Path.Combine(workspacePath, "scripts", "build-harness-dashboard.ps1")), Path.Combine(workspacePath, "scripts", "build-harness-dashboard.ps1"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-shell-wrappers", File.Exists(Path.Combine(workspacePath, "scripts", "new-harness-run.sh")) && File.Exists(Path.Combine(workspacePath, "scripts", "check-harness-policy.sh")) && File.Exists(Path.Combine(workspacePath, "scripts", "write-harness-event.sh")) && File.Exists(Path.Combine(workspacePath, "scripts", "build-harness-dashboard.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-dashboard-i18n-en", File.Exists(Path.Combine(workspacePath, "dashboard", "i18n", "en.json")), Path.Combine(workspacePath, "dashboard", "i18n", "en.json"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-dashboard-i18n-ru", File.Exists(Path.Combine(workspacePath, "dashboard", "i18n", "ru.json")), Path.Combine(workspacePath, "dashboard", "i18n", "ru.json"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "guard-checksums", File.Exists(Path.Combine(workspacePath, ".migration-kit", "guard-checksums.json")), Path.Combine(workspacePath, ".migration-kit", "guard-checksums.json"), "Run `migrator kit update --backup`.");

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

        foreach (var name in new[] { ".agent-state", "AGENTS.md" })
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

## Guarded agent bootstrap

One-command portable bootstrap from the product repository root:

```bash
{{options.ToolCommand}} kit bootstrap-opencode --workspace "{{options.Workspace}}" --source "{{options.Source}}" --config "{{options.Config}}" --opencode-install auto
```

Install modes:

```text
--opencode-install auto             Windows => project-desktop, macOS/Linux/WSL => project-local
--opencode-install project-desktop  Windows OpenCode Desktop project config
--opencode-install project-local    Portable OpenCode CLI config in .opencode-migrator
--opencode-install ci               CI/Codex/manual agents; no OpenCode config install
--opencode-install none             Only install/update the migration workspace and run doctor
```

The legacy shortcut remains available on Windows:

```powershell
{{options.ToolCommand}} kit bootstrap-opencode --workspace "{{options.Workspace}}" --source "{{options.Source}}" --config "{{options.Config}}" --project-desktop
```

For non-OpenCode agents, give the agent `{{Path.Combine(options.Workspace, "AGENT_CONTRACT.md")}}`, `{{Path.Combine(options.Workspace, "prompts", "kickoff-prompt.txt")}}`, and `{{Path.Combine(options.Workspace, "harness", "README.md")}}`.

Then run `/supervised-task` in OpenCode, or give the same kickoff prompt to Codex/CI/another agent. The agent should create or resume the active harness run itself with `{{Path.Combine(options.Workspace, "scripts", "new-harness-run.sh")}}` from bash or `{{Path.Combine(options.Workspace, "scripts", "new-harness-run.ps1")}}` from PowerShell; you should not manually create `{{Path.Combine(options.Workspace, "runs", "run-001")}}`.

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

Harness autopilot run from bash:

```bash
./{{Path.Combine(options.Workspace, "scripts", "new-harness-run.sh")}} -TaskTitle "Pilot migration batch" -Goal "Run one bounded artifact-only Selenium to Playwright migration batch."
./{{Path.Combine(options.Workspace, "scripts", "check-harness-policy.sh")}} -Workspace "{{options.Workspace}}" -RepoRoot .
```

Harness autopilot run from PowerShell:

```powershell
.\{{Path.Combine(options.Workspace, "scripts", "new-harness-run.ps1")}} -TaskTitle "Pilot migration batch" -Goal "Run one bounded artifact-only Selenium to Playwright migration batch."
.\{{Path.Combine(options.Workspace, "scripts", "check-harness-policy.ps1")}} -Workspace "{{options.Workspace}}" -RepoRoot .
```

Harness Kit dogfood smoke from the Migrator repository root:

```bash
scripts/run-harness-dogfood-smoke.sh -Clean
```

or on PowerShell:

```powershell
.\scripts\run-harness-dogfood-smoke.ps1 -Clean
```

Generate Harness dashboard from bash:

```bash
./{{Path.Combine(options.Workspace, "scripts", "build-harness-dashboard.sh")}} -Workspace "{{options.Workspace}}" -Out dashboard/harness -Language en
```

or on PowerShell:

```powershell
.\{{Path.Combine(options.Workspace, "scripts", "build-harness-dashboard.ps1")}} -Workspace "{{options.Workspace}}" -Out dashboard/harness -Language en
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

    static void WriteGuardChecksums(string workspacePath)
    {
        var guardFiles = new[]
        {
            "scripts/check-scope.ps1",
            "scripts/check-final-gate.ps1",
            "scripts/check-harness-policy.ps1",
            "scripts/build-harness-dashboard.ps1"
        };
        var entries = guardFiles.Select(relative =>
        {
            var fullPath = Path.Combine(workspacePath, relative.Replace('/', Path.DirectorySeparatorChar));
            var hash = File.Exists(fullPath)
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))).ToLowerInvariant()
                : "";
            return new SortedDictionary<string, object?>
            {
                ["path"] = relative,
                ["sha256"] = hash
            };
        }).ToArray();

        var payload = new SortedDictionary<string, object?>
        {
            ["schemaVersion"] = "guard-checksums/v1",
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("o"),
            ["files"] = entries
        };

        var path = Path.Combine(workspacePath, ".migration-kit", "guard-checksums.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
        // Prefer bundled/source templates over the product repository current directory.
        // A target repo may legitimately contain its own `templates/migration-kit` folder; it must not
        // shadow the templates shipped with the dotnet tool. Use MIGRATOR_KIT_ROOT only as an explicit
        // developer override for local debugging.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in CandidateKitRootPaths())
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var fullPath = Path.GetFullPath(candidate);
            if (seen.Add(fullPath))
                yield return fullPath;
        }
    }

    static IEnumerable<string> CandidateKitRootPaths()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("MIGRATOR_KIT_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            yield return overrideRoot;

        yield return AppContext.BaseDirectory;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            yield return dir.FullName;
            dir = dir.Parent;
        }

        var currentDirectory = Directory.GetCurrentDirectory();
        yield return currentDirectory;

        dir = new DirectoryInfo(currentDirectory);
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
            || normalized.StartsWith("state/final-gate.md", StringComparison.Ordinal)
            || normalized.StartsWith("state/harness-run.json", StringComparison.Ordinal)
            || normalized.StartsWith("state/harness-events.jsonl", StringComparison.Ordinal)
            || normalized.StartsWith("state/harness-policy-result.", StringComparison.Ordinal);
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

    static string ProcessQuote(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    static ProcessResult RunProcess(string fileName, string arguments, int timeoutMs = 5000)
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
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new ProcessResult(124, stdout, $"Command timed out after {timeoutMs} ms: {fileName} {arguments}");
            }
            return new ProcessResult(process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return new ProcessResult(127, "", ex.Message);
        }
    }

    static string NormalizeOpenCodeInstallMode(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "auto" or "none" or "manual" or "ci" or "project-local" or "project-desktop" or "global" => normalized,
            _ => throw new ArgumentException($"--opencode-install must be one of: auto, none, manual, ci, project-local, project-desktop, global. Got: {value}")
        };
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
  selenium-pw-migrator kit bootstrap-opencode [options]

Commands:
  init          Create a migration workspace from bundled templates.
  update        Safely refresh kit-owned files. Project-owned state is preserved.
  doctor        Validate workspace health and cross-platform prerequisites.
  next-ticket   Generate a bounded prompt for deriving the next actionable ticket.
  bootstrap-opencode
                Install/update the kit, include OpenCode team files, run doctor, and optionally install ProjectDesktop config.

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
  --project-desktop         Shortcut for --opencode-install project-desktop.
  --opencode-install <mode> For kit bootstrap-opencode: auto, none, manual, ci, project-local, project-desktop, global.
  --no-codex-files          Do not install migration/codex files.
  --input <path>            Artifact directory for kit next-ticket.

Examples:
  selenium-pw-migrator kit init --workspace migration --source ./OldTests
  selenium-pw-migrator kit update --workspace migration --backup
  selenium-pw-migrator kit doctor --workspace migration
  selenium-pw-migrator kit next-ticket --workspace migration --input migration/runs/run-053
  selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./OldTests --opencode-install auto
  selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./OldTests --project-desktop
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
        bool NoCodexFiles,
        bool WithTeam,
        bool ProjectDesktop,
        string OpenCodeInstall,
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
                NoCodexFiles: false,
                WithTeam: false,
                ProjectDesktop: false,
                OpenCodeInstall: "manual",
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
                        "--no-codex-files" => options with { NoCodexFiles = true },
                        "--with-team" => options with { WithTeam = true },
                        "--project-desktop" => options with { ProjectDesktop = true, OpenCodeInstall = "project-desktop" },
                        "--opencode-install" => options with { OpenCodeInstall = NormalizeOpenCodeInstallMode(ReadValue()) },
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

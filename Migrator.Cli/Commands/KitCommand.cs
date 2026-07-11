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
    const string SourceScopeSchema = "migration-source-scope/v1";
    const int ScopeContractSchemaVersion = 1;

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
            "bootstrap-agent" => RunBootstrapAgent(options),
            _ => UnknownCommand(command)
        };
    }

    static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown kit command: {command}");
        PrintHelp();
        return 2;
    }


    static int RunBootstrapAgent(KitOptions options)
    {
        var agent = NormalizeAgent(options.Agent);
        if (agent == "opencode")
            return RunBootstrapOpenCode(options with { OpenCodeInstall = string.IsNullOrWhiteSpace(options.OpenCodeInstall) || options.OpenCodeInstall == "manual" ? "auto" : options.OpenCodeInstall });

        var projectRoot = ResolveProjectRoot();
        var workspacePath = ToAbsolutePath(options.Workspace, projectRoot);
        var updateMode = Directory.Exists(workspacePath);
        var bootstrapOptions = options with
        {
            Update = updateMode,
            Backup = options.Backup || updateMode,
            WithTeam = false,
            NoCodexFiles = agent == "generic"
        };

        Console.WriteLine($"Bootstrapping guarded {agent} migration workspace");
        Console.WriteLine("This command installs/updates the migration kit, runs kit doctor, and writes an explicit non-OpenCode agent handoff pack.");
        Console.WriteLine();

        var initExitCode = RunInit(bootstrapOptions);
        if (initExitCode != 0)
            return initExitCode;

        WriteAgentHandoff(workspacePath, bootstrapOptions, agent);

        Console.WriteLine();
        Console.WriteLine("Running kit doctor after bootstrap...");
        var doctorExitCode = RunDoctor(bootstrapOptions);
        if (doctorExitCode != 0)
        {
            Console.Error.WriteLine("bootstrap-agent stopped because kit doctor reported a blocking issue.");
            return doctorExitCode;
        }

        Console.WriteLine();
        Console.WriteLine("BOOTSTRAP_AGENT_READY");
        Console.WriteLine($"Agent: {agent}");
        Console.WriteLine($"Open first: {Path.Combine(options.Workspace, "AGENT_HANDOFF.md")}");
        Console.WriteLine("Next:");
        Console.WriteLine("  1. Give AGENT_HANDOFF.md plus AGENT_CONTRACT.md to the selected agent.");
        Console.WriteLine("  2. Start with prompts/kickoff-prompt.txt unless the agent supports /supervised-task.");
        Console.WriteLine("  3. Let the agent create or resume the active harness run; do not hand-create migration/runs/<run-id>.");
        return 0;
    }

    static string NormalizeAgent(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "generic" : value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "opencode" or "codex" or "generic" => normalized,
            _ => throw new ArgumentException($"--agent must be one of: opencode, codex, generic. Got: {value}")
        };
    }

    static void WriteAgentHandoff(string workspacePath, KitOptions options, string agent)
    {
        var extra = agent == "codex"
            ? $"- Codex-specific pack: `{Path.Combine(options.Workspace, "codex", "CODEX.md")}` and `{Path.Combine(options.Workspace, "codex", "prompts", "ticket-fix-prompt.txt")}`."
            : "- Generic agents should use only the contract, kickoff prompt, harness README, and current-ticket.md unless a project-specific prompt says otherwise.";

        var handoff = $$"""
# Agent Handoff Pack

Agent: `{{agent}}`
Workspace: `{{options.Workspace}}`
Source Selenium path: `{{options.Source}}`
Adapter config: `{{options.Config}}`

## Open these first

1. `{{Path.Combine(options.Workspace, "AGENT_CONTRACT.md")}}` — non-negotiable migration contract.
2. `{{Path.Combine(options.Workspace, "prompts", "kickoff-prompt.txt")}}` — first task prompt.
3. `{{Path.Combine(options.Workspace, "harness", "README.md")}}` — run lifecycle and gates.
4. `{{Path.Combine(options.Workspace, "state", "harness-policy.json")}}` — policy enforced by final gates.

{{extra}}

## Operating rule

The agent must create or resume a harness run through the provided scripts. Do not manually create `{{Path.Combine(options.Workspace, "runs", "run-001")}}`.

PowerShell:

```powershell
.\{{Path.Combine(options.Workspace, "scripts", "new-harness-run.ps1")}} -TaskTitle "Pilot migration batch" -Goal "Run one bounded Selenium to Playwright migration batch."
.\{{Path.Combine(options.Workspace, "scripts", "check-harness-policy.ps1")}} -Workspace "{{options.Workspace}}" -RepoRoot .
```

Bash:

```bash
./{{Path.Combine(options.Workspace, "scripts", "new-harness-run.sh")}} -TaskTitle "Pilot migration batch" -Goal "Run one bounded Selenium to Playwright migration batch."
./{{Path.Combine(options.Workspace, "scripts", "check-harness-policy.sh")}} -Workspace "{{options.Workspace}}" -RepoRoot .
```

## Review surface

Open the dashboard first after a run:

```bash
{{options.ToolCommand}} report serve --input {{Path.Combine(options.Workspace, "runs", "latest")}} --static-only --out {{Path.Combine(options.Workspace, "dashboard", "latest")}} --format both
```

The dashboard is the primary review surface for readiness, TODO categories, unsupported actions, generated files, next actions, and evidence links.
""";
        WriteTextFileSafe(Path.Combine(workspacePath, "AGENT_HANDOFF.md"), handoff, workspacePath, options, neverOverwrite: false);
    }

    static int RunBootstrapOpenCode(KitOptions options)
    {
        var projectRoot = ResolveProjectRoot();
        var workspacePath = ToAbsolutePath(options.Workspace, projectRoot);
        var updateMode = Directory.Exists(workspacePath);
        var bootstrapOptions = options with
        {
            Update = updateMode,
            Backup = options.Backup || updateMode,
            WithTeam = true
        };

        Console.WriteLine("Bootstrapping guarded OpenCode migration workspace");
        Console.WriteLine("This command installs/updates the migration kit, copies the OpenCode command pack into the repository root, runs kit doctor, and can additionally install OpenCode config for Windows Desktop, Unix/WSL CLI, CI, or manual agent handoff.");
        Console.WriteLine();

        var initExitCode = RunInit(bootstrapOptions);
        if (initExitCode != 0)
            return initExitCode;

        if (!options.SkipProjectConfig)
        {
            Console.WriteLine();
            var projectConfigExitCode = ApplyOpenCodeProjectConfig(workspacePath, projectRoot, options);
            if (projectConfigExitCode != 0)
                return projectConfigExitCode;
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("Skipping repository-root OpenCode project config because --skip-project-config was provided.");
        }

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
        Console.WriteLine("  1. Open the repository root in OpenCode.");
        Console.WriteLine("  2. Run /supervised-task waves for one-command wavefront planning and the first bounded wave.");
        Console.WriteLine("  3. Let the orchestrator ask only for missing source/target/framework details; do not hand-create migration/runs/<run-id>.");
        return 0;
    }

    static int ApplyOpenCodeProjectConfig(string workspacePath, string projectRoot, KitOptions options)
    {
        var sourceRoot = Path.Combine(workspacePath, "opencode-team", "global", ".config", "opencode");
        if (!Directory.Exists(sourceRoot))
        {
            Console.Error.WriteLine($"OpenCode team template was not found: {sourceRoot}");
            Console.Error.WriteLine("Run bootstrap-opencode without --no-team support, or install the kit with --with-team first.");
            return 2;
        }

        var configFileName = options.PermissionProfile == "TrustedProject"
            ? "opencode.trusted-project.jsonc"
            : "opencode.jsonc";
        var configSource = Path.Combine(sourceRoot, configFileName);
        if (!File.Exists(configSource))
        {
            Console.Error.WriteLine($"OpenCode config profile was not found: {configSource}");
            return 2;
        }

        var backupRoot = Path.Combine(workspacePath, ".migration-kit", "opencode-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Console.WriteLine("Applying repository-root OpenCode project config...");
        Console.WriteLine($"Source:             {sourceRoot}");
        Console.WriteLine($"Repo root:          {projectRoot}");
        Console.WriteLine($"Permission profile: {options.PermissionProfile}");

        CopyRootFileWithBackup(configSource, Path.Combine(projectRoot, "opencode.jsonc"), backupRoot, overwrite: true);
        CopyRootDirectoryWithBackup(Path.Combine(sourceRoot, "agents"), Path.Combine(projectRoot, ".opencode", "agents"), backupRoot);
        CopyRootDirectoryWithBackup(Path.Combine(sourceRoot, "commands"), Path.Combine(projectRoot, ".opencode", "commands"), backupRoot);

        var agentsTemplate = Path.Combine(workspacePath, "opencode-team", "project-template", "AGENTS.md");
        var rootAgents = Path.Combine(projectRoot, "AGENTS.md");
        if (File.Exists(agentsTemplate))
        {
            if (!File.Exists(rootAgents) || options.Force)
                CopyRootFileWithBackup(agentsTemplate, rootAgents, backupRoot, overwrite: options.Force);
            else
                Console.WriteLine($"keeping existing: {rootAgents}");
        }

        WriteOpenCodeProjectConfigMetadata(workspacePath, options);
        Console.WriteLine("OPENCODE_PROJECT_CONFIG_APPLIED");
        Console.WriteLine("OpenCode commands are installed in the repository root. Next: open the repo in OpenCode and run /supervised-task waves.");
        return 0;
    }

    static void CopyRootFileWithBackup(string source, string destination, string backupRoot, bool overwrite)
    {
        if (File.Exists(destination))
        {
            var existing = File.ReadAllText(destination);
            var next = File.ReadAllText(source);
            if (existing == next)
            {
                Console.WriteLine($"unchanged: {destination}");
                return;
            }

            if (!overwrite)
            {
                Console.WriteLine($"keeping existing: {destination}");
                return;
            }

            BackupRootPath(destination, backupRoot);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
        Console.WriteLine(File.Exists(destination) ? $"write: {destination}" : $"write: {destination}");
    }

    static void CopyRootDirectoryWithBackup(string source, string destination, string backupRoot)
    {
        if (!Directory.Exists(source))
        {
            Console.WriteLine($"skip missing directory: {source}");
            return;
        }

        if (Directory.Exists(destination))
        {
            BackupRootPath(destination, backupRoot);
            Directory.Delete(destination, recursive: true);
        }

        CopyFileSystemEntry(source, destination);
        Console.WriteLine($"sync: {destination}");
    }

    static void BackupRootPath(string path, string backupRoot)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return;

        Directory.CreateDirectory(backupRoot);
        var destination = Path.Combine(backupRoot, Path.GetFileName(path));
        CopyFileSystemEntry(path, destination);
        Console.WriteLine($"backup: {destination}");
    }

    static void WriteOpenCodeProjectConfigMetadata(string workspacePath, KitOptions options)
    {
        var path = Path.Combine(workspacePath, ".migration-kit", "opencode-project-config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = new SortedDictionary<string, object?>
        {
            ["schemaVersion"] = "opencode-project-config/v1",
            ["installedAtUtc"] = DateTimeOffset.UtcNow.ToString("o"),
            ["workspace"] = options.Workspace,
            ["source"] = options.Source,
            ["permissionProfile"] = options.PermissionProfile,
            ["command"] = "kit bootstrap-opencode",
            ["nextCommand"] = "/supervised-task waves"
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"write: {path}");
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
            ? "CI/manual-agent mode selected: no additional OpenCode launcher install was requested."
            : "No additional OpenCode launcher install was requested; the repository-root OpenCode command pack was already applied unless --skip-project-config was used.");
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
            Console.WriteLine("  Run /supervised-task waves for a fresh wavefront start, or /supervised-task for an existing workspace.");
        }
        else if (mode == "ProjectLocal")
        {
            Console.WriteLine("  Start OpenCode CLI with this project-local config:");
            Console.WriteLine(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? $"  $env:OPENCODE_CONFIG = {ProcessQuote(Path.Combine(target, "opencode.jsonc"))}; opencode"
                : $"  OPENCODE_CONFIG={ProcessQuote(Path.Combine(target, "opencode.jsonc"))} opencode");
            Console.WriteLine("  Then run /supervised-task waves for a fresh wavefront start, or /supervised-task for an existing workspace.");
        }
        else
        {
            Console.WriteLine("  Open OpenCode and run /supervised-task waves for a fresh wavefront start, or /supervised-task for an existing workspace.");
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
        var projectRoot = ResolveProjectRoot();
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
            "state/claims", "state/claims/active", "state/claims/completed", "state/claims/stale",
            "tickets", "evidence", "proposals", "scripts", "codex", "harness", "dashboard", "agent-skills", ".migration-kit"
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
        WriteSourceScopeMetadata(workspacePath, projectRoot, options);
        WriteScopeContract(workspacePath, projectRoot, options);
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
        var projectRoot = ResolveProjectRoot();
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
        AddCheck(checks, "agent-skills-map", File.Exists(Path.Combine(workspacePath, "agent-skills", "skill-map.md")), Path.Combine(workspacePath, "agent-skills", "skill-map.md"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "agent-skills-manifest", File.Exists(Path.Combine(workspacePath, "agent-skills", "manifest.json")), Path.Combine(workspacePath, "agent-skills", "manifest.json"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "agent-skills-core", File.Exists(Path.Combine(workspacePath, "agent-skills", "plow-ahead", "SKILL.md")) && File.Exists(Path.Combine(workspacePath, "agent-skills", "agent-watchdog", "SKILL.md")) && File.Exists(Path.Combine(workspacePath, "agent-skills", "read-the-damn-docs", "SKILL.md")), Path.Combine(workspacePath, "agent-skills"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "agent-skills-usage-writer", File.Exists(Path.Combine(workspacePath, "scripts", "write-agent-skill-usage.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "write-agent-skill-usage.sh")) && File.Exists(Path.Combine(workspacePath, "scripts", "record-agent-skill-profile.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "record-agent-skill-profile.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "gate-followup-slicer", File.Exists(Path.Combine(workspacePath, "scripts", "slice-gate-followups.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "slice-gate-followups.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "wave-quality-budget", File.Exists(Path.Combine(workspacePath, "scripts", "evaluate-wave-quality-budget.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "evaluate-wave-quality-budget.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "fresh-wavefront-restart", File.Exists(Path.Combine(workspacePath, "scripts", "start-fresh-wavefront-run.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "start-fresh-wavefront-run.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "mapping-research-memory", File.Exists(Path.Combine(workspacePath, "scripts", "collect-mapping-research-memory.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "collect-mapping-research-memory.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "feedback-bundle-packer", File.Exists(Path.Combine(workspacePath, "scripts", "create-feedback-bundle.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "create-feedback-bundle.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "installed-script-validator", File.Exists(Path.Combine(workspacePath, "scripts", "validate-installed-scripts.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "validate-installed-scripts.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "artifact-hygiene", File.Exists(Path.Combine(workspacePath, "scripts", "validate-run-artifacts.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "validate-run-artifacts.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "current-ticket-lifecycle", File.Exists(Path.Combine(workspacePath, "scripts", "update-current-ticket-status.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "update-current-ticket-status.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "jsonl-ledger-repair", File.Exists(Path.Combine(workspacePath, "scripts", "repair-jsonl-ledger.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "repair-jsonl-ledger.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "sentinel-finding-lifecycle", File.Exists(Path.Combine(workspacePath, "scripts", "update-sentinel-finding-status.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "update-sentinel-finding-status.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-policy", File.Exists(Path.Combine(workspacePath, "state", "harness-policy.json")), Path.Combine(workspacePath, "state", "harness-policy.json"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "scope-contract", File.Exists(Path.Combine(workspacePath, "state", "scope-contract.json")), Path.Combine(workspacePath, "state", "scope-contract.json"), "Run `migrator kit update --backup --source <source-root>` or pass --source on bootstrap.");
        AddCheck(checks, "harness-run-template", File.Exists(Path.Combine(workspacePath, "state", "harness-run-template.json")), Path.Combine(workspacePath, "state", "harness-run-template.json"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-policy-script", File.Exists(Path.Combine(workspacePath, "scripts", "check-harness-policy.ps1")), Path.Combine(workspacePath, "scripts", "check-harness-policy.ps1"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-run-script", File.Exists(Path.Combine(workspacePath, "scripts", "new-harness-run.ps1")), Path.Combine(workspacePath, "scripts", "new-harness-run.ps1"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-event-script", File.Exists(Path.Combine(workspacePath, "scripts", "write-harness-event.ps1")), Path.Combine(workspacePath, "scripts", "write-harness-event.ps1"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-dashboard-script", File.Exists(Path.Combine(workspacePath, "scripts", "build-harness-dashboard.ps1")), Path.Combine(workspacePath, "scripts", "build-harness-dashboard.ps1"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-shell-wrappers", File.Exists(Path.Combine(workspacePath, "scripts", "new-harness-run.sh")) && File.Exists(Path.Combine(workspacePath, "scripts", "check-harness-policy.sh")) && File.Exists(Path.Combine(workspacePath, "scripts", "write-harness-event.sh")) && File.Exists(Path.Combine(workspacePath, "scripts", "build-harness-dashboard.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "claim-lifecycle-scripts", File.Exists(Path.Combine(workspacePath, "scripts", "new-claim.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "new-claim.sh")) && File.Exists(Path.Combine(workspacePath, "scripts", "update-claim-heartbeat.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "update-claim-heartbeat.sh")) && File.Exists(Path.Combine(workspacePath, "scripts", "complete-claim.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "complete-claim.sh")) && File.Exists(Path.Combine(workspacePath, "scripts", "claim-doctor.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "claim-doctor.sh")) && File.Exists(Path.Combine(workspacePath, "scripts", "move-stale-claims.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "move-stale-claims.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "evidence-bundle-scripts", File.Exists(Path.Combine(workspacePath, "scripts", "record-run-evidence.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "record-run-evidence.sh")) && File.Exists(Path.Combine(workspacePath, "scripts", "write-memory-compaction-receipt.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "write-memory-compaction-receipt.sh")) && File.Exists(Path.Combine(workspacePath, "scripts", "evaluate-command-policy.ps1")) && File.Exists(Path.Combine(workspacePath, "scripts", "evaluate-command-policy.sh")), Path.Combine(workspacePath, "scripts"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-dashboard-i18n-en", File.Exists(Path.Combine(workspacePath, "dashboard", "i18n", "en.json")), Path.Combine(workspacePath, "dashboard", "i18n", "en.json"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "harness-dashboard-i18n-ru", File.Exists(Path.Combine(workspacePath, "dashboard", "i18n", "ru.json")), Path.Combine(workspacePath, "dashboard", "i18n", "ru.json"), "Run `migrator kit update --backup`.");
        AddCheck(checks, "guard-checksums", File.Exists(Path.Combine(workspacePath, ".migration-kit", "guard-checksums.json")), Path.Combine(workspacePath, ".migration-kit", "guard-checksums.json"), "Run `migrator kit update --backup`.");

        AddCheck(checks, "nested-workspace", !HasNestedMigrationWorkspace(projectRoot, workspacePath, out var nestedWorkspaceDetail), nestedWorkspaceDetail, "Remove nested migration workspaces such as Web/**/migration/** and run kit/wave commands from the repository root.");

        var dotnet = RunProcess("dotnet", "--version");
        AddCheck(checks, "dotnet", dotnet.ExitCode == 0, dotnet.ExitCode == 0 ? dotnet.StdOut.Trim() : dotnet.StdErr.Trim(), "Install .NET SDK or use a self-contained migrator bundle.");

        AddPowerShell7Check(checks);

        var installedValidatorPath = Path.Combine(workspacePath, "scripts", "validate-installed-scripts.ps1");
        var doctorPowerShell = ResolvePowerShellExecutable();
        if (File.Exists(installedValidatorPath) && doctorPowerShell is not null)
        {
            var syntaxResult = RunProcess(
                doctorPowerShell,
                $"-NoProfile -ExecutionPolicy Bypass -File {Quote(installedValidatorPath)} -Workspace {Quote(workspacePath)} -SkipShell",
                timeoutMs: 30000);
            var syntaxDetail = syntaxResult.ExitCode == 0
                ? "installed migration/scripts PowerShell syntax passed"
                : string.Join(" | ", new[] { syntaxResult.StdOut.Trim(), syntaxResult.StdErr.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));
            AddCheck(checks, "installed-script-syntax", syntaxResult.ExitCode == 0, syntaxDetail, "Run `migration/scripts/validate-installed-scripts.ps1 -Workspace migration`, then `migrator kit update --backup` if a stale kit-owned script is reported.");
        }
        else
        {
            AddCheck(checks, "installed-script-syntax", true, doctorPowerShell is null ? "skipped: PowerShell executable unavailable" : "skipped: validator not installed", "Install PowerShell or run `migrator kit update --backup`.");
        }

        var status = checks.All(c => c.Ok) ? "passed" : "warning";
        var reportDir = Path.Combine(workspacePath, "reports", "kit-doctor");
        Directory.CreateDirectory(reportDir);
        File.WriteAllText(Path.Combine(reportDir, "kit-doctor.md"), WriteDoctorMarkdown(status, checks));
        File.WriteAllText(Path.Combine(reportDir, "kit-doctor.json"), JsonSerializer.Serialize(new KitDoctorReport(DateTimeOffset.UtcNow, status, checks), new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Kit doctor: {status.ToUpperInvariant()}");
        foreach (var check in checks)
            Console.WriteLine($"{(check.Ok ? "OK" : "WARN"),-5} {check.Name,-18} {check.Detail}");
        Console.WriteLine($"Report: {Path.Combine(reportDir, "kit-doctor.md")}");

        return checks.Count(c => !c.Ok && c.Name is "workspace" or "adapter-config" or "installed-script-syntax") > 0 ? 2 : 0;
    }

    static int RunNextTicket(KitOptions options)
    {
        var projectRoot = ResolveProjectRoot();
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
- `migration/state/memory/**`
""";
    }

    static void AddPowerShell7Check(List<KitDoctorCheck> checks)
    {
        var pwsh = RunProcess("pwsh", "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"", timeoutMs: 5000);
        if (pwsh.ExitCode == 0)
        {
            AddCheck(checks, "powershell-7", true, $"pwsh {pwsh.StdOut.Trim()}", "OK");
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var windowsPowerShell = RunProcess("powershell", "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"", timeoutMs: 5000);
            AddCheck(
                checks,
                "powershell-7",
                windowsPowerShell.ExitCode == 0,
                windowsPowerShell.ExitCode == 0 ? $"Windows PowerShell {windowsPowerShell.StdOut.Trim()} (pwsh recommended for Unix shell wrappers)" : "pwsh not found",
                "Install PowerShell 7 (`pwsh`) for the cross-platform `.sh` lifecycle wrappers: https://learn.microsoft.com/powershell/scripting/install/installing-powershell");
            return;
        }

        AddCheck(
            checks,
            "powershell-7",
            false,
            "pwsh not found; Unix `.sh` lifecycle wrappers delegate to PowerShell 7",
            "Install PowerShell 7 (`pwsh`): https://learn.microsoft.com/powershell/scripting/install/installing-powershell");
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

        var relativeToWorkspace = SafeRelativePath(workspacePath, destPath);
        if (options.Update && !neverOverwrite && IsAutoUpdatedKitOwnedFile(relativeToWorkspace))
        {
            File.WriteAllText(destPath, content);
            Console.WriteLine($"kit-overwrite: {destPath}");
            return;
        }

        if (options.Update)
        {
            var updatesRoot = Path.Combine(workspacePath, ".migration-kit", "updates", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            var relative = relativeToWorkspace;
            var updatePath = Path.Combine(updatesRoot, relative + ".new");
            Directory.CreateDirectory(Path.GetDirectoryName(updatePath)!);
            File.WriteAllText(updatePath, content);
            Console.WriteLine($"conflict -> new: {updatePath}");
            return;
        }

        Console.WriteLine($"skip existing: {destPath}");
    }


    static bool IsAutoUpdatedKitOwnedFile(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');

        // These files are part of the migration-kit runtime, not project migration state.
        // `kit update` must apply them in place; otherwise safety fixes land only as
        // `.migration-kit/updates/*.new`, while the workspace keeps executing stale
        // guard scripts/prompts. Mutable files such as current-ticket.md, handoff.md,
        // run-ledger.md and profiles/adapter-config.json still use conflict snapshots.
        return normalized is
            "scripts/check-scope.ps1" or
            "scripts/check-scope.sh" or
            "scripts/check-harness-policy.ps1" or
            "scripts/check-harness-policy.sh" or
            "scripts/new-claim.ps1" or
            "scripts/new-claim.sh" or
            "scripts/update-claim-heartbeat.ps1" or
            "scripts/update-claim-heartbeat.sh" or
            "scripts/complete-claim.ps1" or
            "scripts/complete-claim.sh" or
            "scripts/claim-doctor.ps1" or
            "scripts/claim-doctor.sh" or
            "scripts/move-stale-claims.ps1" or
            "scripts/move-stale-claims.sh" or
            "scripts/record-run-evidence.ps1" or
            "scripts/record-run-evidence.sh" or
            "scripts/write-memory-compaction-receipt.ps1" or
            "scripts/write-memory-compaction-receipt.sh" or
            "scripts/evaluate-command-policy.ps1" or
            "scripts/evaluate-command-policy.sh" or
            "scripts/check-loop-guard.ps1" or
            "scripts/check-loop-guard.sh" or
            "scripts/check-final-gate.ps1" or
            "scripts/check-final-gate.sh" or
            "scripts/build-harness-dashboard.ps1" or
            "scripts/build-harness-dashboard.sh" or
            "scripts/new-harness-run.ps1" or
            "scripts/new-harness-run.sh" or
            "scripts/write-harness-event.ps1" or
            "scripts/write-harness-event.sh" or
            "scripts/write-agent-skill-usage.ps1" or
            "scripts/write-agent-skill-usage.sh" or
            "scripts/record-agent-skill-profile.ps1" or
            "scripts/record-agent-skill-profile.sh" or
            "scripts/slice-gate-followups.ps1" or
            "scripts/slice-gate-followups.sh" or
            "scripts/evaluate-wave-quality-budget.ps1" or
            "scripts/evaluate-wave-quality-budget.sh" or
            "scripts/start-fresh-wavefront-run.ps1" or
            "scripts/start-fresh-wavefront-run.sh" or
            "scripts/collect-mapping-research-memory.ps1" or
            "scripts/collect-mapping-research-memory.sh" or
            "scripts/create-feedback-bundle.ps1" or
            "scripts/create-feedback-bundle.sh" or
            "scripts/validate-installed-scripts.ps1" or
            "scripts/validate-installed-scripts.sh" or
            "scripts/validate-run-artifacts.ps1" or
            "scripts/validate-run-artifacts.sh" or
            "scripts/update-current-ticket-status.ps1" or
            "scripts/update-current-ticket-status.sh" or
            "scripts/repair-jsonl-ledger.ps1" or
            "scripts/repair-jsonl-ledger.sh" or
            "scripts/update-sentinel-finding-status.ps1" or
            "scripts/update-sentinel-finding-status.sh" or
            "scripts/export-opencode-session.ps1" or
            "scripts/export-opencode-session.sh" or
            "scripts/write-sentinel-finding.ps1" or
            "scripts/write-sentinel-finding.sh" or
            "scripts/complete-sentinel-inspection.ps1" or
            "scripts/complete-sentinel-inspection.sh" or
            "state/continuation-contract.md"
            || normalized.StartsWith("prompts/", StringComparison.Ordinal)
            || normalized.StartsWith("agent-skills/", StringComparison.Ordinal);
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
--opencode-install ci               CI/Codex/manual agents; no additional OpenCode launcher install
--opencode-install none             Apply repository-root command pack only, then run doctor
--skip-project-config               Do not copy opencode.jsonc/.opencode/AGENTS.md into repo root
```

`bootstrap-opencode` applies the repository-root command pack by default:

```text
opencode.jsonc
.opencode/agents/*
.opencode/commands/*
AGENTS.md, when missing
```

The legacy shortcut remains available on Windows:

```powershell
{{options.ToolCommand}} kit bootstrap-opencode --workspace "{{options.Workspace}}" --source "{{options.Source}}" --config "{{options.Config}}" --project-desktop
```

For non-OpenCode agents, prefer the explicit handoff command instead of using an OpenCode install mode as a workaround:

```bash
{{options.ToolCommand}} kit bootstrap-agent --agent codex --workspace "{{options.Workspace}}" --source "{{options.Source}}" --config "{{options.Config}}"
{{options.ToolCommand}} kit bootstrap-agent --agent generic --workspace "{{options.Workspace}}" --source "{{options.Source}}" --config "{{options.Config}}"
```

This writes `{{Path.Combine(options.Workspace, "AGENT_HANDOFF.md")}}`, then gives the agent `{{Path.Combine(options.Workspace, "AGENT_CONTRACT.md")}}`, `{{Path.Combine(options.Workspace, "prompts", "kickoff-prompt.txt")}}`, and `{{Path.Combine(options.Workspace, "harness", "README.md")}}`.

After bootstrap, open the repository in OpenCode and start the user-friendly wavefront mode:

```text
/supervised-task waves
```

That mode should auto-detect the source/target/framework, run `kit doctor`, create `migration/plan`, materialize `wave-001`, and run only the wave-local migration. Manual repair scripts remain available if project config was skipped or an older workspace is being repaired:

```powershell
.\{{Path.Combine(options.Workspace, "scripts", "apply-opencode-project-config.ps1")}} -RepoRoot . -Workspace "{{options.Workspace}}"
```

```bash
./{{Path.Combine(options.Workspace, "scripts", "apply-opencode-project-config.sh")}} --repo-root . --workspace "{{options.Workspace}}"
```

For non-OpenCode agents, give the same kickoff prompt to Codex/CI/another agent. The agent should create or resume the active harness run itself with `{{Path.Combine(options.Workspace, "scripts", "new-harness-run.sh")}}` from bash or `{{Path.Combine(options.Workspace, "scripts", "new-harness-run.ps1")}}` from PowerShell; you should not manually create `{{Path.Combine(options.Workspace, "runs", "run-001")}}`.

## Open this first after a run

The dashboard is the primary review surface. Generate it before reading raw JSON/TXT artifacts:

```bash
{{options.ToolCommand}} report serve --input {{Path.Combine(options.Workspace, "runs", "latest")}} --static-only --out {{Path.Combine(options.Workspace, "dashboard", "latest")}} --format both
```

Open `{{Path.Combine(options.Workspace, "dashboard", "latest", "report-dashboard.html")}}` first. It groups readiness, TODO root causes, unsupported actions, generated files, next actions, evidence links, and agent run history.

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

Agent skill map for reusable behavior contracts:

```text
{{Path.Combine(options.Workspace, "agent-skills", "skill-map.md")}}
```

Recommended first skills for OpenCode/Codex-style runs:

```text
{{Path.Combine(options.Workspace, "agent-skills", "plow-ahead", "SKILL.md")}}
{{Path.Combine(options.Workspace, "agent-skills", "agent-watchdog", "SKILL.md")}}
{{Path.Combine(options.Workspace, "agent-skills", "read-the-damn-docs", "SKILL.md")}}
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

    static bool ExistingJsonEqualsIgnoringProperties(string path, IReadOnlyDictionary<string, object?> payload, params string[] ignoredProperties)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            using var existing = JsonDocument.Parse(File.ReadAllText(path));
            if (existing.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var ignored = new HashSet<string>(ignoredProperties, StringComparer.OrdinalIgnoreCase);
            var existingComparable = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in existing.RootElement.EnumerateObject())
            {
                if (!ignored.Contains(property.Name))
                    existingComparable[property.Name] = JsonSerializer.Serialize(property.Value);
            }

            var nextComparable = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in payload)
            {
                if (!ignored.Contains(pair.Key))
                    nextComparable[pair.Key] = JsonSerializer.Serialize(pair.Value);
            }

            return JsonSerializer.Serialize(existingComparable) == JsonSerializer.Serialize(nextComparable);
        }
        catch
        {
            return false;
        }
    }

    static void WriteJsonFileIfSemanticChanged(string path, SortedDictionary<string, object?> payload, params string[] volatileProperties)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (ExistingJsonEqualsIgnoringProperties(path, payload, volatileProperties))
        {
            Console.WriteLine($"unchanged: {path}");
            return;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"write: {path}");
    }

    static void WriteVersionFile(string workspacePath, KitOptions options, bool updateMode)
    {
        var path = Path.Combine(workspacePath, ".migration-kit", "version.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var previousInstalledAt = DateTimeOffset.UtcNow.ToString("o");
        var previousSource = string.Empty;
        if (File.Exists(path))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("installedAtUtc", out var installedAt))
                    previousInstalledAt = installedAt.GetString() ?? previousInstalledAt;
                if (doc.RootElement.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.String)
                    previousSource = source.GetString() ?? string.Empty;
            }
            catch
            {
                // Keep a fresh installedAt/source if previous metadata was not valid JSON.
            }
        }

        var effectiveSource = IsPlaceholderSource(options.Source) && !IsPlaceholderSource(previousSource)
            ? previousSource
            : options.Source;

        var payload = new SortedDictionary<string, object?>
        {
            ["kitVersion"] = KitVersion,
            ["installedAtUtc"] = previousInstalledAt,
            ["updatedAtUtc"] = DateTimeOffset.UtcNow.ToString("o"),
            ["updateMode"] = updateMode,
            ["workspace"] = options.Workspace,
            ["source"] = effectiveSource,
            ["target"] = options.Target,
            ["config"] = options.Config,
            ["output"] = options.Output,
            ["toolCommand"] = options.ToolCommand,
            ["opencodeProjectConfig"] = options.WithTeam ? (options.SkipProjectConfig ? "skipped" : "available-for-bootstrap-opencode") : "not-installed",
            ["installer"] = "cli-kit-command"
        };
        WriteJsonFileIfSemanticChanged(path, payload, "updatedAtUtc");
    }

    static void WriteSourceScopeMetadata(string workspacePath, string projectRoot, KitOptions options)
    {
        var effectiveSource = options.Source;
        if (IsPlaceholderSource(effectiveSource) && TryReadVersionSource(workspacePath, out var previousSource) && !IsPlaceholderSource(previousSource))
            effectiveSource = previousSource;

        if (IsPlaceholderSource(effectiveSource))
        {
            Console.WriteLine("skip source-scope metadata: --source was not configured");
            return;
        }

        var sourceFullPath = Path.GetFullPath(ToAbsolutePath(effectiveSource, projectRoot));
        var payload = new SortedDictionary<string, object?>
        {
            ["schemaVersion"] = SourceScopeSchema,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("o"),
            ["configuredBy"] = options.WithTeam ? "kit bootstrap-opencode" : "kit init/bootstrap-agent",
            ["source"] = effectiveSource,
            ["sourceRoot"] = effectiveSource,
            ["sourceFullPath"] = sourceFullPath,
            ["workspace"] = options.Workspace,
            ["workspaceFullPath"] = workspacePath,
            ["projectRoot"] = projectRoot,
            ["target"] = options.Target,
            ["config"] = options.Config,
            ["output"] = options.Output
        };

        WriteJsonFileIfSemanticChanged(Path.Combine(workspacePath, "state", "source-scope.json"), payload, "generatedAtUtc");
    }

    static void WriteScopeContract(string workspacePath, string projectRoot, KitOptions options)
    {
        var effectiveSource = options.Source;
        if (IsPlaceholderSource(effectiveSource) && TryReadVersionSource(workspacePath, out var previousSource) && !IsPlaceholderSource(previousSource))
            effectiveSource = previousSource;

        var warnings = new List<string>();
        var normalizedSourceRoot = NormalizeContractPath(effectiveSource, projectRoot, allowPlaceholder: true);
        var allowedSourceRoots = new List<string>();
        if (IsPlaceholderSource(effectiveSource))
        {
            normalizedSourceRoot = string.Empty;
            warnings.Add("--source was not configured; source writes are forbidden until the scope contract is regenerated with an explicit source root.");
        }
        else if (string.IsNullOrWhiteSpace(normalizedSourceRoot) || normalizedSourceRoot == "." || normalizedSourceRoot.StartsWith("../", StringComparison.Ordinal))
        {
            warnings.Add($"Source root '{effectiveSource}' does not resolve to a safe repository-relative path; source writes are forbidden by this contract.");
            normalizedSourceRoot = NormalizePathSeparators(effectiveSource);
        }
        else
        {
            allowedSourceRoots.Add(normalizedSourceRoot);
        }

        var normalizedWorkspace = NormalizeContractPath(options.Workspace, projectRoot, allowPlaceholder: false);
        if (string.IsNullOrWhiteSpace(normalizedWorkspace) || normalizedWorkspace == ".")
            normalizedWorkspace = NormalizePathSeparators(options.Workspace);

        var payload = new SortedDictionary<string, object?>
        {
            ["schemaVersion"] = ScopeContractSchemaVersion,
            ["runId"] = ExtractRunId(options.Output),
            ["ticketId"] = "initial-scope",
            ["createdAtUtc"] = DateTimeOffset.UtcNow.ToString("o"),
            ["sourceRoot"] = normalizedSourceRoot,
            ["workspaceRoot"] = normalizedWorkspace,
            ["allowedSourceRoots"] = allowedSourceRoots.ToArray(),
            ["allowedFiles"] = Array.Empty<string>(),
            ["forbiddenRoots"] = new[]
            {
                ".git",
                "node_modules",
                "bin",
                "obj",
                "Migrator.Core",
                "Migrator.Roslyn",
                "Migrator.Tests",
                "Migrator.Cli"
            },
            ["allowedCommandKinds"] = new[]
            {
                "dotnet-test-scoped",
                "migrator-verify-project",
                "migrator-kit-doctor",
                "git-diff-readonly",
                "claim-heartbeat",
                "evidence-write"
            },
            ["forbiddenCommandPatterns"] = new[]
            {
                "dotnet test .",
                "dotnet test --no-filter",
                "git clean -fdx",
                "rm -rf",
                "Remove-Item -Recurse -Force"
            },
            ["maxChangedFiles"] = 50,
            ["requiresEvidence"] = true,
            ["requiresClaim"] = false,
            ["warnings"] = warnings.ToArray(),
            ["notes"] = "Agent must not inspect, test, or edit outside this scope-contract unless a new contract explicitly allows it."
        };

        WriteJsonFileIfSemanticChanged(Path.Combine(workspacePath, "state", "scope-contract.json"), payload, "createdAtUtc");
    }

    static string ExtractRunId(string outputPath)
    {
        var normalized = NormalizePathSeparators(outputPath).TrimEnd('/');
        var name = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return !string.IsNullOrWhiteSpace(name) && name.StartsWith("run-", StringComparison.OrdinalIgnoreCase) ? name : "run-001";
    }

    static string NormalizeContractPath(string path, string projectRoot, bool allowPlaceholder)
    {
        if (allowPlaceholder && IsPlaceholderSource(path))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            var full = Path.GetFullPath(ToAbsolutePath(path, projectRoot));
            var root = Path.GetFullPath(projectRoot);
            var relative = Path.GetRelativePath(root, full);
            return NormalizePathSeparators(relative).TrimEnd('/');
        }
        catch
        {
            return NormalizePathSeparators(path).TrimStart("./".ToCharArray()).TrimEnd('/');
        }
    }

    static string NormalizePathSeparators(string path)
        => path.Replace('\\', '/').Trim();

    static bool IsPlaceholderSource(string? source)
        => string.IsNullOrWhiteSpace(source)
            || source.Contains("<SOURCE", StringComparison.OrdinalIgnoreCase)
            || source.Contains("SOURCE_SELENIUM_PROJECT_PATH", StringComparison.OrdinalIgnoreCase);

    static bool TryReadVersionSource(string workspacePath, out string source)
    {
        source = string.Empty;
        var path = Path.Combine(workspacePath, ".migration-kit", "version.json");
        if (!File.Exists(path))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("source", out var property) && property.ValueKind == JsonValueKind.String)
            {
                source = property.GetString() ?? string.Empty;
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }

        return false;
    }

    static void WriteGuardChecksums(string workspacePath)
    {
        var guardFiles = new[]
        {
            "scripts/check-scope.ps1",
            "scripts/check-scope.sh",
            "scripts/check-final-gate.ps1",
            "scripts/check-final-gate.sh",
            "scripts/check-harness-policy.ps1",
            "scripts/check-harness-policy.sh",
            "scripts/new-claim.ps1",
            "scripts/new-claim.sh",
            "scripts/update-claim-heartbeat.ps1",
            "scripts/update-claim-heartbeat.sh",
            "scripts/complete-claim.ps1",
            "scripts/complete-claim.sh",
            "scripts/claim-doctor.ps1",
            "scripts/claim-doctor.sh",
            "scripts/move-stale-claims.ps1",
            "scripts/move-stale-claims.sh",
            "scripts/record-run-evidence.ps1",
            "scripts/record-run-evidence.sh",
            "scripts/write-memory-compaction-receipt.ps1",
            "scripts/write-memory-compaction-receipt.sh",
            "scripts/evaluate-command-policy.ps1",
            "scripts/evaluate-command-policy.sh",
            "scripts/check-loop-guard.ps1",
            "scripts/check-loop-guard.sh",
            "scripts/build-harness-dashboard.ps1",
            "scripts/build-harness-dashboard.sh",
            "scripts/export-opencode-session.ps1",
            "scripts/export-opencode-session.sh",
            "scripts/write-sentinel-finding.ps1",
            "scripts/write-sentinel-finding.sh",
            "scripts/complete-sentinel-inspection.ps1",
            "scripts/complete-sentinel-inspection.sh",
            "scripts/write-agent-skill-usage.ps1",
            "scripts/write-agent-skill-usage.sh",
            "scripts/record-agent-skill-profile.ps1",
            "scripts/record-agent-skill-profile.sh",
            "scripts/slice-gate-followups.ps1",
            "scripts/slice-gate-followups.sh",
            "scripts/evaluate-wave-quality-budget.ps1",
            "scripts/evaluate-wave-quality-budget.sh",
            "scripts/start-fresh-wavefront-run.ps1",
            "scripts/start-fresh-wavefront-run.sh",
            "scripts/collect-mapping-research-memory.ps1",
            "scripts/collect-mapping-research-memory.sh",
            "scripts/create-feedback-bundle.ps1",
            "scripts/create-feedback-bundle.sh",
            "scripts/validate-installed-scripts.ps1",
            "scripts/validate-installed-scripts.sh",
            "scripts/validate-run-artifacts.ps1",
            "scripts/validate-run-artifacts.sh",
            "scripts/update-current-ticket-status.ps1",
            "scripts/update-current-ticket-status.sh",
            "scripts/repair-jsonl-ledger.ps1",
            "scripts/repair-jsonl-ledger.sh",
            "scripts/update-sentinel-finding-status.ps1",
            "scripts/update-sentinel-finding-status.sh"
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
        WriteJsonFileIfSemanticChanged(path, payload, "generatedAtUtc");
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
            || normalized.StartsWith("state/scope-contract.json", StringComparison.Ordinal)
            || normalized.StartsWith("state/claims/", StringComparison.Ordinal)
            || normalized.StartsWith("state/memory/", StringComparison.Ordinal)
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

    static bool HasNestedMigrationWorkspace(string projectRoot, string workspacePath, out string detail)
    {
        var projectRootFull = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var workspaceFull = Path.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var nested = new List<string>();
        var ignoredSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", "bin", "obj", "node_modules", ".vs", "playwright-report", "TestResults" };

        if (!Directory.Exists(projectRootFull))
        {
            detail = $"repo root missing: {projectRootFull}";
            return true;
        }

        foreach (var dir in Directory.EnumerateDirectories(projectRootFull, "migration", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Equals(workspaceFull, StringComparison.OrdinalIgnoreCase))
                continue;

            var relativeParts = Path.GetRelativePath(projectRootFull, full).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (relativeParts.Any(part => ignoredSegments.Contains(part)))
                continue;

            if (Directory.Exists(Path.Combine(full, "state"))
                || Directory.Exists(Path.Combine(full, "runs"))
                || Directory.Exists(Path.Combine(full, "plan"))
                || File.Exists(Path.Combine(full, "AGENT_CONTRACT.md")))
            {
                nested.Add(Path.GetRelativePath(projectRootFull, full).Replace('\\', '/'));
            }
        }

        detail = nested.Count == 0
            ? "no nested migration workspace artifacts outside repo-root workspace"
            : "nested migration workspace artifacts outside repo-root workspace: " + string.Join("; ", nested);
        return nested.Count > 0;
    }

    static string ResolveProjectRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
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

    static string NormalizePermissionProfile(string value)
    {
        var normalized = value.Trim();
        return normalized.ToLowerInvariant() switch
        {
            "lownoise" or "low-noise" => "LowNoise",
            "trustedproject" or "trusted-project" => "TrustedProject",
            _ => throw new ArgumentException($"--permission-profile must be one of: LowNoise, TrustedProject. Got: {value}")
        };
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
  selenium-pw-migrator kit bootstrap-agent --agent <codex|generic|opencode> [options]

Commands:
  init          Create a migration workspace from bundled templates.
  update        Safely refresh kit-owned files. Project-owned state is preserved.
  doctor        Validate workspace health and cross-platform prerequisites.
  next-ticket   Generate a bounded prompt for deriving the next actionable ticket.
  bootstrap-opencode
                Install/update the kit, copy repository-root OpenCode commands, run doctor, and optionally install Desktop/CLI config.
  bootstrap-agent
                Install/update the kit and write an explicit handoff pack for Codex, generic agents, or OpenCode.

Common options:
  --workspace <path>        Migration workspace root. Default: migration
  --source <path>           Source Selenium tests/project path; used to write state/scope-contract.json.
  --target-path <path>      Target project/output path metadata.
  --config <path>           Adapter config path. Default: migration/profiles/adapter-config.json
  --out <path>              Default run output path. Default: migration/runs/run-001
  --tool-command <cmd>      Command shown in generated docs. Default: selenium-pw-migrator
  --backup                  Snapshot existing workspace before update/init.
  --force                   Overwrite kit-owned files instead of writing .new conflicts.
  --with-team               Install optional OpenCode team templates.
  --project-desktop         Shortcut for --opencode-install project-desktop.
  --opencode-install <mode> For kit bootstrap-opencode: auto, none, manual, ci, project-local, project-desktop, global.
  --permission-profile <p>  OpenCode permission profile: LowNoise or TrustedProject. Default: LowNoise.
  --skip-project-config     Do not copy opencode.jsonc/.opencode/AGENTS.md into the repository root.
  --agent <name>            For kit bootstrap-agent: codex, generic, opencode. Default: generic.
  --no-codex-files          Do not install migration/codex files.
  --input <path>            Artifact directory for kit next-ticket.

Examples:
  selenium-pw-migrator kit init --workspace migration --source ./OldTests
  selenium-pw-migrator kit update --workspace migration --backup
  selenium-pw-migrator kit doctor --workspace migration
  selenium-pw-migrator kit next-ticket --workspace migration --input migration/runs/run-053
  selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./OldTests --opencode-install auto
  selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./OldTests --project-desktop
  selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./OldTests
  selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./OldTests

Generated orchestration files:
  migration/state/scope-contract.json fixes allowed source/workspace roots for waves.
  migration/scripts/new-claim.* and claim-doctor.* provide file-based claim/lease MVP.
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
        bool SkipProjectConfig,
        string OpenCodeInstall,
        string PermissionProfile,
        string Agent,
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
                SkipProjectConfig: false,
                OpenCodeInstall: "manual",
                PermissionProfile: "LowNoise",
                Agent: "generic",
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
                        "--permission-profile" => options with { PermissionProfile = NormalizePermissionProfile(ReadValue()) },
                        "--skip-project-config" => options with { SkipProjectConfig = true },
                        "--agent" => options with { Agent = NormalizeAgent(ReadValue()) },
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

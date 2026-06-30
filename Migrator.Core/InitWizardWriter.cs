using System.Text.Json;

namespace Migrator.Core;

/// <summary>
/// Writes the safe starter workspace produced by `init --wizard`.
/// This class never edits source tests and never overwrites a non-empty workspace.
/// </summary>
public sealed class InitWizardWriter
{
    private readonly InitWizardOptions _options;

    public InitWizardWriter(InitWizardOptions options)
    {
        _options = options;
    }

    public InitWizardResult Write()
    {
        var workspace = Path.GetFullPath(_options.WorkspacePath);
        var created = new List<string>();
        var warnings = new List<string>();

        if (Directory.Exists(workspace) && Directory.EnumerateFileSystemEntries(workspace).Any())
        {
            return new InitWizardResult(
                Status: "failed",
                WorkspacePath: workspace,
                ConfigPath: Path.Combine(workspace, "profiles", "adapter-config.json"),
                CreatedFiles: Array.Empty<string>(),
                Warnings: new[] { $"Migration workspace '{workspace}' already exists and is not empty. Choose another --workspace/--out path or move the existing files." },
                NextSteps: Array.Empty<string>());
        }

        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(workspace, "profiles"));
        Directory.CreateDirectory(Path.Combine(workspace, "state"));
        Directory.CreateDirectory(Path.Combine(workspace, "runs"));
        Directory.CreateDirectory(Path.Combine(workspace, "evidence"));

        var configPath = Path.Combine(workspace, "profiles", "adapter-config.json");
        WriteStarterConfig(configPath, created);
        WriteCurrentTicket(workspace, created);
        WriteRunLedger(workspace, created);
        WriteReadme(workspace, created);
        WriteNextCommands(workspace, created);
        WriteGitignore(workspace, created);

        if (_options.TargetProjectExists)
        {
            warnings.Add("Existing target project selected: scaffold generation was skipped. Run discover-target before the first migrate run.");
        }
        else if (IsDotNetTarget(_options.TargetBackendId))
        {
            var scaffoldPath = Path.Combine(workspace, "scaffold");
            var scaffold = new ScaffoldWriter(new ScaffoldOptions
            {
                OutPath = scaffoldPath,
                TargetTestFramework = _options.TargetTestFramework,
                Namespace = string.IsNullOrWhiteSpace(_options.TargetNamespace) ? "Migration.Playwright" : _options.TargetNamespace!,
                ProjectName = "Migration.Playwright.Tests"
            }).Write();

            if (scaffold.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var file in scaffold.CreatedFiles)
                    created.Add(Path.Combine("scaffold", file));
            }
            else
            {
                warnings.AddRange(scaffold.Warnings.Select(w => $"Scaffold skipped: {w}"));
            }
        }
        else
        {
            warnings.Add("Playwright TypeScript target selected: scaffold generation is not implemented by init yet. Use an existing @playwright/test project.");
        }

        if (_options.InstallAgentKit)
            WriteAgentKit(workspace, created);

        var nextSteps = BuildNextSteps().ToArray();
        return new InitWizardResult(
            Status: "completed",
            WorkspacePath: workspace,
            ConfigPath: configPath,
            CreatedFiles: created.ToArray(),
            Warnings: warnings.ToArray(),
            NextSteps: nextSteps);
    }

    void WriteStarterConfig(string path, List<string> created)
    {
        var sourceName = ResolveSourceProjectName(_options.SourcePath);
        var targetFramework = NormalizeTargetTestFramework(_options.TargetTestFramework) ?? "nunit";
        var config = new ProjectAdapterConfig(
            SourceProjectName: sourceName,
            UiTargets: Array.Empty<UiTargetMapping>(),
            PageObjects: Array.Empty<PageObjectMapping>(),
            Methods: Array.Empty<MethodMapping>(),
            LocatorSettings: new LocatorSettings(
                _options.DefaultTestIdAttribute,
                new[] { _options.DefaultTestIdAttribute, "data-testid", "data-test-id", "data-test", "data-tid" }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()),
            TestHost: new TestHostConfig
            {
                TargetTestFramework = targetFramework,
                Namespace = string.IsNullOrWhiteSpace(_options.TargetNamespace) ? null : _options.TargetNamespace,
                BaseClass = string.IsNullOrWhiteSpace(_options.TargetBaseClass) ? null : _options.TargetBaseClass
            },
            Verification: new VerificationConfig
            {
                TargetFramework = "net8.0",
                AutoDiscoverNearestProject = true,
                AutoDiscoverProjectReferences = true,
                AutoDiscoverBuildFiles = true,
                AutoDiscoverPackageReferences = false,
                DisableDefaultPackageReferences = false
            },
            QualityGates: new QualityGatesConfig
            {
                FailOnInvalidGeneratedSyntax = true,
                FailOnPageTodo = true,
                FailOnMultipleMatchingScopes = true,
                FailOnPlaceholderLeftovers = true,
                FailOnSuspiciousLiteralVariables = true,
                FailOnLocalProfileLeaks = true
            });

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        File.WriteAllText(path, JsonSerializer.Serialize(config, options));
        created.Add(Path.Combine("profiles", "adapter-config.json"));
    }

    void WriteCurrentTicket(string workspace, List<string> created)
    {
        var content = $@"# Current Migration Ticket

Status: draft
Created: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC

## Source
- Path: `{_options.SourcePath}`
- Detected frontend: `{_options.SourceFrontendId}`
- Detected source framework: `{_options.SourceTestFramework}`

## Target
- Backend: `{_options.TargetBackendId}`
- Test framework: `{_options.TargetTestFramework}`
- Default test id attribute: `{_options.DefaultTestIdAttribute}`

## Safety notes
- Do not edit source tests during migration generation.
- Do not invent selectors; add mappings only from source/POM/target evidence.
- Run `config-validate` after every config/profile edit.
";
        File.WriteAllText(Path.Combine(workspace, "current-ticket.md"), content);
        created.Add("current-ticket.md");
    }

    void WriteRunLedger(string workspace, List<string> created)
    {
        var content = $@"# Migration Run Ledger

| Run | Date UTC | Command | Status | Notes |
|---|---|---|---|---|
| init | {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} | init --wizard | completed | Starter workspace created. |
";
        File.WriteAllText(Path.Combine(workspace, "state", "run-ledger.md"), content);
        created.Add(Path.Combine("state", "run-ledger.md"));
    }

    void WriteReadme(string workspace, List<string> created)
    {
        var content = $@"# Migration Workspace

This workspace was generated by `selenium-pw-migrator init --wizard`.

## Selected shape

- Source path: `{_options.SourcePath}`
- Source frontend: `{_options.SourceFrontendId}`
- Source test framework: `{_options.SourceTestFramework}`
- Target backend: `{_options.TargetBackendId}`
- Target test framework: `{_options.TargetTestFramework}`
- Default test id attribute: `{_options.DefaultTestIdAttribute}`

## Files

- `profiles/adapter-config.json` — safe starter config.
- `current-ticket.md` — current migration ticket/checklist.
- `state/run-ledger.md` — append-only run notes.
- `next-commands.md` — exact first commands to run.
- `scaffold/` — optional Playwright .NET scaffold when no target project exists.

## Rule of thumb

Treat generated mappings as reviewable evidence, not magic. Prefer `index-pom`, `helper-inventory`, and `discover-target` before adding broad config rules.
";
        File.WriteAllText(Path.Combine(workspace, "README.md"), content);
        created.Add("README.md");
    }

    void WriteNextCommands(string workspace, List<string> created)
    {
        var source = EscapePath(_options.SourcePath);
        var config = EscapePath(Path.Combine(_options.WorkspacePath, "profiles", "adapter-config.json"));
        var runRoot = EscapePath(Path.Combine(_options.WorkspacePath, "runs"));
        var isDotNet = IsDotNetTarget(_options.TargetBackendId);
        var targetFrameworkFlag = isDotNet
            ? $" --target-test-framework {_options.TargetTestFramework}"
            : "";
        var targetFlag = isDotNet ? "--target dotnet" : "--target ts";

        var discover = _options.TargetProjectExists && !string.IsNullOrWhiteSpace(_options.TargetProjectPath)
            ? $"dotnet run --project Migrator.Cli -- --mode discover-target --input {EscapePath(_options.TargetProjectPath!)} --out {EscapePath(Path.Combine(_options.WorkspacePath, "target-discovery"))} --format both"
            : null;

        var lines = new List<string>
        {
            "# Next Commands",
            "",
            "Run these from the repository root.",
            "",
            "```bash",
            $"dotnet run --project Migrator.Cli -- --mode config-validate --config {config} --validation-mode strict --out {EscapePath(Path.Combine(_options.WorkspacePath, "config-validate"))}",
        };

        if (discover != null)
            lines.Add(discover);

        lines.Add($"dotnet run --project Migrator.Cli -- --mode analyze --input {source} --config {config} {targetFlag}{targetFrameworkFlag} --out {Path.Combine(runRoot, "run-001-analysis").Replace('\\', '/')}");
        lines.Add($"dotnet run --project Migrator.Cli -- --mode migrate --input {source} --config {config} {targetFlag}{targetFrameworkFlag} --out {Path.Combine(runRoot, "run-001-generated").Replace('\\', '/')}");
        if (isDotNet)
        {
            lines.Add($"dotnet run --project Migrator.Cli -- --mode verify-project --input {source} --config {config}{targetFrameworkFlag} --out {Path.Combine(runRoot, "run-001-verify-project").Replace('\\', '/')}");
        }
        else
        {
            var tsProjectFlag = !string.IsNullOrWhiteSpace(_options.TargetProjectPath) ? $" --ts-project {EscapePath(_options.TargetProjectPath!)}" : "";
            lines.Add($"dotnet run --project Migrator.Cli -- --mode verify-ts-project --input {Path.Combine(runRoot, "run-001-generated").Replace('\\', '/')}{tsProjectFlag} --out {Path.Combine(runRoot, "run-001-verify-ts").Replace('\\', '/')}");
        }
        lines.Add("```");

        File.WriteAllText(Path.Combine(workspace, "next-commands.md"), string.Join(Environment.NewLine, lines) + Environment.NewLine);
        created.Add("next-commands.md");
    }

    void WriteGitignore(string workspace, List<string> created)
    {
        var content = @"runs/*
!runs/.gitkeep
evidence/*.zip
*.tmp
*.bak
*.new
";
        File.WriteAllText(Path.Combine(workspace, ".gitignore"), content);
        File.WriteAllText(Path.Combine(workspace, "runs", ".gitkeep"), "");
        created.Add(".gitignore");
        created.Add(Path.Combine("runs", ".gitkeep"));
    }

    void WriteAgentKit(string workspace, List<string> created)
    {
        var loops = Path.Combine(workspace, ".agent-loops");
        var state = Path.Combine(workspace, ".agent-state");
        Directory.CreateDirectory(loops);
        Directory.CreateDirectory(state);

        File.WriteAllText(Path.Combine(loops, "README.md"), @"# Agent Loop Kit

This lightweight kit was installed by `init --wizard`.

Use it to keep agents inside ticket boundaries:
- read `current-ticket.md` first;
- run generated commands from `next-commands.md`;
- append meaningful progress to `state/run-ledger.md`;
- stop on unsafe selector/POM guesses.
");
        File.WriteAllText(Path.Combine(loops, "kickoff-prompt.txt"), @"You are working inside a Selenium-to-Playwright migration workspace. Read current-ticket.md, profiles/adapter-config.json, state/run-ledger.md, and next-commands.md. Do not edit source tests. Do not invent selectors. Run config-validate before trusting config changes.
");
        File.WriteAllText(Path.Combine(loops, "resume-prompt.txt"), @"Resume from state/run-ledger.md and current-ticket.md. Continue only the current ticket. Preserve generated evidence and report all failed checks honestly.
");
        File.WriteAllText(Path.Combine(state, "current-migration-batch.md"), "# Current Migration Batch\n\nInitialized by init --wizard.\n");

        created.Add(Path.Combine(".agent-loops", "README.md"));
        created.Add(Path.Combine(".agent-loops", "kickoff-prompt.txt"));
        created.Add(Path.Combine(".agent-loops", "resume-prompt.txt"));
        created.Add(Path.Combine(".agent-state", "current-migration-batch.md"));
    }

    IEnumerable<string> BuildNextSteps()
    {
        yield return $"Review {_options.WorkspacePath}/README.md and {_options.WorkspacePath}/current-ticket.md.";
        yield return "Run config-validate against profiles/adapter-config.json.";
        if (_options.TargetProjectExists)
            yield return "Run discover-target for the existing Playwright project before adding mappings.";
        else if (IsDotNetTarget(_options.TargetBackendId))
            yield return "Build the generated scaffold after filling auth/routes.";
        yield return "Run analyze, then migrate, then verify-project using next-commands.md.";
    }

    static bool IsDotNetTarget(string targetBackendId) =>
        targetBackendId.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        || targetBackendId.Equals("playwright-dotnet", StringComparison.OrdinalIgnoreCase);

    static string ResolveSourceProjectName(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return "SeleniumTests";

        var full = Path.GetFullPath(sourcePath);
        if (File.Exists(full))
            return SanitizeIdentifier(Path.GetFileNameWithoutExtension(full));

        var name = new DirectoryInfo(full).Name;
        return string.IsNullOrWhiteSpace(name) ? "SeleniumTests" : SanitizeIdentifier(name);
    }

    static string SanitizeIdentifier(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var sanitized = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "SeleniumTests" : sanitized;
    }

    static string EscapePath(string value) => value.Replace('\\', '/');

    static string? NormalizeTargetTestFramework(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "nunit";

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "nunit" or "n-unit" => "nunit",
            "xunit" or "x-unit" => "xunit",
            _ => null
        };
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

internal sealed record CliCommandInfo(
    string Name,
    string Group,
    string Summary,
    string Details,
    string InputUsage,
    string DefaultOut,
    bool RequiresInput,
    bool PreflightInputExists,
    string[] Examples);

internal static class CliCommandCatalog
{
    public const string Stable = "stable";
    public const string Experimental = "experimental";
    public const string Internal = "internal";

    static readonly CliCommandInfo[] Commands =
    {
        StableCommand("init", "migration", false, false,
            "Create a safe starter migration workspace with the onboarding wizard.",
            "Writes profiles/adapter-config.json, current-ticket.md, state/run-ledger.md, README.md, next-commands.md, and optional scaffold/agent-loop files. Use direct form `selenium-pw-migrator init --wizard` or mode form `--mode init --wizard`.",
            "Optional source path via --source-path <path> for direct init or --input <path> for mode form.",
            "selenium-pw-migrator init --wizard --source-path ./OldTests --target dotnet --target-test-framework xunit --workspace migration"),
        StableCommand("start", "start", false, false,
            "Run the product-repo onboarding wizard and print the next safe command.",
            "Asks or accepts the minimum project facts: Selenium tests path, target backend, existing Playwright project, and agent choice. Writes start-summary, profile skeleton, and next-commands artifacts, then points to doctor/pilot/agent bootstrap. Use direct form `selenium-pw-migrator start --input ./OldTests --agent codex`.",
            "Optional source path via --input <path> or --source-path <path>; interactive consoles can provide it when omitted.",
            "selenium-pw-migrator start --input ./OldTests --agent opencode --workspace migration",
            "selenium-pw-migrator start --source-path ./OldTests --agent codex --target dotnet --target-project ./PlaywrightTests"),
        StableCommand("pilot", "pilot", true, true,
            "Select a representative bounded migration slice before the first real batch.",
            "Scans Selenium test files and chooses a small set covering simple smoke tests, PageObject-heavy files, table/filter patterns, assertions, waits, and custom helpers. Writes pilot-selection.md/json, selected-tests.txt, and next-commands.md. It does not edit source files.",
            "Source Selenium test file or directory.",
            "selenium-pw-migrator pilot --input ./OldTests --max-tests 10 --out migration/pilot",
            "selenium-pw-migrator --mode pilot --input ./OldTests --max-tests 10 --format both"),
        StableCommand("runbook", "runbook", true, true,
            "Generate a practical migration plan for a project before the first run.",
            "Reads source/project signals, optional config layers, source/target capability metadata, generation policy, and writes runbook.md/json with pilot scope, first command chain, risk map, artifacts to collect, and acceptance checklist. Does not modify source files.",
            "Source Selenium test file or project directory.",
            "selenium-pw-migrator runbook --input ./OldTests --target dotnet --target-test-framework xunit --generation-policy conservative --out runbook",
            "selenium-pw-migrator --mode runbook --input ./OldTests --config ./adapter-config.json --out runbook --format both"),
        StableCommand("framework-matrix", "framework-matrix", true, true,
            "Write explicit source and target test-framework support, detection, and readiness reports.",
            "Scans source framework signals for C# NUnit/xUnit/MSTest, Java JUnit4/JUnit5/TestNG, and Python pytest/unittest. Writes framework-matrix.md/json and source-framework-detection.md/json. This mode is read-only and keeps MSTest/Java/Python target paths clearly marked as unsupported or planned unless implemented.",
            "Source Selenium test file or project directory.",
            "selenium-pw-migrator framework matrix --input ./OldTests --target dotnet --target-test-framework xunit --out framework-matrix",
            "selenium-pw-migrator --mode framework-matrix --input ./OldTests --target ts --out framework-matrix --format both"),
        StableCommand("playground", "playground", false, false,
            "Create a five-minute public demo workspace with ready commands and expected outputs.",
            "Writes a self-contained Selenium C# sample, starter adapter config, expected Playwright output, static dashboard sample, PR description sample, manifest, and try-this-first guide. Read-only with respect to real projects; it writes only inside the playground output directory.",
            "No input required. Use --out to choose the playground directory.",
            "selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative",
            "selenium-pw-migrator --mode playground --out playground --format both"),
        StableCommand("playground-verify", "playground-verify", false, false,
            "Verify that a generated playground still has the complete public-demo contract.",
            "Checks the disposable playground root for the manifest, ready command chain, sample Selenium source, adapter config, expected Playwright output, dashboard sample, PR pack sample, and selector-safety wording. Use direct form `selenium-pw-migrator playground verify --input playground` after generating the playground.",
            "Optional playground root via --input <path>; defaults to ./playground.",
            "selenium-pw-migrator playground verify --input playground --out playground-verify",
            "selenium-pw-migrator --mode playground-verify --input playground --format both"),
        StableCommand("memory", "memory", false, false,
            "Manage project-scoped migration memory for supervised runs.",
            "Direct command family: `selenium-pw-migrator memory init|add|explain|doctor|summarize|recall`. Stores inspectable JSON/JSONL under migration/state/memory so later bounded actions, Reviewer, Watchdog, and Final Gate can reuse project-local decisions without relying on chat memory.",
            "No source input required; use --workspace <migration-root> for the migration workspace.",
            "selenium-pw-migrator memory init --workspace migration",
            "selenium-pw-migrator memory add --kind decision \"Keep POM unresolved until target mapping exists\"",
            "selenium-pw-migrator memory doctor --workspace migration --format both --out migration/memory-doctor"),
        StableCommand("migration", "migration", false, false,
            "Plan divide-and-conquer migration waves with project-scoped memory guidance.",
            "Direct command family: `selenium-pw-migrator migration inventory|cluster|plan|plan show|run-wave`. The wavefront planner is read-only; `run-wave` materializes one selected wave as a bounded project-local workspace with source-scope, generated output folder, config-delta, memory-delta, run summary, and migrate scripts. In OpenCode, prefer `/supervised-task waves` to auto-run plan/run-wave from a fresh repository.",
            "Source Selenium test directory via --input <path>; use --workspace <migration-root> for memory guidance.",
            "selenium-pw-migrator migration plan --input ./OldTests --strategy wavefront --workspace migration --out migration/plan",
            "selenium-pw-migrator migration plan show --plan migration/plan",
            "selenium-pw-migrator migration run-wave --plan migration/plan --wave wave-001 --workspace migration --out migration/runs/wave-001",
            "OpenCode: /supervised-task waves",
            "selenium-pw-migrator migration inventory --input ./OldTests --out migration/plan"),
        StableCommand("config-merge", "config merge-deltas", false, false,
            "Merge project-local wave config deltas into a reviewable adapter-config candidate.",
            "Direct command family: `selenium-pw-migrator config merge-deltas|validate-merge`. It reads observed/reviewable `config-delta.json` files, writes `adapter-config.merged.json`, `merge-report.md/json`, and `conflicts.jsonl`, and never edits or promotes the base adapter-config automatically.",
            "Requires --base <adapter-config.json>; accepts --deltas <file|directory|glob> or defaults to migration/state/memory/config-deltas.",
            "selenium-pw-migrator config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge",
            "selenium-pw-migrator config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge"),
        StableCommand("install-doctor", "install", false, false,
            "Explain the active install channel, resolved executable, version, and update command.",
            "Prints what `selenium-pw-migrator` currently resolves to, the inferred channel (npm, standalone, dotnet-tool, source, or unknown), PATH candidates, runtime metadata, and the safest install/update command. Use direct form `selenium-pw-migrator doctor install` after install or before asking users to update.",
            "No input required.",
            "selenium-pw-migrator doctor install --out install-doctor --format both",
            "selenium-pw-migrator self update --print-command"),
        StableCommand("release-doctor", "release-doctor", false, false,
            "Check NuGet/npm/standalone preview readiness before publishing the public tool.",
            "Validates package metadata, version/changelog consistency, release scripts, README_TOOL packing docs, publish workflow dry-run support, NuGet secret references, npm/standalone smoke scripts, install diagnostics, agent bootstrap docs, and repository hygiene. Use direct form `selenium-pw-migrator doctor release` from the repository root.",
            "Optional repository root via --input <path>; defaults to the current directory.",
            "selenium-pw-migrator doctor release --out release-doctor",
            "selenium-pw-migrator --mode release-doctor --input . --format both"),
        StableCommand("analyze", "analysis", true, true,
            "Parse and analyze Selenium tests without generating target files.",
            "Produces reports, unmapped target lists, unsupported action lists, and draft adapter-config hints.",
            "Source Selenium test file or directory.",
            "selenium-pw-migrator --mode analyze --input ./OldTests --out analysis --format both"),
        InternalCommand("dump-ir", "ir-dump", true, true,
            "Dump parser/adapter IR for diagnostics and golden-baseline refactors.",
            "Defaults to legacy IR. Use --ir-version v2 or both to inspect the newer MigrationDocument IR.",
            "Source Selenium test file or directory.",
            "selenium-pw-migrator --mode dump-ir --input ./OldTests --config ./adapter-config.json --out ir-dump --ir-version both"),
        StableCommand("migrate", "generated-tests", true, true,
            "Parse, adapt, and generate Playwright target files.",
            "Uses the selected source frontend and target backend. Does not modify the source project. Use --target-test-framework nunit|xunit for Playwright .NET output and --generation-policy conservative|balanced|aggressive to control review risk.",
            "Source Selenium test file or directory.",
            "selenium-pw-migrator --mode migrate --input ./OldTests --config ./adapter-config.json --target-test-framework xunit --generation-policy balanced --out generated-tests"),
        StableCommand("verify", "verify", true, true,
            "Validate generated code quality with syntax/TODO/config checks.",
            "Runs renderer-level verification without creating a temporary project build.",
            "Source Selenium test file or directory.",
            "selenium-pw-migrator --mode verify --input ./OldTests --config ./adapter-config.json --out verify"),
        StableCommand("verify-project", "verify-project", true, true,
            "Project-aware verification for generated Playwright .NET code.",
            "Creates a temporary verification project, adds configured references, runs dotnet build, and classifies diagnostics. Use --target-test-framework nunit|xunit to choose default test packages when config does not override them.",
            "Source Selenium test directory.",
            "selenium-pw-migrator --mode verify-project --input ./OldTests --config ./adapter-config.json --target-test-framework xunit --out verify-project"),
        ExperimentalCommand("verify-ts-project", "verify-ts-project", true, true,
            "Project-aware verification for generated Playwright TypeScript specs.",
            "Copies generated specs into a workspace, creates tsconfig.migrator.json, and runs npx tsc --noEmit.",
            "Generated TS migration folder or .spec.ts file; pass --ts-project for the real Playwright TS project.",
            "selenium-pw-migrator --mode verify-ts-project --input migration/generated-ts --ts-project ./playwright-ts --out verify-ts-project"),
        StableCommand("doctor", "doctor", true, true,
            "Preflight diagnostics and safe setup repair planning for migration workflows.",
            "Checks input scope, config layers, project references, dotnet availability, POM/source truth, and workspace hygiene. Add --fix for a reversible dry-run plan, or --fix --apply to create safe workspace files and .doctor.new config candidates without editing source tests.",
            "Source tests/project directory to validate before migration.",
            "selenium-pw-migrator --mode doctor --input ./OldTests --config ./adapter-config.json --out doctor",
            "selenium-pw-migrator --mode doctor --input ./OldTests --fix --dry-run --out doctor-fix",
            "selenium-pw-migrator --mode doctor --input ./OldTests --config ./adapter-config.json --fix --apply --out doctor-fix"),
        ExperimentalCommand("explain-todo", "explain-todo", true, true,
            "Explain remaining TODO/root causes from existing migration artifacts.",
            "Reads report/verify/proposal artifacts and writes explain-todo plus agent-next-task outputs.",
            "Directory with migration artifact files.",
            "selenium-pw-migrator --mode explain-todo --input migration/verify-project --out explain-todo --format both"),
        ExperimentalCommand("smoke-plan", "smoke-plan", true, true,
            "Rank generated tests by runtime readiness.",
            "Reads generated files and verification artifacts; writes smoke-plan and runtime checklist outputs. Does not run tests.",
            "Directory with migration/verification artifacts.",
            "selenium-pw-migrator --mode smoke-plan --input migration/verify-project --out smoke-plan --format both"),
        ExperimentalCommand("runtime-classify", "runtime-classify", true, true,
            "Classify Playwright/NUnit/xUnit runtime failures and produce a runtime feedback loop after smoke runs.",
            "Groups common runtime failures such as locator-not-found, strict-mode-violation, timeout-wait-state, assertion-mismatch, navigation-route-missing, auth/session-not-ready, test-data-missing, modal/dialog-state, frame/shadow-dom, and environment/flaky-infra. Also indexes trace zips, screenshots, videos, console/network artifacts, generated/source context links, root-cause groups, suggested config/profile fixes, smoke rerun plan, and runtime readiness score.",
            "Runtime log file, trace zip, or directory with log/report/trace artifacts.",
            "selenium-pw-migrator --mode runtime-classify --input migration/runtime-logs --out runtime-classify --format both"),
        ExperimentalCommand("selector-evidence", "selector-evidence", true, true,
            "Explain where generated selectors and locators came from.",
            "Scans Selenium POM/source selectors, adapter-config UiTarget mappings, and generated Playwright locators. Writes selector-evidence.md/json with confidence scores, unsafe/inferred selectors, and cannot-prove gaps. Read-only; never invents selectors or edits config/source.",
            "Source project, generated migration run, or artifact directory containing source/config/generated locator evidence.",
            "selenium-pw-migrator selector evidence --input migration/runs/latest --config ./adapter-config.json --out selector-evidence",
            "selenium-pw-migrator --mode selector-evidence --input ./OldTests --config ./adapter-config.json --out selector-evidence --format both"),
        ExperimentalCommand("config-author", "config-proposals", true, true,
            "Propose small evidence-driven adapter-config changes without editing config files.",
            "Reads selector-evidence, index-pom, helper-inventory, discover-target, explain-todo, runtime feedback, triage, and config validation artifacts. Writes config-proposals.md/json and config-proposals.patch with safety classification and config-diff commands. Read-only; never invents selectors or applies patches.",
            "Migration run/artifact directory, source project, or evidence file.",
            "selenium-pw-migrator config author --input migration/runs/latest --config ./adapter-config.json --out config-proposals --format both",
            "selenium-pw-migrator --mode config-author --input migration/runs/latest --config ./adapter-config.json --out config-proposals --format both"),
        ExperimentalCommand("learn-pack", "learn-pack", true, true,
            "Extract reusable migration knowledge from completed run artifacts.",
            "Reads selector evidence, helper inventory, POM index, config proposals, runtime feedback, verify reports, generated files, and current config. Writes learn-pack.md/json, learn-changelog.md, learning-safety-report.md, and reusable-profile-layer.json. Read-only; never edits source/config/generated files and never exports suppressions/source-only identifiers.",
            "Completed migration run, artifact directory, or evidence file.",
            "selenium-pw-migrator learn pack --input migration/runs/latest --config ./adapter-config.json --out learn-pack --format both",
            "selenium-pw-migrator --mode learn-pack --input migration/runs/latest --config ./adapter-config.json --out learn-pack --format both"),
        ExperimentalCommand("migration-board", "migration-board", true, true,
            "Build an HTML dashboard from existing migration artifacts.",
            "Reads report, explain-todo, smoke-plan, verify, project-verify, generated files, and related artifacts.",
            "Directory with migration artifact files.",
            "selenium-pw-migrator --mode migration-board --input migration/verify-project --out board --format both"),
        ExperimentalCommand("report-serve", "report-dashboard", true, true,
            "Build and optionally serve a local migration triage dashboard.",
            "Reads migration run artifacts, compares sibling runs, groups TODO/unsupported/unmapped/root-cause/runtime items, links verify/runtime diagnostics, exports a shareable evidence zip, and writes starter accept/defer/create-ticket triage decisions. Use direct form `selenium-pw-migrator report serve --input <run> --port 5077`; pass --port 0 or --static-only for static files only.",
            "Directory with migration artifact files.",
            "selenium-pw-migrator report serve --input migration/runs/latest --port 5077 --out report-dashboard",
            "selenium-pw-migrator --mode report-serve --input migration/runs/latest --static-only --out report-dashboard"),
        StableCommand("evidence-pack", "evidence", true, true,
            "Create a redacted shareable zip from migration run artifacts.",
            "Packs reports, generated Migrator output, config/profile layers, dashboard/runtime artifacts, selected logs, a manifest, and checksums. Source-like files are excluded unless generated by Migrator or --include-source is explicit.",
            "Directory with migration run artifacts.",
            "selenium-pw-migrator evidence pack --input migration/runs/run-042 --out evidence/run-042.zip",
            "selenium-pw-migrator --mode evidence-pack --input migration/runs/run-042 --out evidence/run-042.zip"),
        ExperimentalCommand("pr-pack", "review", true, true,
            "Create a migration PR/review bundle from run artifacts.",
            "Reads runbook, report-serve triage, runtime feedback, selector evidence, verify reports, evidence manifests, and generated files. Writes pr-summary.md, pr-pack.json, reviewer-checklist.md, and suggested-pr-description.md. Read-only; does not edit source/config/generated files.",
            "Migration run/artifact directory with reports and generated Migrator output.",
            "selenium-pw-migrator pr pack --input migration/runs/run-042 --out migration/pr-pack --format both",
            "selenium-pw-migrator --mode pr-pack --input migration/runs/latest --config ./adapter-config.json --out pr-pack --format both"),
        ExperimentalCommand("agent-contract", "agent-contract", true, true,
            "Generate a ticket-specific agent contract pack for safe migration loops.",
            "Reads a migration ticket/workspace/artifact directory and writes agent-contract.md/json, allowed paths, stop policy, exact next commands, report template, and agent-prompts for coordinator/migrator/verifier roles. Read-only for source tests and generated output.",
            "Migration ticket, workspace, source project, or artifact directory.",
            "selenium-pw-migrator agent contract --input migration/current-ticket.md --out migration/agent-contract --format both",
            "selenium-pw-migrator --mode agent-contract --input migration/runs/latest --config ./adapter-config.json --out agent-contract --format both"),
        ExperimentalCommand("profile-list", "profile-marketplace", false, false,
            "List built-in migration profiles available offline.",
            "Shows versioned, validated starter profile layers bundled with the CLI. No remote index is used.",
            "No input required.",
            "selenium-pw-migrator profile list"),
        ExperimentalCommand("profile-search", "profile-marketplace", true, false,
            "Search built-in migration profiles by id, framework, backend, or capability.",
            "Searches the offline profile catalog and writes markdown/json catalog artifacts.",
            "Search query such as selenium-nunit, xunit, csharp, or data-tid.",
            "selenium-pw-migrator profile search selenium-nunit"),
        ExperimentalCommand("profile-recommend", "profile-marketplace", true, true,
            "Score built-in profiles against a source project and recommend install order.",
            "Detects language/framework/POM/helper/test-id signals, assigns compatibility scores and writes profile-recommendations.md/json. Does not install or modify profiles.",
            "Source Selenium tests/project directory.",
            "selenium-pw-migrator profile recommend --input ./OldTests --target-test-framework xunit --out profile-recommendations"),
        ExperimentalCommand("profile-inspect", "profile-marketplace", true, false,
            "Inspect a profile before installing it.",
            "Explains metadata, supported patterns, required evidence, safety level, limitations, and config summary.",
            "Built-in profile id.",
            "selenium-pw-migrator profile inspect basic-csharp-xunit"),
        ExperimentalCommand("profile-install", "profiles", true, false,
            "Install a built-in profile as a reviewed config layer.",
            "Writes <profile-id>.adapter-config.json and metadata into --out (default: profiles). Existing files are never overwritten silently.",
            "Built-in profile id.",
            "selenium-pw-migrator profile install basic-csharp-nunit --out profiles"),
        ExperimentalCommand("profile-diff", "profile-marketplace", false, false,
            "Compare an existing adapter config against a profile/config layer.",
            "Reads --before and --after, highlights semantic count changes and risky suppressions/source-only identifiers.",
            "--before adapter-config.json --after profile-id-or-config.json.",
            "selenium-pw-migrator profile diff --before adapter-config.json --after basic-csharp-xunit --out profile-diff"),
        ExperimentalCommand("profile-match", "profile-match", true, true,
            "Estimate whether existing config/profile layers can be reused for a source project.",
            "Compares source signals against one or more --config layers and writes profile-match reports.",
            "Source tests/project directory plus one or more --config profile layers.",
            "selenium-pw-migrator --mode profile-match --input ./OldTests --config ./profiles/base.adapter.json --out profile-match"),
        StableCommand("capabilities", "capabilities", false, false,
            "List built-in source frontends and target backends with support status.",
            "Writes source/target capability matrices for users and extension authors. Does not process project code.",
            "No input required.",
            "selenium-pw-migrator --mode capabilities --out capabilities --format both"),
        ExperimentalCommand("config-schema", "config-schema", false, false,
            "Write/copy adapter-config JSON Schema for editors and agents.",
            "Does not validate project code; use config-validate for safety checks.",
            "No input required.",
            "selenium-pw-migrator --mode config-schema --out schema"),
        StableCommand("config-validate", "config-validate", false, false,
            "Validate adapter-config structure and agent-safety rules.",
            "Use --config one or more times, or --input for a single config file. Fails dangerous config in strict/production modes.",
            "--config adapter-config.json or --input adapter-config.json.",
            "selenium-pw-migrator --mode config-validate --config ./adapter-config.json --target ts --validation-mode production --out config-validate"),
        InternalCommand("config-normalize", "config-normalize", false, false,
            "Convert legacy adapter-config v1 layers into migration-profile v2 shape.",
            "Compatibility/maintainer command. Writes normalized profile output without modifying source config files.",
            "--config adapter-config.json or --input adapter-config.json.",
            "selenium-pw-migrator --mode config-normalize --config ./adapter-config.json --out config-normalize"),
        StableCommand("config-diff", "config-diff", false, false,
            "Compare adapter-config/profile documents and highlight risky agent changes.",
            "Requires --before and --after. Does not process source files.",
            "No --input; requires --before <path> and --after <path>.",
            "selenium-pw-migrator --mode config-diff --before adapter.old.json --after adapter-config.json --out config-diff"),
        StableCommand("guard", "guard", false, false,
            "Compare two migration artifact directories and fail on regressions.",
            "Requires --before and --after. Intended for CI/agent safety checks.",
            "No --input; requires --before <path> and --after <path>.",
            "selenium-pw-migrator --mode guard --before migration/baseline --after migration/current --out guard"),
        StableCommand("propose", "mapping-proposals", true, true,
            "Generate mapping proposals from migration artifacts.",
            "Reads reports and generated output; writes mapping-proposals.md/json. Does not modify config.",
            "Directory with report/generation artifacts.",
            "selenium-pw-migrator --mode propose --input migration/generated --config ./adapter-config.json --format both"),
        StableCommand("discover-target", "target-discovery", true, false,
            "Scan a target Playwright .NET project and collect infrastructure facts.",
            "Writes target inventory, target style notes, adapter-config draft, and discovery warnings. Does not modify config.",
            "Target Playwright .NET project root directory.",
            "selenium-pw-migrator --mode discover-target --input ./team-playwright-tests --out target-discovery"),
        StableCommand("index-pom", "pom-index", true, true,
            "Scan Selenium PageObjects/source files and target Playwright/Kontur POMs for selector evidence.",
            "Writes POM index, inferred candidates, target-side POM evidence, and adapter-config POM draft. Missing source mappings require review.",
            "Selenium project/PageObject directory or target Playwright/Kontur POM directory.",
            "selenium-pw-migrator --mode index-pom --input ./OldTests --out pom-index --format both"),
        StableCommand("helper-inventory", "helper-inventory", true, false,
            "Scan helper/POM method bodies and infer MethodSemantics candidates.",
            "Use before mapping or suppressing project helper wrappers. Does not modify config/source.",
            "Selenium helper/POM/source directory or file.",
            "selenium-pw-migrator --mode helper-inventory --input ./OldTests --out helper-inventory --format both"),
        ExperimentalCommand("orchestrate", "orchestration", true, true,
            "Dry-run analyze -> migrate -> verify -> propose orchestration.",
            "Writes stage artifacts into subdirectories and an orchestration report. Does not auto-apply proposals or run runtime tests.",
            "Source Selenium tests directory.",
            "selenium-pw-migrator --mode orchestrate --input ./OldTests --config ./adapter-config.json --out orchestration --format both"),
        StableCommand("scaffold", "generated-scaffold", false, false,
            "Generate a minimal compile-ready Playwright .NET test project scaffold.",
            "Creates csproj, GeneratedTestBase, TestSettings, ExampleSmokeTest, adapter-config draft, README, and .gitignore. Supports --target-test-framework nunit|xunit.",
            "No input required.",
            "selenium-pw-migrator --mode scaffold --target-test-framework xunit --out generated-scaffold"),
        StableCommand("bootstrap-project", "project-bootstrap", false, false,
            "Create reusable migration profile skeletons for a new project.",
            "Uses --input when provided to derive project naming and nearest .csproj; otherwise uses the current directory.",
            "Optional source project/tests path.",
            "selenium-pw-migrator --mode bootstrap-project --input ./OldTests --out bootstrap-oldtests"),
    };

    internal static IReadOnlyList<CliCommandInfo> All => Commands;

    internal static bool IsValidMode(string mode) => Commands.Any(c => string.Equals(c.Name, mode, StringComparison.OrdinalIgnoreCase));

    internal static CliCommandInfo Get(string mode) => Commands.First(c => string.Equals(c.Name, mode, StringComparison.OrdinalIgnoreCase));

    internal static bool RequiresInput(string mode) => Get(mode).RequiresInput;

    internal static bool ShouldPreflightInputExists(string mode) => Get(mode).PreflightInputExists;

    internal static string FormatModeList() => string.Join("|", Commands.Select(c => c.Name));

    internal static void WriteGlobalHelp()
    {
        Console.WriteLine(BuildGlobalHelp());
    }

    internal static void WriteCommandHelp(string mode)
    {
        if (!IsValidMode(mode))
        {
            Console.Error.WriteLine($"Unknown mode: {mode}");
            WriteGlobalHelp();
            return;
        }

        Console.WriteLine(BuildCommandHelp(Get(mode)));
    }

    internal static string BuildGlobalHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Usage: selenium-pw-migrator --mode <mode> [options]");
        sb.AppendLine();
        sb.AppendLine("Use `selenium-pw-migrator --mode <mode> --help` for command-specific help.");
        sb.AppendLine();
        sb.AppendLine("Modes:");
        foreach (var group in new[] { Stable, Experimental, Internal })
        {
            var commands = Commands.Where(c => c.Group == group).ToArray();
            if (commands.Length == 0)
                continue;

            sb.AppendLine($"  {GroupTitle(group)}:");
            foreach (var command in commands)
                sb.AppendLine($"    {command.Name.PadRight(18)} {command.Summary}");
        }

        sb.AppendLine();
        AppendCommonOptions(sb);
        AppendExitCodes(sb);
        sb.AppendLine("Examples:");
        foreach (var example in Commands.Where(c => c.Group == Stable).Take(8).SelectMany(c => c.Examples.Take(1)))
            sb.AppendLine($"  {example}");
        sb.AppendLine();
        sb.AppendLine("Output workspace examples:");
        sb.AppendLine("  --out orchestration-7          writes to migration/orchestration-7");
        sb.AppendLine("  --out migration/custom-run     writes to migration/custom-run");
        sb.AppendLine("  --out C:\\temp\\migration-run    writes to absolute path C:\\temp\\migration-run");
        return sb.ToString();
    }

    internal static string BuildCommandHelp(CliCommandInfo command)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Usage: selenium-pw-migrator --mode {command.Name} {(command.RequiresInput ? "--input <path> " : string.Empty)}[options]");
        sb.AppendLine();
        sb.AppendLine($"Mode: {command.Name}");
        sb.AppendLine($"Group: {GroupTitle(command.Group)}");
        sb.AppendLine($"Default --out: {command.DefaultOut}");
        sb.AppendLine();
        sb.AppendLine(command.Summary);
        sb.AppendLine(command.Details);
        sb.AppendLine();
        sb.AppendLine("Input:");
        sb.AppendLine($"  {command.InputUsage}");
        sb.AppendLine();
        AppendCommonOptions(sb);
        sb.AppendLine("Examples:");
        foreach (var example in command.Examples)
            sb.AppendLine($"  {example}");
        sb.AppendLine();
        AppendExitCodes(sb);
        return sb.ToString();
    }

    static CliCommandInfo StableCommand(string name, string defaultOut, bool requiresInput, bool preflightInputExists, string summary, string details, string inputUsage, params string[] examples) =>
        new(name, Stable, summary, details, inputUsage, defaultOut, requiresInput, preflightInputExists, examples);

    static CliCommandInfo ExperimentalCommand(string name, string defaultOut, bool requiresInput, bool preflightInputExists, string summary, string details, string inputUsage, params string[] examples) =>
        new(name, Experimental, summary, details, inputUsage, defaultOut, requiresInput, preflightInputExists, examples);

    static CliCommandInfo InternalCommand(string name, string defaultOut, bool requiresInput, bool preflightInputExists, string summary, string details, string inputUsage, params string[] examples) =>
        new(name, Internal, summary, details, inputUsage, defaultOut, requiresInput, preflightInputExists, examples);

    static string GroupTitle(string group) => group switch
    {
        Stable => "Stable public commands",
        Experimental => "Experimental preview commands",
        Internal => "Internal/maintainer commands",
        _ => group
    };

    static void AppendCommonOptions(StringBuilder sb)
    {
        sb.AppendLine("Options:");
        sb.AppendLine("  --mode <mode>                    Operation mode.");
        sb.AppendLine("  --input <file-or-directory>      Source, artifact, or project path depending on mode.");
        sb.AppendLine("  --out <output-directory>         Output directory inside --workspace by default.");
        sb.AppendLine("  --workspace <directory>          Migration artifacts root (default: migration).");
        sb.AppendLine("  --target <dotnet|ts|playwright-dotnet|playwright-typescript>");
        sb.AppendLine("                                   Generation target for migrate/orchestrate.");
        sb.AppendLine("  --target-test-framework <nunit|xunit>");
        sb.AppendLine("                                   Test framework for Playwright .NET output/scaffold/verify defaults.");
        sb.AppendLine("  --generation-policy <conservative|balanced|aggressive>");
        sb.AppendLine("                                   Controls mapped helper risk: more TODOs, current behavior, or more active code.");
        sb.AppendLine("  --agent <opencode|codex|generic|manual>");
        sb.AppendLine("                                   Agent handoff preference for start/onboarding.");
        sb.AppendLine("  --max-tests <number>             Pilot slice budget for `pilot` (default: 10).");
        sb.AppendLine("  --selected-tests <file>          Limit analyze/migrate/verify/verify-project to file::Class.Test entries.");
        sb.AppendLine("  --wizard                         Run init in guided/onboarding mode.");
        sb.AppendLine("  --fix                           Add safe doctor repair plan artifacts.");
        sb.AppendLine("  --dry-run                       Preview doctor fixes without writing project/config files.");
        sb.AppendLine("  --apply                         Apply safe doctor fixes inside workspace or .doctor.new config files.");
        sb.AppendLine("  --source-path <path>             Source path for direct `init --wizard` form.");
        sb.AppendLine("  --test-id-attribute <attr>       Default test id attribute for init config.");
        sb.AppendLine("  --target-project <path>          Existing target project path for init/discover-target handoff.");
        sb.AppendLine("  --target-namespace <namespace>   Target namespace for init-generated config/scaffold.");
        sb.AppendLine("  --target-base-class <class>      Target base class for init-generated config.");
        sb.AppendLine("  --install-kit|--no-install-kit   Include or skip lightweight agent loop files during init.");
        sb.AppendLine("  --source <auto|csharp-selenium|java-selenium|python-selenium>");
        sb.AppendLine("                                   Source frontend for source-processing modes.");
        sb.AppendLine("  --config <adapter-config.json>   Adapter config layer. Can be repeated.");
        sb.AppendLine("  --before <path> / --after <path> Required by config-diff and guard.");
        sb.AppendLine("  --format <text|json|both>        Report format (default: both).");
        sb.AppendLine("  --ir-version <legacy|v2|both>    IR dump schema for dump-ir.");
        sb.AppendLine("  --render-ir <legacy|v2>          Experimental render input model.");
        sb.AppendLine("  --validation-mode <warn|strict|production>");
        sb.AppendLine("                                   Config validation strictness.");
        sb.AppendLine("  --recursive-artifacts            Allow nested artifact lookup where supported.");
        sb.AppendLine("  --port <number>                  Local report server port for report-serve. Use 0 for static only.");
        sb.AppendLine("  --static-only, --no-server       Generate report-serve static dashboard without starting a server.");
        sb.AppendLine("  --include-source                 Include source-like files in evidence-pack after explicit review.");
        sb.AppendLine("  --fail-on-unsupported            Exit code 2 if unsupported actions exist.");
        sb.AppendLine("  --fail-on-todo                   Exit code 3 if TODO comments exist.");
        sb.AppendLine("  --help, -h                       Show global or command-specific help.");
        sb.AppendLine("  --version, -v                    Show CLI version metadata and exit.");
        sb.AppendLine();
    }

    static void AppendExitCodes(StringBuilder sb)
    {
        sb.AppendLine("Exit codes:");
        sb.AppendLine("  0  Success or help shown.");
        sb.AppendLine("  1  User/input error or verification/quality gate failure.");
        sb.AppendLine("  2  Invalid config, invalid source/target/mode, unsupported gate, or preflight failure.");
        sb.AppendLine("  3  TODO quality gate or stage failure.");
        sb.AppendLine("  4  Generated syntax errors detected.");
        sb.AppendLine();
    }
}

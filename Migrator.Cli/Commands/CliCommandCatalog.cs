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
            "Optional source path via --source <path> for direct init or --input <path> for mode form.",
            "selenium-pw-migrator init --wizard --source ./OldTests --target dotnet --target-test-framework xunit --workspace migration"),
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
            "Uses the selected source frontend and target backend. Does not modify the source project. Use --target-test-framework nunit|xunit for Playwright .NET output.",
            "Source Selenium test file or directory.",
            "selenium-pw-migrator --mode migrate --input ./OldTests --config ./adapter-config.json --target-test-framework xunit --out generated-tests"),
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
            "Classify Playwright/NUnit runtime failure logs after smoke runs.",
            "Groups common runtime failures such as locator-not-found, timeouts, assertion mismatches, navigation failures, and setup issues.",
            "Runtime log file or directory with log/report artifacts.",
            "selenium-pw-migrator --mode runtime-classify --input migration/runtime-logs --out runtime-classify --format both"),
        ExperimentalCommand("migration-board", "migration-board", true, true,
            "Build an HTML dashboard from existing migration artifacts.",
            "Reads report, explain-todo, smoke-plan, verify, project-verify, generated files, and related artifacts.",
            "Directory with migration artifact files.",
            "selenium-pw-migrator --mode migration-board --input migration/verify-project --out board --format both"),
        ExperimentalCommand("report-serve", "report-dashboard", true, true,
            "Build and optionally serve a local migration dashboard.",
            "Reads migration run artifacts, compares sibling runs, groups TODO/unsupported/unmapped items, links verify/runtime diagnostics, and exports a shareable evidence zip. Use direct form `selenium-pw-migrator report serve --input <run> --port 5077`; pass --port 0 or --static-only for static files only.",
            "Directory with migration artifact files.",
            "selenium-pw-migrator report serve --input migration/runs/latest --port 5077 --out report-dashboard",
            "selenium-pw-migrator --mode report-serve --input migration/runs/latest --static-only --out report-dashboard"),
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
            "Scan Selenium PageObjects/source files and collect source-truth facts.",
            "Writes POM index, inferred candidates, and adapter-config POM draft. Missing POMs require review.",
            "Selenium project/PageObject directory.",
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
        sb.AppendLine("  --wizard                         Run init in guided/onboarding mode.");
        sb.AppendLine("  --fix                           Add safe doctor repair plan artifacts.");
        sb.AppendLine("  --dry-run                       Preview doctor fixes without writing project/config files.");
        sb.AppendLine("  --apply                         Apply safe doctor fixes inside workspace or .doctor.new config files.");
        sb.AppendLine("  --source <path>                  Source path for direct `init --wizard` form.");
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
        sb.AppendLine("  --fail-on-unsupported            Exit code 2 if unsupported actions exist.");
        sb.AppendLine("  --fail-on-todo                   Exit code 3 if TODO comments exist.");
        sb.AppendLine("  --help, -h                       Show global or command-specific help.");
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

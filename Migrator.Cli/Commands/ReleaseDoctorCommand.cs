using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

internal static class ReleaseDoctorCommand
{
    const string ExpectedPackageId = "SeleniumPlaywrightMigrator";
    const string ExpectedToolCommand = "selenium-pw-migrator";

    public static int RunReleaseDoctor(string inputPath, string outPath, string format)
    {
        var root = ResolveRepositoryRoot(inputPath);
        Directory.CreateDirectory(outPath);

        var checks = new List<ReleaseDoctorCheck>();
        var projectPath = Path.Combine(root, "Migrator.Cli", "Migrator.Cli.csproj");
        XDocument? project = File.Exists(projectPath) ? XDocument.Load(projectPath) : null;

        AddFileChecks(root, checks);
        AddPackageMetadataChecks(project, checks);
        AddScriptChecks(root, project, checks);
        AddWorkflowChecks(root, project, checks);
        AddDocumentationChecks(root, project, checks);
        AddToolManifestExampleChecks(root, project, checks);
        AddInstallUxChecks(root, checks);
        AddRepositoryHygieneChecks(root, checks);

        var failed = checks.Count(c => c.Status == "fail");
        var warnings = checks.Count(c => c.Status == "warn");
        var status = failed == 0 ? "passed" : "failed";
        var report = new ReleaseDoctorReport(
            "release-doctor/v1",
            DateTimeOffset.UtcNow,
            root,
            status,
            failed,
            warnings,
            checks.ToArray());

        if (format == "text" || format == "both")
            File.WriteAllText(Path.Combine(outPath, "release-doctor-report.md"), BuildMarkdown(report), new UTF8Encoding(false));
        if (format == "json" || format == "both")
            File.WriteAllText(Path.Combine(outPath, "release-doctor-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));

        Console.WriteLine($"Release doctor: {status} ({failed} failed, {warnings} warnings)");
        Console.WriteLine($"Report: {Path.GetFullPath(outPath)}");
        return failed == 0 ? 0 : 2;
    }

    static string ResolveRepositoryRoot(string inputPath)
    {
        var start = string.IsNullOrWhiteSpace(inputPath) ? Directory.GetCurrentDirectory() : Path.GetFullPath(inputPath);
        var dir = File.Exists(start) ? new DirectoryInfo(Path.GetDirectoryName(start) ?? Directory.GetCurrentDirectory()) : new DirectoryInfo(start);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Migrator.sln")) && File.Exists(Path.Combine(dir.FullName, "Migrator.Cli", "Migrator.Cli.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Path.GetFullPath(start);
    }

    static void AddFileChecks(string root, List<ReleaseDoctorCheck> checks)
    {
        foreach (var file in new[]
        {
            "Migrator.sln",
            "README.md",
            "USER_GUIDE.md",
            "CHANGELOG.md",
            "LICENSE",
            "SECURITY.md",
            "CONTRIBUTING.md",
            "Migrator.Cli/README_TOOL.md",
            "assets/icon.png",
            "docs/release-process.md",
            "docs/packaging-and-distribution.md",
            "docs/tool-installation.md",
            ".github/workflows/ci.yml",
            ".github/workflows/publish-nuget.yml",
            ".github/workflows/full-validation.yml",
            ".github/workflows/publish-npm.yml",
            "scripts/pack-tool.sh",
            "scripts/pack-tool.ps1",
            "scripts/verify-nupkg-contents.sh",
            "scripts/verify-nupkg-contents.ps1",
            "scripts/smoke-local-tool-package.sh",
            "scripts/smoke-local-tool-package.ps1",
            "scripts/push-tool.sh",
            "scripts/push-tool.ps1",
            "scripts/package-standalone.sh",
            "scripts/package-standalone.ps1",
            "scripts/verify-standalone-package.ps1",
            "scripts/smoke-npm-registry-install.sh",
            "scripts/smoke-npm-registry-install.ps1",
            "scripts/verify-release-artifacts.ps1",
        })
        {
            Add(checks, File.Exists(Path.Combine(root, ToOsPath(file))), "file", file, "required release file exists", "missing required release file");
        }
    }

    static void AddPackageMetadataChecks(XDocument? project, List<ReleaseDoctorCheck> checks)
    {
        Add(checks, project != null, "metadata", "Migrator.Cli/Migrator.Cli.csproj", "package project is readable", "package project is missing or invalid");
        if (project == null)
            return;

        RequireValue(project, checks, "PackAsTool", "true");
        RequireValue(project, checks, "ToolCommandName", ExpectedToolCommand);
        RequireValue(project, checks, "PackageId", ExpectedPackageId);
        RequireValue(project, checks, "PackageReadmeFile", "README_TOOL.md");
        RequireValue(project, checks, "PackageLicenseExpression", "MIT");
        RequireValue(project, checks, "PackageIcon", "assets/icon.png");
        RequirePresent(project, checks, "Title");
        RequirePresent(project, checks, "Version");
        RequirePresent(project, checks, "Authors");
        RequirePresent(project, checks, "Company");
        RequirePresent(project, checks, "Description");
        RequirePresent(project, checks, "PackageTags");
        RequirePresent(project, checks, "PackageProjectUrl");
        RequirePresent(project, checks, "RepositoryUrl");
        RequirePresent(project, checks, "PackageReleaseNotes");

        var version = Value(project, "Version");
        Add(checks,
            Regex.IsMatch(version, @"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$"),
            "metadata", "Version", "version is SemVer-compatible", $"version is not SemVer-compatible: {version}");
        Add(checks,
            Value(project, "Description").Contains("AST", StringComparison.OrdinalIgnoreCase),
            "metadata", "Description", "description keeps AST positioning", "description should mention AST as the differentiator");
        Add(checks,
            Value(project, "PackageTags").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains("ast", StringComparer.OrdinalIgnoreCase),
            "metadata", "PackageTags", "NuGet tags include ast", "NuGet tags should include ast");
    }

    static void AddScriptChecks(string root, XDocument? project, List<ReleaseDoctorCheck> checks)
    {
        var packageId = project == null ? ExpectedPackageId : Value(project, "PackageId");
        foreach (var script in new[]
        {
            "scripts/pack-tool.sh",
            "scripts/smoke-local-tool-package.sh",
            "scripts/pack-tool.ps1",
            "scripts/push-tool.ps1",
            "scripts/install-local-tool.ps1",
            "scripts/smoke-local-tool-package.ps1",
            "scripts/verify-nupkg-contents.ps1",
        })
        {
            var path = Path.Combine(root, ToOsPath(script));
            if (!File.Exists(path))
                continue;
            var text = File.ReadAllText(path);
            Add(checks, text.Contains(packageId, StringComparison.Ordinal), "scripts", script, "script default PackageId matches csproj", $"script does not reference PackageId {packageId}");
        }
    }

    static void AddWorkflowChecks(string root, XDocument? project, List<ReleaseDoctorCheck> checks)
    {
        var packageId = project == null ? ExpectedPackageId : Value(project, "PackageId");
        var workflowPath = Path.Combine(root, ".github", "workflows", "publish-nuget.yml");
        if (!File.Exists(workflowPath))
            return;
        var workflow = File.ReadAllText(workflowPath);
        Add(checks, workflow.Contains("workflow_dispatch", StringComparison.Ordinal), "workflow", "publish-nuget.yml", "publish workflow is manual", "publish workflow should use workflow_dispatch");
        Add(checks, workflow.Contains("dry_run", StringComparison.Ordinal), "workflow", "publish-nuget.yml", "publish workflow supports dry-run", "publish workflow should support dry_run");
        Add(checks, workflow.Contains("NuGet/login@v1", StringComparison.Ordinal), "workflow", "publish-nuget.yml", "publish workflow uses NuGet Trusted Publishing login", "publish workflow should use NuGet/login@v1 for Trusted Publishing");
        Add(checks, workflow.Contains("id-token: write", StringComparison.Ordinal), "workflow", "publish-nuget.yml", "publish workflow can request GitHub OIDC token", "publish workflow should grant id-token: write for NuGet Trusted Publishing");
        Add(checks, workflow.Contains("steps.nuget-login.outputs.NUGET_API_KEY", StringComparison.Ordinal), "workflow", "publish-nuget.yml", "publish workflow passes short-lived NuGet API key", "publish workflow should pass the NuGet/login@v1 NUGET_API_KEY output to push-tool");
        Add(checks, workflow.Contains("nuget-production", StringComparison.Ordinal), "workflow", "publish-nuget.yml", "publish workflow uses protected environment hook", "publish workflow should use a production environment gate");
        Add(checks, workflow.Contains(packageId, StringComparison.Ordinal), "workflow", "publish-nuget.yml", "workflow package path matches PackageId", $"workflow should reference {packageId} package path/artifact");
    }

    static void AddDocumentationChecks(string root, XDocument? project, List<ReleaseDoctorCheck> checks)
    {
        var packageId = project == null ? ExpectedPackageId : Value(project, "PackageId");
        var version = project == null ? "" : Value(project, "Version");
        foreach (var doc in new[] { "README.md", "USER_GUIDE.md", "Migrator.Cli/README_TOOL.md", "docs/release-process.md", "docs/packaging-and-distribution.md", "docs/tool-installation.md" })
        {
            var path = Path.Combine(root, ToOsPath(doc));
            if (!File.Exists(path))
                continue;
            var text = File.ReadAllText(path);
            Add(checks, text.Contains(ExpectedToolCommand, StringComparison.Ordinal), "docs", doc, "doc mentions tool command", $"doc should mention {ExpectedToolCommand}");
        }

        foreach (var doc in new[] { "docs/release-process.md", "docs/packaging-and-distribution.md", "Migrator.Cli/README_TOOL.md" })
        {
            var path = Path.Combine(root, ToOsPath(doc));
            if (!File.Exists(path))
                continue;
            var text = File.ReadAllText(path);
            Add(checks, text.Contains(packageId, StringComparison.Ordinal), "docs", doc, "doc PackageId matches csproj", $"doc should mention PackageId {packageId}");
        }

        var changelog = Path.Combine(root, "CHANGELOG.md");
        if (File.Exists(changelog) && !string.IsNullOrWhiteSpace(version))
        {
            var text = File.ReadAllText(changelog);
            Add(checks, text.Contains(version, StringComparison.Ordinal), "docs", "CHANGELOG.md", "changelog contains current version", $"CHANGELOG.md should contain {version}");
        }
    }

    static void AddToolManifestExampleChecks(string root, XDocument? project, List<ReleaseDoctorCheck> checks)
    {
        var packageId = project == null ? ExpectedPackageId : Value(project, "PackageId");
        var expectedManifestKey = packageId.ToLowerInvariant();
        const string manifest = "examples/tool-manifest/dotnet-tools.json";
        var path = Path.Combine(root, ToOsPath(manifest));
        if (!File.Exists(path))
            return;

        var text = File.ReadAllText(path);
        Add(checks,
            text.Contains($"\"{expectedManifestKey}\"", StringComparison.Ordinal),
            "examples",
            manifest,
            "tool manifest example uses the current PackageId key",
            $"tool manifest example should use key {expectedManifestKey}");
        Add(checks,
            !text.Contains("seleniumplaywrightastmigrator", StringComparison.OrdinalIgnoreCase),
            "examples",
            manifest,
            "tool manifest example does not reference the old PackageId",
            "tool manifest example still references seleniumplaywrightastmigrator");
        Add(checks,
            text.Contains(ExpectedToolCommand, StringComparison.Ordinal),
            "examples",
            manifest,
            "tool manifest example exposes the public tool command",
            $"tool manifest example should expose {ExpectedToolCommand}");
    }

    static void AddInstallUxChecks(string root, List<ReleaseDoctorCheck> checks)
    {
        AddSourceContains(root, checks, "Migrator.Cli/Program.cs", "InstallDoctorCommand.RunInstallDoctor", "install-ux", "doctor install direct command is wired");
        AddSourceContains(root, checks, "Migrator.Cli/Commands/InstallDoctorCommand.cs", "install-doctor/v1", "install-ux", "install doctor writes versioned reports");
        AddSourceContains(root, checks, "Migrator.Cli/Commands/SelfCommand.cs", "SELF_UPDATE_COMMAND", "install-ux", "self update prints channel-specific update command");
        AddSourceContains(root, checks, "Migrator.Cli/Commands/KitCommand.cs", "bootstrap-agent", "agent-ux", "agent-generic bootstrap command is wired");
        AddSourceContains(root, checks, "Migrator.Cli/Commands/KitCommand.cs", "AGENT_HANDOFF.md", "agent-ux", "agent handoff pack is generated");
        AddSourceContains(root, checks, "README.md", "Choose your path", "docs", "README starts with three public entry paths");
        AddSourceContains(root, checks, "README.md", "npm update -g selenium-pw-migrator", "docs", "README documents npm update path");
        AddSourceContains(root, checks, "README.md", "doctor install", "docs", "README documents install diagnostics");
        AddSourceContains(root, checks, "README.md", "selenium-pw-migrator start", "docs", "README documents product-repo onboarding wizard");
        AddSourceContains(root, checks, "README.md", "selenium-pw-migrator pilot", "docs", "README documents representative pilot selection");
        AddSourceContains(root, checks, "Migrator.Cli/Commands/StartCommand.cs", "start-wizard/v1", "start-ux", "start wizard writes versioned onboarding reports");
        AddSourceContains(root, checks, "Migrator.Cli/Commands/StartCommand.cs", "start-dispatch.json", "start-ux", "start wizard leaves a no-menu dispatch state for /supervised-task");
        AddSourceContains(root, checks, "Migrator.Cli/Commands/StartCommand.cs", "runs", "start-ux", "start dashboard command points at run artifacts");
        AddSourceContains(root, checks, "Migrator.Cli/Commands/PilotCommand.cs", "pilot-selection/v1", "pilot-ux", "pilot selection writes versioned reports");
        AddSourceContains(root, checks, "Migrator.Cli/Commands/PilotCommand.cs", "selected-input", "pilot-ux", "pilot next commands operate on the selected slice");
        AddSourceContains(root, checks, "templates/opencode-team/global/.config/opencode/commands/supervised-task.md", "Start-workspace no-menu fallback", "agent-ux", "/supervised-task suppresses broad menus after start");
        AddSourceContains(root, checks, "Migrator.Cli/Program.cs", "suggested-config-patch", "todo-ux", "explain-todo writes suggested config patch artifacts");
        AddSourceContains(root, checks, "docs/report-serve-dashboard.md", "Open this first", "docs", "dashboard is documented as first review surface");
    }

    static void AddSourceContains(string root, List<ReleaseDoctorCheck> checks, string relativePath, string expected, string category, string okMessage)
    {
        var path = Path.Combine(root, ToOsPath(relativePath));
        var exists = File.Exists(path);
        var text = exists ? File.ReadAllText(path) : string.Empty;
        Add(checks,
            exists && text.Contains(expected, StringComparison.Ordinal),
            category,
            relativePath,
            okMessage,
            exists ? $"{relativePath} should contain {expected}" : $"{relativePath} is missing");
    }

    static void AddRepositoryHygieneChecks(string root, List<ReleaseDoctorCheck> checks)
    {
        var problematicDocs = Directory.Exists(Path.Combine(root, "docs"))
            ? Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.pdf", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name) && (name.Contains("#U", StringComparison.OrdinalIgnoreCase) || name.Contains('╨') || name.Length > 120))
                .ToArray()
            : Array.Empty<string>();
        Add(checks, problematicDocs.Length == 0, "repository", "docs/*.pdf", "no mojibake/overlong PDF names in docs", problematicDocs.Length == 0 ? "" : "rename or remove problematic PDF names: " + string.Join(", ", problematicDocs));
    }

    static void RequireValue(XDocument project, List<ReleaseDoctorCheck> checks, string element, string expected)
    {
        var actual = Value(project, element);
        Add(checks, string.Equals(actual, expected, StringComparison.Ordinal), "metadata", element, $"{element} is {expected}", $"{element} expected {expected}, actual {actual}");
    }

    static void RequirePresent(XDocument project, List<ReleaseDoctorCheck> checks, string element)
    {
        Add(checks, !string.IsNullOrWhiteSpace(Value(project, element)), "metadata", element, $"{element} is present", $"{element} is missing or empty");
    }

    static string Value(XDocument doc, string elementName) => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == elementName)?.Value.Trim() ?? string.Empty;

    static void Add(List<ReleaseDoctorCheck> checks, bool condition, string category, string item, string ok, string fail, string failStatus = "fail")
    {
        checks.Add(new ReleaseDoctorCheck(category, item, condition ? "pass" : failStatus, condition ? ok : fail));
    }

    static string BuildMarkdown(ReleaseDoctorReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Release Doctor Report");
        sb.AppendLine();
        sb.AppendLine($"Status: **{report.Status}**");
        sb.AppendLine($"Repository root: `{report.RepositoryRoot}`");
        sb.AppendLine($"Generated at: `{report.GeneratedAtUtc:O}`");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- Failed checks: {report.FailedChecks}");
        sb.AppendLine($"- Warnings: {report.WarningChecks}");
        sb.AppendLine($"- Total checks: {report.Checks.Length}");
        sb.AppendLine();
        sb.AppendLine("## Checks");
        sb.AppendLine();
        sb.AppendLine("| Status | Category | Item | Message |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var check in report.Checks.OrderBy(c => c.Status).ThenBy(c => c.Category).ThenBy(c => c.Item, StringComparer.OrdinalIgnoreCase))
            sb.AppendLine($"| {Escape(check.Status)} | {Escape(check.Category)} | `{Escape(check.Item)}` | {Escape(check.Message)} |");
        return sb.ToString();
    }

    static string Escape(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    static string ToOsPath(string value) => value.Replace('/', Path.DirectorySeparatorChar);
}

internal sealed record ReleaseDoctorReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string RepositoryRoot,
    string Status,
    int FailedChecks,
    int WarningChecks,
    ReleaseDoctorCheck[] Checks);

internal sealed record ReleaseDoctorCheck(string Category, string Item, string Status, string Message);

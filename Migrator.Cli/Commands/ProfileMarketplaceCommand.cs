using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Migrator.Core;

internal static class ProfileMarketplaceCommand
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    static readonly BuiltInProfile[] BuiltInProfiles =
    {
        new(
            Id: "basic-csharp-nunit",
            Version: "1.0.0",
            SourceLanguage: "csharp",
            SourceFramework: "nunit",
            TargetBackend: "dotnet",
            TargetFramework: "nunit",
            SafetyLevel: "safe-starter",
            CompatibilityRange: ">=0.1.0",
            Summary: "Minimal Selenium C# NUnit to Playwright .NET NUnit starter profile.",
            SupportedPatterns: new[]
            {
                "Playwright .NET NUnit renderer host defaults",
                "Default data-testid/data-test-id/data-test/data-tid discovery hint",
                "verify-project package defaults for NUnit"
            },
            RequiredEvidence: new[]
            {
                "Confirm the target project really uses NUnit.",
                "Run discover-target when an existing Playwright project is available.",
                "Inspect source PageObjects before adding selector mappings."
            },
            KnownLimitations: new[]
            {
                "Does not contain project-specific selectors or POM mappings.",
                "Does not suppress assertions or source helpers."
            },
            Changelog: new[] { "1.0.0: Initial built-in public starter profile." },
            ConfigJson: BuildStarterConfigJson("nunit", "basic-csharp-nunit")),
        new(
            Id: "basic-csharp-xunit",
            Version: "1.0.0",
            SourceLanguage: "csharp",
            SourceFramework: "xunit",
            TargetBackend: "dotnet",
            TargetFramework: "xunit",
            SafetyLevel: "safe-starter",
            CompatibilityRange: ">=0.1.0",
            Summary: "Minimal Selenium C# xUnit to Playwright .NET xUnit starter profile.",
            SupportedPatterns: new[]
            {
                "Playwright .NET xUnit renderer host defaults",
                "Default data-testid/data-test-id/data-test/data-tid discovery hint",
                "verify-project package defaults for xUnit"
            },
            RequiredEvidence: new[]
            {
                "Confirm the target project really uses xUnit.",
                "Run discover-target when an existing Playwright project is available.",
                "Inspect source PageObjects before adding selector mappings."
            },
            KnownLimitations: new[]
            {
                "Does not contain project-specific selectors or POM mappings.",
                "Does not suppress assertions or source helpers."
            },
            Changelog: new[] { "1.0.0: Initial built-in public starter profile." },
            ConfigJson: BuildStarterConfigJson("xunit", "basic-csharp-xunit")),
        new(
            Id: "basic-csharp-nunit-data-tid",
            Version: "1.0.0",
            SourceLanguage: "csharp",
            SourceFramework: "nunit",
            TargetBackend: "dotnet",
            TargetFramework: "nunit",
            SafetyLevel: "safe-starter",
            CompatibilityRange: ">=0.1.0",
            Summary: "NUnit starter profile using data-tid as the default test id attribute.",
            SupportedPatterns: new[]
            {
                "Playwright .NET NUnit renderer host defaults",
                "data-tid locator convention",
                "verify-project package defaults for NUnit"
            },
            RequiredEvidence: new[]
            {
                "Confirm the application really uses data-tid.",
                "Do not assume data-tid values without source/POM evidence."
            },
            KnownLimitations: new[]
            {
                "Does not add actual selector mappings.",
                "Does not suppress assertions or source helpers."
            },
            Changelog: new[] { "1.0.0: Initial built-in public data-tid starter profile." },
            ConfigJson: BuildStarterConfigJson("nunit", "basic-csharp-nunit-data-tid", "data-tid"))
    };

    public static int RunList(string outPath, string format)
    {
        Directory.CreateDirectory(outPath);
        var report = BuildCatalogReport(BuiltInProfiles, query: null);
        WriteCatalogReport(report, outPath, format, "profile-list");
        PrintCatalog(report);
        return 0;
    }

    public static int RunSearch(string query, string outPath, string format)
    {
        Directory.CreateDirectory(outPath);
        query = query?.Trim() ?? string.Empty;
        var matches = string.IsNullOrWhiteSpace(query)
            ? BuiltInProfiles
            : BuiltInProfiles.Where(p => ProfileMatches(p, query)).ToArray();

        var report = BuildCatalogReport(matches, query);
        WriteCatalogReport(report, outPath, format, "profile-search");
        PrintCatalog(report);
        return 0;
    }

    public static int RunInspect(string profileId, string outPath, string format)
    {
        Directory.CreateDirectory(outPath);
        var profile = FindBuiltIn(profileId);
        if (profile == null)
        {
            Console.Error.WriteLine($"Profile not found: {profileId}");
            PrintKnownProfileIds();
            return 2;
        }

        var validation = ValidateProfile(profile);
        var report = BuildInspectionReport(profile, validation);
        WriteInspectionReport(report, outPath, format);
        Console.WriteLine($"Profile: {profile.Id} ({profile.Version})");
        Console.WriteLine($"Safety: {profile.SafetyLevel}");
        Console.WriteLine($"Validation: {validation.Status}");
        Console.WriteLine($"Reports written to: {Path.GetFullPath(outPath)}");
        return validation.Errors.Length > 0 ? 2 : 0;
    }

    public static int RunInstall(string profileId, string outPath, string format)
    {
        Directory.CreateDirectory(outPath);
        var profile = FindBuiltIn(profileId);
        if (profile == null)
        {
            Console.Error.WriteLine($"Profile not found: {profileId}");
            PrintKnownProfileIds();
            return 2;
        }

        var validation = ValidateProfile(profile);
        if (validation.Errors.Length > 0)
        {
            Console.Error.WriteLine($"Profile {profile.Id} failed validation and was not installed:");
            foreach (var error in validation.Errors)
                Console.Error.WriteLine($"  - {error}");
            return 2;
        }

        var targetPath = Path.Combine(outPath, profile.Id + ".adapter-config.json");
        var wrotePath = targetPath;
        var noOverwrite = false;
        if (File.Exists(targetPath))
        {
            noOverwrite = true;
            wrotePath = Path.Combine(outPath, profile.Id + ".adapter-config.new.json");
        }

        File.WriteAllText(wrotePath, EnsureTrailingNewLine(profile.ConfigJson));
        var metadataPath = Path.Combine(outPath, profile.Id + ".profile-metadata.json");
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(ToProfileMetadata(profile), JsonOptions) + Environment.NewLine);

        var report = new ProfileInstallReport(
            SchemaVersion: "profile-install-report/v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Profile: ToCatalogEntry(profile),
            Status: noOverwrite ? "installed-as-new-file" : "installed",
            InstalledConfigPath: Path.GetFullPath(wrotePath),
            MetadataPath: Path.GetFullPath(metadataPath),
            NoOverwrite: noOverwrite,
            Validation: validation,
            NextSteps: BuildInstallNextSteps(profile, wrotePath));

        WriteInstallReport(report, outPath, format);
        Console.WriteLine(noOverwrite
            ? $"Profile already existed. Wrote reviewed config layer to: {Path.GetFullPath(wrotePath)}"
            : $"Installed profile config layer: {Path.GetFullPath(wrotePath)}");
        Console.WriteLine($"Metadata: {Path.GetFullPath(metadataPath)}");
        return 0;
    }

    public static int RunDiff(string? beforePath, string? afterPath, string outPath, string format)
    {
        if (string.IsNullOrWhiteSpace(beforePath) || string.IsNullOrWhiteSpace(afterPath))
        {
            Console.Error.WriteLine("profile diff requires --before <adapter-config.json> --after <profile-id-or-config.json>.");
            return 2;
        }

        Directory.CreateDirectory(outPath);
        try
        {
            var before = ReadConfigOrProfileId(beforePath, "before");
            var after = ReadConfigOrProfileId(afterPath, "after");
            var changes = BuildDiffChanges(before.Config, after.Config).ToArray();
            var risks = BuildDiffRisks(before.Config, after.Config).ToArray();
            var report = new ProfileDiffReport(
                SchemaVersion: "profile-diff-report/v1",
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                Before: before.Source,
                After: after.Source,
                Summary: new[]
                {
                    $"UiTargets: {before.Config.UiTargets.Length} -> {after.Config.UiTargets.Length}",
                    $"PageObjects: {before.Config.PageObjects.Length} -> {after.Config.PageObjects.Length}",
                    $"Methods: {before.Config.Methods.Length} -> {after.Config.Methods.Length}",
                    $"ParameterizedMethods: {before.Config.ParameterizedMethods.Length} -> {after.Config.ParameterizedMethods.Length}",
                    $"Tables: {before.Config.Tables.Length} -> {after.Config.Tables.Length}",
                    $"Pagination: {before.Config.Pagination.Length} -> {after.Config.Pagination.Length}",
                    $"SourceOnlyIdentifiers: {before.Config.SourceOnlyIdentifiers.Length} -> {after.Config.SourceOnlyIdentifiers.Length}",
                    $"SuppressedMethods: {before.Config.SuppressedMethods.Length} -> {after.Config.SuppressedMethods.Length}",
                    $"SuppressedMethodPatterns: {before.Config.SuppressedMethodPatterns.Length} -> {after.Config.SuppressedMethodPatterns.Length}",
                    $"Target framework: {before.Config.TestHost?.TargetTestFramework ?? "nunit(default)"} -> {after.Config.TestHost?.TargetTestFramework ?? "nunit(default)"}"
                },
                Changes: changes,
                Risks: risks);

            WriteDiffReport(report, outPath, format);
            Console.WriteLine("=== Profile Diff ===");
            Console.WriteLine($"Before: {report.Before}");
            Console.WriteLine($"After: {report.After}");
            Console.WriteLine($"Changes: {report.Changes.Length}");
            Console.WriteLine($"Risks: {report.Risks.Length}");
            Console.WriteLine($"Reports written to: {Path.GetFullPath(outPath)}");
            return risks.Any(r => r.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)) ? 2 : 0;
        }
        catch (ConfigValidationError ex)
        {
            Console.Error.WriteLine("Config error:");
            foreach (var err in ex.Errors)
                Console.Error.WriteLine(err);
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Profile diff failed: {ex.Message}");
            return 2;
        }
    }

    static BuiltInProfile? FindBuiltIn(string? id) => BuiltInProfiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    static bool ProfileMatches(BuiltInProfile profile, string query)
    {
        var haystack = string.Join(" ", new[]
        {
            profile.Id,
            profile.Summary,
            profile.SourceLanguage,
            profile.SourceFramework,
            profile.TargetBackend,
            profile.TargetFramework,
            profile.SafetyLevel
        }.Concat(profile.SupportedPatterns).Concat(profile.KnownLimitations));
        if (haystack.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        var tokens = SplitSearchTokens(query);
        return tokens.Length > 0 && tokens.All(token => haystack.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    static string[] SplitSearchTokens(string query) => query
        .Split(new[] { ' ', '-', '_', '/', '.', ':', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(token => token.Length > 0)
        .ToArray();

    static ProfileCatalogReport BuildCatalogReport(IEnumerable<BuiltInProfile> profiles, string? query)
    {
        var entries = profiles.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Select(ToCatalogEntry).ToArray();
        return new ProfileCatalogReport(
            SchemaVersion: "profile-catalog/v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Query: query,
            Sources: new[] { "built-in/offline" },
            Profiles: entries);
    }

    static ProfileInspectionReport BuildInspectionReport(BuiltInProfile profile, ProfileValidationResult validation)
    {
        var config = ConfigValidator.ValidateJson(profile.ConfigJson, profile.Id);
        return new ProfileInspectionReport(
            SchemaVersion: "profile-inspection/v1",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Profile: ToCatalogEntry(profile),
            Metadata: ToProfileMetadata(profile),
            Validation: validation,
            ConfigSummary: new ProfileConfigSummary(
                SourceProjectName: config.SourceProjectName ?? string.Empty,
                UiTargets: config.UiTargets.Length,
                PageObjects: config.PageObjects.Length,
                Methods: config.Methods.Length,
                ParameterizedMethods: config.ParameterizedMethods.Length,
                Tables: config.Tables.Length,
                Pagination: config.Pagination.Length,
                SourceOnlyIdentifiers: config.SourceOnlyIdentifiers.Length,
                SuppressedMethods: config.SuppressedMethods.Length,
                SuppressedMethodPatterns: config.SuppressedMethodPatterns.Length,
                TargetKnownTypes: config.TargetKnownTypes.Length,
                TargetKnownIdentifiers: config.TargetKnownIdentifiers.Length,
                TargetTestFramework: config.TestHost?.TargetTestFramework ?? "nunit(default)",
                DefaultTestIdAttribute: config.LocatorSettings?.DefaultTestIdAttribute ?? "Playwright GetByTestId default"),
            ConfigJson: profile.ConfigJson);
    }

    static ProfileValidationResult ValidateProfile(BuiltInProfile profile)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        ProjectAdapterConfig config;
        try
        {
            config = ConfigValidator.ValidateJson(profile.ConfigJson, profile.Id);
        }
        catch (ConfigValidationError ex)
        {
            errors.AddRange(ex.Errors);
            return new ProfileValidationResult("failed", errors.ToArray(), warnings.ToArray());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            errors.Add(ex.Message);
            return new ProfileValidationResult("failed", errors.ToArray(), warnings.ToArray());
        }

        if (string.IsNullOrWhiteSpace(profile.Id)) errors.Add("Profile id is required.");
        if (string.IsNullOrWhiteSpace(profile.Version)) errors.Add("Profile version is required.");
        if (string.IsNullOrWhiteSpace(profile.CompatibilityRange)) errors.Add("Profile compatibility range is required.");
        if (profile.Changelog.Length == 0) errors.Add("Profile changelog is required.");
        if (profile.RequiredEvidence.Length == 0) errors.Add("Profile required evidence is required.");
        if (config.SourceOnlyIdentifiers.Length > 3)
            warnings.Add("Profile contains multiple SourceOnlyIdentifiers; inspect them before install.");
        if (config.SourceOnlyIdentifiers.Any(IsBroadIdentifier))
            errors.Add("Profile cannot add broad SourceOnlyIdentifiers without project-specific explanation.");
        if (config.SuppressedMethods.Length > 0 || config.SuppressedMethodPatterns.Length > 0)
            errors.Add("Built-in marketplace profiles must not silently suppress methods/assertions.");
        if (!string.Equals(profile.TargetFramework, config.TestHost?.TargetTestFramework, StringComparison.OrdinalIgnoreCase))
            errors.Add("Profile metadata target framework does not match TestHost.TargetTestFramework.");

        return new ProfileValidationResult(errors.Count == 0 ? "passed" : "failed", errors.ToArray(), warnings.ToArray());
    }

    static bool IsBroadIdentifier(string value)
    {
        var trimmed = value.Trim();
        return trimmed is "page" or "driver" or "browser" or "test" or "Assert" || trimmed.Length <= 2 || trimmed.Contains('*', StringComparison.Ordinal);
    }

    static ProfileConfigInput ReadConfigOrProfileId(string value, string role)
    {
        var builtIn = FindBuiltIn(value);
        if (builtIn != null)
            return new ProfileConfigInput($"built-in:{builtIn.Id}", ConfigValidator.ValidateJson(builtIn.ConfigJson, builtIn.Id));

        if (!File.Exists(value))
            throw new FileNotFoundException($"{role} profile/config not found: {value}");

        var json = File.ReadAllText(value);
        return new ProfileConfigInput(Path.GetFullPath(value), ConfigValidator.ValidateJson(json, value));
    }

    static IEnumerable<ProfileDiffChange> BuildDiffChanges(ProjectAdapterConfig before, ProjectAdapterConfig after)
    {
        foreach (var change in CompareCount("UiTargets", before.UiTargets.Length, after.UiTargets.Length)) yield return change;
        foreach (var change in CompareCount("PageObjects", before.PageObjects.Length, after.PageObjects.Length)) yield return change;
        foreach (var change in CompareCount("Methods", before.Methods.Length, after.Methods.Length)) yield return change;
        foreach (var change in CompareCount("ParameterizedMethods", before.ParameterizedMethods.Length, after.ParameterizedMethods.Length)) yield return change;
        foreach (var change in CompareCount("Tables", before.Tables.Length, after.Tables.Length)) yield return change;
        foreach (var change in CompareCount("Pagination", before.Pagination.Length, after.Pagination.Length)) yield return change;
        foreach (var change in CompareCount("SourceOnlyIdentifiers", before.SourceOnlyIdentifiers.Length, after.SourceOnlyIdentifiers.Length)) yield return change;
        foreach (var change in CompareCount("SuppressedMethods", before.SuppressedMethods.Length, after.SuppressedMethods.Length)) yield return change;
        foreach (var change in CompareCount("SuppressedMethodPatterns", before.SuppressedMethodPatterns.Length, after.SuppressedMethodPatterns.Length)) yield return change;

        var beforeFramework = before.TestHost?.TargetTestFramework ?? "nunit";
        var afterFramework = after.TestHost?.TargetTestFramework ?? "nunit";
        if (!string.Equals(beforeFramework, afterFramework, StringComparison.OrdinalIgnoreCase))
            yield return new ProfileDiffChange("TestHost.TargetTestFramework", "changed", beforeFramework, afterFramework);
    }

    static IEnumerable<ProfileDiffChange> CompareCount(string section, int before, int after)
    {
        if (before != after)
            yield return new ProfileDiffChange(section, after > before ? "added" : "removed", before.ToString(), after.ToString());
    }

    static IEnumerable<ProfileDiffRisk> BuildDiffRisks(ProjectAdapterConfig before, ProjectAdapterConfig after)
    {
        var addedSourceOnly = after.SourceOnlyIdentifiers.Except(before.SourceOnlyIdentifiers, StringComparer.Ordinal).ToArray();
        if (addedSourceOnly.Length > 0)
        {
            yield return new ProfileDiffRisk(
                "warning",
                "PROFILE_ADDS_SOURCE_ONLY_IDENTIFIERS",
                $"Profile adds {addedSourceOnly.Length} source-only identifier(s).",
                "SourceOnlyIdentifiers",
                "Review each identifier and keep only project-proven source-only helpers.");
        }

        var addedSuppressions = after.SuppressedMethods.Except(before.SuppressedMethods, StringComparer.Ordinal).Count()
            + after.SuppressedMethodPatterns.Except(before.SuppressedMethodPatterns, StringComparer.Ordinal).Count();
        if (addedSuppressions > 0)
        {
            yield return new ProfileDiffRisk(
                "error",
                "PROFILE_ADDS_SUPPRESSIONS",
                $"Profile adds {addedSuppressions} method suppression(s).",
                "SuppressedMethods/SuppressedMethodPatterns",
                "Do not install suppressions without a source-truth review.");
        }
    }

    static string BuildStarterConfigJson(string targetFramework, string sourceProjectName, string? defaultTestIdAttribute = null)
    {
        var packages = targetFramework.Equals("xunit", StringComparison.OrdinalIgnoreCase)
            ? new[]
            {
                new { Include = "Microsoft.Playwright.Xunit", Version = "1.*" },
                new { Include = "xunit", Version = "2.*" },
                new { Include = "xunit.runner.visualstudio", Version = "2.*" }
            }
            : new[]
            {
                new { Include = "Microsoft.Playwright.NUnit", Version = "1.*" },
                new { Include = "NUnit", Version = "3.*" },
                new { Include = "NUnit3TestAdapter", Version = "4.*" }
            };

        var config = new
        {
            SchemaVersion = ProjectAdapterConfig.CurrentSchemaVersion,
            SourceProjectName = sourceProjectName,
            LocatorSettings = new
            {
                DefaultTestIdAttribute = defaultTestIdAttribute,
                KnownTestIdAttributes = new[] { "data-testid", "data-test-id", "data-test", "data-tid" }
            },
            TestHost = new
            {
                TargetTestFramework = targetFramework,
                Namespace = "Migration.Playwright.Tests",
                BaseClass = "PageTest"
            },
            Verification = new
            {
                TargetFramework = "net8.0",
                AutoDiscoverNearestProject = true,
                AutoDiscoverProjectReferences = true,
                AutoDiscoverBuildFiles = true,
                PackageReferences = packages
            },
            UiTargets = Array.Empty<object>(),
            PageObjects = Array.Empty<object>(),
            Methods = Array.Empty<object>(),
            ParameterizedMethods = Array.Empty<object>(),
            Tables = Array.Empty<object>(),
            Pagination = Array.Empty<object>(),
            SourceOnlyIdentifiers = Array.Empty<string>(),
            SuppressedMethods = Array.Empty<string>(),
            SuppressedMethodPatterns = Array.Empty<string>()
        };

        return JsonSerializer.Serialize(config, JsonOptions);
    }

    static string EnsureTrailingNewLine(string value) => value.EndsWith(Environment.NewLine, StringComparison.Ordinal) ? value : value + Environment.NewLine;

    static ProfileCatalogEntry ToCatalogEntry(BuiltInProfile profile) => new(
        profile.Id,
        profile.Version,
        profile.SourceLanguage,
        profile.SourceFramework,
        profile.TargetBackend,
        profile.TargetFramework,
        profile.SafetyLevel,
        profile.CompatibilityRange,
        profile.Summary,
        profile.KnownLimitations);

    static ProfileMetadata ToProfileMetadata(BuiltInProfile profile) => new(
        profile.Id,
        profile.Version,
        profile.SourceLanguage,
        profile.SourceFramework,
        profile.TargetBackend,
        profile.TargetFramework,
        profile.SupportedPatterns,
        profile.RequiredEvidence,
        profile.SafetyLevel,
        profile.KnownLimitations,
        profile.Changelog,
        profile.CompatibilityRange,
        new[]
        {
            "Run config-validate --validation-mode production before using this layer in CI.",
            "Run profile inspect before install.",
            "Treat this as a config layer, not hidden behavior."
        });

    static string[] BuildInstallNextSteps(BuiltInProfile profile, string installedPath) => new[]
    {
        $"Review {installedPath} before using it.",
        $"Run: selenium-pw-migrator --mode config-validate --config {installedPath} --validation-mode production --out config-validate",
        "Layer project-specific config after this profile instead of editing built-in assumptions directly.",
        "Run discover-target/profile-match before adding selector or POM mappings."
    };

    static void PrintKnownProfileIds()
    {
        Console.Error.WriteLine("Known built-in profiles:");
        foreach (var profile in BuiltInProfiles.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            Console.Error.WriteLine($"  - {profile.Id}");
    }

    static void PrintCatalog(ProfileCatalogReport report)
    {
        Console.WriteLine("=== Profile Catalog ===");
        if (!string.IsNullOrWhiteSpace(report.Query))
            Console.WriteLine($"Query: {report.Query}");
        Console.WriteLine($"Profiles: {report.Profiles.Length}");
        foreach (var profile in report.Profiles)
            Console.WriteLine($"  - {profile.Id} {profile.Version} [{profile.SourceLanguage}/{profile.SourceFramework} -> {profile.TargetBackend}/{profile.TargetFramework}] {profile.SafetyLevel}");
    }

    static void WriteCatalogReport(ProfileCatalogReport report, string outPath, string format, string basename)
    {
        if (format is "json" or "both")
            File.WriteAllText(Path.Combine(outPath, basename + ".json"), JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);
        if (format is "text" or "both")
            File.WriteAllText(Path.Combine(outPath, basename + ".md"), BuildCatalogMarkdown(report));
    }

    static void WriteInspectionReport(ProfileInspectionReport report, string outPath, string format)
    {
        if (format is "json" or "both")
            File.WriteAllText(Path.Combine(outPath, "profile-inspection.json"), JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);
        if (format is "text" or "both")
            File.WriteAllText(Path.Combine(outPath, "profile-inspection.md"), BuildInspectionMarkdown(report));
    }

    static void WriteInstallReport(ProfileInstallReport report, string outPath, string format)
    {
        if (format is "json" or "both")
            File.WriteAllText(Path.Combine(outPath, "profile-install-report.json"), JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);
        if (format is "text" or "both")
            File.WriteAllText(Path.Combine(outPath, "profile-install-report.md"), BuildInstallMarkdown(report));
    }

    static void WriteDiffReport(ProfileDiffReport report, string outPath, string format)
    {
        if (format is "json" or "both")
            File.WriteAllText(Path.Combine(outPath, "profile-diff-report.json"), JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);
        if (format is "text" or "both")
            File.WriteAllText(Path.Combine(outPath, "profile-diff-report.md"), BuildDiffMarkdown(report));
    }

    static string BuildCatalogMarkdown(ProfileCatalogReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Profile Catalog");
        sb.AppendLine();
        sb.AppendLine("Source: `built-in/offline`");
        if (!string.IsNullOrWhiteSpace(report.Query))
            sb.AppendLine($"Query: `{EscapeMd(report.Query)}`");
        sb.AppendLine();
        sb.AppendLine("| ID | Version | Source | Target | Safety | Summary |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var p in report.Profiles)
            sb.AppendLine($"| `{p.Id}` | `{p.Version}` | `{p.SourceLanguage}/{p.SourceFramework}` | `{p.TargetBackend}/{p.TargetFramework}` | `{p.SafetyLevel}` | {EscapeMd(p.Summary)} |");
        return sb.ToString();
    }

    static string BuildInspectionMarkdown(ProfileInspectionReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Profile Inspection");
        sb.AppendLine();
        sb.AppendLine($"Profile: `{report.Profile.Id}` `{report.Profile.Version}`");
        sb.AppendLine($"Source: `{report.Profile.SourceLanguage}/{report.Profile.SourceFramework}`");
        sb.AppendLine($"Target: `{report.Profile.TargetBackend}/{report.Profile.TargetFramework}`");
        sb.AppendLine($"Safety: `{report.Profile.SafetyLevel}`");
        sb.AppendLine($"Validation: `{report.Validation.Status}`");
        sb.AppendLine();
        sb.AppendLine("## Supported patterns");
        foreach (var item in report.Metadata.SupportedPatterns) sb.AppendLine($"- {EscapeMd(item)}");
        sb.AppendLine();
        sb.AppendLine("## Required evidence");
        foreach (var item in report.Metadata.RequiredEvidence) sb.AppendLine($"- {EscapeMd(item)}");
        sb.AppendLine();
        sb.AppendLine("## Known limitations");
        foreach (var item in report.Metadata.KnownLimitations) sb.AppendLine($"- {EscapeMd(item)}");
        sb.AppendLine();
        sb.AppendLine("## Config summary");
        sb.AppendLine($"- Target framework: `{report.ConfigSummary.TargetTestFramework}`");
        sb.AppendLine($"- Default test id attribute: `{report.ConfigSummary.DefaultTestIdAttribute}`");
        sb.AppendLine($"- UiTargets: {report.ConfigSummary.UiTargets}");
        sb.AppendLine($"- Methods: {report.ConfigSummary.Methods}");
        sb.AppendLine($"- SourceOnlyIdentifiers: {report.ConfigSummary.SourceOnlyIdentifiers}");
        sb.AppendLine($"- Suppressions: {report.ConfigSummary.SuppressedMethods + report.ConfigSummary.SuppressedMethodPatterns}");
        if (report.Validation.Errors.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Validation errors");
            foreach (var error in report.Validation.Errors) sb.AppendLine($"- {EscapeMd(error)}");
        }
        if (report.Validation.Warnings.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Validation warnings");
            foreach (var warning in report.Validation.Warnings) sb.AppendLine($"- {EscapeMd(warning)}");
        }
        return sb.ToString();
    }

    static string BuildInstallMarkdown(ProfileInstallReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Profile Install Report");
        sb.AppendLine();
        sb.AppendLine($"Status: `{report.Status}`");
        sb.AppendLine($"Profile: `{report.Profile.Id}` `{report.Profile.Version}`");
        sb.AppendLine($"Installed config: `{report.InstalledConfigPath}`");
        sb.AppendLine($"Metadata: `{report.MetadataPath}`");
        sb.AppendLine($"No-overwrite fallback used: `{report.NoOverwrite.ToString().ToLowerInvariant()}`");
        sb.AppendLine();
        sb.AppendLine("## Next steps");
        foreach (var step in report.NextSteps) sb.AppendLine($"- {EscapeMd(step)}");
        return sb.ToString();
    }

    static string BuildDiffMarkdown(ProfileDiffReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Profile Diff Report");
        sb.AppendLine();
        sb.AppendLine($"Before: `{report.Before}`");
        sb.AppendLine($"After: `{report.After}`");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        foreach (var item in report.Summary) sb.AppendLine($"- {EscapeMd(item)}");
        sb.AppendLine();
        sb.AppendLine("## Risks");
        if (report.Risks.Length == 0) sb.AppendLine("No high-risk profile changes detected.");
        foreach (var risk in report.Risks)
            sb.AppendLine($"- **{risk.Severity.ToUpperInvariant()} {risk.Code}**: {EscapeMd(risk.Message)} ({EscapeMd(risk.Location)}) Suggested action: {EscapeMd(risk.SuggestedAction)}");
        sb.AppendLine();
        sb.AppendLine("## Changes");
        if (report.Changes.Length == 0) sb.AppendLine("No semantic count changes detected.");
        foreach (var change in report.Changes)
            sb.AppendLine($"- `{change.Section}` {change.ChangeType}: `{change.Before}` -> `{change.After}`");
        return sb.ToString();
    }

    static string EscapeMd(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
    }

    sealed record BuiltInProfile(
        string Id,
        string Version,
        string SourceLanguage,
        string SourceFramework,
        string TargetBackend,
        string TargetFramework,
        string SafetyLevel,
        string CompatibilityRange,
        string Summary,
        string[] SupportedPatterns,
        string[] RequiredEvidence,
        string[] KnownLimitations,
        string[] Changelog,
        string ConfigJson);

    sealed record ProfileConfigInput(string Source, ProjectAdapterConfig Config);
}

public sealed record ProfileCatalogReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string? Query,
    string[] Sources,
    ProfileCatalogEntry[] Profiles);

public sealed record ProfileCatalogEntry(
    string Id,
    string Version,
    string SourceLanguage,
    string SourceFramework,
    string TargetBackend,
    string TargetFramework,
    string SafetyLevel,
    string CompatibilityRange,
    string Summary,
    string[] KnownLimitations);

public sealed record ProfileMetadata(
    string Id,
    string Version,
    string SourceLanguage,
    string SourceFramework,
    string TargetBackend,
    string TargetFramework,
    string[] SupportedPatterns,
    string[] RequiredEvidence,
    string SafetyLevel,
    string[] KnownLimitations,
    string[] Changelog,
    string CompatibilityRange,
    string[] SafetyRules);

public sealed record ProfileValidationResult(string Status, string[] Errors, string[] Warnings);

public sealed record ProfileInspectionReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    ProfileCatalogEntry Profile,
    ProfileMetadata Metadata,
    ProfileValidationResult Validation,
    ProfileConfigSummary ConfigSummary,
    string ConfigJson);

public sealed record ProfileConfigSummary(
    string SourceProjectName,
    int UiTargets,
    int PageObjects,
    int Methods,
    int ParameterizedMethods,
    int Tables,
    int Pagination,
    int SourceOnlyIdentifiers,
    int SuppressedMethods,
    int SuppressedMethodPatterns,
    int TargetKnownTypes,
    int TargetKnownIdentifiers,
    string TargetTestFramework,
    string DefaultTestIdAttribute);

public sealed record ProfileInstallReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    ProfileCatalogEntry Profile,
    string Status,
    string InstalledConfigPath,
    string MetadataPath,
    bool NoOverwrite,
    ProfileValidationResult Validation,
    string[] NextSteps);

public sealed record ProfileDiffReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string Before,
    string After,
    string[] Summary,
    ProfileDiffChange[] Changes,
    ProfileDiffRisk[] Risks);

public sealed record ProfileDiffChange(string Section, string ChangeType, string Before, string After);

public sealed record ProfileDiffRisk(string Severity, string Code, string Message, string Location, string SuggestedAction);

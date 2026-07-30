using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Migrator.Lab.Contracts;

namespace Migrator.Lab;

public static partial class ScenarioSpecLoader
{
    public const string SupportedSchemaVersion = "lab-scenario/v1";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) }
    };

    public static ScenarioCatalogEntry Load(string scenarioFile)
    {
        var fullPath = Path.GetFullPath(scenarioFile);
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var issues = new List<ScenarioValidationIssue>();

        if (!File.Exists(fullPath))
        {
            issues.Add(Error("SCENARIO_FILE_MISSING", $"Scenario file does not exist: {fullPath}"));
            return new ScenarioCatalogEntry(fullPath, directory, null, issues.ToArray());
        }

        ScenarioSpec? scenario;
        try
        {
            var json = File.ReadAllText(fullPath);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            ValidateJsonShape(document.RootElement, issues);
            scenario = JsonSerializer.Deserialize<ScenarioSpec>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            issues.Add(Error("SCENARIO_JSON_INVALID", ex.Message));
            return new ScenarioCatalogEntry(fullPath, directory, null, issues.ToArray());
        }
        catch (IOException ex)
        {
            issues.Add(Error("SCENARIO_READ_FAILED", ex.Message));
            return new ScenarioCatalogEntry(fullPath, directory, null, issues.ToArray());
        }

        if (scenario == null)
        {
            issues.Add(Error("SCENARIO_EMPTY", "Scenario JSON deserialized to null."));
            return new ScenarioCatalogEntry(fullPath, directory, null, issues.ToArray());
        }

        scenario = NormalizeNullValues(scenario, issues);
        ValidateContract(scenario, directory, issues);
        return new ScenarioCatalogEntry(fullPath, directory, scenario, issues.ToArray());
    }

    static ScenarioSpec NormalizeNullValues(ScenarioSpec scenario, List<ScenarioValidationIssue> issues)
    {
        var source = scenario.Source;
        if (source is null)
        {
            issues.Add(Error("SCHEMA_SECTION_NULL", "$.source must not be null."));
            source = new ScenarioSourceSpec();
        }

        var project = scenario.Project;
        if (project is null)
        {
            issues.Add(Error("SCHEMA_SECTION_NULL", "$.project must not be null."));
            project = new ScenarioProjectSpec();
        }

        var app = scenario.App;
        if (app is null)
        {
            issues.Add(Error("SCHEMA_SECTION_NULL", "$.app must not be null."));
            app = new ScenarioAppSpec();
        }

        var oracle = scenario.Oracle;
        if (oracle is null)
        {
            issues.Add(Error("SCHEMA_SECTION_NULL", "$.oracle must not be null."));
            oracle = new ScenarioOracleSpec();
        }

        var qualityBudget = scenario.QualityBudget;
        if (qualityBudget is null)
        {
            issues.Add(Error("SCHEMA_SECTION_NULL", "$.qualityBudget must not be null."));
            qualityBudget = new ScenarioQualityBudget();
        }

        var expected = scenario.Expected;
        if (expected is null)
        {
            issues.Add(Error("SCHEMA_SECTION_NULL", "$.expected must not be null."));
            expected = new ScenarioExpectedSpec();
        }

        var implementation = scenario.Implementation;
        if (implementation is null)
        {
            issues.Add(Error("SCHEMA_SECTION_NULL", "$.implementation must not be null."));
            implementation = new ScenarioImplementationSpec();
        }

        return scenario with
        {
            Tags = scenario.Tags ?? Array.Empty<string>(),
            Source = source with
            {
                Features = source.Features ?? Array.Empty<string>(),
                MigrationFiles = source.MigrationFiles ?? Array.Empty<string>()
            },
            Project = project with
            {
                Files = project.Files ?? Array.Empty<string>(),
                References = project.References ?? Array.Empty<string>(),
                MsBuild = project.MsBuild ?? new ScenarioMsBuildSpec()
            },
            App = app with
            {
                BaseUrlEnvironmentVariable = string.IsNullOrWhiteSpace(app.BaseUrlEnvironmentVariable)
                    ? "MIGRATOR_LAB_APP_URL"
                    : app.BaseUrlEnvironmentVariable,
                Pages = app.Pages ?? Array.Empty<JsonElement>()
            },
            Oracle = oracle,
            QualityBudget = qualityBudget,
            Expected = expected,
            Implementation = implementation
        };
    }

    static void ValidateJsonShape(JsonElement root, List<ScenarioValidationIssue> issues)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            issues.Add(Error("SCHEMA_TYPE_INVALID", "$ must be a JSON object."));
            return;
        }

        ValidateObject(
            root,
            "$",
            required: new[] { "schemaVersion", "id", "tags", "source", "project", "app", "oracle", "qualityBudget", "expected", "implementation" },
            allowed: new[] { "schemaVersion", "id", "seed", "tags", "source", "project", "app", "oracle", "qualityBudget", "expected", "implementation" },
            issues);

        if (TryGetObject(root, "source", "$.source", issues, out var source))
        {
            ValidateObject(
                source,
                "$.source",
                required: new[] { "language", "testFramework", "template", "features", "migrationFiles" },
                allowed: new[] { "language", "testFramework", "template", "features", "migrationFiles", "adapterConfig" },
                issues);
        }

        if (TryGetObject(root, "project", "$.project", issues, out var project))
        {
            ValidateObject(
                project,
                "$.project",
                required: new[] { "entryProject", "files" },
                allowed: new[] { "entryProject", "files", "references", "msBuild" },
                issues);

            if (TryGetObject(project, "msBuild", "$.project.msBuild", issues, out var msBuild))
            {
                ValidateObject(
                    msBuild,
                    "$.project.msBuild",
                    required: Array.Empty<string>(),
                    allowed: new[] { "nullable", "implicitUsings", "fileScopedNamespace" },
                    issues);
            }
        }

        if (TryGetObject(root, "app", "$.app", issues, out var app))
        {
            ValidateObject(
                app,
                "$.app",
                required: new[] { "baseUrlEnvironmentVariable", "pages" },
                allowed: new[] { "baseUrlEnvironmentVariable", "pages" },
                issues);
        }

        if (TryGetObject(root, "qualityBudget", "$.qualityBudget", issues, out var qualityBudget))
        {
            ValidateObject(
                qualityBudget,
                "$.qualityBudget",
                required: Array.Empty<string>(),
                allowed: new[] { "todoMax", "unmappedMax", "unsupportedMax", "warningsMax" },
                issues);
        }

        if (TryGetObject(root, "expected", "$.expected", issues, out var expected))
        {
            ValidateObject(
                expected,
                "$.expected",
                required: new[] { "status" },
                allowed: new[] { "status" },
                issues);
        }

        if (TryGetObject(root, "implementation", "$.implementation", issues, out var implementation))
        {
            ValidateObject(
                implementation,
                "$.implementation",
                required: new[] { "state", "block" },
                allowed: new[] { "state", "block", "notes", "contentHash" },
                issues);
        }
    }

    static void ValidateObject(
        JsonElement element,
        string path,
        string[] required,
        string[] allowed,
        List<ScenarioValidationIssue> issues)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Add(Error("SCHEMA_TYPE_INVALID", $"{path} must be a JSON object."));
            return;
        }

        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowedSet.Contains(property.Name))
                issues.Add(Error("SCHEMA_PROPERTY_UNKNOWN", $"Unknown property {path}.{property.Name}."));
        }

        foreach (var property in required)
        {
            if (!element.TryGetProperty(property, out _))
                issues.Add(Error("SCHEMA_PROPERTY_REQUIRED", $"Required property {path}.{property} is missing."));
        }
    }

    static bool TryGetObject(
        JsonElement parent,
        string propertyName,
        string path,
        List<ScenarioValidationIssue> issues,
        out JsonElement value)
    {
        if (!parent.TryGetProperty(propertyName, out value))
            return false;

        if (value.ValueKind != JsonValueKind.Object)
        {
            issues.Add(Error("SCHEMA_TYPE_INVALID", $"{path} must be a JSON object."));
            return false;
        }

        return true;
    }

    static void ValidateContract(ScenarioSpec scenario, string scenarioDirectory, List<ScenarioValidationIssue> issues)
    {
        if (!string.Equals(scenario.SchemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
            issues.Add(Error("SCHEMA_VERSION_UNSUPPORTED", $"Expected schemaVersion '{SupportedSchemaVersion}', got '{scenario.SchemaVersion}'."));

        if (string.IsNullOrWhiteSpace(scenario.Id) || !ScenarioIdPattern().IsMatch(scenario.Id))
            issues.Add(Error("SCENARIO_ID_INVALID", "id must use lowercase letters, digits, and hyphens, and contain 3-80 characters."));

        ValidateNormalizedValues("tag", scenario.Tags, issues, required: true);
        ValidateNormalizedValues("feature", scenario.Source.Features, issues, required: true, lowerCaseRequired: false);

        if (!string.Equals(scenario.Source.Language, "csharp", StringComparison.OrdinalIgnoreCase))
            issues.Add(Error("SOURCE_LANGUAGE_UNSUPPORTED", "The v1 lab contract currently supports source.language='csharp'."));

        if (!string.Equals(scenario.Source.TestFramework, "nunit", StringComparison.OrdinalIgnoreCase))
            issues.Add(Error("SOURCE_FRAMEWORK_UNSUPPORTED", "The v1 lab contract currently supports source.testFramework='nunit'."));

        if (string.IsNullOrWhiteSpace(scenario.Source.Template))
            issues.Add(Error("SOURCE_TEMPLATE_MISSING", "source.template is required."));

        if (scenario.Project.Files.Length == 0)
            issues.Add(Error("PROJECT_FILES_MISSING", "project.files must contain at least one file."));

        if (scenario.Source.MigrationFiles.Length == 0)
            issues.Add(Error("MIGRATION_FILES_MISSING", "source.migrationFiles must contain at least one source file."));

        ValidateRelativePaths("project.files", scenario.Project.Files, scenarioDirectory, scenario.Implementation.State, issues);
        ValidateRelativePaths("project.entryProject", new[] { scenario.Project.EntryProject }, scenarioDirectory, ScenarioImplementationState.Planned, issues);
        ValidateRelativePaths("source.migrationFiles", scenario.Source.MigrationFiles, scenarioDirectory, ScenarioImplementationState.Planned, issues);
        if (!string.IsNullOrWhiteSpace(scenario.Source.AdapterConfig))
            ValidateRelativePaths("source.adapterConfig", new[] { scenario.Source.AdapterConfig }, scenarioDirectory, ScenarioImplementationState.Planned, issues);
        ValidateRelativePaths("project.references", scenario.Project.References, scenarioDirectory, ScenarioImplementationState.Planned, issues);
        ValidateMigrationFiles(scenario, issues);
        ValidateAdapterConfig(scenario, issues);
        ValidateEntryProject(scenario, issues);
        ValidateProjectReferences(scenario, issues);
        ValidateBudgets(scenario.QualityBudget, issues);

        if (string.IsNullOrWhiteSpace(scenario.App.BaseUrlEnvironmentVariable))
            issues.Add(Error("APP_BASE_URL_ENV_MISSING", "app.baseUrlEnvironmentVariable is required."));

        if (scenario.App.Pages.Length == 0 && scenario.Expected.Status is ScenarioStatus.Pass or ScenarioStatus.PassWithWarnings)
            issues.Add(Warning("APP_PAGES_EMPTY", "Passing runtime scenarios should define at least one app page before they become ready."));

        if (string.IsNullOrWhiteSpace(scenario.Implementation.Block))
            issues.Add(Warning("IMPLEMENTATION_BLOCK_MISSING", "implementation.block should identify the delivery block that owns this scenario."));

        ValidateReadyFileInventory(scenario, scenarioDirectory, issues);
        ValidateReadyContentHash(scenario, scenarioDirectory, issues);
    }

    static void ValidateMigrationFiles(ScenarioSpec scenario, List<ScenarioValidationIssue> issues)
    {
        var projectFiles = scenario.Project.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var migrationFile in scenario.Source.MigrationFiles)
        {
            if (!projectFiles.Contains(migrationFile))
                issues.Add(Error("MIGRATION_FILE_NOT_IN_PROJECT", $"source.migrationFiles entry is not listed in project.files: {migrationFile}"));

            if (!string.Equals(Path.GetExtension(migrationFile), ".cs", StringComparison.OrdinalIgnoreCase))
                issues.Add(Error("MIGRATION_FILE_NOT_CSHARP", $"source.migrationFiles must contain C# source files: {migrationFile}"));
        }
    }



    static void ValidateAdapterConfig(ScenarioSpec scenario, List<ScenarioValidationIssue> issues)
    {
        var adapterConfig = scenario.Source.AdapterConfig;
        if (string.IsNullOrWhiteSpace(adapterConfig))
            return;

        if (!scenario.Project.Files.Contains(adapterConfig, StringComparer.OrdinalIgnoreCase))
            issues.Add(Error("ADAPTER_CONFIG_NOT_IN_PROJECT", $"source.adapterConfig is not listed in project.files: {adapterConfig}"));

        if (!string.Equals(Path.GetExtension(adapterConfig), ".json", StringComparison.OrdinalIgnoreCase))
            issues.Add(Error("ADAPTER_CONFIG_NOT_JSON", $"source.adapterConfig must reference a .json file: {adapterConfig}"));
    }

    static void ValidateEntryProject(ScenarioSpec scenario, List<ScenarioValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(scenario.Project.EntryProject))
        {
            issues.Add(Error("ENTRY_PROJECT_MISSING", "project.entryProject is required."));
            return;
        }

        if (!scenario.Project.Files.Contains(scenario.Project.EntryProject, StringComparer.OrdinalIgnoreCase))
            issues.Add(Error("ENTRY_PROJECT_NOT_IN_FILES", $"project.entryProject is not listed in project.files: {scenario.Project.EntryProject}"));

        if (!string.Equals(Path.GetExtension(scenario.Project.EntryProject), ".csproj", StringComparison.OrdinalIgnoreCase))
            issues.Add(Error("ENTRY_PROJECT_NOT_CSPROJ", $"project.entryProject must reference a .csproj file: {scenario.Project.EntryProject}"));
    }

    static void ValidateProjectReferences(ScenarioSpec scenario, List<ScenarioValidationIssue> issues)
    {
        foreach (var reference in scenario.Project.References)
        {
            if (!scenario.Project.Files.Contains(reference, StringComparer.OrdinalIgnoreCase))
                issues.Add(Error("PROJECT_REFERENCE_NOT_IN_FILES", $"project.references entry is not listed in project.files: {reference}"));

            if (!string.Equals(Path.GetExtension(reference), ".csproj", StringComparison.OrdinalIgnoreCase))
                issues.Add(Error("PROJECT_REFERENCE_NOT_CSPROJ", $"project.references must contain .csproj paths: {reference}"));
        }
    }


    static void ValidateReadyFileInventory(ScenarioSpec scenario, string scenarioDirectory, List<ScenarioValidationIssue> issues)
    {
        if (scenario.Implementation.State != ScenarioImplementationState.Ready || !Directory.Exists(scenarioDirectory))
            return;

        var declared = scenario.Project.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actual = Directory.EnumerateFiles(scenarioDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(scenarioDirectory, path).Replace('\\', '/'))
            .Where(path => !string.Equals(path, "scenario.json", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Split('/').Any(part =>
                part.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || part.Equals("TestResults", StringComparison.OrdinalIgnoreCase)
                || part.Equals(".vs", StringComparison.OrdinalIgnoreCase)))
            .Where(path => !string.Equals(Path.GetFileName(path), ".DS_Store", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var path in actual.Where(path => !declared.Contains(path)))
            issues.Add(Error("READY_PROJECT_FILE_UNLISTED", $"Ready scenario contains an unlisted fixture file: {path}"));
    }

    static void ValidateReadyContentHash(ScenarioSpec scenario, string scenarioDirectory, List<ScenarioValidationIssue> issues)
    {
        if (scenario.Implementation.State != ScenarioImplementationState.Ready)
            return;

        if (!ScenarioContentHasher.IsWellFormed(scenario.Implementation.ContentHash))
        {
            issues.Add(Error("READY_CONTENT_HASH_INVALID", "Ready scenarios require implementation.contentHash in lowercase sha256:<64 hex> form."));
            return;
        }

        if (scenario.Project.Files.Any(path => !File.Exists(Path.Combine(scenarioDirectory, path.Replace('/', Path.DirectorySeparatorChar)))))
            return;

        try
        {
            var actual = ScenarioContentHasher.Compute(scenarioDirectory, scenario.Project.Files);
            if (!string.Equals(actual, scenario.Implementation.ContentHash, StringComparison.Ordinal))
                issues.Add(Error("READY_CONTENT_HASH_MISMATCH", $"Ready scenario content changed. Expected {scenario.Implementation.ContentHash}, actual {actual}."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            issues.Add(Error("READY_CONTENT_HASH_FAILED", $"Could not hash ready scenario files: {ex.Message}"));
        }
    }

    static void ValidateBudgets(ScenarioQualityBudget budget, List<ScenarioValidationIssue> issues)
    {
        if (budget.TodoMax < 0 || budget.UnmappedMax < 0 || budget.UnsupportedMax < 0 || budget.WarningsMax < 0)
            issues.Add(Error("QUALITY_BUDGET_NEGATIVE", "Quality budget values must be non-negative."));
    }

    static void ValidateNormalizedValues(
        string label,
        string[] values,
        List<ScenarioValidationIssue> issues,
        bool required,
        bool lowerCaseRequired = true)
    {
        if (required && values.Length == 0)
            issues.Add(Error($"{label.ToUpperInvariant()}S_MISSING", $"At least one {label} is required."));

        var duplicates = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            issues.Add(Error($"DUPLICATE_{label.ToUpperInvariant()}", $"Duplicate {label} values: {string.Join(", ", duplicates)}."));

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                issues.Add(Error($"EMPTY_{label.ToUpperInvariant()}", $"Empty {label} values are not allowed."));
            else if (lowerCaseRequired && !string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal))
                issues.Add(Error($"{label.ToUpperInvariant()}_NOT_NORMALIZED", $"{label} '{value}' must be lowercase."));
        }
    }

    static void ValidateRelativePaths(
        string field,
        string[] paths,
        string scenarioDirectory,
        ScenarioImplementationState state,
        List<ScenarioValidationIssue> issues)
    {
        var duplicates = paths
            .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            issues.Add(Error("DUPLICATE_PROJECT_PATH", $"Duplicate values in {field}: {string.Join(", ", duplicates)}."));

        var scenarioRoot = Path.GetFullPath(scenarioDirectory) + Path.DirectorySeparatorChar;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                issues.Add(Error("PROJECT_PATH_EMPTY", $"{field} contains an empty path."));
                continue;
            }

            if (Path.IsPathRooted(path))
            {
                issues.Add(Error("PROJECT_PATH_ABSOLUTE", $"{field} must contain relative paths: {path}"));
                continue;
            }

            var resolved = Path.GetFullPath(Path.Combine(scenarioDirectory, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!resolved.StartsWith(scenarioRoot, StringComparison.OrdinalIgnoreCase))
                issues.Add(Error("PROJECT_PATH_ESCAPES_SCENARIO", $"{field} escapes the scenario directory: {path}"));

            if (state == ScenarioImplementationState.Ready && field == "project.files" && !File.Exists(resolved))
                issues.Add(Error("READY_PROJECT_FILE_MISSING", $"Ready scenario file is missing: {path}"));
        }
    }

    static ScenarioValidationIssue Error(string code, string message) => new(ValidationIssueSeverity.Error, code, message);
    static ScenarioValidationIssue Warning(string code, string message) => new(ValidationIssueSeverity.Warning, code, message);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,78}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex ScenarioIdPattern();
}

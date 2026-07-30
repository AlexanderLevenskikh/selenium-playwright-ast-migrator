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
            Source = source with { Features = source.Features ?? Array.Empty<string>() },
            Project = project with
            {
                Files = project.Files ?? Array.Empty<string>(),
                References = project.References ?? Array.Empty<string>(),
                MsBuild = project.MsBuild ?? new ScenarioMsBuildSpec()
            },
            App = app with { Pages = app.Pages ?? Array.Empty<JsonElement>() },
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
                required: new[] { "language", "testFramework", "template", "features" },
                allowed: new[] { "language", "testFramework", "template", "features" },
                issues);
        }

        if (TryGetObject(root, "project", "$.project", issues, out var project))
        {
            ValidateObject(
                project,
                "$.project",
                required: new[] { "files" },
                allowed: new[] { "files", "references", "msBuild" },
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
                required: new[] { "pages" },
                allowed: new[] { "pages" },
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
                allowed: new[] { "state", "block", "notes" },
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

        ValidateRelativePaths("project.files", scenario.Project.Files, scenarioDirectory, scenario.Implementation.State, issues);
        ValidateRelativePaths("project.references", scenario.Project.References, scenarioDirectory, ScenarioImplementationState.Planned, issues);
        ValidateBudgets(scenario.QualityBudget, issues);

        if (scenario.App.Pages.Length == 0 && scenario.Expected.Status is ScenarioStatus.Pass or ScenarioStatus.PassWithWarnings)
            issues.Add(Warning("APP_PAGES_EMPTY", "Passing runtime scenarios should define at least one app page before they become ready."));

        if (string.IsNullOrWhiteSpace(scenario.Implementation.Block))
            issues.Add(Warning("IMPLEMENTATION_BLOCK_MISSING", "implementation.block should identify the delivery block that owns this scenario."));
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

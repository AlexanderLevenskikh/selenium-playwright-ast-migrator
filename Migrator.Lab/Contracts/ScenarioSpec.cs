using System.Text.Json;
using System.Text.Json.Serialization;

namespace Migrator.Lab.Contracts;

public sealed record ScenarioSpec
{
    public string SchemaVersion { get; init; } = "";
    public string Id { get; init; } = "";
    public int? Seed { get; init; }
    public string[] Tags { get; init; } = Array.Empty<string>();
    public ScenarioSourceSpec Source { get; init; } = new();
    public ScenarioProjectSpec Project { get; init; } = new();
    public ScenarioAppSpec App { get; init; } = new();
    public ScenarioOracleSpec Oracle { get; init; } = new();
    public ScenarioQualityBudget QualityBudget { get; init; } = new();
    public ScenarioExpectedSpec Expected { get; init; } = new();
    public ScenarioImplementationSpec Implementation { get; init; } = new();
}

public sealed record ScenarioSourceSpec
{
    public string Language { get; init; } = "";
    public string TestFramework { get; init; } = "";
    public string Template { get; init; } = "";
    public string[] Features { get; init; } = Array.Empty<string>();
    public string[] MigrationFiles { get; init; } = Array.Empty<string>();
    public string AdapterConfig { get; init; } = "";
}

public sealed record ScenarioProjectSpec
{
    public string EntryProject { get; init; } = "";
    public string[] Files { get; init; } = Array.Empty<string>();
    public string[] References { get; init; } = Array.Empty<string>();
    public ScenarioMsBuildSpec MsBuild { get; init; } = new();
}

public sealed record ScenarioMsBuildSpec
{
    public bool Nullable { get; init; }
    public bool ImplicitUsings { get; init; }
    public bool FileScopedNamespace { get; init; }
}

public sealed record ScenarioAppSpec
{
    public string BaseUrlEnvironmentVariable { get; init; } = "MIGRATOR_LAB_APP_URL";
    public JsonElement[] Pages { get; init; } = Array.Empty<JsonElement>();
}

public sealed record ScenarioOracleSpec
{
    public JsonElement Source { get; init; }
    public JsonElement Target { get; init; }
    public JsonElement Semantic { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Diagnostics { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement MustNot { get; init; }
}

public sealed record ScenarioQualityBudget
{
    public int TodoMax { get; init; }
    public int UnmappedMax { get; init; }
    public int UnsupportedMax { get; init; }
    public int WarningsMax { get; init; }
}

public sealed record ScenarioExpectedSpec
{
    public ScenarioStatus Status { get; init; } = ScenarioStatus.Pass;
}

public sealed record ScenarioImplementationSpec
{
    public ScenarioImplementationState State { get; init; } = ScenarioImplementationState.Planned;
    public string Block { get; init; } = "";
    public string Notes { get; init; } = "";
    public string ContentHash { get; init; } = "";
}

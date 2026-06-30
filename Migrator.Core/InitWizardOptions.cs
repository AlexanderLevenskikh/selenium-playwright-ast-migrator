namespace Migrator.Core;

/// <summary>
/// Options for creating a starter migration workspace from the init wizard.
/// </summary>
public sealed class InitWizardOptions
{
    public string WorkspacePath { get; init; } = "migration";
    public string SourcePath { get; init; } = "";
    public string SourceFrontendId { get; init; } = "selenium-csharp";
    public string SourceLanguage { get; init; } = "csharp";
    public string SourceTestFramework { get; init; } = "unknown";
    public string TargetBackendId { get; init; } = "playwright-dotnet";
    public string TargetTestFramework { get; init; } = "nunit";
    public bool TargetProjectExists { get; init; }
    public string? TargetProjectPath { get; init; }
    public string DefaultTestIdAttribute { get; init; } = "data-testid";
    public bool InstallAgentKit { get; init; }
    public string? TargetNamespace { get; init; }
    public string? TargetBaseClass { get; init; }
}

public sealed record InitWizardResult(
    string Status,
    string WorkspacePath,
    string ConfigPath,
    string[] CreatedFiles,
    string[] Warnings,
    string[] NextSteps);

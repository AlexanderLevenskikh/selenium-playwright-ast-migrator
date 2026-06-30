namespace Migrator.Core;

public sealed class ScaffoldOptions
{
    public string OutPath { get; init; } = null!;
    public string Format { get; init; } = "both";
    public string Namespace { get; init; } = "Example.E2ETests";
    public string ProjectName { get; init; } = "Example.E2ETests.Playwright";

    /// <summary>
    /// Target test framework for the generated Playwright .NET scaffold.
    /// Supported values: nunit (default) and xunit.
    /// </summary>
    public string TargetTestFramework { get; init; } = "nunit";
}

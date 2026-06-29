namespace Migrator.Core;

/// <summary>
/// Describes what a target backend can render and how production-ready that support is.
/// This mirrors source capability reporting so users and contributors can separate stable
/// public APIs from experimental rendering paths.
/// </summary>
public sealed record TargetCapabilityReport(
    string SchemaVersion,
    TargetSpec Target,
    string Status,
    string Summary,
    IReadOnlyList<TargetCapabilityItem> Capabilities,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> RecommendedValidation)
{
    public bool IsProductionReady => string.Equals(Status, "stable", StringComparison.OrdinalIgnoreCase);
}

public sealed record TargetCapabilityItem(
    string Area,
    string Support,
    string Details,
    IReadOnlyList<string> Examples)
{
    public bool IsSupported => !string.Equals(Support, "none", StringComparison.OrdinalIgnoreCase);
}

public static class TargetCapabilityCatalog
{
    public static TargetCapabilityReport ForTarget(TargetSpec target)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        return target.Id.ToLowerInvariant() switch
        {
            "playwright-dotnet" => PlaywrightDotNet(target),
            "playwright-typescript" => PlaywrightTypeScript(target),
            _ => Unknown(target)
        };
    }

    static TargetCapabilityReport PlaywrightDotNet(TargetSpec target) => new(
        SchemaVersion: "target-capabilities/v1",
        Target: target,
        Status: "stable",
        Summary: "Primary production target backend for Playwright .NET/NUnit output.",
        Capabilities: new[]
        {
            Strong("legacy-ir-rendering", "Renders the mature legacy TestFileModel action model used by the production C# path.", "ClickAsync", "FillAsync", "Expect(...).ToBeVisibleAsync"),
            Strong("ir-v2-rendering", "IR V2 documents can be rendered through the compatibility bridge while the canonical renderer evolves.", "MigrationDocument", "legacy bridge"),
            Strong("project-verification", "Generated C# can be compiled through verify-project with configured package/project references.", "temporary csproj", "NUnit", "Microsoft.Playwright.NUnit"),
            Strong("config-driven-mappings", "UiTargets, method mappings, table/list, navigation and wait mappings are supported through adapter-config/profile files.", "UiTargets", "ParameterizedMethods", "Tables", "NavigationUrls"),
            Strong("scaffold", "Can generate a minimal Playwright .NET scaffold for proof-of-compilation pilots.", "GeneratedTestBase", "TestSettings", "ExampleSmokeTest"),
            Basic("runtime-readiness", "The backend emits TODO/root-cause reports and smoke-plan artifacts, but it does not execute browser runtime tests itself.", "smoke-plan", "runtime-classify")
        },
        Limitations: new[]
        {
            "Generated runtime correctness still depends on source-backed selectors and target project helper availability.",
            "IR V2 direct rendering is intentionally conservative; parity with the legacy renderer is guarded by tests."
        },
        RecommendedValidation: new[]
        {
            "Run verify and verify-project after each profile change.",
            "Use migration-quality-dashboard and migration-quality-tickets to reduce TODO categories before broad rollout.",
            "Run a small runtime smoke set in the real Playwright .NET project before scaling."
        });

    static TargetCapabilityReport PlaywrightTypeScript(TargetSpec target) => new(
        SchemaVersion: "target-capabilities/v1",
        Target: target,
        Status: "experimental-preview",
        Summary: "Experimental target backend for Playwright Test TypeScript specs.",
        Capabilities: new[]
        {
            Basic("legacy-ir-rendering", "Renders common Selenium actions/assertions from the legacy action model into Playwright Test specs.", "page.locator(...).click()", "expect(...).toHaveText(...)"),
            Basic("ir-v2-rendering", "Has a native IR V2 renderer for preview flows and parity expansion.", "MigrationDocument", "PlaywrightTypeScriptIrV2Renderer"),
            Basic("project-verification", "Generated specs can be type-checked inside an existing Playwright TS project through verify-ts-project.", "tsconfig.migrator.json", "npx tsc --noEmit"),
            Basic("config-driven-mappings", "Target-specific statements are supported through Targets.playwright-typescript mappings.", "Targets.playwright-typescript.TargetStatements", "TestHost"),
            Limited("test-host", "Imports, fixtures, and generated wrapper style are configurable but still preview-level compared with the .NET path.", "test import", "expect import", "fixture name"),
            Limited("runtime-readiness", "Runtime browser execution is outside the backend; use the real target Playwright project for smoke tests.", "playwright test", "trace viewer")
        },
        Limitations: new[]
        {
            "TypeScript output is experimental and should be validated in a real Playwright TS project before use.",
            "Project helper mappings must use target-specific statements; legacy C# TargetStatements are unsafe for production TS output.",
            "Fixture/import conventions vary heavily across TS projects and may require profile customization."
        },
        RecommendedValidation: new[]
        {
            "Run config-validate --target ts --validation-mode production before migration.",
            "Run migrate --target ts, then verify-ts-project with --ts-project pointing at the real Playwright TS project.",
            "Review every unsupported action and every generated TODO before runtime smoke."
        });

    static TargetCapabilityReport Unknown(TargetSpec target) => new(
        SchemaVersion: "target-capabilities/v1",
        Target: target,
        Status: "unknown",
        Summary: "No capability profile is registered for this target backend.",
        Capabilities: new[]
        {
            None("legacy-ir-rendering", "Unknown."),
            None("ir-v2-rendering", "Unknown."),
            None("project-verification", "Unknown."),
            None("config-driven-mappings", "Unknown."),
            None("runtime-readiness", "Unknown.")
        },
        Limitations: new[] { "Unknown target capability profile." },
        RecommendedValidation: new[] { "Inspect generated code and verification artifacts manually." });

    static TargetCapabilityItem Strong(string area, string details, params string[] examples) => new(area, "strong", details, examples);
    static TargetCapabilityItem Basic(string area, string details, params string[] examples) => new(area, "basic", details, examples);
    static TargetCapabilityItem Limited(string area, string details, params string[] examples) => new(area, "limited", details, examples);
    static TargetCapabilityItem None(string area, string details, params string[] examples) => new(area, "none", details, examples);
}

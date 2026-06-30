using System.Text.RegularExpressions;
using Xunit;

namespace Migrator.Tests;

public class CliProductizationTests
{
    [Fact]
    public void Program_UsesCommandCatalogForModeValidationDefaultOutAndInputPreflight()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));

        Assert.Contains("CliCommandCatalog.IsValidMode(mode)", program);
        Assert.Contains("CliCommandCatalog.Get(mode).DefaultOut", program);
        Assert.Contains("CliCommandCatalog.RequiresInput(mode)", program);
        Assert.Contains("CliCommandCatalog.ShouldPreflightInputExists(mode)", program);
        Assert.DoesNotContain("mode != \"analyze\" && mode != \"dump-ir\"", program);
    }

    [Fact]
    public void Program_HelpRequestsReturnSuccessAndSupportCommandHelp()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));

        Assert.Contains("IsHelpRequest(args)", program);
        Assert.Contains("FindOptionValue(args, \"--mode\")", program);
        Assert.Contains("CliCommandCatalog.WriteCommandHelp(helpMode)", program);
        Assert.Matches(@"if \(IsHelpRequest\(args\)\)[\s\S]*return 0;", program);
    }

    [Fact]
    public void CommandCatalog_SeparatesStableExperimentalAndInternalCommands()
    {
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));

        Assert.Contains("public const string Stable = \"stable\"", catalog);
        Assert.Contains("public const string Experimental = \"experimental\"", catalog);
        Assert.Contains("public const string Internal = \"internal\"", catalog);
        Assert.Contains("StableCommand(\"migrate\"", catalog);
        Assert.Contains("StableCommand(\"verify-project\"", catalog);
        Assert.Contains("ExperimentalCommand(\"verify-ts-project\"", catalog);
        Assert.Contains("ExperimentalCommand(\"orchestrate\"", catalog);
        Assert.Contains("InternalCommand(\"dump-ir\"", catalog);
        Assert.Contains("InternalCommand(\"config-normalize\"", catalog);
    }

    [Fact]
    public void CommandCatalog_CoversAllDocumentedModesWithDefaultOutputs()
    {
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));
        var expectedModes = new[]
        {
            "init",
            "analyze",
            "dump-ir",
            "migrate",
            "verify",
            "verify-project",
            "verify-ts-project",
            "doctor",
            "explain-todo",
            "smoke-plan",
            "runtime-classify",
            "migration-board",
            "report-serve",
            "profile-match",
            "capabilities",
            "config-schema",
            "config-validate",
            "config-normalize",
            "config-diff",
            "guard",
            "propose",
            "discover-target",
            "index-pom",
            "helper-inventory",
            "orchestrate",
            "scaffold",
            "bootstrap-project",
        };

        foreach (var mode in expectedModes)
        {
            Assert.Matches($@"(StableCommand|ExperimentalCommand|InternalCommand)\(""{Regex.Escape(mode)}"",\s*""[^""]+""", catalog);
        }
    }

    [Fact]
    public void CommandCatalog_HasCommandSpecificHelpForKeyUserFlows()
    {
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));

        Assert.Contains("BuildCommandHelp(CliCommandInfo command)", catalog);
        Assert.Contains("Use `selenium-pw-migrator --mode <mode> --help`", catalog);
        Assert.Contains("selenium-pw-migrator init --wizard --source ./OldTests", catalog);
        Assert.Contains("selenium-pw-migrator --mode migrate --input ./OldTests", catalog);
        Assert.Contains("selenium-pw-migrator --mode doctor --input ./OldTests", catalog);
        Assert.Contains("selenium-pw-migrator report serve --input migration/runs/latest --port 5077", catalog);
        Assert.Contains("selenium-pw-migrator --mode verify-project --input ./OldTests", catalog);
        Assert.Contains("selenium-pw-migrator --mode helper-inventory --input ./OldTests", catalog);
        Assert.Contains("--target-test-framework <nunit|xunit>", catalog);
        Assert.Contains("--fix", catalog);
        Assert.Contains("--apply", catalog);
        Assert.Contains("--dry-run", catalog);
        Assert.Contains("selenium-pw-migrator --mode doctor --input ./OldTests --fix --dry-run", catalog);
        Assert.Contains("selenium-pw-migrator --mode scaffold --target-test-framework xunit", catalog);
    }


    [Fact]
    public void Program_WiresTargetTestFrameworkThroughCliConfigScaffoldAndVerifyProject()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var models = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Models/CliReportModels.cs"));

        Assert.Contains("--target-test-framework", program);
        Assert.Contains("NormalizeDirectCommand(args)", program);
        Assert.Contains("RunInitWizard(opts, targetBackend)", program);
        Assert.Contains("--test-id-attribute", program);
        Assert.Contains("--fix", program);
        Assert.Contains("DoctorFixPlanner", program);
        Assert.Contains("RunReportServe(inputPath, outPath, format, recursiveArtifacts, opts.Port, opts.StaticOnly)", program);
        Assert.Contains("BuildReportServeDashboardReport", program);
        Assert.Contains("CreateReportServeEvidenceZip", program);
        Assert.Contains("ServeStaticDashboard", program);
        Assert.Contains("report-serve", program);
        Assert.Contains("--port", program);
        Assert.Contains("--static-only", program);
        Assert.Contains("ApplyTargetTestFrameworkOverride", program);
        Assert.Contains("TargetTestFramework = targetTestFramework ?? \"nunit\"", program);
        Assert.Contains("BuildPackageReferences(verification, projectReferences, config)", program);
        Assert.Contains("Microsoft.Playwright.Xunit", program);
        Assert.Contains("string? TargetTestFramework", models);
        Assert.Contains("ReportServeDashboardReport", models);
        Assert.Contains("int Port", models);
        var runtimeClassifier = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/RuntimeFailureClassifierCommand.cs"));
        Assert.Contains("runtime-next-tickets.md", runtimeClassifier);
        Assert.Contains("TraceArtifacts", models);
        Assert.Contains("ContextLinks", models);
        Assert.Contains("LikelyOwner", models);

    }

    static string FindRepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}

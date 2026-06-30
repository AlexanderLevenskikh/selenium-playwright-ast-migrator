using Xunit;

namespace Migrator.Tests;

public class ProfileMarketplaceTests
{
    [Fact]
    public void ProfileMarketplace_HasOfflineBuiltInProfilesAndSafetyMetadata()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/ProfileMarketplaceCommand.cs"));

        Assert.Contains("BuiltInProfiles", command);
        Assert.Contains("basic-csharp-nunit", command);
        Assert.Contains("basic-csharp-xunit", command);
        Assert.Contains("basic-csharp-nunit-data-tid", command);
        Assert.Contains("SplitSearchTokens", command);
        Assert.Contains("built-in/offline", command);
        Assert.Contains("SafetyLevel", command);
        Assert.Contains("RequiredEvidence", command);
        Assert.Contains("KnownLimitations", command);
        Assert.Contains("Changelog", command);
        Assert.Contains("CompatibilityRange", command);
    }

    [Fact]
    public void ProfileMarketplace_InstallWritesReviewedConfigLayerWithoutSilentOverwrite()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/ProfileMarketplaceCommand.cs"));
        var docs = File.ReadAllText(FindRepositoryFile("docs/profile-marketplace.md"));

        Assert.Contains(".adapter-config.json", command);
        Assert.Contains(".adapter-config.new.json", command);
        Assert.Contains("profile-metadata.json", command);
        Assert.Contains("NoOverwrite", command);
        Assert.Contains("ConfigValidator.ValidateJson", command);
        Assert.Contains("Existing files are not silently overwritten", docs);
        Assert.Contains("reviewed config layer", docs);
    }

    [Fact]
    public void ProfileMarketplace_DoesNotShipSuppressionsOrBroadSourceOnlyIdentifiers()
    {
        var command = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/ProfileMarketplaceCommand.cs"));

        Assert.Contains("Built-in marketplace profiles must not silently suppress methods/assertions", command);
        Assert.Contains("Profile cannot add broad SourceOnlyIdentifiers", command);
        Assert.Contains("SuppressedMethods = Array.Empty<string>()", command);
        Assert.Contains("SuppressedMethodPatterns = Array.Empty<string>()", command);
        Assert.Contains("SourceOnlyIdentifiers = Array.Empty<string>()", command);
    }

    [Fact]
    public void Program_NormalizesDirectProfileCommands()
    {
        var program = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Program.cs"));
        var catalog = File.ReadAllText(FindRepositoryFile("Migrator.Cli/Commands/CliCommandCatalog.cs"));

        Assert.Contains("string.Equals(args[0], \"profile\"", program);
        Assert.Contains("\"list\" => \"profile-list\"", program);
        Assert.Contains("\"search\" => \"profile-search\"", program);
        Assert.Contains("\"inspect\" => \"profile-inspect\"", program);
        Assert.Contains("\"install\" => \"profile-install\"", program);
        Assert.Contains("\"diff\" => \"profile-diff\"", program);
        Assert.Contains("ExperimentalCommand(\"profile-list\"", catalog);
        Assert.Contains("ExperimentalCommand(\"profile-install\", \"profiles\"", catalog);
    }

    [Fact]
    public void Docs_DescribeProfileMarketplaceWorkflow()
    {
        var docs = File.ReadAllText(FindRepositoryFile("docs/profile-marketplace.md"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));

        Assert.Contains("selenium-pw-migrator profile list", docs);
        Assert.Contains("selenium-pw-migrator profile search selenium-nunit", docs);
        Assert.Contains("selenium-pw-migrator profile inspect basic-csharp-xunit", docs);
        Assert.Contains("selenium-pw-migrator profile install basic-csharp-nunit", docs);
        Assert.Contains("selenium-pw-migrator profile diff", docs);
        Assert.Contains("Remote profile indexes are intentionally not implemented yet", docs);
        Assert.Contains("docs/profile-marketplace.md", readme);
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

using Xunit;

namespace Migrator.Tests;

[Trait("Layer", "Contract")]
public sealed class LabCliContractTests
{
    [Fact]
    public void Cli_ExposesLabAsOneCommandFamilyWithoutASecondBinary()
    {
        var program = Read("Migrator.Cli/Program.cs");
        var command = Read("Migrator.Cli/Commands/LabCommand.cs");
        var project = Read("Migrator.Cli/Migrator.Cli.csproj");

        Assert.Contains("string.Equals(args[0], \"lab\"", program);
        Assert.Contains("selenium-pw-migrator lab run", command);
        Assert.Contains("selenium-pw-migrator lab replay", command);
        Assert.Contains("selenium-pw-migrator lab baseline", command);
        Assert.Contains("selenium-pw-migrator lab diff", command);
        Assert.Contains("LabRunCoordinator", command);
        Assert.Contains("LabDiffEngine", command);
        Assert.Contains("--suite <name>", command);
        Assert.Contains("selenium-pw-migrator lab validate", command);
        Assert.Contains("selenium-pw-migrator lab list", command);
        Assert.Contains("selenium-pw-migrator lab app serve", command);
        Assert.Contains("Migrator.Lab\\Migrator.Lab.csproj", project);
        Assert.DoesNotContain("<OutputType>Exe</OutputType>", Read("Migrator.Lab/Migrator.Lab.csproj"));
    }

    [Fact]
    public void LabSchema_IsPackedWithTheDotnetTool()
    {
        var project = Read("Migrator.Cli/Migrator.Cli.csproj");
        var schema = Read("schemas/lab-scenario.schema.json");

        Assert.Contains("lab-scenario.schema.json", project);
        Assert.Contains("lab-scenario/v1", schema);
        Assert.Contains("UNSUPPORTED_AS_EXPECTED", schema);
        Assert.Contains("INFRASTRUCTURE_FAILURE", schema);
        Assert.Contains("migrationFiles", schema);
        Assert.Contains("contentHash", schema);
    }

    static string Read(string relativePath) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Migrator.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Migrator.sln.");
    }
}

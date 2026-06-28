using System.Xml.Linq;

namespace Migrator.Tests;

/// <summary>
/// PROD-01 architecture boundary tests.
/// These tests protect the cross-language compiler-like shape:
/// Core owns contracts/IR, source frontends own parsing, target backends own rendering,
/// and CLI remains the composition root.
/// </summary>
public class ArchitectureDependencyTests
{
    static readonly IReadOnlyDictionary<string, string[]> AllowedProjectReferences = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Migrator.Core"] = Array.Empty<string>(),
        ["Migrator.Roslyn"] = new[] { "Migrator.Core" },
        ["Migrator.SeleniumCSharp"] = new[] { "Migrator.Core" },
        ["Migrator.PlaywrightDotNet"] = new[] { "Migrator.Core" },
        ["Migrator.PlaywrightTypeScript"] = new[] { "Migrator.Core" },
        ["Migrator.Cli"] = new[]
        {
            "Migrator.Core",
            "Migrator.Roslyn",
            "Migrator.SeleniumCSharp",
            "Migrator.PlaywrightDotNet",
            "Migrator.PlaywrightTypeScript"
        },
        ["Migrator.Tests"] = new[]
        {
            "Migrator.Core",
            "Migrator.Roslyn",
            "Migrator.SeleniumCSharp",
            "Migrator.PlaywrightDotNet",
            "Migrator.PlaywrightTypeScript"
        }
    };

    [Fact]
    public void ProjectReferences_MatchArchitectureMatrix()
    {
        var root = FindRepositoryRoot();

        foreach (var (projectName, allowed) in AllowedProjectReferences)
        {
            var actual = ReadProjectReferences(root, projectName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            var expected = allowed.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

            Assert.True(
                actual.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase),
                $"{projectName} project references changed.\nExpected: {string.Join(", ", expected)}\nActual:   {string.Join(", ", actual)}");
        }
    }

    [Fact]
    public void CoreProject_HasNoParserOrRendererPackageReferences()
    {
        var root = FindRepositoryRoot();
        var packages = ReadPackageReferences(root, "Migrator.Core");

        Assert.DoesNotContain(packages, p => p.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packages, p => p.StartsWith("Microsoft.Playwright", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packages, p => p.Contains("Selenium", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CoreSource_DoesNotImportImplementationProjects()
    {
        var root = FindRepositoryRoot();
        var coreDir = Path.Combine(root, "Migrator.Core");
        var forbiddenUsings = new[]
        {
            "using Migrator.Roslyn",
            "using Migrator.SeleniumCSharp",
            "using Migrator.PlaywrightDotNet",
            "using Migrator.PlaywrightTypeScript"
        };

        var violations = Directory.GetFiles(coreDir, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => new { file, line, lineNumber = index + 1 }))
            .Where(entry => forbiddenUsings.Any(forbidden => entry.line.TrimStart().StartsWith(forbidden, StringComparison.Ordinal)))
            .Select(entry => $"{Path.GetRelativePath(root, entry.file)}:{entry.lineNumber}: {entry.line.Trim()}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Migrator.Core must stay implementation-agnostic. Forbidden using(s):\n" + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void SourceAndTargetProjects_DoNotReferenceEachOther()
    {
        var root = FindRepositoryRoot();
        var forbidden = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Migrator.Roslyn"] = new[] { "Migrator.PlaywrightDotNet", "Migrator.PlaywrightTypeScript" },
            ["Migrator.SeleniumCSharp"] = new[] { "Migrator.PlaywrightDotNet", "Migrator.PlaywrightTypeScript", "Migrator.Roslyn" },
            ["Migrator.PlaywrightDotNet"] = new[] { "Migrator.Roslyn", "Migrator.SeleniumCSharp", "Migrator.PlaywrightTypeScript" },
            ["Migrator.PlaywrightTypeScript"] = new[] { "Migrator.Roslyn", "Migrator.SeleniumCSharp", "Migrator.PlaywrightDotNet" }
        };

        foreach (var (projectName, forbiddenRefs) in forbidden)
        {
            var actual = ReadProjectReferences(root, projectName).ToArray();
            var violations = actual.Intersect(forbiddenRefs, StringComparer.OrdinalIgnoreCase).ToArray();

            Assert.True(
                violations.Length == 0,
                $"{projectName} must not reference source/target implementation peers: {string.Join(", ", violations)}");
        }
    }

    static string[] ReadProjectReferences(string root, string projectName)
    {
        var projectPath = Path.Combine(root, projectName, projectName + ".csproj");
        Assert.True(File.Exists(projectPath), $"Missing project file: {projectPath}");

        var doc = XDocument.Load(projectPath);
        return doc.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(NormalizeProjectReferenceName)
            .ToArray();
    }

    static string[] ReadPackageReferences(string root, string projectName)
    {
        var projectPath = Path.Combine(root, projectName, projectName + ".csproj");
        Assert.True(File.Exists(projectPath), $"Missing project file: {projectPath}");

        var doc = XDocument.Load(projectPath);
        return doc.Descendants("PackageReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToArray();
    }

    static string NormalizeProjectReferenceName(string include)
    {
        var normalized = include.Replace('\\', '/');
        var fileName = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        return Path.GetFileNameWithoutExtension(fileName);
    }

    static string FindRepositoryRoot()
    {
        var probes = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Environment.GetEnvironmentVariable("BUILD_SOURCESDIRECTORY"),
            Environment.GetEnvironmentVariable("GITHUB_WORKSPACE")
        };

        foreach (var probe in probes.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var dir = new DirectoryInfo(probe!);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Migrator.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new DirectoryNotFoundException($"Could not find repository root from {AppContext.BaseDirectory}");
    }
}

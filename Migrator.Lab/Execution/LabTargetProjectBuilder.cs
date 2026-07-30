using System.Text;
using System.Text.RegularExpressions;

namespace Migrator.Lab.Execution;

public sealed record LabTargetProject(
    string RootDirectory,
    string ProjectPath,
    string RuntimeArtifactsDirectory,
    string Route,
    string[] GeneratedFiles);

public static partial class LabTargetProjectBuilder
{
    public static LabTargetProject Prepare(
        string migrationDirectory,
        string targetRoot,
        string route)
    {
        var sourceGenerated = Path.Combine(migrationDirectory, "generated");
        if (!Directory.Exists(sourceGenerated))
            throw new DirectoryNotFoundException($"Generated migration directory not found: {sourceGenerated}");

        RecreateDirectory(targetRoot);
        var generatedRoot = Path.Combine(targetRoot, "Generated");
        var runtimeRoot = Path.Combine(targetRoot, "Runtime");
        var runtimeArtifacts = Path.Combine(targetRoot, "runtime-artifacts");
        Directory.CreateDirectory(generatedRoot);
        Directory.CreateDirectory(runtimeRoot);
        Directory.CreateDirectory(runtimeArtifacts);

        var generatedFiles = Directory.GetFiles(sourceGenerated, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (generatedFiles.Length == 0)
            throw new InvalidOperationException($"No generated C# files found in {sourceGenerated}.");

        var copiedFiles = new List<string>();
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in generatedFiles)
        {
            var destination = Path.Combine(generatedRoot, Path.GetFileName(source));
            File.Copy(source, destination, overwrite: true);
            copiedFiles.Add(destination);
            namespaces.Add(ReadNamespace(File.ReadAllText(source)) ?? "");
        }

        var index = 0;
        foreach (var @namespace in namespaces.OrderBy(value => value, StringComparer.Ordinal))
        {
            var suffix = string.IsNullOrWhiteSpace(@namespace)
                ? "Global"
                : Regex.Replace(@namespace, "[^A-Za-z0-9_.-]", "_").Replace('.', '_');
            var basePath = Path.Combine(runtimeRoot, $"LabRuntimePageTest-{++index:D2}-{suffix}.cs");
            File.WriteAllText(basePath, BuildRuntimeBase(@namespace), new UTF8Encoding(false));
        }

        var projectPath = Path.Combine(targetRoot, "Migrator.Lab.TargetRuntime.csproj");
        File.WriteAllText(projectPath, BuildProjectFile(), new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(targetRoot, "Directory.Packages.props"),
            "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>\n",
            new UTF8Encoding(false));

        return new LabTargetProject(
            Path.GetFullPath(targetRoot),
            Path.GetFullPath(projectPath),
            Path.GetFullPath(runtimeArtifacts),
            route,
            copiedFiles.Select(Path.GetFullPath).ToArray());
    }

    static string BuildProjectFile() => """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <LangVersion>latest</LangVersion>
            <IsPackable>false</IsPackable>
            <IsTestProject>true</IsTestProject>
            <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
            <ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>
            <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
            <DirectoryPackagesPropsPath>$(MSBuildThisFileDirectory)Directory.Packages.props</DirectoryPackagesPropsPath>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.7.0" />
            <PackageReference Include="Microsoft.Playwright.NUnit" Version="1.52.0" />
            <PackageReference Include="NUnit" Version="4.2.2" />
            <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
          </ItemGroup>
        </Project>
        """;

    static string BuildRuntimeBase(string @namespace)
    {
        var namespaceLine = string.IsNullOrWhiteSpace(@namespace)
            ? ""
            : $"namespace {@namespace};\n\n";

        return $$"""
            using Microsoft.Playwright;
            using NUnit.Framework;
            using NUnit.Framework.Interfaces;

            {{namespaceLine}}public abstract class PageTest : Microsoft.Playwright.NUnit.PageTest
            {
                bool labTracingStarted;

                [SetUp]
                public async Task MigratorLabNavigateAsync()
                {
                    var baseUrl = Environment.GetEnvironmentVariable("MIGRATOR_LAB_APP_URL")
                        ?? throw new InvalidOperationException("MIGRATOR_LAB_APP_URL is not set.");
                    var route = Environment.GetEnvironmentVariable("MIGRATOR_LAB_TARGET_ROUTE") ?? "/";

                    await Context.Tracing.StartAsync(new TracingStartOptions
                    {
                        Screenshots = true,
                        Snapshots = true,
                        Sources = true
                    });
                    labTracingStarted = true;
                    await Page.GotoAsync(new Uri(new Uri(baseUrl, UriKind.Absolute), route).AbsoluteUri);
                }

                [TearDown]
                public async Task MigratorLabCaptureFailureAsync()
                {
                    if (!labTracingStarted)
                        return;

                    var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
                    if (!failed)
                    {
                        await Context.Tracing.StopAsync();
                        return;
                    }

                    var root = Environment.GetEnvironmentVariable("MIGRATOR_LAB_RUNTIME_ARTIFACTS")
                        ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "migrator-lab-runtime-artifacts");
                    Directory.CreateDirectory(root);
                    var testName = SanitizeFileName(TestContext.CurrentContext.Test.Name);

                    try
                    {
                        await Page.ScreenshotAsync(new PageScreenshotOptions
                        {
                            Path = Path.Combine(root, testName + ".png"),
                            FullPage = true
                        });
                    }
                    finally
                    {
                        await Context.Tracing.StopAsync(new TracingStopOptions
                        {
                            Path = Path.Combine(root, testName + ".zip")
                        });
                    }
                }

                static string SanitizeFileName(string value)
                {
                    var invalid = Path.GetInvalidFileNameChars();
                    return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
                }
            }
            """;
    }

    static string? ReadNamespace(string source)
    {
        var match = NamespaceRegex().Match(source);
        return match.Success ? match.Groups[1].Value : null;
    }

    static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
    }

    [GeneratedRegex(@"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*[;{]", RegexOptions.Multiline)]
    private static partial Regex NamespaceRegex();
}

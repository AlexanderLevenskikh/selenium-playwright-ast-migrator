// Light Roslyn compile-smoke for generated source.
// This is NOT a full dotnet build of the generated Playwright project — it only validates
// that generated C# code has valid syntax, correct attribute usage, and resolves against
// Playwright/NUnit framework references. Unsupported actions become comments and must not
// break compilation.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Migrator.Tests;

public static class CompileChecker
{
    public static Diagnostic[] CompileErrors(string generatedSource, params string[] additionalSources) {
        var compilation = BuildCompilation(generatedSource, additionalSources);
        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id != "CS8019")
            .ToArray();
    }

    public static bool CompilesWithoutErrors(string generatedSource, params string[] additionalSources)
    {
        return !CompileErrors(generatedSource, additionalSources).Any();
    }

    public static string FormatErrors(string generatedSource, params string[] additionalSources)
    {
        var errors = CompileErrors(generatedSource, additionalSources);
        if (!errors.Any())
            return "No compilation errors.";

        var lines = new List<string> { $"{errors.Length} compilation error(s):" };
        foreach (var e in errors)
            lines.Add($"  {e}");
        lines.Add("");
        lines.Add("Generated source:");
        lines.AddRange(generatedSource.Split('\n').Select((l, i) => $"  {i + 1}: {l}"));
        return string.Join("\n", lines);
    }

    static CSharpCompilation BuildCompilation(string generatedSource, params string[] additionalSources)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        var allSources = new[] { generatedSource }.Concat(additionalSources).ToArray();
        var trees = allSources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)).ToArray();

        var references = GetReferences();

        return CSharpCompilation.Create(
            "MigratorGenerated",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    static IEnumerable<MetadataReference> GetReferences()
    {
        var basePath = Path.GetDirectoryName(typeof(CompileChecker).Assembly.Location)!;
        var loadedAsms = AppDomain.CurrentDomain.GetAssemblies();

        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Runtime",
            "System.Collections",
            "System.Linq",
            "System.Threading.Tasks",
            "System.Text.RegularExpressions",
            "Microsoft.Bcl.AsyncInterfaces",
            "Microsoft.Playwright.NUnit",
            "Microsoft.Playwright",
            "nunit.framework",
            "netstandard",
        };

        var references = new List<MetadataReference>();

        // 1. Add references from loaded assemblies
        foreach (var asm in loadedAsms)
        {
            var name = asm.GetName().Name;
            if (name != null && needed.Contains(name) && !string.IsNullOrEmpty(asm.Location))
            {
                references.Add(MetadataReference.CreateFromFile(asm.Location));
                needed.Remove(name);
            }
        }

        // 2. Search output dir for remaining needed DLLs
        if (needed.Count > 0)
        {
            var files = Directory.GetFiles(basePath, "*.dll");
            foreach (var file in files)
            {
                var asmName = Path.GetFileNameWithoutExtension(file);
                if (needed.Contains(asmName))
                {
                    references.Add(MetadataReference.CreateFromFile(file));
                    needed.Remove(asmName);
                }
            }
        }

        // 3. Add System.Private.CoreLib from the runtime (this is where System.Object lives in .NET Core)
        var coreLibPath = FindCoreLib();
        if (coreLibPath != null)
            references.Add(MetadataReference.CreateFromFile(coreLibPath));

        return references;
    }

    static string? FindCoreLib()
    {
        // Use TRUSTED_PLATFORM_ASSEMBLIES for portable CoreLib resolution
        // (works in CI, Linux, and corporate environments)
        var tpAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrEmpty(tpAssemblies))
        {
            foreach (var path in tpAssemblies.Split(Path.PathSeparator))
            {
                if (Path.GetFileName(path) == "System.Private.CoreLib.dll" && File.Exists(path))
                    return path;
            }
        }

        // Fallback: search from test assembly location upward
        var dir = Path.GetDirectoryName(typeof(CompileChecker).Assembly.Location)!;
        for (int i = 0; i < 20; i++)
        {
            var coreLib = Path.Combine(dir, "System.Private.CoreLib.dll");
            if (File.Exists(coreLib))
                return coreLib;
            var parent = Path.GetDirectoryName(dir);
            if (parent == null)
                break;
            dir = parent;
        }

        return null;
    }
}

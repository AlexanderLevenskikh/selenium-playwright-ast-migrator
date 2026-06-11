using System;
using System.IO;
using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.Roslyn;
using Migrator.SeleniumCSharp;

var parser = new RoslynTestFileParser();
var renderer = new PlaywrightDotNetRenderer();

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: Migrator.Cli <file.cs|directory> [--config config.json] [--output output.cs]");
    return 1;
}

var inputPath = args[0];
var configIndex = Array.IndexOf(args, "--config");
var outputIndex = Array.IndexOf(args, "--output");

string? configPath = configIndex >= 0 && configIndex + 1 < args.Length ? args[configIndex + 1] : null;
string? outputPath = outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1] : null;

IProjectAdapter? adapter = null;

if (configPath != null && File.Exists(configPath))
{
    adapter = new DefaultProjectAdapter(configPath);
    Console.WriteLine($"Loaded adapter config: {configPath}");
}

var pipeline = new MigrationPipeline(parser, renderer, adapter);

IEnumerable<PipelineResult> results;

if (Directory.Exists(inputPath))
{
    results = pipeline.ProcessDirectory(inputPath);
}
else
{
    results = new[] { pipeline.ProcessFile(inputPath) };
}

int totalFiles = 0;
int totalTests = 0;
int totalUnsupported = 0;
int totalMapped = 0;
int totalUnmapped = 0;

foreach (var result in results)
{
    var report = result.Report;

    totalFiles++;
    totalTests += report.TotalTests;
    totalUnsupported += report.UnsupportedCount;
    totalMapped += report.MappedTargets;
    totalUnmapped += report.UnmappedTargets;

    var outDir = outputPath != null
        ? Path.GetDirectoryName(Path.GetFullPath(outputPath))
        : Path.Combine(Path.GetDirectoryName(inputPath) ?? ".", "Output");
    var outName = outputPath?.Contains(Path.DirectorySeparatorChar.ToString()) == true
        ? Path.GetFileName(outputPath)
        : $"{result.SourceModel.ClassName}Playwright.cs";
    Directory.CreateDirectory(outDir ?? ".");
    var fullOut = Path.Combine(outDir ?? ".", outName);
    File.WriteAllText(fullOut, report.GeneratedOutput);

    Console.WriteLine($"Processed: {report.SourceFilePath}");
    Console.WriteLine($"  Tests: {report.TotalTests}");
    Console.WriteLine($"  Unsupported: {report.UnsupportedCount}");
    Console.WriteLine($"  Semantic: {report.SemanticActions}, SyntaxFallback: {report.SyntaxFallbackActions}");
    Console.WriteLine($"  Mapped: {report.MappedTargets}, Unmapped: {report.UnmappedTargets}");
    Console.WriteLine($"  TODO comments: {report.TodoComments}");
    Console.WriteLine($"  Output: {fullOut}");
}

Console.WriteLine();
Console.WriteLine($"Total: {totalFiles} files, {totalTests} tests, {totalUnsupported} unsupported actions");
Console.WriteLine($"  Mapped: {totalMapped}, Unmapped: {totalUnmapped}");

return totalUnsupported > 0 ? 1 : 0;

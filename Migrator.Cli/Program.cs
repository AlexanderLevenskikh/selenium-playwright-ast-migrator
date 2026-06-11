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

IEnumerable<TestFileModel> models;

if (Directory.Exists(inputPath))
{
    models = parser.ParseDirectory(inputPath);
}
else
{
    models = new[] { parser.Parse(inputPath) };
}

int totalFiles = 0;
int totalTests = 0;
int totalUnsupported = 0;
int totalMapped = 0;
int totalUnmapped = 0;

foreach (var model in models)
{
    var output = renderer.Render(model, adapter);

    var allActions = model.Tests.SelectMany(t => t.BodyActions).ToList();
    var allSetupActions = model.SetUpActions.ToList();
    var allFileActions = allActions.Concat(allSetupActions).ToList();
    var unsupportedCount = allActions.OfType<UnsupportedAction>().Count();
    var semanticCount = allFileActions.Count(a => a.Confidence == RecognitionConfidence.Semantic);
    var syntaxFallbackCount = allFileActions.Count(a => a.Confidence == RecognitionConfidence.SyntaxFallback);

    var mappedTargets = 0;
    var unmappedTargets = 0;
    if (adapter is DefaultProjectAdapter dpa)
    {
        mappedTargets = dpa.MappedTargets;
        unmappedTargets = dpa.UnmappedTargets;
    }

    var todoComments = output.Split('\n').Count(line =>
        line.TrimStart().StartsWith("// TODO:"));

    totalFiles++;
    totalTests += model.Tests.Count();
    totalUnsupported += unsupportedCount;
    totalMapped += mappedTargets;
    totalUnmapped += unmappedTargets;

    var outDir = outputPath != null
        ? Path.GetDirectoryName(Path.GetFullPath(outputPath))
        : Path.Combine(Path.GetDirectoryName(inputPath) ?? ".", "Output");
    var outName = outputPath?.Contains(Path.DirectorySeparatorChar.ToString()) == true
        ? Path.GetFileName(outputPath)
        : $"{model.ClassName}Playwright.cs";
    Directory.CreateDirectory(outDir ?? ".");
    var fullOut = Path.Combine(outDir ?? ".", outName);
    File.WriteAllText(fullOut, output);

    var report = new MigrationReport(
        SourceFilePath: model.FilePath,
        TotalTests: model.Tests.Count(),
        SuccessfullyConvertedTests: model.Tests.Count(t => !t.BodyActions.Any(a => a is UnsupportedAction)),
        UnsupportedActions: model.Tests.SelectMany(t => t.BodyActions).OfType<UnsupportedAction>(),
        GeneratedOutput: fullOut,
        SemanticActions: semanticCount,
        SyntaxFallbackActions: syntaxFallbackCount,
        UnsupportedCount: unsupportedCount,
        MappedTargets: mappedTargets,
        UnmappedTargets: unmappedTargets,
        TodoComments: todoComments
    );

    Console.WriteLine($"Processed: {model.FilePath}");
    Console.WriteLine($"  Tests: {report.TotalTests}");
    Console.WriteLine($"  Unsupported: {report.UnsupportedCount}");
    Console.WriteLine($"  Semantic: {report.SemanticActions}, SyntaxFallback: {report.SyntaxFallbackActions}");
    Console.WriteLine($"  Mapped: {report.MappedTargets}, Unmapped: {report.UnmappedTargets}");
    Console.WriteLine($"  TODO comments: {report.TodoComments}");
    Console.WriteLine($"  Output: {report.GeneratedOutput}");
}

Console.WriteLine();
Console.WriteLine($"Total: {totalFiles} files, {totalTests} tests, {totalUnsupported} unsupported actions");
Console.WriteLine($"  Mapped: {totalMapped}, Unmapped: {totalUnmapped}");

return totalUnsupported > 0 ? 1 : 0;

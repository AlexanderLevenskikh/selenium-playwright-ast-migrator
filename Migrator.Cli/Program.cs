using Migrator.Core;
using Migrator.Core.Models;
using Migrator.PlaywrightDotNet;
using Migrator.Roslyn;

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

foreach (var model in models)
{
    var output = renderer.Render(model);
    var unsupportedCount = model.Tests.SelectMany(t => t.BodyActions)
        .OfType<UnsupportedAction>().Count();

    totalFiles++;
    totalTests += model.Tests.Count(t => t.Name != "__SetUp__");
    totalUnsupported += unsupportedCount;

    var outDir = outputPath != null
        ? Path.GetDirectoryName(Path.GetFullPath(outputPath))
        : Path.Combine(Path.GetDirectoryName(inputPath) ?? ".", "Output");
    var outName = outputPath?.Contains(Path.DirectorySeparatorChar.ToString()) == true
        ? Path.GetFileName(outputPath)
        : $"{model.ClassName}Playwright.cs";
    Directory.CreateDirectory(outDir ?? ".");
    var fullOut = Path.Combine(outDir ?? ".", outName);
    File.WriteAllText(fullOut, output);

    var allActions = model.Tests.SelectMany(t => t.BodyActions).ToList();
    var semanticCount = allActions.Count(a => a.Confidence == Migrator.Core.RecognitionConfidence.Semantic);
    var syntaxFallbackCount = allActions.Count(a => a.Confidence == Migrator.Core.RecognitionConfidence.SyntaxFallback);

    var report = new MigrationReport(
        SourceFilePath: model.FilePath,
        TotalTests: model.Tests.Count(t => t.Name != "__SetUp__"),
        SuccessfullyConvertedTests: model.Tests.Count(t => t.Name != "__SetUp__" && !t.BodyActions.Any(a => a is UnsupportedAction)),
        UnsupportedActions: model.Tests.SelectMany(t => t.BodyActions).OfType<UnsupportedAction>(),
        GeneratedOutput: fullOut,
        SemanticActions: semanticCount,
        SyntaxFallbackActions: syntaxFallbackCount
    );

    Console.WriteLine($"Processed: {model.FilePath}");
    Console.WriteLine($"  Tests: {report.TotalTests}");
    Console.WriteLine($"  Unsupported: {report.UnsupportedActions.Count()}");
    Console.WriteLine($"  Output: {report.GeneratedOutput}");
}

Console.WriteLine();
Console.WriteLine($"Total: {totalFiles} files, {totalTests} tests, {totalUnsupported} unsupported actions");

return totalUnsupported > 0 ? 1 : 0;

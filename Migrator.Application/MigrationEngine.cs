using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Migrator.Core;
using Migrator.Roslyn;
using Migrator.SeleniumCSharp;
using Migrator.PlaywrightDotNet;

namespace Migrator.Application;

/// <summary>
/// Conservative read-only API over the existing production pipeline.
/// In alpha an unsupported source shape is reported, never silently counted as transformed.
/// </summary>
public sealed class MigrationEngine
{
    public MigrationAnalysis Analyze(string sourcePath, string? configPath = null, CancellationToken cancellationToken = default)
    {
        var scope = ResolveSource(sourcePath);
        var files = Discover(scope);
        var configHash = HashConfig(configPath);
        var beforeHash = HashScope(scope);
        var surveys = new List<FileSurvey>(files.Count);
        var allFindings = new List<MigrationFinding>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = RelativePath(scope, file);
            var findings = new List<MigrationFinding>();
            var names = new List<string>();
            var disposition = "supported";
            var parsedCount = 0;
            var declaredCount = 0;
            try
            {
                var text = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(text, path: file);
                var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                if (errors.Length > 0)
                {
                    disposition = "parse_error";
                    foreach (var diagnostic in errors.Take(10))
                    {
                        var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
                        findings.Add(Finding(relative, line, "SOURCE_PARSE_ERROR", diagnostic.ToString(), "syntax"));
                    }
                }
                else
                {
                    var classes = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>();
                    var fixtures = new HashSet<string>(StringComparer.Ordinal);
                    var hasXunit = false;
                    foreach (var type in classes)
                    {
                        foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
                        {
                            var attrs = method.AttributeLists.SelectMany(list => list.Attributes)
                                .Select(a => a.Name.ToString().Split('.').Last())
                                .Select(a => a.EndsWith("Attribute", StringComparison.Ordinal) ? a[..^9] : a)
                                .ToArray();
                            var nunit = attrs.Any(a => a is "Test" or "TestCase" or "TestCaseSource");
                            var xunit = attrs.Any(a => a is "Fact" or "Theory");
                            if (!nunit && !xunit) continue;
                            declaredCount++;
                            names.Add(type.Identifier.ValueText + "." + method.Identifier.ValueText);
                            fixtures.Add(type.Identifier.ValueText + "@" + type.SpanStart);
                            hasXunit |= xunit;
                        }
                    }

                    if (hasXunit)
                    {
                        disposition = "unsupported_source_framework";
                        findings.Add(Finding(relative, 0, "UNSUPPORTED_SOURCE_FRAMEWORK",
                            "xUnit source [Fact]/[Theory] is not supported by this alpha. The declarations are counted but not converted.", "syntax"));
                    }
                    else if (fixtures.Count > 1)
                    {
                        disposition = "multiple_fixtures";
                        findings.Add(Finding(relative, 0, "MULTIPLE_FIXTURES",
                            "Multiple test classes were discovered in one file. The legacy parser selects one; generation is blocked to avoid silent loss.", "syntax"));
                    }
                    else if (declaredCount == 0)
                    {
                        disposition = "no_test_declarations";
                    }
                    else
                    {
                        try
                        {
                            var parser = new RoslynTestFileParser(LoadConfig(configPath));
                            var model = parser.Parse(file);
                            parsedCount = model.Tests.Count();
                            if (parsedCount != declaredCount)
                            {
                                disposition = "incomplete_parser";
                                findings.Add(Finding(relative, 0, "DECLARATION_COUNT_MISMATCH",
                                    $"Discovered {declaredCount} annotated methods but parser returned {parsedCount}.", "syntax"));
                            }
                        }
                        catch (Exception ex)
                        {
                            disposition = "parser_error";
                            findings.Add(Finding(relative, 0, "PARSER_ERROR", ex.Message, "diagnostic"));
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                disposition = "read_error";
                findings.Add(Finding(relative, 0, "READ_ERROR", ex.Message, "diagnostic"));
            }

            var survey = new FileSurvey(relative, disposition, declaredCount, parsedCount, names, findings);
            surveys.Add(survey);
            allFindings.AddRange(findings);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var afterHash = HashScope(scope);
        if (!string.Equals(beforeHash, afterHash, StringComparison.Ordinal))
            throw new InvalidOperationException("SOURCE_CHANGED_DURING_ANALYSIS: retry from a stable checkout.");

        var count = surveys.Sum(x => x.DeclaredTests);
        var parsed = surveys.Sum(x => x.ParsedTests);
        var status = surveys.Any(x => x.Disposition is not ("supported" or "no_test_declarations"))
            ? "partial" : count == 0 ? "empty" : "complete";
        var id = Digest("migrator-analysis/v1alpha1\n" + scope + "\n" + beforeHash + "\n" + configHash
            + "\n" + (configPath == null ? "unconfigured" : Path.GetFullPath(configPath)));
        return new MigrationAnalysis(ContractVersions.Analysis, id, scope, beforeHash, configHash,
            status, count, parsed, surveys, allFindings,
            new[] { "Discovery is attribute-based and does not expand runtime data cases.",
                    "Lightweight Roslyn project resolution is not a complete MSBuild evaluation.",
                    "Only Selenium C# NUnit to Playwright .NET is in the supported alpha path." });
    }

    public MigrationPreview Preview(
        string sourcePath, string? configPath = null, string? expectedAnalysisId = null,
        CancellationToken cancellationToken = default)
    {
        var before = Analyze(sourcePath, configPath, cancellationToken);
        if (expectedAnalysisId != null && !string.Equals(before.AnalysisId, expectedAnalysisId, StringComparison.Ordinal))
            throw new InvalidOperationException("STALE_ANALYSIS: input/configuration differs from the requested analysis.");
        var config = GenerationPolicy.Apply(LoadConfig(configPath), GenerationPolicy.Conservative);
        var parsed = new List<PipelineResult>();
        var errors = new List<MigrationFinding>();
        foreach (var survey in before.Files.Where(f => f.Disposition == "supported"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = File.Exists(before.SourceScope)
                ? before.SourceScope : Path.Combine(before.SourceScope, survey.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                // Deliberately new per-file: renderer and adapter contain mutable state/caches.
                var pipeline = new MigrationPipeline(
                    new RoslynTestFileParser(config),
                    new PlaywrightDotNetRenderer(),
                    new DefaultProjectAdapter(config));
                parsed.Add(pipeline.ProcessFile(fullPath));
            }
            catch (Exception ex)
            {
                errors.Add(Finding(survey.RelativePath, 0, "GENERATION_ERROR", ex.Message, "diagnostic"));
            }
        }

        var after = Analyze(sourcePath, configPath, cancellationToken);
        if (!string.Equals(before.AnalysisId, after.AnalysisId, StringComparison.Ordinal))
            throw new InvalidOperationException("SOURCE_CHANGED_DURING_PREVIEW: retry with a fresh snapshot.");
        var artifact = TargetArtifact.Create(parsed,
            result => GeneratedNaming.GetPlaywrightFileName(result.SourceModel.ClassName));
        var files = artifact.Files.Select(file =>
        {
            var report = parsed.Single(result => result.SourceModel.FilePath == file.SourceFilePath).Report;
            return new GeneratedFile(
                RelativePath(before.SourceScope, file.SourceFilePath),
                file.RelativePath,
                file.Content,
                file.ContentSha256,
                report.TotalTests,
                report.FullyConvertedTests,
                report.TodoComments);
        }).ToArray();
        var findings = new List<MigrationFinding>(before.Findings);
        findings.AddRange(errors);
        foreach (var result in parsed)
        {
            var relative = RelativePath(before.SourceScope, result.SourceModel.FilePath);
            foreach (var unsupported in result.Report.UnsupportedActions)
                findings.Add(Finding(relative, unsupported.SourceLine, "UNSUPPORTED_ACTION",
                    unsupported.Reason + ": " + unsupported.SourceText, "recognized"));
            if (result.Report.TodoComments > 0)
                findings.Add(Finding(relative, 0, "GENERATED_TODO",
                    $"Generated output includes {result.Report.TodoComments} TODO comments; manual review required.", "generated"));
        }

        var complete = after.DiscoveryStatus == "complete"
            && errors.Count == 0
            && parsed.Sum(x => x.Report.TotalTests) == after.DeclaredTests
            && parsed.All(x => x.Report.TotalTests == x.Report.FullyConvertedTests && x.Report.TodoComments == 0);
        var status = complete ? "complete" : parsed.Count == 0 ? "blocked" : "partial";
        var candidateId = Digest(before.AnalysisId + "\n" + artifact.TargetHash + "\nconservative");
        return new MigrationPreview(ContractVersions.Preview, before.AnalysisId, candidateId,
            artifact.TargetHash, status, files, findings);
    }


    /// <summary>
    /// Resolve a finding against a fresh, matching snapshot. Findings not present in
    /// the analysis can originate from preview, which is evaluated read-only on demand.
    /// No cached finding can silently outlive its source.
    /// </summary>
    public FindingExplanation Explain(
        string sourcePath, string findingId, string? configPath = null,
        string? expectedAnalysisId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(findingId))
            throw new ArgumentException("A finding ID is required.", nameof(findingId));

        var analysis = Analyze(sourcePath, configPath, cancellationToken);
        if (expectedAnalysisId != null
            && !string.Equals(expectedAnalysisId, analysis.AnalysisId, StringComparison.Ordinal))
            throw new InvalidOperationException("STALE_ANALYSIS: source/configuration changed.");

        var finding = analysis.Findings.FirstOrDefault(f => f.Id == findingId);
        string? candidateId = null;
        if (finding == null)
        {
            var preview = Preview(sourcePath, configPath, analysis.AnalysisId, cancellationToken);
            finding = preview.Findings.FirstOrDefault(f => f.Id == findingId);
            candidateId = preview.CandidateId;
        }
        if (finding == null)
            throw new KeyNotFoundException("UNKNOWN_FINDING: the finding is not part of the current source snapshot.");

        var excerpt = new List<SourceExcerptLine>();
        if (finding.Line > 0)
        {
            var file = File.Exists(analysis.SourceScope)
                ? analysis.SourceScope
                : Path.Combine(analysis.SourceScope,
                    finding.SourcePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file))
            {
                var lines = File.ReadAllLines(file);
                var from = Math.Max(1, finding.Line - 2);
                var to = Math.Min(lines.Length, finding.Line + 2);
                for (var line = from; line <= to; line++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var text = lines[line - 1];
                    excerpt.Add(new SourceExcerptLine(line, text.Length <= 320 ? text : text[..320] + "…"));
                }
            }
        }

        // A source read after the snapshot could otherwise attach unrelated evidence.
        var fresh = Analyze(sourcePath, configPath, cancellationToken);
        if (fresh.AnalysisId != analysis.AnalysisId)
            throw new InvalidOperationException("SOURCE_CHANGED_DURING_EXPLANATION: retry with a fresh analysis.");
        return new FindingExplanation(ContractVersions.Explanation, analysis.AnalysisId, finding.Id,
            finding.Code, finding.Message, finding.EvidenceKind, finding.SourcePath,
            finding.Line, excerpt, NextAction(finding.Code), candidateId,
            new[] { "The excerpt is source evidence, not a verified target mapping.",
                    "An AST/syntax match does not establish runtime behavioral equivalence.",
                    "Source position uses legacy line information, not an exact symbol span." });
    }

    static string NextAction(string code) => code switch
    {
        "MULTIPLE_FIXTURES" =>
            "Split fixtures into individually supported source files or extend the parser; never mark the omitted fixture as converted.",
        "UNSUPPORTED_SOURCE_FRAMEWORK" =>
            "Use an explicitly supported NUnit input or implement and test the corresponding source frontend before migration.",
        "DECLARATION_COUNT_MISMATCH" =>
            "Review parser selection and compare the discovered declarations with the parsed model; block full-completion claims.",
        "SOURCE_PARSE_ERROR" => "Resolve the C# parse diagnostics in the source file and rerun analysis.",
        "PARSER_ERROR" => "Inspect the parser diagnostic and relevant project references before attempting generation.",
        "READ_ERROR" => "Check file access and input encoding before retrying.",
        "GENERATION_ERROR" => "Inspect the adapter/renderer diagnostic and candidate configuration; do not invent a replacement.",
        "UNSUPPORTED_ACTION" => "Inspect the invoked helper and its resolved symbol before proposing a project-specific mapping.",
        "GENERATED_TODO" => "Review the TODO and source evidence. A generated comment does not count as migrated behavior.",
        _ => "Inspect the source and supporting diagnostics. Do not assume the conversion is correct."
    };

    static MigrationFinding Finding(string path, int line, string code, string message, string evidence)
        => new(Digest(path + ":" + line + ":" + code + ":" + message)[..20], code, path, line, message, evidence);

    static string ResolveSource(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("Source path is required.");
        var path = Path.GetFullPath(input);
        if (!Directory.Exists(path) && !File.Exists(path))
            throw new FileNotFoundException("Source scope not found.", path);
        if (File.Exists(path) && !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only .cs files are supported as single-file input.");
        return path;
    }

    static IReadOnlyList<string> Discover(string path) =>
        File.Exists(path) ? (InputFixtureDiscoveryPolicy.IsFixtureFile(path) ? new[] { path } : Array.Empty<string>())
        : InputFixtureDiscoveryPolicy.DiscoverFixtureFiles(path);

    static ProjectAdapterConfig LoadConfig(string? path) =>
        string.IsNullOrWhiteSpace(path) ? new ProjectAdapterConfig { SourceProjectName = "Unconfigured" }
        : ConfigValidator.ValidateJson(File.ReadAllText(Path.GetFullPath(path)), Path.GetFullPath(path));

    static string HashConfig(string? path) =>
        string.IsNullOrWhiteSpace(path) ? Digest("unconfigured")
        : DigestBytes(File.ReadAllBytes(Path.GetFullPath(path)));

    static string HashScope(string scope)
    {
        if (File.Exists(scope))
            return ContentTreeHasher.ComputeBytes(new[] { (Path.GetFileName(scope), File.ReadAllBytes(scope)) });
        var files = Directory.GetFiles(scope, "*", SearchOption.AllDirectories)
            .Where(p => new[] { ".cs", ".csproj", ".props", ".targets" }
                .Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => (RelativePath(scope, p), File.ReadAllBytes(p)));
        return ContentTreeHasher.ComputeBytes(files);
    }

    static string RelativePath(string scope, string file) => (File.Exists(scope)
        ? Path.GetFileName(file)
        : Path.GetRelativePath(scope, file)).Replace('\\', '/');

    static string Digest(string text) => DigestBytes(Encoding.UTF8.GetBytes(text));
    static string DigestBytes(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Migrator.Core.Models;

namespace Migrator.Core;

/// <summary>
/// Reads migration artifacts and generates deterministic mapping proposals.
/// Never invents selectors — marks RequiresSourceTruth = true when source truth is needed.
/// </summary>
public sealed class ProposalGenerator
{
    /// <summary>
    /// Input data for proposal generation.
    /// </summary>
    public sealed class ProposalInput
    {
        public MigrationSummaryReport? MigrationReport { get; init; }
        public VerifyReport? VerifyReport { get; init; }
        public IReadOnlyList<UnmappedTargetInfo> UnmappedTargets { get; init; } = Array.Empty<UnmappedTargetInfo>();
        public IReadOnlyList<UnsupportedMethodInfo> UnsupportedActions { get; init; } = Array.Empty<UnsupportedMethodInfo>();
        public ProjectAdapterConfig? ExistingConfig { get; init; }
        public IReadOnlyList<string> GeneratedFiles { get; init; } = Array.Empty<string>();
        public Dictionary<string, string> GeneratedFileContents { get; init; } = new();
    }

    public IReadOnlyList<MappingProposal> Generate(ProposalInput input)
    {
        var proposals = new List<MappingProposal>();
        int proposalIndex = 0;

        // Deduce already-mapped expressions from existing config
        var mappedExpressions = new HashSet<string>(StringComparer.Ordinal);
        if (input.ExistingConfig != null)
        {
            foreach (var ui in input.ExistingConfig.UiTargets)
                mappedExpressions.Add(ui.SourceExpression);
            foreach (var m in input.ExistingConfig.Methods)
                mappedExpressions.Add(m.SourceMethod);
            foreach (var m in input.ExistingConfig.ParameterizedMethods)
                mappedExpressions.Add(m.SourceMethodPattern);
            foreach (var t in input.ExistingConfig.Tables)
                mappedExpressions.Add(t.SourceExpression);
            foreach (var p in input.ExistingConfig.Pagination)
                mappedExpressions.Add(p.SourceExpression);
            // Scoped mappings
            if (input.ExistingConfig.Scopes != null)
            {
                foreach (var scope in input.ExistingConfig.Scopes)
                {
                    foreach (var ui in scope.UiTargets)
                        mappedExpressions.Add(ui.SourceExpression);
                    foreach (var m in scope.Methods)
                        mappedExpressions.Add(m.SourceMethod);
                    foreach (var m in scope.ParameterizedMethods)
                        mappedExpressions.Add(m.SourceMethodPattern);
                    foreach (var t in scope.Tables)
                        mappedExpressions.Add(t.SourceExpression);
                }
            }
        }

        // Phase 4: UiTarget proposals from unmapped targets
        proposals.AddRange(GenerateUiTargetProposals(input, mappedExpressions, ref proposalIndex));

        // Phase 5-6: MethodMapping / ParameterizedMethodMapping proposals
        proposals.AddRange(GenerateMethodProposals(input, mappedExpressions, ref proposalIndex));

        // Phase 8: Table/List proposals
        proposals.AddRange(GenerateTableProposals(input, mappedExpressions, ref proposalIndex));

        // Phase 7: Scope proposals
        proposals.AddRange(GenerateScopeProposals(input, ref proposalIndex));

        // Phase 9: QualityGate proposals from verify report
        proposals.AddRange(GenerateQualityGateProposals(input, ref proposalIndex));

        // Manual migration proposals for clearly unsupported patterns
        proposals.AddRange(GenerateManualMigrationProposals(input, ref proposalIndex));

        // Normalize paths to relative
        NormalizeFilePaths(proposals);

        // Phase 3: Sort by score descending
        return proposals.OrderByDescending(p => p.Score).ToList();
    }

    // --- UiTarget proposals ---

    List<MappingProposal> GenerateUiTargetProposals(ProposalInput input, HashSet<string> mappedExpressions, ref int idx)
    {
        var proposals = new List<MappingProposal>();

        // Group unmapped targets by source expression
        var grouped = input.UnmappedTargets
            .GroupBy(u => u.SourceExpression)
            .ToList();

        foreach (var group in grouped)
        {
            var sourceExpr = group.Key;

            // Skip if already mapped
            if (mappedExpressions.Contains(sourceExpr))
                continue;

            // Skip table/pagination patterns (handled separately)
            if (sourceExpr.Contains(".ElementAt(") || sourceExpr.Contains(".Items.Count") ||
                sourceExpr.Contains(".Pagination."))
                continue;

            // Collect affected files
            var affectedFiles = input.UnmappedTargets
                .Where(u => u.SourceExpression == sourceExpr)
                .Select(u => u.ExampleFile)
                .Distinct()
                .ToList();

            var totalOccurrences = group.Sum(u => u.Usages);
            var score = ComputeScore(totalOccurrences, affectedFiles.Count, isCompileBlocker: false);

            var proposal = new MappingProposal
            {
                Id = $"UIT-{++idx}",
                Kind = ProposalKind.UiTarget,
                Title = $"Add UiTarget mapping for `{sourceExpr}`",
                Priority = PriorityFromScore(score),
                Confidence = totalOccurrences >= 3 ? ProposalConfidence.High : ProposalConfidence.Medium,
                Evidence = $"Source expression `{sourceExpr}` appears {totalOccurrences} time(s) across {affectedFiles.Count} file(s). Not mapped in current adapter config.",
                AffectedFiles = affectedFiles,
                Occurrences = totalOccurrences,
                SuggestedConfigSnippet = BuildUiTargetSnippet(sourceExpr),
                RequiresSourceTruth = true,
                Reason = $"{sourceExpr} is used as a locator target but is not mapped. Each occurrence produces a TODO comment in generated code.",
                Risks = "Applying without source truth will generate invalid selectors. Verify selector via PageObject inspection.",
                NextAction = $"Inspect PageObject classes for `{sourceExpr}`. Search for corresponding WithDataTestId/WithDataTest/WithDataTid. Add UiTarget mapping to the narrowest scope.",
                Score = score
            };

            proposals.Add(proposal);
        }

        return proposals;
    }

    // --- MethodMapping / ParameterizedMethodMapping proposals ---

    List<MappingProposal> GenerateMethodProposals(ProposalInput input, HashSet<string> mappedExpressions, ref int idx)
    {
        var proposals = new List<MappingProposal>();
        if (!input.UnsupportedActions.Any())
            return proposals;

        // Group by base method pattern (receiver.Method)
        var methodGroups = input.UnsupportedActions
            .Select(u => NormalizeMethodSignature(u.MethodOrSourceText))
            .GroupBy(n => n.BaseSignature)
            .ToList();

        foreach (var group in methodGroups)
        {
            var baseSig = group.Key;

            // Skip if already mapped
            if (mappedExpressions.Contains(baseSig))
                continue;

            var allItems = input.UnsupportedActions
                .Where(u => NormalizeMethodSignature(u.MethodOrSourceText).BaseSignature == baseSig)
                .ToList();

            var affectedFiles = allItems.Select(u => u.ExampleFile).Distinct().ToList();
            var totalOccurrences = allItems.Sum(u => u.Count);

            // Check if arguments vary — if so, propose ParameterizedMethodMapping
            var uniqueSignatures = group.Select(n => n.FullSignature).Distinct().ToList();
            var isParameterized = uniqueSignatures.Count > 1;

            var score = ComputeScore(totalOccurrences, affectedFiles.Count, isCompileBlocker: false);

            if (isParameterized)
            {
                var pattern = ExtractParameterizedPattern(uniqueSignatures);
                var proposal = new MappingProposal
                {
                    Id = $"PMT-{++idx}",
                    Kind = ProposalKind.ParameterizedMethodMapping,
                    Title = $"Add ParameterizedMethodMapping for `{pattern}`",
                    Priority = PriorityFromScore(score),
                    Confidence = ProposalConfidence.Medium,
                    Evidence = $"Method pattern `{pattern}` has {uniqueSignatures.Count} distinct argument combinations across {totalOccurrences} invocation(s) in {affectedFiles.Count} file(s).",
                    AffectedFiles = affectedFiles,
                    Occurrences = totalOccurrences,
                    SuggestedConfigSnippet = BuildParameterizedSnippet(pattern),
                    RequiresSourceTruth = true,
                    Reason = "Same helper method is called with different arguments. A single MethodMapping would require multiple duplicate config entries.",
                    Risks = "Parameterized pattern may not cover all argument permutations. Test each variation after applying.",
                    NextAction = $"Confirm helper semantics for `{baseSig}` in source PageObject/helper class. Add ParameterizedMethodMapping with raw placeholders.",
                    Score = score
                };
                proposals.Add(proposal);
            }
            else
            {
                var proposal = new MappingProposal
                {
                    Id = $"MM-{++idx}",
                    Kind = ProposalKind.MethodMapping,
                    Title = $"Add MethodMapping for `{baseSig}`",
                    Priority = PriorityFromScore(score),
                    Confidence = totalOccurrences >= 3 ? ProposalConfidence.High : ProposalConfidence.Medium,
                    Evidence = $"Method `{baseSig}` appears {totalOccurrences} time(s) across {affectedFiles.Count} file(s). Currently produces TODO/manual review comments.",
                    AffectedFiles = affectedFiles,
                    Occurrences = totalOccurrences,
                    SuggestedConfigSnippet = BuildMethodSnippet(baseSig),
                    RequiresSourceTruth = true,
                    Reason = $"Repeated helper call `{baseSig}` currently produces TODO/manual review and adds noise to generated tests.",
                    Risks = "Helper semantics may depend on runtime state. Verify behavior before generating target statements.",
                    NextAction = $"Inspect source PageObject/helper class for `{baseSig}`. Add MethodMapping with target statements.",
                    Score = score
                };
                proposals.Add(proposal);
            }
        }

        return proposals;
    }

    // --- Table/List proposals ---

    List<MappingProposal> GenerateTableProposals(ProposalInput input, HashSet<string> mappedExpressions, ref int idx)
    {
        var proposals = new List<MappingProposal>();

        // Detect ElementAt patterns from unmapped targets
        var elementAtTargets = input.UnmappedTargets
            .Where(u => u.SourceExpression.Contains(".ElementAt("))
            .ToList();

        if (!elementAtTargets.Any())
            return proposals;

        // Group by table base (e.g. "page.Table")
        var tableGroups = elementAtTargets
            .Select(u => ExtractTableBase(u.SourceExpression))
            .Where(tb => tb != null)
            .GroupBy(tb => tb!)
            .ToList();

        foreach (var group in tableGroups)
        {
            var tableBase = group.Key;

            if (mappedExpressions.Contains(tableBase))
                continue;

            var affectedFiles = elementAtTargets
                .Where(u => ExtractTableBase(u.SourceExpression) == tableBase)
                .Select(u => u.ExampleFile)
                .Distinct()
                .ToList();

            var totalOccurrences = elementAtTargets
                .Where(u => ExtractTableBase(u.SourceExpression) == tableBase)
                .Sum(u => u.Usages);

            // Table patterns are compile blockers
            var score = ComputeScore(totalOccurrences, affectedFiles.Count, isCompileBlocker: true);

            var proposal = new MappingProposal
            {
                Id = $"TM-{++idx}",
                Kind = ProposalKind.TableMapping,
                Title = $"Add TableMapping for `{tableBase}`",
                Priority = PriorityFromScore(score),
                Confidence = ProposalConfidence.Medium,
                Evidence = $"Table pattern `{tableBase}.Items.ElementAt(...)` appears {totalOccurrences} time(s) across {affectedFiles.Count} file(s).",
                AffectedFiles = affectedFiles,
                Occurrences = totalOccurrences,
                SuggestedConfigSnippet = BuildTableSnippet(tableBase),
                RequiresSourceTruth = true,
                Reason = $"Unresolved table row access patterns block compile. Each occurrence generates raw Selenium code in output.",
                Risks = "Row selector must match actual HTML. Verify via browser DevTools or PageObject source.",
                NextAction = $"Find source truth for `{tableBase}` row selector. Search for WithDataTest/WithDataTestId in PageObject. Add TableMapping to config.",
                Score = score
            };
            proposals.Add(proposal);
        }

        // Detect pagination patterns
        var paginationTargets = input.UnmappedTargets
            .Where(u => u.SourceExpression.Contains(".Pagination.") && !mappedExpressions.Contains(u.SourceExpression))
            .ToList();

        foreach (var pg in paginationTargets)
        {
            var score = ComputeScore(pg.Usages, 1, isCompileBlocker: true);
            var proposal = new MappingProposal
            {
                Id = $"PM-{++idx}",
                Kind = ProposalKind.PaginationMapping,
                Title = $"Add PaginationMapping for `{pg.SourceExpression}`",
                Priority = PriorityFromScore(score),
                Confidence = ProposalConfidence.Medium,
                Evidence = $"Pagination pattern `{pg.SourceExpression}` appears {pg.Usages} time(s).",
                AffectedFiles = new List<string> { pg.ExampleFile },
                Occurrences = pg.Usages,
                SuggestedConfigSnippet = BuildPaginationSnippet(pg.SourceExpression),
                RequiresSourceTruth = true,
                Reason = "Unresolved pagination access generates raw Selenium code.",
                Risks = "Pagination selectors may vary across page types.",
                NextAction = $"Find source truth for `{pg.SourceExpression}`. Add PaginationMapping to config.",
                Score = score
            };
            proposals.Add(proposal);
        }

        return proposals;
    }

    // --- Scope proposals ---

    List<MappingProposal> GenerateScopeProposals(ProposalInput input, ref int idx)
    {
        var proposals = new List<MappingProposal>();

        if (input.VerifyReport == null || input.VerifyReport.Files == null)
            return proposals;

        // Detect scope-related issues from verify report
        var scopeWarnings = input.VerifyReport.Files
            .Where(f => f.Issues.Any(i => i.Category.Contains("Scope", StringComparison.Ordinal)))
            .ToList();

        if (!scopeWarnings.Any())
            return proposals;

        foreach (var file in scopeWarnings)
        {
            var scopeIssues = file.Issues
                .Where(i => i.Category.Contains("Scope", StringComparison.Ordinal))
                .ToList();

            var score = ComputeScore(scopeIssues.Count, 1, isCompileBlocker: false);
            var fileName = Path.GetFileName(file.SourceFile);

            var proposal = new MappingProposal
            {
                Id = $"SCOPE-{++idx}",
                Kind = ProposalKind.ProfileScope,
                Title = $"Add profile scope for `{fileName}`",
                Priority = PriorityFromScore(score),
                Confidence = ProposalConfidence.Medium,
                Evidence = $"Verify report found {scopeIssues.Count} scope-related issue(s) for `{file.SourceFile}`.",
                AffectedFiles = new List<string> { file.SourceFile },
                Occurrences = scopeIssues.Count,
                SuggestedConfigSnippet = BuildScopeSnippet(fileName),
                RequiresSourceTruth = true,
                Reason = "File may need different route, TestHost, or scoped mappings than the global config.",
                Risks = "Scope too narrow may miss related files. Scope too broad may conflict with other mappings.",
                NextAction = $"Review scope issues for `{file.SourceFile}`. Determine if a dedicated scope with TestHost/route is needed.",
                Score = score
            };
            proposals.Add(proposal);
        }

        return proposals;
    }

    // --- QualityGate proposals ---

    List<MappingProposal> GenerateQualityGateProposals(ProposalInput input, ref int idx)
    {
        var proposals = new List<MappingProposal>();
        if (input.VerifyReport == null)
            return proposals;

        var vr = input.VerifyReport;

        // Placeholder leftovers
        if (vr.PlaceholderLeftovers > 0)
        {
            var score = ComputeScore(vr.PlaceholderLeftovers, vr.GeneratedFilesChecked, isCompileBlocker: true);
            proposals.Add(new MappingProposal
            {
                Id = $"QG-{++idx}",
                Kind = ProposalKind.QualityGate,
                Title = "Enable strict gate for placeholder leftovers",
                Priority = PriorityFromScore(score),
                Confidence = ProposalConfidence.High,
                Evidence = $"Verify report found {vr.PlaceholderLeftovers} placeholder leftover(s) across {vr.GeneratedFilesChecked} generated files.",
                AffectedFiles = vr.Files.Where(f => f.Issues.Any(i => i.Message.Contains("placeholder", StringComparison.OrdinalIgnoreCase)))
                    .Select(f => f.SourceFile).ToList(),
                Occurrences = vr.PlaceholderLeftovers,
                SuggestedConfigSnippet = "{\n  \"QualityGates\": {\n    \"FailOnPlaceholderLeftovers\": true\n  }\n}",
                RequiresSourceTruth = false,
                Reason = "Placeholder leftovers produce invalid or misleading generated code.",
                Risks = "Enabling may block CI until all placeholders are resolved.",
                NextAction = "Review placeholder leftovers in generated files. Fix root cause or enable strict gate in config.",
                Score = score
            });
        }

        // TODO comments
        if (vr.TodoComments > 50)
        {
            var score = ComputeScore(vr.TodoComments, vr.GeneratedFilesChecked, isCompileBlocker: false);
            proposals.Add(new MappingProposal
            {
                Id = $"QG-{++idx}",
                Kind = ProposalKind.QualityGate,
                Title = "Set MaxTodoComments quality gate",
                Priority = PriorityFromScore(score),
                Confidence = ProposalConfidence.High,
                Evidence = $"Verify report found {vr.TodoComments} TODO comment(s) across {vr.GeneratedFilesChecked} generated files.",
                AffectedFiles = vr.Files.Where(f => f.Issues.Any(i => i.Message.Contains("TODO", StringComparison.OrdinalIgnoreCase)))
                    .Select(f => f.SourceFile).ToList(),
                Occurrences = vr.TodoComments,
                SuggestedConfigSnippet = $@"{{
  ""QualityGates"": {{
    ""MaxTodoComments"": {vr.TodoComments}
  }}
}}",
                RequiresSourceTruth = false,
                Reason = "High TODO density indicates incomplete migration. A gate helps track improvement.",
                Risks = "Setting too low will block progress. Use as a soft limit first.",
                NextAction = "Set MaxTodoComments in config to current count, then incrementally reduce as mappings are added.",
                Score = score
            });
        }

        // Syntax errors
        if (vr.SyntaxErrors > 0)
        {
            var score = ComputeScore(vr.SyntaxErrors, 1, isCompileBlocker: true);
            proposals.Add(new MappingProposal
            {
                Id = $"QG-{++idx}",
                Kind = ProposalKind.QualityGate,
                Title = "Fix syntax errors in generated code",
                Priority = ProposalPriority.High,
                Confidence = ProposalConfidence.High,
                Evidence = $"Verify report found {vr.SyntaxErrors} syntax error(s) in generated code.",
                AffectedFiles = vr.Files.Where(f => f.Issues.Any(i => i.Severity == IssueSeverity.Error))
                    .Select(f => f.SourceFile).ToList(),
                Occurrences = vr.SyntaxErrors,
                SuggestedConfigSnippet = null,
                RequiresSourceTruth = false,
                Reason = "Syntax errors prevent compilation. Must be fixed before runtime testing.",
                Risks = null,
                NextAction = "Review syntax errors in verify-report.json. Fix root cause in adapter/renderer or config.",
                Score = score
            });
        }

        return proposals;
    }

    // --- Manual migration proposals ---

    List<MappingProposal> GenerateManualMigrationProposals(ProposalInput input, ref int idx)
    {
        var proposals = new List<MappingProposal>();

        // Detect patterns that clearly need manual intervention
        var manualPatterns = input.UnsupportedActions
            .Where(u =>
                u.MethodOrSourceText.Contains("Navigation.", StringComparison.Ordinal) ||
                u.MethodOrSourceText.Contains(".Open<", StringComparison.Ordinal) ||
                u.MethodOrSourceText.Contains(".ClickAndOpen<", StringComparison.Ordinal))
            .GroupBy(u => ExtractManualPattern(u.MethodOrSourceText))
            .Where(g => g.Key != null)
            .ToList();

        foreach (var group in manualPatterns)
        {
            var pattern = group.Key!;
            var affectedFiles = group.Select(u => u.ExampleFile).Distinct().ToList();
            var totalOccurrences = group.Sum(u => u.Count);
            var score = ComputeScore(totalOccurrences, affectedFiles.Count, isCompileBlocker: false);

            var proposal = new MappingProposal
            {
                Id = $"MANUAL-{++idx}",
                Kind = ProposalKind.ManualMigration,
                Title = $"Manual migration needed for `{pattern}`",
                Priority = PriorityFromScore(score),
                Confidence = ProposalConfidence.Low,
                Evidence = $"Pattern `{pattern}` appears {totalOccurrences} time(s) across {affectedFiles.Count} file(s). Cannot be auto-mapped.",
                AffectedFiles = affectedFiles,
                Occurrences = totalOccurrences,
                SuggestedConfigSnippet = null,
                RequiresSourceTruth = true,
                Reason = $"Pattern `{pattern}` involves navigation/modal/page-object instantiation that cannot be automatically translated.",
                Risks = "Manual migration is error-prone. Review each occurrence carefully.",
                NextAction = $"For each occurrence of `{pattern}`, manually write equivalent Playwright code. Consider extracting to a shared helper method.",
                Score = score
            };
            proposals.Add(proposal);
        }

        return proposals;
    }

    // --- Scoring ---

    int ComputeScore(int occurrences, int affectedFiles, bool isCompileBlocker)
    {
        var score = 0;

        // Occurrences weight
        score += occurrences * 2;

        // Affected files weight
        score += affectedFiles * 5;

        // Compile blocker multiplier
        if (isCompileBlocker)
            score *= 3;

        // High occurrence bonus
        if (occurrences >= 10)
            score += 20;
        else if (occurrences >= 5)
            score += 10;
        else if (occurrences >= 3)
            score += 5;

        return score;
    }

    ProposalPriority PriorityFromScore(int score)
    {
        if (score >= 30) return ProposalPriority.High;
        if (score >= 10) return ProposalPriority.Medium;
        return ProposalPriority.Low;
    }

    // --- Snippet builders ---

    string BuildUiTargetSnippet(string sourceExpr)
    {
        return $@"{{
  ""SourceExpression"": ""{sourceExpr}"",
  ""TargetExpression"": ""<SOURCE_TRUTH_REQUIRED>"",
  ""TargetKind"": ""TestId"",
  ""TestIdAttribute"": ""<SOURCE_TRUTH_REQUIRED>""
}}";
    }

    string BuildMethodSnippet(string sourceMethod)
    {
        return $@"{{
  ""SourceMethod"": ""{sourceMethod}"",
  ""TargetStatements"": [
    ""// SOURCE_TRUTH_REQUIRED""
  ],
  ""RequiresReview"": true
}}";
    }

    string BuildParameterizedSnippet(string pattern)
    {
        return $@"{{
  ""SourceMethodPattern"": ""{pattern}"",
  ""TargetStatements"": [
    ""// SOURCE_TRUTH_REQUIRED""
  ],
  ""RequiresReview"": true
}}";
    }

    string BuildTableSnippet(string tableBase)
    {
        return $@"{{
  ""SourceExpression"": ""{tableBase}"",
  ""RowTarget"": {{
    ""TargetExpression"": ""<SOURCE_TRUTH_REQUIRED>"",
    ""TargetKind"": ""TestId"",
    ""TestIdAttribute"": ""<SOURCE_TRUTH_REQUIRED>""
  }}
}}";
    }

    string BuildPaginationSnippet(string sourceExpr)
    {
        return $@"{{
  ""SourceExpression"": ""{sourceExpr}"",
  ""TargetExpression"": ""<SOURCE_TRUTH_REQUIRED>"",
  ""TargetKind"": ""TestId"",
  ""TestIdAttribute"": ""<SOURCE_TRUTH_REQUIRED>""
}}";
    }

    string BuildScopeSnippet(string fileName)
    {
        var safeName = fileName.Replace("Filter", "");
        return "{\n" +
            $"  \"Name\": \"{safeName}\",\n" +
            $"  \"SourcePathPatterns\": [\"**/{fileName}.cs\"],\n" +
            "  \"TestHost\": {\n" +
            "    \"SetUpStatements\": [\n" +
            "      \"await Page.GotoAsync(\\\"<test-login>\\\");\",\n" +
            "      \"await Page.GotoAsync(\\\"<ROUTE_SOURCE_TRUTH_REQUIRED>\\\");\"\n" +
            "    ]\n" +
            "  }\n" +
            "}";
    }

    // --- Helpers ---

    void NormalizeFilePaths(List<MappingProposal> proposals)
    {
        var allFiles = proposals.SelectMany(p => p.AffectedFiles).ToList();
        if (!allFiles.Any())
            return;

        // Find common root: try input directory first, then fall back to shared prefix
        var root = FindCommonRoot(allFiles);
        if (string.IsNullOrEmpty(root))
            return;

        foreach (var proposal in proposals)
        {
            proposal.AffectedFiles = proposal.AffectedFiles
                .Select(f => ToRelative(f, root))
                .ToList();
        }
    }

    string? FindCommonRoot(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return null;

        var trimmed = paths.Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (trimmed.Count == 0) return null;

        // Start with the first path's directory
        var common = Path.GetDirectoryName(trimmed[0]) ?? trimmed[0];

        foreach (var path in trimmed.Skip(1))
        {
            var dir = Path.GetDirectoryName(path) ?? path;
            // Find common prefix
            while (!dir.StartsWith(common, StringComparison.OrdinalIgnoreCase) && common.Length > 1)
            {
                common = Path.GetDirectoryName(common) ?? "";
            }
        }

        // Ensure root ends with directory separator
        if (!common.EndsWith(Path.DirectorySeparatorChar.ToString()) && !common.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            common += Path.DirectorySeparatorChar;

        return common;
    }

    string ToRelative(string path, string root)
    {
        if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var relative = path.Substring(root.Length);
            // Normalize separators to forward slash for cross-platform readability
            return relative.Replace('\\', '/');
        }
        // If path doesn't share root, just return filename
        return Path.GetFileName(path);
    }

    (string BaseSignature, string FullSignature) NormalizeMethodSignature(string text)
    {
        // Extract receiver.Method from text like "page.Loader.ValidateLoading()" or "page.NameSort.Sort(Ascending)"
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(\w+(?:\.\w+)*?)\.(\w+)\((.*)\)");
        if (match.Success)
        {
            var receiver = match.Groups[1].Value;
            var method = match.Groups[2].Value;
            var baseSig = $"{receiver}.{method}";
            return (baseSig, text);
        }

        // Fallback: treat entire text as signature
        return (text, text);
    }

    string? ExtractParameterizedPattern(IEnumerable<string> signatures)
    {
        if (!signatures.Any()) return null;

        var parts = signatures.Select(s =>
        {
            var match = System.Text.RegularExpressions.Regex.Match(s, @"(.*)\((.*)\)");
            return match.Success ? (match.Groups[1].Value, match.Groups[2].Value) : (s, "");
        }).ToList();

        var basePart = parts.Select(p => p.Item1).Distinct().FirstOrDefault();
        if (basePart == null) return signatures.First();

        var argGroups = parts.Select(p => p.Item2).Distinct().ToList();
        if (argGroups.Count <= 1)
            return $"{basePart}({argGroups.FirstOrDefault() ?? ""})";

        // Detect placeholder pattern
        var firstArgs = argGroups.First().Split(',');
        var placeholders = firstArgs.Select((a, i) => "{" + $"arg{i}" + "}").ToList();
        return $"{basePart}({string.Join(", ", placeholders)})";
    }

    string? ExtractTableBase(string expr)
    {
        var itemsIdx = expr.IndexOf(".Items");
        if (itemsIdx > 0)
            return expr.Substring(0, itemsIdx);
        return null;
    }

    string? ExtractManualPattern(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(\w+(?:\.\w+)*?)\.(\w+(?:<[^>]*>)?)");
        return match.Success ? $"{match.Groups[1].Value}.{match.Groups[2].Value}" : null;
    }
}

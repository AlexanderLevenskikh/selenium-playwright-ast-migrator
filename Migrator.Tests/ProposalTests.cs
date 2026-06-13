using Migrator.Core;
using Migrator.Core.Models;
using Xunit;

namespace Migrator.Tests;

public class ProposalTests
{
    static ProjectAdapterConfig EmptyConfig => new ProjectAdapterConfig(
        SourceProjectName: "Test",
        UiTargets: Array.Empty<UiTargetMapping>(),
        PageObjects: Array.Empty<PageObjectMapping>(),
        Methods: Array.Empty<MethodMapping>()
    );

    static ProjectAdapterConfig ConfigWithMappedUiTarget(string expr) => new ProjectAdapterConfig(
        SourceProjectName: "Test",
        UiTargets: new[] { new UiTargetMapping(expr, "some-target", "TestId") },
        PageObjects: Array.Empty<PageObjectMapping>(),
        Methods: Array.Empty<MethodMapping>()
    );

    // 1. UiTarget proposal from unmapped targets
    [Fact]
    public void Propose_UiTarget_FromUnmappedTargets()
    {
        var generator = new ProposalGenerator();
        var input = new ProposalGenerator.ProposalInput
        {
            MigrationReport = null,
            VerifyReport = null,
            UnmappedTargets = new[]
            {
                new UnmappedTargetInfo("page.ActionsPanel", 8, "DeleteLightBox.cs", 15, ""),
                new UnmappedTargetInfo("page.ActionsPanel", 4, "StopReasons.cs", 22, ""),
            },
            UnsupportedActions = Array.Empty<UnsupportedMethodInfo>(),
            ExistingConfig = EmptyConfig,
        };

        var proposals = generator.Generate(input);

        var uiTargetProposals = proposals.Where(p => p.Kind == ProposalKind.UiTarget).ToList();
        Assert.Single(uiTargetProposals);

        var p = uiTargetProposals[0];
        Assert.Equal("page.ActionsPanel", p.Title.Split("`")[1]);
        Assert.Equal(12, p.Occurrences);
        Assert.Contains("DeleteLightBox.cs", p.AffectedFiles);
        Assert.Contains("StopReasons.cs", p.AffectedFiles);
        Assert.True(p.RequiresSourceTruth);
        Assert.Contains("<SOURCE_TRUTH_REQUIRED>", p.SuggestedConfigSnippet!);
    }

    // 6. Does not invent selectors
    [Fact]
    public void Propose_DoesNotInventSelector()
    {
        var generator = new ProposalGenerator();
        var input = new ProposalGenerator.ProposalInput
        {
            MigrationReport = null,
            VerifyReport = null,
            UnmappedTargets = new[]
            {
                new UnmappedTargetInfo("page.SomeWidget", 5, "Test.cs", 10, ""),
            },
            UnsupportedActions = Array.Empty<UnsupportedMethodInfo>(),
            ExistingConfig = EmptyConfig,
        };

        var proposals = generator.Generate(input);

        foreach (var p in proposals)
        {
            if (p.SuggestedConfigSnippet != null)
            {
                Assert.DoesNotContain("data-test-id='some-widget'", p.SuggestedConfigSnippet);
                Assert.DoesNotContain("GetByTestId(\"some-widget\")", p.SuggestedConfigSnippet);
            }
        }

        var uiProposal = proposals.FirstOrDefault(p => p.Kind == ProposalKind.UiTarget);
        Assert.NotNull(uiProposal);
        Assert.True(uiProposal.RequiresSourceTruth);
    }

    // 7. Skips already mapped UiTarget
    [Fact]
    public void Propose_SkipsAlreadyMappedUiTarget()
    {
        var generator = new ProposalGenerator();
        var config = ConfigWithMappedUiTarget("page.User");

        var input = new ProposalGenerator.ProposalInput
        {
            MigrationReport = null,
            VerifyReport = null,
            UnmappedTargets = new[]
            {
                new UnmappedTargetInfo("page.User", 10, "Test.cs", 10, ""),
                new UnmappedTargetInfo("page.Other", 3, "Test.cs", 20, ""),
            },
            UnsupportedActions = Array.Empty<UnsupportedMethodInfo>(),
            ExistingConfig = config,
        };

        var proposals = generator.Generate(input);

        var uiTargetProposals = proposals.Where(p => p.Kind == ProposalKind.UiTarget).ToList();
        Assert.Single(uiTargetProposals);
        Assert.DoesNotContain("page.User", uiTargetProposals[0].Title);
        Assert.Contains("page.Other", uiTargetProposals[0].Title);
    }

    // 2. MethodMapping from repeated unsupported invocations
    [Fact]
    public void Propose_MethodMapping_FromRepeatedUnsupportedInvocation()
    {
        var generator = new ProposalGenerator();
        var input = new ProposalGenerator.ProposalInput
        {
            MigrationReport = null,
            VerifyReport = null,
            UnmappedTargets = Array.Empty<UnmappedTargetInfo>(),
            UnsupportedActions = new[]
            {
                new UnsupportedMethodInfo("page.Loader.ValidateLoading()", 5, "Test1.cs", 10),
                new UnsupportedMethodInfo("page.Loader.ValidateLoading()", 3, "Test2.cs", 20),
            },
            ExistingConfig = EmptyConfig,
        };

        var proposals = generator.Generate(input);

        var methodProposals = proposals.Where(p => p.Kind == ProposalKind.MethodMapping).ToList();
        Assert.Single(methodProposals);

        var p = methodProposals[0];
        Assert.Contains("ValidateLoading", p.Title);
        Assert.Equal(8, p.Occurrences);
        Assert.True(p.RequiresSourceTruth);
        Assert.Contains("// SOURCE_TRUTH_REQUIRED", p.SuggestedConfigSnippet!);
    }

    // 3. ParameterizedMethodMapping for same method, different args
    [Fact]
    public void Propose_ParameterizedMethodMapping_ForSameMethodDifferentArgs()
    {
        var generator = new ProposalGenerator();
        var input = new ProposalGenerator.ProposalInput
        {
            MigrationReport = null,
            VerifyReport = null,
            UnmappedTargets = Array.Empty<UnmappedTargetInfo>(),
            UnsupportedActions = new[]
            {
                new UnsupportedMethodInfo("page.Principal.InputAndSelect(\"Admin\")", 2, "Test1.cs", 10),
                new UnsupportedMethodInfo("page.Principal.InputAndSelect(\"User\")", 2, "Test2.cs", 20),
                new UnsupportedMethodInfo("page.Principal.InputAndSelect(\"Manager\")", 2, "Test3.cs", 30),
            },
            ExistingConfig = EmptyConfig,
        };

        var proposals = generator.Generate(input);

        var paramProposals = proposals.Where(p => p.Kind == ProposalKind.ParameterizedMethodMapping).ToList();
        Assert.Single(paramProposals);

        var p = paramProposals[0];
        Assert.Contains("InputAndSelect", p.Title);
        Assert.Equal(6, p.Occurrences);
        Assert.True(p.RequiresSourceTruth);
    }

    // 4. TableMapping from ElementAt unresolved pattern
    [Fact]
    public void Propose_TableMapping_FromElementAtUnresolvedPattern()
    {
        var generator = new ProposalGenerator();
        var input = new ProposalGenerator.ProposalInput
        {
            MigrationReport = null,
            VerifyReport = null,
            UnmappedTargets = new[]
            {
                new UnmappedTargetInfo("page.Grid.Items.ElementAt(0)", 3, "Test1.cs", 10, ""),
                new UnmappedTargetInfo("page.Grid.Items.ElementAt(1)", 2, "Test1.cs", 15, ""),
                new UnmappedTargetInfo("page.Grid.Items.ElementAt(0)", 1, "Test2.cs", 20, ""),
            },
            UnsupportedActions = Array.Empty<UnsupportedMethodInfo>(),
            ExistingConfig = EmptyConfig,
        };

        var proposals = generator.Generate(input);

        var tableProposals = proposals.Where(p => p.Kind == ProposalKind.TableMapping).ToList();
        Assert.Single(tableProposals);

        var p = tableProposals[0];
        Assert.Equal(ProposalPriority.High, p.Priority);
        Assert.Contains("page.Grid", p.Title);
        Assert.True(p.RequiresSourceTruth);
        Assert.Contains("RowTarget", p.SuggestedConfigSnippet!);
    }

    // 5. Scope proposal when different files need different TestHost
    [Fact]
    public void Propose_Scope_WhenDifferentFilesNeedDifferentTestHost()
    {
        var generator = new ProposalGenerator();

        var verifyReport = new VerifyReport(
            Status: "failed",
            FilesChecked: 2,
            GeneratedFilesChecked: 2,
            TodoComments: 10,
            PageTodoCalls: 0,
            UnsupportedActions: 0,
            UnmappedTargets: 0,
            RawExpressions: 0,
            SyntaxErrors: 0,
            ScopeWarnings: 2,
            ConfigWarnings: 0,
            PlaceholderLeftovers: 0,
            SuspiciousLiteralVariables: 0,
            DuplicateLocalVariables: 0,
            Files: new[]
            {
                new VerifyFileResult(
                    "BudgetItemsFilter.cs", null, null, "warning",
                    new[]
                    {
                        new VerifyIssue("Scope", IssueSeverity.Warning, "No matching scope found", "BudgetItemsFilter.cs", null),
                    }),
            },
            Issues: Array.Empty<VerifyIssue>()
        );

        var input = new ProposalGenerator.ProposalInput
        {
            MigrationReport = null,
            VerifyReport = verifyReport,
            UnmappedTargets = Array.Empty<UnmappedTargetInfo>(),
            UnsupportedActions = Array.Empty<UnsupportedMethodInfo>(),
            ExistingConfig = EmptyConfig,
        };

        var proposals = generator.Generate(input);

        var scopeProposals = proposals.Where(p => p.Kind == ProposalKind.ProfileScope).ToList();
        Assert.Single(scopeProposals);

        var p = scopeProposals[0];
        Assert.Contains("BudgetItemsFilter", p.Title);
        Assert.True(p.RequiresSourceTruth);
    }

    // 8. Ranks High for repeated compile blocker
    [Fact]
    public void Propose_RanksHighForRepeatedCompileBlocker()
    {
        var generator = new ProposalGenerator();
        var input = new ProposalGenerator.ProposalInput
        {
            MigrationReport = null,
            VerifyReport = null,
            UnmappedTargets = new[]
            {
                new UnmappedTargetInfo("page.Table.Items.ElementAt(0)", 10, "Test1.cs", 10, ""),
                new UnmappedTargetInfo("page.Table.Items.ElementAt(1)", 8, "Test2.cs", 15, ""),
                new UnmappedTargetInfo("page.Table.Items.ElementAt(2)", 5, "Test3.cs", 20, ""),
                new UnmappedTargetInfo("page.Table.Items.ElementAt(0)", 3, "Test4.cs", 25, ""),
                new UnmappedTargetInfo("page.Table.Items.ElementAt(1)", 2, "Test5.cs", 30, ""),
                new UnmappedTargetInfo("page.Table.Items.ElementAt(3)", 1, "Test6.cs", 35, ""),
            },
            UnsupportedActions = Array.Empty<UnsupportedMethodInfo>(),
            ExistingConfig = EmptyConfig,
        };

        var proposals = generator.Generate(input);

        var tableProposals = proposals.Where(p => p.Kind == ProposalKind.TableMapping).ToList();
        Assert.Single(tableProposals);

        var p = tableProposals[0];
        Assert.Equal(ProposalPriority.High, p.Priority);
        Assert.True(p.Score >= 30);
    }

    // 9. Writes Markdown and JSON
    [Fact]
    public void Propose_WritesMarkdownAndJson()
    {
        var generator = new ProposalGenerator();
        var input = new ProposalGenerator.ProposalInput
        {
            MigrationReport = null,
            VerifyReport = null,
            UnmappedTargets = new[]
            {
                new UnmappedTargetInfo("page.Widget", 3, "Test.cs", 10, ""),
            },
            UnsupportedActions = Array.Empty<UnsupportedMethodInfo>(),
            ExistingConfig = EmptyConfig,
        };

        var proposals = generator.Generate(input);

        var json = ProposalWriter.ToJson(proposals);
        Assert.Contains("\"proposals\"", json);
        Assert.Contains("page.Widget", json);
        Assert.Contains("SOURCE_TRUTH_REQUIRED", json);

        var md = ProposalWriter.ToMarkdown(proposals);
        Assert.Contains("# Mapping Proposals", md);
        Assert.Contains("## Summary", md);
        Assert.Contains("## Top proposals", md);
        Assert.Contains("## Proposals by kind", md);
        Assert.Contains("UiTarget", md);
        Assert.Contains("Agent constraints", md);
    }

    // 10. Handles missing optional reports
    [Fact]
    public void Propose_HandlesMissingOptionalReports()
    {
        var generator = new ProposalGenerator();
        var input = new ProposalGenerator.ProposalInput
        {
            MigrationReport = null,
            VerifyReport = null,
            UnmappedTargets = new[]
            {
                new UnmappedTargetInfo("page.ActionsPanel", 3, "Test.cs", 10, ""),
            },
            UnsupportedActions = Array.Empty<UnsupportedMethodInfo>(),
            ExistingConfig = null,
        };

        var proposals = generator.Generate(input);

        Assert.NotEmpty(proposals);
        Assert.Single(proposals.Where(p => p.Kind == ProposalKind.UiTarget));
    }

    // 11. Output contains agent-friendly next actions
    [Fact]
    public void Propose_OutputContainsAgentFriendlyNextActions()
    {
        var generator = new ProposalGenerator();
        var input = new ProposalGenerator.ProposalInput
        {
            MigrationReport = null,
            VerifyReport = null,
            UnmappedTargets = new[]
            {
                new UnmappedTargetInfo("page.SomePanel", 5, "Test.cs", 10, ""),
            },
            UnsupportedActions = Array.Empty<UnsupportedMethodInfo>(),
            ExistingConfig = EmptyConfig,
        };

        var proposals = generator.Generate(input);
        var md = ProposalWriter.ToMarkdown(proposals);

        Assert.Contains("Next action:", md);
        Assert.Contains("Agent constraints", md);
        Assert.Contains("Do not invent selectors", md);
        Assert.Contains("source truth", md);

        var p = proposals.First();
        Assert.NotEmpty(p.NextAction);
        Assert.Contains("PageObject", p.NextAction);
    }
}

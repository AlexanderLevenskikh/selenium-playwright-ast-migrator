using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Migrator.Core;
using Migrator.Core.Models;
using System.Text.Json;

namespace Migrator.SeleniumCSharp;

public class DefaultProjectAdapter : IProjectAdapter
{
    readonly Dictionary<string, MappedTarget> _targetMap = new();
    readonly Dictionary<string, string> _pageObjectMap = new();
    readonly Dictionary<string, string> _methodMap = new();
    readonly Dictionary<string, (string[] Statements, bool RequiresReview)> _methodStatementsMap = new();

    public DefaultProjectAdapter()
    {
    }

    public DefaultProjectAdapter(ProjectAdapterConfig config)
    {
        if (config == null) return;

        foreach (var mapping in config.UiTargets)
        {
            var kind = mapping.TargetKind switch
            {
                "TestId" => TargetKind.PlaywrightLocator,
                "Locator" => TargetKind.PlaywrightLocator,
                "PageObjectProperty" => TargetKind.PageObjectProperty,
                "RawExpression" => TargetKind.RawExpression,
                _ => TargetKind.PlaywrightLocator
            };

            string? testIdAttribute = null;
            if (kind == TargetKind.PlaywrightLocator && mapping.TargetKind == "TestId")
            {
                testIdAttribute = mapping.TestIdAttribute
                    ?? config.LocatorSettings?.DefaultTestIdAttribute;
            }

            _targetMap[mapping.SourceExpression] = new MappedTarget(
                mapping.SourceExpression, mapping.TargetExpression, kind, testIdAttribute);
        }

        foreach (var po in config.PageObjects)
            _pageObjectMap[po.SourceType] = po.VariableName;

        foreach (var m in config.Methods)
        {
            if (m.TargetMethod != null)
                _methodMap[m.SourceMethod] = m.TargetMethod;
            if (m.TargetStatements != null && m.TargetStatements.Length > 0)
                _methodStatementsMap[m.SourceMethod] = (m.TargetStatements, m.RequiresReview);
        }
    }

    public DefaultProjectAdapter(string configPath)
        : this(LoadConfig(configPath))
    {
    }

    static ProjectAdapterConfig LoadConfig(string configPath)
    {
        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<ProjectAdapterConfig>(json);
        if (config == null)
            throw new InvalidOperationException($"Failed to deserialize adapter config from {configPath}");
        return config;
    }

    public TargetExpression ResolveTarget(string sourceExpression)
    {
        if (_targetMap.TryGetValue(sourceExpression, out var target))
            return target;

        foreach (var entry in _targetMap)
        {
            if (sourceExpression.StartsWith(entry.Key + ".", StringComparison.Ordinal) ||
                sourceExpression == entry.Key)
            {
                return entry.Value;
            }
        }

        return new UnresolvedTarget(sourceExpression);
    }

    public string? ResolvePageObjectVariable(string sourceType)
    {
        return _pageObjectMap.GetValueOrDefault(sourceType);
    }

    public string? ResolveMethodTarget(string sourceMethod)
    {
        return _methodMap.GetValueOrDefault(sourceMethod);
    }

    public TestFileModel Adapt(TestFileModel sourceModel)
    {
        var adaptedTests = sourceModel.Tests.Select(AdaptTest).ToList();
        var adaptedSetup = sourceModel.SetUpActions.SelectMany(AdaptAction).ToList();

        return new TestFileModel(
            FilePath: sourceModel.FilePath,
            Namespace: sourceModel.Namespace,
            ClassName: sourceModel.ClassName,
            BaseClassName: sourceModel.BaseClassName,
            SetUpActions: adaptedSetup,
            Tests: adaptedTests
        );
    }

    TestModel AdaptTest(TestModel test)
    {
        var adaptedActions = test.BodyActions.SelectMany(AdaptAction).ToList();

        return new TestModel(
            Name: test.Name,
            Category: test.Category,
            CaseData: test.CaseData,
            Parameters: test.Parameters,
            BodyActions: adaptedActions
        );
    }

    IEnumerable<TestAction> AdaptAction(TestAction action)
    {
        return action switch
        {
            ClickAction click => new[] { new ClickAction(
                click.SourceLine,
                ResolveTarget(click.Target.SourceExpression),
                click.Confidence) },
            SendKeysAction sendKeys => new[] { new SendKeysAction(
                sendKeys.SourceLine,
                ResolveTarget(sendKeys.Target.SourceExpression),
                sendKeys.TextExpression,
                sendKeys.Confidence) },
            PressAction press => new[] { new PressAction(
                press.SourceLine,
                ResolveTarget(press.Target.SourceExpression),
                press.KeyName,
                press.Confidence) },
            TextAssertionAction ta => new[] { new TextAssertionAction(
                ta.SourceLine,
                ResolveTarget(ta.Target.SourceExpression),
                ta.Kind,
                ta.ExpectedValue,
                ta.Confidence) },
            VisibilityAssertionAction va => new[] { new VisibilityAssertionAction(
                va.SourceLine,
                ResolveTarget(va.Target.SourceExpression),
                va.Kind,
                va.Confidence) },
            WaitForAction wa => new[] { new WaitForAction(
                wa.SourceLine,
                ResolveTarget(wa.Target.SourceExpression),
                wa.Confidence) },
            MethodInvocationAction mi => TryResolveMethodMapping(mi),
            RawStatementAction raw => TryResolveRawStatement(raw),
            LocalDeclarationAction lds => TryResolveLocalDeclaration(lds),
            _ => new[] { action }
        };
    }

    IEnumerable<TestAction> TryResolveMethodMapping(MethodInvocationAction mi)
    {
        var fullText = mi.FullSourceText.TrimEnd(';');
        if (_methodStatementsMap.TryGetValue(fullText, out var mapping))
        {
            return new[]
            {
                new MappedMethodInvocationAction(
                    mi.SourceLine,
                    mi.FullSourceText,
                    mapping.Statements,
                    mapping.RequiresReview)
            };
        }

        if (!string.IsNullOrEmpty(mi.MethodName) && _methodStatementsMap.TryGetValue(mi.MethodName, out var methodMapping))
        {
            return new[]
            {
                new MappedMethodInvocationAction(
                    mi.SourceLine,
                    mi.FullSourceText,
                    methodMapping.Statements,
                    methodMapping.RequiresReview)
            };
        }

        return new[] { mi };
    }

    IEnumerable<TestAction> TryResolveRawStatement(RawStatementAction raw)
    {
        var text = raw.SourceText.Trim().TrimEnd(';');

        if (text.StartsWith("var ", StringComparison.Ordinal) && text.Contains('='))
        {
            var eqIndex = text.IndexOf('=');
            var initExpr = text.Substring(eqIndex + 1).Trim();
            if (initExpr.Contains('('))
            {
                if (_methodStatementsMap.TryGetValue(initExpr, out var mapping))
                {
                    return new[]
                    {
                        new MappedMethodInvocationAction(
                            raw.SourceLine,
                            raw.SourceText,
                            mapping.Statements,
                            mapping.RequiresReview)
                    };
                }
            }
        }

        if (_methodStatementsMap.TryGetValue(text, out var stmtMapping))
        {
            return new[]
            {
                new MappedMethodInvocationAction(
                    raw.SourceLine,
                    raw.SourceText,
                    stmtMapping.Statements,
                    stmtMapping.RequiresReview)
            };
        }

        return new[] { raw };
    }

    IEnumerable<TestAction> TryResolveLocalDeclaration(LocalDeclarationAction lds)
    {
        var initExpr = lds.InitializationValue.Trim().TrimEnd(';');
        if (initExpr.Contains('('))
        {
            if (_methodStatementsMap.TryGetValue(initExpr, out var mapping))
            {
                return new[]
                {
                    new MappedMethodInvocationAction(
                        lds.SourceLine,
                        $"{lds.VariableType} {lds.VariableName} = {lds.InitializationValue}",
                        mapping.Statements,
                        mapping.RequiresReview)
                };
            }
        }

        return new[] { lds };
    }
}

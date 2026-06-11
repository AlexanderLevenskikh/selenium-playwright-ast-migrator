using System;
using System.Collections.Generic;
using System.IO;
using Migrator.Core;
using Migrator.Core.Models;
using System.Text.Json;

namespace Migrator.SeleniumCSharp;

/// <summary>
/// Concrete adapter implementation. Pure resolution — no side effects on ResolveTarget.
/// Uses ProjectAdapterConfig (neutral models) to resolve source expressions to target expressions.
/// </summary>
public class DefaultProjectAdapter : IProjectAdapter
{
    readonly Dictionary<string, MappedTarget> _targetMap = new();
    readonly Dictionary<string, string> _pageObjectMap = new();
    readonly Dictionary<string, string> _methodMap = new();

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
            _targetMap[mapping.SourceExpression] = new MappedTarget(
                mapping.SourceExpression, mapping.TargetExpression, kind);
        }

        foreach (var po in config.PageObjects)
            _pageObjectMap[po.SourceType] = po.VariableName;

        foreach (var m in config.Methods)
            _methodMap[m.SourceMethod] = m.TargetMethod;
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

    /// <summary>
    /// Pure resolution — no side effects. Returns MappedTarget if mapping exists,
    /// UnresolvedTarget otherwise.
    /// </summary>
    public TargetExpression ResolveTarget(string sourceExpression)
    {
        if (_targetMap.TryGetValue(sourceExpression, out var target))
            return target;

        // Try prefix matching: "page.User.Click" should match "page.User"
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

    /// <summary>
    /// Apply adapter mappings to a parsed file model, producing a target model
    /// where ClickAction/SendKeysAction carry resolved TargetExpression.
    /// </summary>
    public TestFileModel Adapt(TestFileModel sourceModel)
    {
        var adaptedTests = sourceModel.Tests.Select(AdaptTest).ToList();
        var adaptedSetup = sourceModel.SetUpActions.Select(AdaptAction).ToList();

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
        var adaptedActions = test.BodyActions.Select(AdaptAction).ToList();

        return new TestModel(
            Name: test.Name,
            Category: test.Category,
            CaseData: test.CaseData,
            Parameters: test.Parameters,
            BodyActions: adaptedActions
        );
    }

    TestAction AdaptAction(TestAction action)
    {
        return action switch
        {
            ClickAction click => new ClickAction(
                click.SourceLine,
                ResolveTarget(click.Target.SourceExpression),
                click.Confidence),
            SendKeysAction sendKeys => new SendKeysAction(
                sendKeys.SourceLine,
                ResolveTarget(sendKeys.Target.SourceExpression),
                sendKeys.TextExpression,
                sendKeys.Confidence),
            _ => action
        };
    }
}

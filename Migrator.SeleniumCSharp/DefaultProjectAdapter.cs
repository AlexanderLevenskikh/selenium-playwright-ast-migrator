using Migrator.Core;
using Migrator.Core.Models;
using System.Text.Json;

namespace Migrator.SeleniumCSharp;

/// <summary>
/// Concrete adapter implementation. Uses ProjectAdapterConfig (neutral models) to resolve
/// source expressions to target expressions. Config is loaded from JSON file.
/// </summary>
public class DefaultProjectAdapter : IProjectAdapter
{
    readonly ProjectAdapterConfig? _config;
    readonly Dictionary<string, TargetExpression> _targetCache = new();
    readonly Dictionary<string, string> _pageObjectCache = new();
    readonly Dictionary<string, string> _methodCache = new();
    int _mappedCount;
    int _unmappedCount;

    public DefaultProjectAdapter()
    {
        _config = null;
    }

    public DefaultProjectAdapter(ProjectAdapterConfig config)
    {
        _config = config;
        PreloadMappings();
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

    void PreloadMappings()
    {
        if (_config == null) return;

        foreach (var mapping in _config.UiTargets)
            _targetCache[mapping.SourceExpression] = TargetExpression.Mapped(mapping.SourceExpression, mapping.TargetExpression);

        foreach (var po in _config.PageObjects)
            _pageObjectCache[po.SourceType] = po.VariableName;

        foreach (var m in _config.Methods)
            _methodCache[m.SourceMethod] = m.TargetMethod;
    }

    public TargetExpression ResolveTarget(string sourceExpression)
    {
        if (_config == null)
        {
            _unmappedCount++;
            return TargetExpression.Unmapped(sourceExpression);
        }

        if (_targetCache.TryGetValue(sourceExpression, out var target))
        {
            _mappedCount++;
            return target;
        }

        // Try prefix matching: "page.User.Click" should match "page.User"
        foreach (var entry in _targetCache)
        {
            if (sourceExpression.StartsWith(entry.Key + ".", StringComparison.Ordinal) ||
                sourceExpression == entry.Key)
            {
                _mappedCount++;
                return entry.Value;
            }
        }

        _unmappedCount++;
        return TargetExpression.Unmapped(sourceExpression);
    }

    public string? ResolvePageObjectVariable(string sourceType)
    {
        return _pageObjectCache.GetValueOrDefault(sourceType);
    }

    public string? ResolveMethodTarget(string sourceMethod)
    {
        return _methodCache.GetValueOrDefault(sourceMethod);
    }

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
                click.TargetExpression,
                click.Confidence),
            SendKeysAction sendKeys => new SendKeysAction(
                sendKeys.SourceLine,
                sendKeys.TargetExpression,
                sendKeys.TextExpression,
                sendKeys.Confidence),
            _ => action
        };
    }

    public int MappedTargets => _mappedCount;
    public int UnmappedTargets => _unmappedCount;
}

using System.Text.Json;
using Migrator.Core;
using Migrator.Core.Models;

namespace Migrator.SeleniumCSharp;

public class SeleniumCSharpActionExtractor : IActionExtractor
{
    readonly SeleniumCSharpConfig? _config;

    public SeleniumCSharpActionExtractor()
    {
    }

    public SeleniumCSharpActionExtractor(SeleniumCSharpConfig config)
    {
        _config = config;
    }

    public static SeleniumCSharpConfig LoadConfig(string configPath)
    {
        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<SeleniumCSharpConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Failed to deserialize config");
    }

    public IEnumerable<TestAction> Extract(MethodDeclarationInfo methodInfo)
    {
        var actions = new List<TestAction>();

        foreach (var field in methodInfo.Fields)
        {
            if (IsPageObjectType(field.Type))
            {
                actions.Add(new PageObjectFieldAction(field.Line, field.Name, field.Type));
            }
        }

        return actions;
    }

    bool IsPageObjectType(string type)
    {
        return type.EndsWith("Page") || type.EndsWith("ModalPage") || type.Contains("PageObject");
    }
}

using System.Text.Json;

namespace Migrator.Core;

/// <summary>
/// Validates ProjectAdapterConfig for structural correctness before runtime use.
/// Pure Core logic — no Roslyn, no Selenium, no Playwright dependencies.
/// </summary>
public static class ConfigValidator
{
    static readonly HashSet<string> SupportedTargetKinds = new(StringComparer.Ordinal)
    {
        "TestId",
        "Locator",
        "Text",
        "PageObjectProperty",
        "RawExpression",
        "CssSelector",
        "TestIdBeginning",
        "ClassNameBeginning"
    };

    /// <summary>
    /// Validates the config, throwing ConfigValidationError if any issues found.
    /// </summary>
    public static void Validate(ProjectAdapterConfig config)
    {
        var errors = new List<string>();

        ValidateUiTargets(config.UiTargets, "UiTargets", errors);
        ValidateMethods(config.Methods, "Methods", errors);
        ValidateParameterizedMethods(config.ParameterizedMethods, "ParameterizedMethods", errors);
        ValidateScopes(config.Scopes, errors);
        ValidateQualityGates(config.QualityGates, errors);
        ValidateVerification(config.Verification, errors);
        ValidateTables(config.Tables, "Tables", errors);
        ValidatePagination(config.Pagination, "Pagination", errors);
        ValidateIdentifierList(config.SourceOnlyIdentifiers, "SourceOnlyIdentifiers", errors);
        ValidateIdentifierList(config.TargetKnownTypes, "TargetKnownTypes", errors);
        ValidateIdentifierList(config.TargetKnownIdentifiers, "TargetKnownIdentifiers", errors);

        if (errors.Count > 0)
            throw new ConfigValidationError(errors);
    }

    /// <summary>
    /// Validates the raw JSON string, deserializing and then running structural validation.
    /// Also detects common user errors like using SourceExpression instead of SourceMethod in Methods.
    /// Returns deserialized config if valid, throws ConfigValidationError otherwise.
    /// </summary>
    public static ProjectAdapterConfig ValidateJson(string json, string? configPath = null)
    {
        var config = JsonSerializer.Deserialize<ProjectAdapterConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        if (config == null)
        {
            var path = !string.IsNullOrEmpty(configPath) ? configPath : "adapter-config.json";
            throw new ConfigValidationError(new[]
            {
                $"Failed to deserialize config from {path}. The file may be empty or malformed JSON."
            });
        }

        // Check raw JSON for Methods entries using SourceExpression instead of SourceMethod
        var jsonErrors = DetectMethodsSourceExpressionInJson(json);
        if (jsonErrors.Count > 0)
        {
            throw new ConfigValidationError(jsonErrors);
        }

        Validate(config);
        return config;
    }

    /// <summary>
    /// Detects Methods entries that use "SourceExpression" instead of "SourceMethod" in the raw JSON.
    /// This catches the common user error that deserialization silently ignores.
    /// </summary>
    static List<string> DetectMethodsSourceExpressionInJson(string json)
    {
        var errors = new List<string>();
        var doc = TryParseJsonDocument(json);
        if (doc == null) return errors;

        var root = doc.RootElement;

        // Check top-level Methods
        CheckMethodsSection(root, "Methods", errors);

        // Check Scopes[].Methods
        if (root.TryGetProperty("Scopes", out var scopesElem) && scopesElem.ValueKind == JsonValueKind.Array)
        {
            int si = 0;
            foreach (var scope in scopesElem.EnumerateArray())
            {
                string scopeId;
                if (scope.TryGetProperty("Name", out var nameElem) && nameElem.ValueKind == JsonValueKind.String)
                    scopeId = nameElem.GetString() ?? si.ToString();
                else
                    scopeId = si.ToString();

                if (scope.TryGetProperty("Methods", out var scopeMethods) && scopeMethods.ValueKind == JsonValueKind.Array)
                {
                    int mi = 0;
                    foreach (var method in scopeMethods.EnumerateArray())
                    {
                        if (method.TryGetProperty("SourceExpression", out _) && !method.TryGetProperty("SourceMethod", out _))
                        {
                            var sourceExpr = method.TryGetProperty("SourceExpression", out var val) && val.ValueKind == JsonValueKind.String
                                ? val.GetString() ?? ""
                                : "";
                            errors.Add($"Scopes[{scopeId}].Methods[{mi}] uses SourceExpression, but Methods mappings require SourceMethod." +
                                (string.IsNullOrEmpty(sourceExpr) ? "" : $" Did you mean \"SourceMethod\": \"{sourceExpr}\"?"));
                        }
                        mi++;
                    }
                }
                si++;
            }
        }

        return errors;
    }

    static void CheckMethodsSection(JsonElement root, string sectionName, List<string> errors)
    {
        if (!root.TryGetProperty(sectionName, out var methodsElem) || methodsElem.ValueKind != JsonValueKind.Array)
            return;

        int i = 0;
        foreach (var method in methodsElem.EnumerateArray())
        {
            if (method.TryGetProperty("SourceExpression", out _) && !method.TryGetProperty("SourceMethod", out _))
            {
                var sourceExpr = method.TryGetProperty("SourceExpression", out var val) && val.ValueKind == JsonValueKind.String
                    ? val.GetString() ?? ""
                    : "";
                errors.Add($"{sectionName}[{i}] uses SourceExpression, but Methods mappings require SourceMethod." +
                    (string.IsNullOrEmpty(sourceExpr) ? "" : $" Did you mean \"SourceMethod\": \"{sourceExpr}\"?"));
            }
            i++;
        }
    }

    static JsonDocument? TryParseJsonDocument(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    private static void ValidateUiTargets(UiTargetMapping[] targets, string section, List<string> errors)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            var t = targets[i];
            var prefix = $"{section}[{i}]";

            if (string.IsNullOrEmpty(t.SourceExpression))
                errors.Add($"{prefix} has missing SourceExpression. {section} mappings must use SourceExpression.");

            if (string.IsNullOrEmpty(t.TargetExpression))
                errors.Add($"{prefix} has missing TargetExpression.");

            ValidateTargetKind(t.TargetKind, $"{prefix}.TargetKind", errors);
        }
    }

    private static void ValidateTargetKind(string? targetKind, string path, List<string> errors)
    {
        if (string.IsNullOrEmpty(targetKind))
        {
            errors.Add($"{path} is missing.");
        }
        else if (!SupportedTargetKinds.Contains(targetKind))
        {
            errors.Add($"{path} = \"{targetKind}\" is not supported. Supported values: {string.Join(", ", SupportedTargetKinds)}.");
        }
    }

    private static void ValidateMethods(MethodMapping[] methods, string section, List<string> errors)
    {
        for (int i = 0; i < methods.Length; i++)
        {
            var m = methods[i];
            var prefix = $"{section}[{i}]";

            if (string.IsNullOrEmpty(m.SourceMethod))
            {
                // Check if the user used SourceExpression by mistake — but MethodMapping doesn't have SourceExpression.
                // The deserialized object will have null SourceMethod. We detect this by checking the raw JSON.
                // For now, just report the missing field with a hint.
                errors.Add($"{prefix} has missing SourceMethod. {section} mappings must use SourceMethod, not SourceExpression.");
            }

            if (string.IsNullOrEmpty(m.TargetMethod) && (m.TargetStatements == null || m.TargetStatements.Length == 0))
            {
                if (string.IsNullOrEmpty(m.SourceMethod))
                {
                    // Already reported missing SourceMethod above; skip to avoid double-reporting
                }
                else
                {
                    errors.Add($"{prefix} has no TargetMethod or TargetStatements.");
                }
            }
        }
    }

    private static void ValidateParameterizedMethods(ParameterizedMethodMapping[] methods, string section, List<string> errors)
    {
        for (int i = 0; i < methods.Length; i++)
        {
            var m = methods[i];
            var prefix = $"{section}[{i}]";

            if (string.IsNullOrEmpty(m.SourceMethodPattern))
                errors.Add($"{prefix} has missing SourceMethodPattern.");

            if (m.TargetStatements == null || m.TargetStatements.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(m.TargetExpression))
                    errors.Add($"{prefix} has missing TargetStatements or TargetExpression.");
            }
            else if (!string.IsNullOrWhiteSpace(m.TargetExpression))
            {
                errors.Add($"{prefix} has both TargetStatements and TargetExpression; they are mutually exclusive. Use one or the other.");
            }
        }
    }

    private static void ValidateScopes(ProfileScope[] scopes, List<string> errors)
    {
        for (int i = 0; i < scopes.Length; i++)
        {
            var scope = scopes[i];
            var scopeId = !string.IsNullOrEmpty(scope.Name) ? scope.Name : i.ToString();
            var prefix = $"Scopes[{scopeId}]";

            if (scope.SourcePathPatterns == null || scope.SourcePathPatterns.Length == 0)
                errors.Add($"{prefix} has missing SourcePathPatterns.");

            if (scope.UiTargets.Length > 0)
                ValidateUiTargets(scope.UiTargets, $"{prefix}.UiTargets", errors);

            if (scope.Methods.Length > 0)
                ValidateMethods(scope.Methods, $"{prefix}.Methods", errors);

            if (scope.ParameterizedMethods.Length > 0)
                ValidateParameterizedMethods(scope.ParameterizedMethods, $"{prefix}.ParameterizedMethods", errors);

            if (scope.Tables.Length > 0)
                ValidateTables(scope.Tables, $"{prefix}.Tables", errors);

            if (scope.Pagination.Length > 0)
                ValidatePagination(scope.Pagination, $"{prefix}.Pagination", errors);

            ValidateIdentifierList(scope.TargetKnownTypes, $"{prefix}.TargetKnownTypes", errors);
            ValidateIdentifierList(scope.TargetKnownIdentifiers, $"{prefix}.TargetKnownIdentifiers", errors);
        }
    }

    private static void ValidateQualityGates(QualityGatesConfig? gates, List<string> errors)
    {
        if (gates == null) return;

        if (gates.MaxTodoComments.HasValue && gates.MaxTodoComments.Value < 0)
            errors.Add("QualityGates.MaxTodoComments cannot be negative.");

        if (gates.MaxUnsupportedActions.HasValue && gates.MaxUnsupportedActions.Value < 0)
            errors.Add("QualityGates.MaxUnsupportedActions cannot be negative.");

        if (gates.MaxUnmappedTargets.HasValue && gates.MaxUnmappedTargets.Value < 0)
            errors.Add("QualityGates.MaxUnmappedTargets cannot be negative.");

        if (gates.MaxRawExpressions.HasValue && gates.MaxRawExpressions.Value < 0)
            errors.Add("QualityGates.MaxRawExpressions cannot be negative.");
    }


    private static void ValidateVerification(VerificationConfig? verification, List<string> errors)
    {
        if (verification == null) return;

        if (!string.IsNullOrWhiteSpace(verification.TargetFramework) &&
            !System.Text.RegularExpressions.Regex.IsMatch(verification.TargetFramework.Trim(), @"^net[0-9]+(\.[0-9]+)?([A-Za-z0-9.-]+)?$"))
        {
            errors.Add($"Verification.TargetFramework looks invalid: '{verification.TargetFramework}'. Example: net8.0.");
        }

        if (!string.IsNullOrWhiteSpace(verification.Solution) &&
            !verification.Solution.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Verification.Solution should point to a .sln file.");
        }

        for (var i = 0; i < verification.ProjectReferences.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(verification.ProjectReferences[i]))
                errors.Add($"Verification.ProjectReferences[{i}] is empty.");
            else if (!verification.ProjectReferences[i].EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Verification.ProjectReferences[{i}] should point to a .csproj file.");
        }

        for (var i = 0; i < verification.PackageReferences.Length; i++)
        {
            var package = verification.PackageReferences[i];
            if (string.IsNullOrWhiteSpace(package.Include))
                errors.Add($"Verification.PackageReferences[{i}].Include is missing.");
            if (string.IsNullOrWhiteSpace(package.Version))
                errors.Add($"Verification.PackageReferences[{i}].Version is missing.");
        }

        for (var i = 0; i < verification.AssemblyReferences.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(verification.AssemblyReferences[i]))
                errors.Add($"Verification.AssemblyReferences[{i}] is empty.");
        }
    }

    private static void ValidateIdentifierList(string[] identifiers, string section, List<string> errors)
    {
        for (var i = 0; i < identifiers.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(identifiers[i]))
            {
                errors.Add($"{section}[{i}] is empty.");
                continue;
            }

            var value = identifiers[i].Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^@?[A-Za-z_]\w*$"))
                errors.Add($"{section}[{i}] must be a C# identifier, got '{value}'.");
        }
    }

    private static void ValidateTables(TableConfig[] tables, string section, List<string> errors)
    {
        for (int i = 0; i < tables.Length; i++)
        {
            var t = tables[i];
            var prefix = $"{section}[{i}]";

            if (string.IsNullOrEmpty(t.SourceExpression))
                errors.Add($"{prefix} has missing SourceExpression.");

            if (t.RowTarget == null)
            {
                errors.Add($"{prefix} has missing RowTarget.");
            }
            else
            {
                if (string.IsNullOrEmpty(t.RowTarget.TargetExpression))
                    errors.Add($"{prefix}.RowTarget has missing TargetExpression.");

                ValidateTargetKind(t.RowTarget.TargetKind, $"{prefix}.RowTarget.TargetKind", errors);
            }

            if (t.Pagination?.Forward != null)
            {
                if (string.IsNullOrEmpty(t.Pagination.Forward.TargetExpression))
                    errors.Add($"{prefix}.Pagination.Forward has missing TargetExpression.");

                ValidateTargetKind(t.Pagination.Forward.TargetKind, $"{prefix}.Pagination.Forward.TargetKind", errors);
            }
        }
    }

    private static void ValidatePagination(PaginationConfig[] pagination, string section, List<string> errors)
    {
        for (int i = 0; i < pagination.Length; i++)
        {
            var p = pagination[i];
            var prefix = $"{section}[{i}]";

            if (string.IsNullOrEmpty(p.SourceExpression))
                errors.Add($"{prefix} has missing SourceExpression.");

            if (string.IsNullOrEmpty(p.TargetExpression))
                errors.Add($"{prefix} has missing TargetExpression.");

            ValidateTargetKind(p.TargetKind, $"{prefix}.TargetKind", errors);
        }
    }
}

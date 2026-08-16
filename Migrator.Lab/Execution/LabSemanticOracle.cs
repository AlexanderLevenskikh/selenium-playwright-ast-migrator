using System.Text.Json;
using System.Text.RegularExpressions;
using Migrator.Lab.Contracts;
using Migrator.Lab.LabApp;

namespace Migrator.Lab.Execution;

public static class LabSemanticOracle
{
    public static LabSemanticOracleSummary Evaluate(
        ScenarioSpec scenario,
        LabSourceTestSummary targetTests,
        LabMigrationSummary migration,
        LabProjectVerifySummary projectVerify,
        IReadOnlyList<LabAppObservation> observations)
    {
        var checks = new List<LabSemanticCheck>();
        var issues = new List<string>();
        var expectedTargetTests = ReadInt(scenario.Oracle.Target, "mustPassTests");
        AddCheck(
            "target-test-count",
            expectedTargetTests?.ToString() ?? "<required>",
            $"{targetTests.Passed}/{targetTests.Total}",
            expectedTargetTests.HasValue &&
            targetTests.Passed == expectedTargetTests.Value &&
            targetTests.Total == expectedTargetTests.Value);

        var semantic = scenario.Oracle.Semantic;
        var expectedEvents = ExpectedEvents(scenario);
        var observedEvents = observations.Select(item => item.Event).ToArray();
        if (expectedEvents.Length > 0)
        {
            AddCheck(
                "event-sequence",
                string.Join(" -> ", expectedEvents),
                string.Join(" -> ", observedEvents),
                IsOrderedSubsequence(expectedEvents, observedEvents));
        }

        var timeoutMaxMs = ReadInt(semantic, "timeoutMaxMs");
        if (timeoutMaxMs.HasValue)
        {
            var lastExpectedEvent = expectedEvents.LastOrDefault();
            var firstObservation = observations.FirstOrDefault();
            var terminalObservation = string.IsNullOrWhiteSpace(lastExpectedEvent)
                ? observations.LastOrDefault()
                : observations.LastOrDefault(item => string.Equals(item.Event, lastExpectedEvent, StringComparison.Ordinal));
            var elapsedMs = firstObservation == null || terminalObservation == null
                ? (long?)null
                : Math.Max(0, (long)(terminalObservation.ObservedAtUtc - firstObservation.ObservedAtUtc).TotalMilliseconds);
            AddCheck(
                "semantic-time-budget",
                $"<= {timeoutMaxMs.Value} ms",
                elapsedMs.HasValue ? $"{elapsedMs.Value} ms" : "insufficient observations",
                elapsedMs.HasValue && elapsedMs.Value <= timeoutMaxMs.Value);
        }

        if (TryGetProperty(semantic, "dom", out var domChecks) && domChecks.ValueKind == JsonValueKind.Array)
        {
            var finalDom = observations.LastOrDefault()?.Dom
                ?? new Dictionary<string, LabAppDomElementState>(StringComparer.Ordinal);
            foreach (var item in domChecks.EnumerateArray())
                EvaluateDomCheck(item, finalDom, AddCheck);
        }

        var activeGeneratedLines = ReadGeneratedLines(migration.GeneratedFiles, includeCommentLines: false);
        var activeGenerated = string.Join(Environment.NewLine, activeGeneratedLines);
        var allGenerated = ReadGeneratedSource(migration.GeneratedFiles, includeCommentLines: true);
        var expectedCount = ReadInt(semantic, "count");
        if (expectedCount.HasValue)
        {
            var hasCountAssertion = HasCountAssertion(activeGeneratedLines, expectedCount.Value);
            AddCheck(
                "generated-count-oracle",
                $"ToHaveCountAsync({expectedCount.Value})",
                hasCountAssertion ? "structured count assertion found" : "structured count assertion missing",
                hasCountAssertion);
        }

        var orderedTexts = ReadStringArray(semantic, "orderedTexts");
        if (orderedTexts.Length > 0)
        {
            var matchedTexts = MatchOrderedTextAssertions(activeGeneratedLines, orderedTexts);
            AddCheck(
                "generated-ordered-text",
                string.Join(" -> ", orderedTexts),
                matchedTexts.Length == 0 ? "no structured text assertions found" : string.Join(" -> ", matchedTexts),
                matchedTexts.Length == orderedTexts.Length);
        }

        foreach (var token in ReadStringArray(semantic, "generatedContains"))
        {
            var found = HasGeneratedContainsEvidence(activeGeneratedLines, token);
            AddCheck(
                "generated-contains",
                token,
                found ? "structured active evidence found" : "structured active evidence missing",
                found);
        }

        foreach (var token in ReadStringArray(semantic, "generatedNotContains"))
        {
            AddCheck(
                "generated-not-contains",
                token,
                activeGenerated.Contains(token, StringComparison.Ordinal) ? "found" : "absent",
                !activeGenerated.Contains(token, StringComparison.Ordinal));
        }

        var diagnosticText = string.Join(
            Environment.NewLine,
            new[] { allGenerated }
                .Concat(projectVerify.Diagnostics)
                .Concat(projectVerify.DiagnosticCategories)
                .Concat(migration.Issues));
        var mustContainAny = ReadStringArray(scenario.Oracle.Diagnostics, "mustContainAny");
        if (mustContainAny.Length > 0)
        {
            var matched = mustContainAny.Where(token => diagnosticText.Contains(token, StringComparison.OrdinalIgnoreCase)).ToArray();
            AddCheck("diagnostics-must-contain-any", string.Join(" | ", mustContainAny), string.Join(", ", matched), matched.Length > 0);
        }

        var mustNotContain = ReadStringArray(scenario.Oracle.Diagnostics, "mustNotContain");
        foreach (var token in mustNotContain)
        {
            AddCheck(
                "diagnostics-must-not-contain",
                token,
                diagnosticText.Contains(token, StringComparison.OrdinalIgnoreCase) ? "found" : "absent",
                !diagnosticText.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        var forbiddenDiagnostics = ReadStringArray(semantic, "forbiddenDiagnostics");
        foreach (var token in forbiddenDiagnostics)
        {
            AddCheck(
                "forbidden-diagnostic",
                token,
                diagnosticText.Contains(token, StringComparison.OrdinalIgnoreCase) ? "found" : "absent",
                !diagnosticText.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        var harnessEvidence = ReadString(semantic, "harnessEvidence");
        if (!string.IsNullOrWhiteSpace(harnessEvidence))
            EvaluateHarnessEvidence(harnessEvidence!, scenario, projectVerify, AddCheck);

        if (ReadBool(scenario.Oracle.MustNot, "corruptNeighbourCode") == true)
        {
            var neighbourPreserved = expectedEvents.Length > 0 && IsOrderedSubsequence(expectedEvents, observedEvents);
            AddCheck(
                "unsupported-neighbour-preserved",
                "expected neighbour business event",
                neighbourPreserved ? "observed" : "not observed",
                neighbourPreserved);
        }

        return new LabSemanticOracleSummary
        {
            Passed = issues.Count == 0,
            ExpectedEvents = expectedEvents,
            ObservedEvents = observedEvents,
            Checks = checks.ToArray(),
            Issues = issues.ToArray()
        };

        void AddCheck(string kind, string expected, string actual, bool passed)
        {
            checks.Add(new LabSemanticCheck
            {
                Kind = kind,
                Expected = expected,
                Actual = actual,
                Passed = passed
            });
            if (!passed)
                issues.Add($"Semantic oracle failed ({kind}): expected {expected}; actual {actual}.");
        }
    }


    internal static string[] ExpectedEvents(ScenarioSpec scenario) =>
        ReadStringArray(scenario.Oracle.Semantic, "events");

    static bool HasCountAssertion(IReadOnlyList<string> lines, int expectedCount)
    {
        var pattern = $@"\bToHaveCountAsync\s*\(\s*{expectedCount}\s*\)";
        return lines.Any(line => Regex.IsMatch(line, pattern, RegexOptions.CultureInvariant));
    }

    static string[] MatchOrderedTextAssertions(IReadOnlyList<string> lines, IReadOnlyList<string> expectedTexts)
    {
        var matched = new List<string>();
        var expectedIndex = 0;
        foreach (var line in lines)
        {
            if (expectedIndex >= expectedTexts.Count || !IsTextAssertionLine(line))
                continue;

            var expected = expectedTexts[expectedIndex];
            if (!ContainsCSharpStringLiteral(line, expected))
                continue;

            matched.Add(expected);
            expectedIndex++;
        }
        return matched.ToArray();
    }

    static bool HasGeneratedContainsEvidence(IReadOnlyList<string> lines, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        // Lower-case/plain-text tokens in the current contract represent assertion payloads
        // (for example "beta" or "ready"). Requiring an assertion-bearing line avoids
        // a false PASS when the same literal merely survives in unrelated generated code.
        if (char.IsLower(token[0]) || token.Any(ch => char.IsWhiteSpace(ch)))
            return lines.Any(line => IsAssertionLine(line) && ContainsCSharpStringLiteral(line, token));

        // Identifier-like metadata tokens (TestCaseSource, Retry, Parallelizable, ...) are
        // matched on C# token boundaries instead of raw substring containment.
        var pattern = $@"(?<![A-Za-z0-9_]){Regex.Escape(token)}(?![A-Za-z0-9_])";
        return lines.Any(line => Regex.IsMatch(line, pattern, RegexOptions.CultureInvariant));
    }

    static bool IsTextAssertionLine(string line) =>
        line.Contains("ToHaveTextAsync", StringComparison.Ordinal)
        || line.Contains("ToContainTextAsync", StringComparison.Ordinal);

    static bool IsAssertionLine(string line) =>
        line.Contains("Expect(", StringComparison.Ordinal)
        || line.Contains("Assert.That(", StringComparison.Ordinal)
        || line.Contains(".Should()", StringComparison.Ordinal);

    static bool ContainsCSharpStringLiteral(string line, string value) =>
        line.Contains("\"" + EscapeCSharpString(value) + "\"", StringComparison.Ordinal);

    static string EscapeCSharpString(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    static void EvaluateHarnessEvidence(
        string expected,
        ScenarioSpec scenario,
        LabProjectVerifySummary projectVerify,
        Action<string, string, string, bool> addCheck)
    {
        if (string.Equals(expected, "cpm-isolated", StringComparison.OrdinalIgnoreCase))
        {
            var harness = projectVerify.Harness;
            var passed = projectVerify.ReportPresent
                && string.Equals(projectVerify.Status, "passed", StringComparison.OrdinalIgnoreCase)
                && harness.CentralPackageManagementDetected
                && string.Equals(harness.CentralPackageManagementMode, "isolated", StringComparison.OrdinalIgnoreCase)
                && harness.ManagePackageVersionsCentrallyDisabled
                && harness.DirectoryPackagesPropsPathPinned;
            addCheck(
                "verify-project-harness",
                "CPM detected and isolated with pinned local Directory.Packages.props",
                $"status={projectVerify.Status}, detected={harness.CentralPackageManagementDetected}, mode={harness.CentralPackageManagementMode}, disabled={harness.ManagePackageVersionsCentrallyDisabled}, pinned={harness.DirectoryPackagesPropsPathPinned}",
                passed);
            return;
        }

        if (string.Equals(expected, "transitive-warning-isolated", StringComparison.OrdinalIgnoreCase))
        {
            var expectedProjects = scenario.Project.References
                .Append(scenario.Project.EntryProject)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var observedProjects = projectVerify.ProjectReferences.Select(Path.GetFileName).ToArray();
            var missing = expectedProjects
                .Where(expectedProject => !observedProjects.Contains(expectedProject, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var passed = projectVerify.ReportPresent
                && string.Equals(projectVerify.Status, "passed", StringComparison.OrdinalIgnoreCase)
                && missing.Length == 0;
            addCheck(
                "verify-project-transitive-references",
                string.Join(", ", expectedProjects),
                missing.Length == 0 ? string.Join(", ", observedProjects) : "missing: " + string.Join(", ", missing),
                passed);
        }
    }

    static void EvaluateDomCheck(
        JsonElement item,
        IReadOnlyDictionary<string, LabAppDomElementState> dom,
        Action<string, string, string, bool> addCheck)
    {
        var selector = ReadString(item, "selector") ?? "";
        if (!selector.StartsWith('#') || selector.Length < 2)
        {
            addCheck("dom-selector", selector, "unsupported selector in lab-scenario/v1", false);
            return;
        }

        var id = selector[1..];
        if (!dom.TryGetValue(id, out var state))
        {
            addCheck("dom-element", selector, "missing from final observation", false);
            return;
        }

        var expectedText = ReadString(item, "text");
        if (expectedText != null)
            addCheck("dom-text", $"{selector}={expectedText}", state.Text, string.Equals(state.Text, expectedText, StringComparison.Ordinal));

        var expectedVisible = ReadBool(item, "visible");
        if (expectedVisible.HasValue)
            addCheck("dom-visible", $"{selector}={expectedVisible.Value}", state.Visible.ToString(), state.Visible == expectedVisible.Value);

        var expectedValue = ReadString(item, "value");
        if (expectedValue != null)
            addCheck("dom-value", $"{selector}={expectedValue}", state.Value, string.Equals(state.Value, expectedValue, StringComparison.Ordinal));

        var expectedEnabled = ReadBool(item, "enabled");
        if (expectedEnabled.HasValue)
            addCheck("dom-enabled", $"{selector}={expectedEnabled.Value}", state.Enabled.ToString(), state.Enabled == expectedEnabled.Value);

        var expectedChecked = ReadBool(item, "checked");
        if (expectedChecked.HasValue)
            addCheck("dom-checked", $"{selector}={expectedChecked.Value}", state.Checked.ToString(), state.Checked == expectedChecked.Value);
    }

    static string ReadGeneratedSource(IEnumerable<string> files, bool includeCommentLines) =>
        string.Join(Environment.NewLine, ReadGeneratedLines(files, includeCommentLines));

    static string[] ReadGeneratedLines(IEnumerable<string> files, bool includeCommentLines)
    {
        var lines = new List<string>();
        foreach (var file in files.Where(File.Exists))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (!includeCommentLines && line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;
                lines.Add(line);
            }
        }
        return lines.ToArray();
    }

    static bool IsOrderedSubsequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var expectedIndex = 0;
        foreach (var item in actual)
        {
            if (expectedIndex < expected.Count && string.Equals(expected[expectedIndex], item, StringComparison.Ordinal))
                expectedIndex++;
        }
        return expectedIndex == expected.Count;
    }

    static int? ReadInt(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    static bool? ReadBool(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ? value.GetBoolean()
            : null;

    static string? ReadString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static string[] ReadStringArray(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }

    static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}

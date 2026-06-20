# Method Mapping: Exact vs Template

## Overview

Adapter config supports two approaches for mapping project-specific helpers:

1. **Exact `MethodMapping`** — maps a specific call to generated statements.
2. **Template `MethodMapping`** — maps a method name pattern with argument substitution.


## Placeholder mental model

Treat profile mappings as two layers:

```text
UiTargets translate nouns.
Methods / ParameterizedMethods translate verbs.
```

`{source}` is the old Selenium receiver. `{TARGET}` is the resolved Playwright expression for that receiver. Prefer `{TARGET}` in active target statements so a single behavior mapping can apply to many mapped source objects.

See [Placeholder mental model: nouns and verbs](placeholder-mental-model.md).

## Priority Rule

```
Exact MethodMapping > Template MethodMapping > Generic fallback/TODO
```

If an exact match exists for the full source call, it takes priority over any template.

## When to Use Exact Mapping

Use exact `MethodMapping` for:

- Rare one-off helpers (1-2 occurrences across test files).
- Helpers with complex semantics (conditional logic, branching, retry).
- Helpers that require project-specific behavior.

Example:

```json
{
  "SourceMethod": "page.UserInput.InputTextAndSelectValue(\"Test User\")",
  "TargetStatements": [
    "await Page.Locator(\"[data-test-id='t_widget_searchfilter']\").FillAsync(\"Test User\");",
    "await Page.Locator(\"[data-test-id='t_widget_searchfilter']\").PressAsync(\"Enter\");"
  ],
  "RequiresReview": true
}
```

## When to Use Template Mapping

Template mapping is available in the config schema but **deferred for this codebase** based on helper frequency analysis.

Decision rule: template mapping warranted only when:

1. Helper appears **3+ times** across files.
2. Call signature is stable (same method name, same argument types).
3. Receiver can be resolved through `UiTargets`.
4. Arguments can be safely substituted (string literals, variable names).

Template example (schema reserved):

```json
{
  "MethodTemplates": [
    {
      "SourceMethodName": "InputTextAndSelectValue",
      "ReceiverTarget": true,
      "ArgumentNames": ["value"],
      "TargetStatements": [
        "await {target}.FillAsync({value});",
        "await {target}.PressAsync(\"Enter\");"
      ],
      "RequiresReview": true
    }
  ]
}
```

Where `{target}` resolves to the Playwright locator for the receiver, and `{value}` substitutes the first argument.

## Why Template System Is Deferred

Wide helper frequency analysis across 4 test fixture files (16 tests, 62 actions) found:

| Helper | Occurrences | Template candidate? |
|---|---:|---|
| `ValidateLoading` | 10 | No — complex semantics (conditional loader check) |
| `OpenRegistryAgentPage` | 3 | No — navigation, not element interaction |
| All other helpers | 1-2 | No — exact mapping sufficient |

No helper meets all 5 criteria. Exact mappings remain the appropriate approach.

## Why Project-Specific Names Live in Config

Helper method names are project-specific conventions. Encoding them in Core/Roslyn/Renderer would couple the migrator to a specific project. The config-driven approach keeps the pipeline generic.

## Guidance

- **Exact mapping** for rare or complex helpers.
- **Template mapping** only when a helper repeats 3+ times with stable, simple semantics.
- **Never** encode project-specific helper names in recognizers.
- **Always** keep unmapped helpers as TODO comments — never silently drop actions.

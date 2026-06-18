# TICKET-1: generic navigation declaration should be parsed as MethodInvocationAction

## Status

Implemented in parser-level MVP.

## Symptom

A source declaration like this was parsed as `RawStatementAction`:

```csharp
var productChoosingPage = Browser.GoToPage<DiscountsProductChoosingPage>(DiscountsProductChoosingPage.Uri);
```

Because the declaration variable name is project-specific (`productChoosingPage`) and not in the small `MeaningfulVariableNames` allow-list, `TryExtractLocalDeclaration` returned `null`, and the statement fell back to `RawStatementAction`.

When rendered later, `Browser` could be blocked by `SourceOnlyIdentifiers`, so config mappings never had a chance to apply.

## Expected model

Use `MethodInvocationAction`, not `NavigationAction`:

- receiver: `Browser`
- method: `GoToPage`
- source text: `Browser.GoToPage<DiscountsProductChoosingPage>(DiscountsProductChoosingPage.Uri)`
- arguments: `DiscountsProductChoosingPage.Uri`

This keeps the action generic and lets `ParameterizedMethods` resolve it from adapter config.

## Implemented behavior

`RoslynTestFileParser` now recognizes generic invocation initializers in local declarations for these methods:

- `GoToPage`
- `GoToPageWithUserAccessRight`
- `OpenPage`
- `WaitForPage`
- `Click`
- `ClickAndFollow`
- `ClickAndOpen`

The existing `Navigation.OpenPage<T>(...)` special case still runs first, so existing `NavigationAction` behavior is preserved for that old pattern.

## Regression tests

Added parser/adapter tests:

- `Parse_GenericInvocationLocalDeclaration_ProducesMethodInvocationAction`
- `ParameterizedMethods_MapGenericInvocationLocalDeclaration`


## Follow-up: placeholder matching with nested comma arguments

Parameterized mappings also need to match complex invocation arguments, for example:

```csharp
var productChoosingPage = Browser.GoToPage<DiscountsProductChoosingPage>(Uri(productId, tariff.TariffId));
```

The old `TryMatchPattern` placeholder regex used `[^,)]+`, so `{url}` stopped at the first comma or closing parenthesis and did not match arguments like `Uri(productId, tariff.TariffId)`.

`DefaultProjectAdapter.TryMatchPattern` now builds the regex from the pattern token-by-token:

- intermediate placeholders use non-greedy `.*?`;
- the final placeholder uses greedy `.*`, so it can capture everything up to the escaped pattern suffix, including commas and nested parentheses.

Additional regression test:

- `ParameterizedMethods_MapGenericInvocationLocalDeclaration_WithNestedCommaArgument`

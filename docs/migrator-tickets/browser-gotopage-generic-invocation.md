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


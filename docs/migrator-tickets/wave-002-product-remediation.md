# Wave-002 product remediation

## Status

Implemented in the migrator engine with regression coverage.

This ticket addresses the reusable product gaps found while investigating wave-002. It deliberately does not hardcode project selectors or helper semantics: those remain adapter-profile responsibilities backed by source/POM evidence.

## Engine fixes

### Recognizer aliases are now effective

All `RecognizerAliases` families are passed to their recognizers:

- `InputMethods`;
- `SelectMethods`;
- `NavigationMethods`;
- `FluentAssertionMethods`.

A project can therefore classify `ComboBox.Enter(value)` as an input action by adding `Enter` to `InputMethods`. `Enter` is not a global default because projects use that name for different behaviors.

### Resolved receiverless project helpers remain mappable

Calls such as `CheckForbiddenInformer()` are preserved as `MethodInvocationAction` even when Roslyn resolves the local helper. The adapter can now apply `Methods`, `ParameterizedMethods`, suppression, or scaffold policy instead of receiving an early `UnsupportedAction`.

`System.*` and `Microsoft.*` receiverless calls remain excluded from this project-helper path.

### Awaited tuple deconstruction preserves the result binding

The parser now preserves declarations such as:

```csharp
var (_, actual) = await LoadAsync();
```

as a structured invocation with:

- `ResultVariable = "(_, actual)"`;
- `IsAwaited = true`;
- normal arguments and generic type arguments.

Mapped .NET output keeps tuple deconstruction. TypeScript target statements use array destructuring and convert C# discards to omitted slots:

```typescript
const [, actual] = await targetApi.load();
```

The declared variables are registered for downstream actions, preventing cascade TODOs such as unresolved `actual` assertions.

### Receiver-qualified generic `Methods` signatures are supported

An exact configuration entry such as:

```json
{
  "SourceMethod": "Browser.GoToPage<T>(uri)",
  "TargetStatements": [
    "var {result} = await TargetNavigation.GoToPageAsync<{T}>(Page, {uri});"
  ],
  "RequiresReview": false
}
```

now matches `Browser.GoToPage<MyPage>(uri)` by receiver, method name, generic arity, and parameter arity. Named parameters, generic placeholders, and `{result}` are substituted without turning the mapping into a broad global `GoToPage` alias.

### Safe FluentAssertions data checks lower to the target framework

The .NET renderer lowers a conservative subset of FluentAssertions checks over resolved data variables:

- `Be` / `NotBe`;
- `BeTrue` / `BeFalse`;
- `BeNull` / `NotBeNull`;
- `BeEmpty` / `NotBeEmpty`.

The output follows `TestHost.TargetTestFramework` (`nunit` or `xunit`). UI/POM-like receivers and unresolved symbols stay on the manual-review path.

## Focused test-run follow-up

The first focused run exposed three adjacent reliability issues, all now fixed:

- fixture discovery now prefers an explicit `[TestFixture]`, then a class that actually owns `[Test]`/`[TestCase]` methods, so colocated DTO/helper classes do not cause `No test class found`;
- resolved `System.*`/`Microsoft.*` calls that are intentionally ignored by the invocation extractor stay ignored instead of being reintroduced as `UnsupportedAction` by the statement fallback;
- `SendKeysAction.TextExpression` is treated as source code. Quoted literals keep their quotes, while variables/member expressions such as `value` or `model.Name` remain expressions in `FillAsync(...)`.

## Remaining adapter-profile work

These items cannot be safely invented by the engine:

1. `SaveButton` needs a source-backed `UiTargets` entry with the real Playwright locator.
2. `ComboBox.Enter` needs both `RecognizerAliases.InputMethods: ["Enter"]` and a source-backed mapping for the ComboBox receiver.
3. `CheckForbiddenInformer()` needs a `Methods`/`ParameterizedMethods` target statement that preserves the helper's real assertion semantics.
4. `Browser.GoToPage<T>` needs a target-side navigation/POM-construction statement appropriate for the destination project.

The runtime migrator reads the actual adapter config passed to the CLI. Proposal files under a migration workspace, such as `config-merge/config-targets.json`, do not become active configuration automatically.

## Regression coverage

`Migrator.Tests/Wave002ProductRemediationTests.cs` covers:

- all recognizer alias families;
- configured `ComboBox.Enter` input lowering;
- resolved receiverless helper mapping;
- awaited tuple deconstruction and downstream data assertions;
- NUnit/xUnit assertion selection and UI safety;
- TypeScript destructuring output;
- receiver-qualified generic `Methods` matching and placeholder substitution.

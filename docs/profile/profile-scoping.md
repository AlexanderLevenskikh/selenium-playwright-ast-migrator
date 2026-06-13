# Profile Scoping

## Problem

A single `adapter-config.json` may be used to process multiple source files from different test suites. Each suite may need different:

- **TestHost**: different base class, namespace, usings, or SetUp statements.
- **UiTargets**: different locators per page.
- **Methods**: different method mappings per page.

Without scoping, global config applies to all files — leading to wrong routes, wrong test hosts, and incorrect locators.

## Solution

`Scopes` in `adapter-config.json` allow per-file configuration overrides.

## Config Shape

```json
{
  "SourceProjectName": "Example.E2ETests",
  "TestHost": {
    "Namespace": "Example.E2ETests.Tests",
    "BaseClass": "PageTest"
  },
  "Scopes": [
    {
      "Name": "CatalogPrincipals",
      "SourcePathPatterns": ["**/CatalogPrincipalsFilter.cs"],
      "TestHost": {
        "Namespace": "Example.E2ETests.Tests",
        "BaseClass": "TestBase",
        "ClassAttributes": ["TestFixture", "Parallelizable(ParallelScope.Self)"],
        "Usings": ["NUnit.Framework", "Example.E2ETests.Infrastructure"],
        "SetUpStatements": [
          "await Page.GotoAsync(\"<test-login>\");",
          "await Page.GotoAsync(\"/catalogs?activeTab=principals\");"
        ]
      }
    }
  ]
}
```

## Scope Selection Rules

1. **`Scopes` is optional**: Configs without `Scopes` behave exactly as before (global config applies to all files).
2. **Global config is the base**: All global `UiTargets`, `Methods`, and `TestHost` are the default.
3. **One matching scope overrides**: When a source file matches a scope's `SourcePathPatterns`, the scope's `TestHost` replaces the global `TestHost`. Scope-specific `UiTargets` and `Methods` override global entries for the same `SourceExpression` / `SourceMethod` keys.
4. **Multiple matching scopes**: If two or more scopes match the same file, the **first scope in config order** is selected. A warning is emitted to stderr.
5. **`ParameterizedMethods` are additive**: Scope-specific parameterized methods extend (not replace) global parameterized methods.

## Path Pattern Matching

`SourcePathPatterns` supports:

- **Exact filename**: `"Widget.cs"` — matches any file named `Widget.cs`.
- **Suffix match**: `"**/CatalogPrincipalsFilter.cs"` — matches any path ending with `CatalogPrincipalsFilter.cs`.
- **Full path**: `"tests/Functional/ButtonTests.cs"` — matches the exact normalized path (case-insensitive).

## Report Output

When a scope is applied, the report includes the active scope name:

```json
{
  "sourceFile": ".../CatalogPrincipalsFilter.cs",
  "activeScope": "CatalogPrincipals"
}
```

If no scope matches or `Scopes` is not defined, `activeScope` is null.

## Multiple Scopes Warning

If multiple scopes match:

```
Warning: multiple profile scopes matched source file 'CatalogPrincipalsFilter.cs': CatalogPrincipals, AnotherScope
```

The first scope in the array order is used deterministically.

## Migration from Global Config

To migrate from a global `TestHost` to scoped:

1. Move the `TestHost` block from the root level into a `Scopes` entry.
2. Set `SourcePathPatterns` to match the relevant source files.
3. Keep global `UiTargets` and `Methods` that are shared across files.
4. Add scope-specific `UiTargets` and `Methods` as needed.

## Known Limitations

- **No inheritance tree**: Scope does not inherit from other scopes. Only global → one scope merge is supported.
- **No complex globbing**: `**` only supports suffix match, not `**/Tests/**/*.cs` full glob.
- **No per-test scoping**: Scoping is per-file, not per-test method within a file.

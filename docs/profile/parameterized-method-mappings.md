# Parameterized Method Mappings

## Problem

Exact method mappings (`SourceMethod`) require one entry per argument variation. When a helper method like `Sort(sortOrder)` is called with different arguments (e.g., `"По возрастанию"`, `"По убыванию"`, or a variable `sortOrder`), you need separate config entries for each. This is verbose and doesn't generalize.

## Solution

`ParameterizedMethodMapping` allows pattern-based matching with `{placeholderName}` syntax. Placeholder substitution is quote-aware: it distinguishes between placeholders inside C# string literals and raw C# expressions.


## Mental model: nouns and verbs

Use `UiTargets` to translate source objects ("nouns") and `ParameterizedMethods` to translate repeated actions ("verbs"). This lets one behavior mapping apply to many mapped objects instead of writing N x M concrete rules.

In `TargetStatements`, `{source}` is the old Selenium receiver expression, while `{TARGET}` is the resolved Playwright target expression for that receiver. Prefer `{TARGET}` for active generated code.

See [Placeholder mental model: nouns and verbs](placeholder-mental-model.md).

## Config Shape

```json
{
  "ParameterizedMethods": [
    {
      "SourceMethodPattern": "page.NameSort.Sort({sortOrder})",
      "TargetStatements": [
        "await Page.Locator(\"[data-test-id='t_principals_principal']\").ClickAsync();",
        "await Page.Locator($\"span:has-text('{sortOrder}')\").WaitForAsync();",
        "await Page.Locator($\"span:has-text('{sortOrder}')\").ClickAsync();"
      ],
      "RequiresReview": true,
      "Description": "Parameterized SortBox.Sort: matches any sortOrder argument"
    }
  ]
}
```

## Placeholder Rules

The adapter detects whether a `{placeholder}` in `TargetStatements` appears inside a C# string literal or as a raw expression.

### `{arg}` outside a C# string literal

Replaced with the raw source argument expression (preserves quotes if the source is a string literal).

```
Config:    "await popup.Locator(\"input\").FillAsync({value});"
Source:    page.Principal.InputAndSelect("ABC")
Output:    await popup.Locator("input").FillAsync("ABC");

Source:    page.Principal.InputAndSelect(name)
Output:    await popup.Locator("input").FillAsync(name);
```

### `{arg}` inside a C# string literal

Two sub-cases:

**Source argument is a string literal** — quotes are stripped, content inserted directly:

```
Config:    "await Page.Locator(\"span:has-text('{sortOrder}')\").ClickAsync();"
Source:    page.NameSort.Sort("asc")
Output:    await Page.Locator("span:has-text('asc')").ClickAsync();
```

**Source argument is a variable/expression** — the string literal is converted to an interpolated string:

```
Config:    "await Page.Locator(\"span:has-text('{sortOrder}')\").ClickAsync();"
Source:    page.NameSort.Sort(sortOrder)
Output:    await Page.Locator($"span:has-text('{sortOrder}')").ClickAsync();
```

## Placeholder Syntax

- Use `{placeholderName}` in `SourceMethodPattern` to match method arguments.
- Use the same `{placeholderName}` in `TargetStatements` to substitute the matched value.
- The regex extracts argument text from the source invocation (e.g., `"По возрастанию"` from `page.NameSort.Sort("По возрастанию")`).

## Priority Order

1. **Exact `SourceMethod` match** — highest priority. If a `MethodMapping` exists for the exact invocation text, it is used.
2. **Parameterized `SourceMethodPattern` match** — falls back here if no exact match.
3. **Generic recognizers / TODO fallback** — if no pattern matches, the original action is preserved (renders as `// TODO`).

## Examples

### Variable argument, raw placeholder
```csharp
// Source: page.Principal.InputAndSelect(principalName);
// Config: "await popup.Locator(\"input\").FillAsync({value});"
// Output: await popup.Locator("input").FillAsync(principalName);
```

### String literal argument, raw placeholder
```csharp
// Source: page.Principal.InputAndSelect("ABC");
// Config: "await popup.Locator(\"input\").FillAsync({value});"
// Output: await popup.Locator("input").FillAsync("ABC");
```

### Selector string with variable argument
```csharp
// Source: page.NameSort.Sort(sortOrder);
// Config: "await Page.Locator($\"span:has-text('{sortOrder}')\").ClickAsync();"
// Output: await Page.Locator($"span:has-text('{sortOrder}')").ClickAsync();
```

### Selector string with string literal argument
```csharp
// Source: page.NameSort.Sort("asc");
// Config: "await Page.Locator(\"span:has-text('{sortOrder}')\").ClickAsync();"
// Output: await Page.Locator("span:has-text('asc')").ClickAsync();
```

## Recommendation

Prefer raw placeholders for method arguments:

```json
"await popup.Locator(\"input\").FillAsync({value});"
```

Use quoted placeholders (inside a string literal) only when you intentionally need to build string content like selector expressions:

```json
"await Page.Locator(\"span:has-text('{sortOrder}')\").ClickAsync();"
```

## Known Limitations

- **No type checking**: Placeholders are replaced based on text matching. No validation of argument types.
- **No nested expressions**: `{placeholder}` is the only supported syntax. No `${expr}` or `{{name}}` variants.
- **Single placeholder per argument position**: `[^,)]+` regex matches up to the next comma or closing paren.
- **Invalid patterns produce warnings**: Malformed patterns (e.g., unclosed braces) log a warning and fall through to TODO.
- **String literal parsing is naive**: Verbatim strings (`@""`) and escaped quotes (`""`) inside strings are handled, but complex multi-line or verbatim interpolated strings may not parse correctly.

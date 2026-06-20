# Placeholder mental model: nouns and verbs

Adapter config is easiest to reason about when it is split into two independent layers:

```text
N source objects  -> N UiTargets
M source methods  -> M behavior mappings

Together they cover N x M concrete usages.
```

The goal is to avoid writing one mapping per concrete source call. Instead, map **objects** once and map **actions** once.

## Short version

```text
UiTargets translate nouns.
Methods / ParameterizedMethods translate verbs.
```

Example:

```text
page.SortBox      -> noun  -> Page.GetByTestId("sort-box")
.Sort(direction)  -> verb  -> open dropdown + TODO select option
```

That is why `{source}` and `{TARGET}` are intentionally different placeholders.

## `{source}` vs `{TARGET}`

### `{source}`

`{source}` is the matched Selenium/source receiver expression.

Example source call:

```csharp
page.SortBox.Sort(SortDirection.Asc);
```

For this pattern:

```json
{
  "SourceMethodPattern": "{source}.Sort({direction})"
}
```

The captured values are:

```text
{source}    = page.SortBox
{direction} = SortDirection.Asc
```

`{source}` is source-world evidence. It may reference old Selenium PageObjects such as `page`, `pagef`, `modal`, `lightbox`, or `WebDriver`. Those identifiers usually do **not** exist in the target Playwright project.

Do not use `{source}` in active generated code unless you are certain it is valid target code.

### `{TARGET}`

`{TARGET}` is the already-resolved Playwright target expression for `{source}`.

It comes from config such as `UiTargets`, table/list mappings, or another target-resolution mechanism.

Example:

```json
{
  "UiTargets": [
    {
      "Source": "page.SortBox",
      "Target": "Page.GetByTestId(\"sort-box\")"
    }
  ]
}
```

Now `{TARGET}` for `page.SortBox` is:

```csharp
Page.GetByTestId("sort-box")
```

This is safe to use in active Playwright code.

## Why this matters

Without the two-layer model, a profile tends to grow as N x M rules:

```text
page.SortBox.Sort(...)
page.Filter.Sort(...)
page.Table.Sort(...)
modal.SortBox.Sort(...)
lightbox.SortBox.Sort(...)
```

With the two-layer model, the profile contains:

1. one `UiTargets` entry per source object;
2. one method mapping per repeated behavior.

That keeps the profile smaller, easier to review, and easier for an agent to extend.

## Example: SortBox.Sort

Source:

```csharp
page.SortBox.Sort(SortDirection.Asc);
```

Object mapping:

```json
{
  "UiTargets": [
    {
      "Source": "page.SortBox",
      "Target": "Page.GetByTestId(\"sort-box\")"
    }
  ]
}
```

Behavior mapping:

```json
{
  "ParameterizedMethods": [
    {
      "SourceMethodPattern": "{source}.Sort({direction})",
      "TargetStatements": [
        "await {TARGET}.ClickAsync();",
        "// TODO: Select sort option {direction} — Playwright needs a project-specific sort option locator"
      ],
      "Description": "SortBox.Sort — open sort dropdown; exact option selection requires project-specific locator",
      "RequiresReview": true
    }
  ]
}
```

Generated output:

```csharp
await Page.GetByTestId("sort-box").ClickAsync();
// TODO: Select sort option SortDirection.Asc — Playwright needs a project-specific sort option locator
```

The mapping is intentionally partial: it safely opens the target sort control and leaves the exact option selection as a reviewable TODO.

## Placeholder rules of thumb

Prefer `{TARGET}` in active target statements:

```json
"await {TARGET}.ClickAsync();"
```

Avoid `{source}` in active target statements:

```json
"await {source}.ClickAsync();"
```

The second form may generate non-compilable code such as:

```csharp
await page.SortBox.ClickAsync();
```

Use `{source}` mostly for comments, diagnostics, or rare cases where the source expression is also valid target code.

Use named argument placeholders for readability:

```json
"SourceMethodPattern": "{source}.Sort({direction})"
```

Prefer this over anonymous `*` when the argument is reused in `TargetStatements`.

## Agent guidance

When adding a new method mapping, the agent should ask:

1. What is the source receiver? (`{source}`)
2. Is that receiver mapped to a target expression? (`{TARGET}`)
3. What behavior is being translated? (method/action)
4. Which arguments need named placeholders?
5. Is the generated statement safe active code, or should it remain `RequiresReview`?

If `{TARGET}` cannot be resolved, do not force active code. Add a `UiTargets` mapping first, create a POM recovery note, or leave a TODO.

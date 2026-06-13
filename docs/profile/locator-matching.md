# Locator Matching & Strategy Rules

This document explains how to configure target mappings and select the right locator strategy
in Migrator adapter config.

## Target Kinds

Each target mapping has a `TargetKind` that determines how the locator is rendered in Playwright.

| TargetKind | Generated Code | Use When |
|---|---|---|
| `TestId` | `Page.GetByTestId("value")` | Element has `data-testid` attribute |
| `TestIdAttribute` | `Page.Locator("[data-test-id='value']")` | Element has `data-test-id` (non-standard) |
| `Locator` | `Page.Locator("value")` | CSS or Playwright selector string |
| `Text` | `Page.GetByText("value")` | Select by visible text (headers, labels, buttons) |
| `RawExpression` | Literal `value` | Fallback: any arbitrary C# expression |

## Match Strategy

The `Match` field on `UiTargetMapping` controls element selection when multiple elements match
the same locator.

| Match | Index | Generated Suffix | Use When |
|---|---|---|---|
| (null/empty) | — | none | Single element match, or first by default |
| `"First"` | — | `.First` | Explicitly select first matching element |
| `"Nth"` | required | `.Nth(index)` | Select Nth matching element (0-based) |

The suffix is appended to any rendered locator:

```csharp
// TestId + Match: First
Page.GetByTestId("row").First

// TestId + Match: Nth, Index: 2
Page.GetByTestId("row").Nth(2)

// Text + Match: First
Page.GetByText("Наименование").First

// TestIdAttribute + Match: Nth, Index: 0
Page.Locator("[data-test-id='row']").Nth(0)
```

## When to Use Each Approach

### TestId + Match (Recommended)

Use when the page has stable test IDs and you need element selection:

```json
{
  "SourceExpression": "page.Name",
  "TargetExpression": "t_principals_name",
  "TargetKind": "TestId",
  "Match": "First"
}
```

### Text Target

Use for elements without test IDs that can be selected by visible text:

```json
{
  "SourceExpression": "page.NameHeader",
  "TargetExpression": "Наименование",
  "TargetKind": "Text"
}
```

Combine with Match when multiple elements share the same text:

```json
{
  "SourceExpression": "page.SortButton",
  "TargetExpression": "Sort",
  "TargetKind": "Text",
  "Match": "First"
}
```

### Locator

Use for CSS or complex Playwright selectors:

```json
{
  "SourceExpression": "page.ApplyButton",
  "TargetExpression": "[data-test='apply-button']",
  "TargetKind": "Locator"
}
```

### RawExpression (Fallback only)

Use only when no other strategy works. RawExpression outputs the value literally:

```json
{
  "SourceExpression": "page.Table.Items",
  "TargetExpression": "Page.Locator(\"[data-test='row']\")",
  "TargetKind": "RawExpression"
}
```

**Prefer Match strategy over RawExpression** whenever possible. RawExpression bypasses the
renderer's locator logic and cannot benefit from Match, TestIdAttribute, or other rendering
features.

## Common Patterns

### Column header without test ID

```json
{
  "SourceExpression": "page.ColumnHeader",
  "TargetExpression": "Наименование",
  "TargetKind": "Text"
}
```

### Row in a table (need specific index)

```json
{
  "SourceExpression": "page.Table.Rows",
  "TargetExpression": "row",
  "TargetKind": "TestId",
  "Match": "Nth",
  "Index": 2
}
```

### First matching element

```json
{
  "SourceExpression": "page.NameCell",
  "TargetExpression": "t_principals_name",
  "TargetKind": "TestId",
  "Match": "First"
}
```

## Known Limitations

- **Match strategy does not work with RawExpression** — RawExpression bypasses the renderer.
- **Row-level sub-selectors** (`.Text`, `.Sum` on table cells) are not yet supported. These
  require understanding of nested locator chains.
- **Parameterized selectors** — locators that depend on method arguments (e.g., `Sort(sortOrder)`)
  are not supported. Requires templated method mapping.
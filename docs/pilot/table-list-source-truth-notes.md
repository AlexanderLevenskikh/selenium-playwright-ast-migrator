# Table/List Source Truth Notes

**Date:** 2026-06-13
**Purpose:** Document proven selectors for Table/List MVP from source PageObjects.

---

## Table Selectors

| Source expression | Selector | Attribute | Source file/class | Notes |
|---|---|---|---|---|
| `page.Table` (container) | `table` | `data-test` | `PageObjects/*Page.cs` (40+ files) | `controlFactory.CreateControl<TableItem>(webDriver.Search(x => x.WithDataTest("table")))` |
| `page.Table.Items.ElementAt(N)` (row) | `t_table_row_item` | `data-test` | `MyControls/TableItem.cs` | `WithDataTest("t_table_row_item").FixedByIndex()` |
| `page.Table.Items.ElementAt(N).Text.Get()` | `t_table_row_item` | `data-test` | `MyControls/TableItem.cs` | Row text via Label control |
| `page.Table.Items.ElementAt(N).Click()` | `t_table_row_item` | `data-test` | `MyControls/TableItem.cs` | Row click |
| `page.Table.Items.ElementAt(N).ClickAndOpen<T>()` | `t_table_row_item` | `data-test` | `MyControls/TableItem.cs` | Row click + navigate |
| `page.Table.Items.ElementAt(N).ItemsLink` | `t_table_row_item` | `data-test` | `MyControls/TableItem.cs` | Same selector, different element type (Link) |
| `page.Table.Items.ElementAt(N).Sum.Get()` | `t_table_row_item` | `data-test` | `MyControls/TableItem.cs` | Row text, parsed as number |
| `page.Table.Items.Count.Get()` | `t_table_row_item` | `data-test` | `MyControls/TableItem.cs` | Count of row elements |

## Pagination Selectors

| Source expression | Selector | Attribute | Source file/class | Notes |
|---|---|---|---|---|
| `page.Pagination.Forward` | `Paging__forwardLink` | `data-tid` | `MyControls/PaginationItem.cs` | `WithTid("Paging__forwardLink")` |
| `page.Pagination.Items.ElementAt(N)` | `Paging__pageLink` | `data-tid` | `MyControls/PaginationItem.cs` | Page link elements |
| `page.Pagination` (container) | `table-paging` | `data-test` | `PageObjects/*Page.cs` | Pagination container |

## Playwright Reference (existing project)

The ArBilling Playwright project already uses these patterns in hand-written tests:

```csharp
// Row access
Page.Locator("[data-test='t_table_row_item']").Nth(2)
Page.Locator("[data-test='t_table_row_item']").First
await Page.Locator("[data-test='t_table_row_item']").CountAsync()

// Row assertions
await Expect(Page.Locator("[data-test='t_table_row_item']").Nth(2)).ToContainTextAsync(text);
await Expect(Page.Locator("[data-test='t_table_row_item']").First).ToBeVisibleAsync();
```

**No Playwright Pagination helper exists** — pagination is not implemented in the target project yet.

## Selector Resolution

| Attribute | CSS selector | Playwright Locator |
|---|---|---|
| `data-test="t_table_row_item"` | `[data-test='t_table_row_item']` | `Page.Locator("[data-test='t_table_row_item']")` |
| `data-test="table"` | `[data-test='table']` | `Page.Locator("[data-test='table']")` |
| `data-tid="Paging__forwardLink"` | `[data-tid='Paging__forwardLink']` | `Page.Locator("[data-tid='Paging__forwardLink']")` |

## Canonical Pattern: ElementAt(N).Text.Get().Should().Be/Contain(...)

This is the dominant pattern (100+ usages in 20+ files):

```
page.Table.Items.ElementAt(0).Text.Get().Should().Be("value")
page.Table.Items.ElementAt(2).Text.Get().Should().Contain("0004")
```

Target:

```csharp
await Assertions.Expect(Page.Locator("[data-test='t_table_row_item']").Nth(0)).ToHaveTextAsync("value");
await Assertions.Expect(Page.Locator("[data-test='t_table_row_item']").Nth(2)).ToContainTextAsync("0004");
```

## Canonical Pattern: Pagination Forward

```
var value = page.Table.Items.ElementAt(1).Text.Get();
page.Pagination.Forward.Click();
page.Table.Items.ElementAt(1).Text.Get().Should().NotBe(value);
```

Target:

```csharp
var value = await Page.Locator("[data-test='t_table_row_item']").Nth(1).TextContentAsync();
await Page.Locator("[data-tid='Paging__forwardLink']").ClickAsync();
// loader wait...
await Assertions.Expect(Page.Locator("[data-test='t_table_row_item']").Nth(1)).Not.ToHaveTextAsync(value);
```

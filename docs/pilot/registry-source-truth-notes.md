# Registry Source Truth Notes

All selectors verified against PageObject source files in `Example.E2ETests`.

## UiTargets

| SourceExpression | Old selector | Verified selector | Attribute | Source file/class | Notes |
|---|---|---|---|---|---|
| `page.Sc` | `t_reports_sc` | `t_dropdownMenu_reports_sc` | `data-test-id` | `RegistryAgentPage:18-19` + runtime proof | Container is `t_reports_sc` + `PopupMenu__caption`, but dropdown click target is `t_dropdownMenu_reports_sc` |
| `page.SalesAmount` | `t_reports_salesAmount` | `t_reports_salesAmount` | `data-test-id` | `RegistryAgentPage:57` | SortBox, confirmed correct |
| `page.Table.Items.ElementAt(2)` | `table-row` | `t_table_row_item` | `data-test` | `TableItem:21-22` | Confirmed correct |
| `page.Table.Items.ElementAt(4)` | `table-row` | `t_table_row_item` | `data-test` | `TableItem:21-22` | Confirmed correct |
| `page.ReportsSubtotalSalesAmount` | `subtotal-sales` | `t_reports_subTotal_salesAmount` | `data-test-id` | `RegistryAgentPage:51` | Confirmed correct |
| `page.ReportsSubtotalAddCalcAmount` | `subtotal-addcalc` | `t_reports_subTotal_addCalcAmount` | `data-test-id` | `RegistryAgentPage:53` | Confirmed correct |
| `page.ReportsSubtotalReward` | `subtotal-reward` | `t_reports_subTotal_reward` | `data-test-id` | `RegistryAgentPage:52` | Confirmed correct |
| `page.TableLoader` | `table-loader` | `table-loader` | `data-test` | `Loader:27` | `loadingMain`, confirmed correct |

## Method Mappings

### `page.Sc.InputAndSelect(value)` — ScCombobox:102-109

Source semantics:
1. `Open()` → `Click()` on container (dropdown)
2. `input.Visible.Wait()` — `//*[contains(@data-test,'headbox-search')]` inner input
3. `input.ClearAndInputText(value)`
4. `MenuItems.Wait().Single(x => x.Text, Contains.Substring(value)).Click()` — `data-test="menu-item"`
5. `acceptButton.Click()` — `data-test="apply-button"`

Generated statements use `t_dropdownMenu_reports_sc` for initial click.

### `page.Sc.ExcludeValue(value)` — ScCombobox:131-139

Source semantics:
1. `Click()` on container (dropdown)
2. `acceptButton.Visible().Wait()` — `data-test="apply-button"`
3. `input.ClearAndInputText(value)`
4. `MenuItems.Wait().Single(...).Click()` — `data-test="menu-item"`
5. `exclude.Click()` — `span[text()='Исключить из результатов']`
6. `acceptButton.Click()` — `data-test="apply-button"`

### `page.Sc.SortSc(value)` — ScCombobox:141-148

Source semantics:
1. `Click()` on container (dropdown)
2. `sortButton.Click()` — `span[text()='СОРТИРОВКА']`
3. Find `span[contains(., value)]`, wait visible, click

### `page.Sc.ClearSort()` — ScCombobox:163-168

Source semantics:
1. `Open()` → `Click()` on container (dropdown)
2. `clearSort.Visible().Wait()` — `data-test="label-reset"`
3. `clearSort.Click()`

### `page.SalesAmount.Sort(value)` — SortBox:24-29

Source semantics:
1. `Click()` on container — `t_reports_salesAmount`
2. Find `span[contains(., value)]`, wait visible, click

Note: SortBox has `sortAsc` (`По возрастанию`) and `sortDesc` (`По убыванию`) as internal elements, but the `Sort(string value)` method uses dynamic XPath with the passed value.

### `page.Loader.ValidateLoading()` / `pagef.Loader.ValidateLoading()` — Loader:39-63

Source semantics: waits for `loadingMain` (`data-test="table-loader"`) and `loadingText` (`Загрузка`) to become hidden. Retry loop with reload on error page.

Generated statement is simplified — only checks `table-loader` hidden. The retry/reload logic is omitted (test infrastructure concern).

## Unresolved / Approximate

| Element | Status | Notes |
|---|---|---|
| `Navigation.OpenRegistryAgentPage()` | Approximate | Uses `TestSettings.LoginRoute` + route; actual runtime URL needed |
| `page.Sc.ExcludeValue` — `exclude` button | Verified | XPath by text `Исключить из результатов`, no `data-test-*` |
| `page.Sc.SortSc` — sort option | Verified | XPath by text, no `data-test-*` |
| `page.SalesAmount.Sort` — sort option | Verified | XPath by text, no `data-test-*` |
| `Loader.ValidateLoading` | Simplified | Retry/reload loop omitted; core wait is correct |

## Key Decision: `t_dropdownMenu_reports_sc`

The `page.Sc` container in `RegistryAgentPage` is initialized with `WithDataTestId("t_reports_sc").WithTid("PopupMenu__caption")`. However, runtime proof showed that clicking `t_reports_sc` does not reliably open the dropdown. The correct click target is `t_dropdownMenu_reports_sc`. This is consistent with `Region` which already uses `t_dropdownMenu_reports_region` (`RegistryAgentPage:54`).

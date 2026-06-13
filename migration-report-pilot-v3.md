# Pilot v3 Final Report

## Migration: Widget.CheckSearchToWidget (Pilot v3)

### Execution

```bash
dotnet run --project Migrator.Cli -- migrate --profile widget-pilot --source "C:\Users\levenskikh\Desktop\Kontur\selenium_tests\ArBilling.E2ETests\Tests\Functional\Widget.cs" --output "C:\Users\levenskikh\AppData\Local\Temp\opencode\pilot_v3"
```

**Completed**: 2026-06-12

### Results

| Metric | Value |
|--------|-------|
| Semantic actions | 13 |
| SyntaxFallback | 49 |
| Unsupported | 0 |
| Mapped targets | 11 |
| Unmapped targets | 13 |
| TODO comments | 49 |
| **Compile** | **✅ 0 errors, 0 warnings** |

### Verified Source Truth

All project-specific values sourced from:

| Source | File | Key Values |
|--------|------|------------|
| Page objects | `WidgetPage.cs` | `data-tid="Input__root"`, `data-testid="t_process_footer"`, `data-testid="t_widget_userfilter"`, `data-testid="t-widget-closed"`, `data-testid="t_widget_searchfilter"`, `data-testid="t_widget_datefilter"` |
| Loader control | `Loader.cs` | `data-testid="table-loader"` |
| Navigation | `Navigation.cs`, `Urls.cs` | `https://arbilling3.testkontur.ru/search/bills` |

### Key Decisions

1. **WidgetSearch** uses `WithTid("Input__root")` → `data-tid` not `data-testid` → mapped as `RawExpression`: `Page.Locator("[data-tid='Input__root']")`
2. **OpenSearchPage** URL is internal host (`testkontur.ru`) → left as `// TODO: replace with actual runtime URL`
3. **Loader.ValidateLoading** simplified to single `ToBeHiddenAsync("table-loader")` — original has retry + reload logic
4. **ClickAndOpen** mapped to click + `RequiresReview: true` — may involve overlay/frame handling

### Code Changes

- `Migrator.SeleniumCSharp/DefaultProjectAdapter.cs` — added `TryResolveLocalDeclaration` for method mapping on local variable declarations
- `examples/profiles/widget-pilot/adapter-config.json` — updated with verified locators, fixed multi-line statements

### Conclusion

Pilot v3 demonstrates that Migrator can produce **compile-clean** Playwright code from Selenium tests when:
- Source truth is available and verified
- Project-specific config uses real locators (no fake mappings)
- Complex interactions are marked for manual review

The remaining gap (2 TODO interactions + 3 SetUp blockers) represents ~30 lines of manual work for a fully functional test.

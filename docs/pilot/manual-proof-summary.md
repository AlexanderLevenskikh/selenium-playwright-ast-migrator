# Manual Proof Summary

Pilot migration of a real Selenium test to Playwright .NET, with manual review of one generated test method.

## Selected test

`CheckSearchToWidget()` — search + Enter key + footer text verification.

Chosen because: 0 unmapped targets, 0 unsupported actions, remaining TODOs are from known generic gaps.

## Report consistency

Fresh migrate run (both text and JSON) — CLI summary and `report.json` match exactly:

| Metric | CLI | JSON |
|---|---|---|
| MappedTargets | 7 | 7 |
| UnmappedTargets | 0 | 0 |
| UnsupportedActions | 0 | 0 |
| TodoComments | 20 | 20 |

No inconsistency between report formats.

## Manual edits — classification

| # | Original (generated TODO) | Manual replacement | Category | Future recognizer? |
|---|---|---|---|---|
| 1 | `[ValidateLoading] page.Loader.ValidateLoading()` | Comment: project-specific loader wait | project-specific helper | No |
| 2 | `[WaitPresence] page.FuterUser.WaitPresence()` | `await Page.GetByTestId("...").WaitForAsync();` | missing generic recognizer | Yes — `WaitPresenceRecognizer` |
| 3 | `[NotBeEmpty] page.FuterUser.Text.Get().Should().NotBeEmpty()` | `InnerTextAsync()` + `Should().NotBeNullOrEmpty()` | missing generic recognizer / assertion translation | Yes — `FluentAssertionsTextRecognizer` |

## Remaining TODOs in the fixed test

1 remaining TODO: `ValidateLoading` — project-specific, no known Playwright equivalent, unknown locator.

## Near-compileable verdict

The `CheckSearchToWidget` method body is syntactically valid C#. Uses standard Playwright APIs (`FillAsync`, `PressAsync`, `WaitForAsync`, `InnerTextAsync`) and FluentAssertions (already in the source project's dependencies).

The overall class won't compile because SetUp navigation is unresolved (`Navigation.OpenSearchPage()`, `ClickAndOpen<WidgetPage>()`).

## Manual effort summary

- **Lines changed**: 6 (2 action lines cleaned, 3 TODOs resolved, 1 project-specific TODO left)
- **Time estimate**: ~10 min for a developer familiar with both codebases
- **Key blocker**: SetUp navigation helpers have no mapped translation

## Top future generic recognizer candidates

| # | Recognizer | Patterns | Impact |
|---|---|---|---|
| 1 | `FluentAssertionsTextRecognizer` | `X.Text.Get().Should().Be(...)`, `X.Text.Get().Should().NotBeEmpty()` | ~8 TODOs in Widget file, ~6 in ButtonTests |
| 2 | `VisibilityWaitRecognizer` | `X.Visible.Wait().EqualTo(true/false)` | ~4 in Widget, ~14 in ButtonTests |
| 3 | `WaitPresenceRecognizer` | `X.WaitPresence()` | ~2 in Widget, structural in others |

## Project-specific helpers (stay manual / profile-based)

1. `ValidateLoading` — custom loader visibility check
2. `InputTextAndSelectValue` — custom dropdown fill + select combo
3. `ManualInputValue` — custom date picker day/month/year setter
4. `ClickAndOpen<TPage>` — custom navigation + page object transition
5. `Navigation.Open*()` — custom setup helpers

## Conclusion

**Can the generated test body be brought to near-compileable without changing Core/Roslyn/Renderer?**

**Partial yes.** The test body is near-compileable with 2 lines of manual code. The full test cannot run because SetUp navigation has no mapped translation.

Both blockers require either adapter config entries for navigation, or project-specific setup that the migrator cannot infer.

# Transformation Rule Inventory — Selenium → Playwright

Phase A baseline, built from implementation reading (commit `734990c`). Machine-readable
version: `artifacts/baseline/transformation-rules.json` (32 rules). This page is the
human-readable companion.

Classification legend (Phase A, section 5 of the task):

| Class | Meaning |
|---|---|
| **A** | deterministic + safe |
| **B** | deterministic after explicit configuration |
| **C** | deterministic skeleton possible, semantic residue remains |
| **D** | requires project-wide contextual analysis |
| **E** | genuinely requires human/agent reasoning |

## Inventory summary

| Rule | Source construct | Target construct | SEMANTIC info used | Phase A | Semantic state |
|---|---|---|---|---|---|
| LOC-01 | `WebDriver.FindElement(By.Id("x"))` inline | `Page.Locator("#x")` | syntax | B | ProjectSpecific |
| LOC-02 | `FindElement(By.CssSelector("x"))` | `Page.Locator("x")` | syntax | B | ProjectSpecific |
| LOC-03 | `FindElement(By.XPath("x"))` | `Page.Locator("xpath=x")` (xpath kept, not CSS) | syntax | A | Known |
| LOC-04 | interpolated `By.Id($"..{v}")` etc. | `Page.Locator($"#..{v}")` | syntax | C | PartiallyKnown |
| LOC-05 | stored locator local / `By` alias | re-used locator; `local[i]`/`ElementAt(i)` → `.Nth(i)` | syntax + in-method data-flow | C | PartiallyKnown |
| LOC-06 | `FindElements(...)` used as collection | same single `Page.Locator`; Nth only when indexed | syntax | D | Unknown |
| LOC-07 | config `UiTarget` mapping | `GetByTestId` / attribute locator / `Match` strategy | config | B | ProjectSpecific |
| ACT-01 | `.Click()` / `.ClickAsync()` | `await locator.ClickAsync()` | syntax (fallback) / symbol+type (semantic) | C | Known |
| ACT-02 | `.SendKeys(v)` / `.InputText(v)` | `await locator.FillAsync(v)` (**keyboard → value**) | syntax / symbol | C | PartiallyKnown |
| ACT-03 | `SendKeys(Keys.Enter…)` | `await locator.PressAsync("Enter")` | syntax | B | Known |
| ACT-04 | `.Clear()` / Clear+SendKeys | FillAsync (covers clear+fill) | syntax | C | PartiallyKnown |
| ACT-05 | configured select/dropdown helpers | config mapping | syntax + config | B | ProjectSpecific |
| WAIT-01 | `new WebDriverWait(...)` + `Until(d => d.FindElement(By…).Displayed)` | `Expect(locator).ToBeVisibleAsync()/ToBeHiddenAsync()` | syntax | A | Known |
| WAIT-02 | `WaitPresence/WaitVisible/WaitClickable/…` | **elided** comment (auto-wait assumption) | syntax | C | PartiallyKnown |
| WAIT-03 | product-state wait by **name heuristic** (`WaitForTableLoaded`, `WaitToast…`) | `ToBeVisible/Hidden/WaitForAsync` from verb/widget heuristics | **method-name heuristic** | C | **Unsafe** |
| WAIT-04 | custom/ambiguous `Wait*` names | TODO `WAIT_REQUIRES_STATE_ASSERTION` | syntax | B | Unknown |
| WAIT-05 | config `WaitPolicies` | kind from config → mapped assertion / elide | config | B | ProjectSpecific |
| ASRT-01 | `Assert.That(targetLocal, Is.EqualTo(literal))` | `Assert.That(...)` preserved (generic, not web-first) | syntax + renderer local table | B | Known |
| ASRT-02 | `Assert.AreEqual(e,a)` | `Assert.That(a, Is.EqualTo(e))` (generic) | syntax / symbol | A | Known |
| ASRT-03 | `x.Text().Get().Should().Be/Contain/BeEmpty/…` | `Expect(locator).ToHaveTextAsync / ToContainTextAsync` (equals/contains) or NUnit `InnerTextAsync` (empty/not) | syntax (string-strip heuristics) | C | PartiallyKnown |
| ASRT-04 | `x.Visible.Wait().Should().BeTrue()/EqualTo(true)` | `Expect(locator).ToBeVisibleAsync()/ToBeHiddenAsync()`; dynamic bool → `ConditionalBlockAction` | syntax | B | Known |
| ASRT-05 | `Assert.Multiple(...)` | inner actions individually, wrapper elided | syntax | B | Known |
| ASRT-06 | `WebDriver.Url.Should().Be(x)/Contain(x)` | `Expect(Page).ToHaveURLAsync(x)` / `Assert.That(Page.Url, Does.Contain(x))` | syntax | B | Known |
| ASRT-07 | `Table.Items.Count.Get().Should().Be(n)` / `Items.ElementAt(i).Text...` | `Expect(locator).ToHaveCountAsync(n)` / `CountAsync()` + `Assert.That`; row text via `.Nth(i)` | syntax + config Table/Pagination | C | PartiallyKnown |
| ASRT-08 | Playwright-native `Expect(...)` in source | pass-through (preserved) | syntax | A | Known |
| FLOW-01 | `foreach` / `.ForEach(lambda)` | rendered collection loop | syntax | C | PartiallyKnown |
| FLOW-02 | `if/else` action blocks | rendered structurally; unresolved condition symbols → TODO | syntax | C | PartiallyKnown |
| PG-01 | `Navigation.OpenPage<T>(url)` / configured nav methods | `Page.GotoAsync(url)` | syntax + config URLs | B | ProjectSpecific |
| IR-01 | unrecognized receiver invocation (POM/project method) | `MethodInvocationAction` → config mapping | syntax / symbol ownership | B | ProjectSpecific |
| IR-02 | config `Method/ParameterizedMethod` templates | target statements + placeholder substitution | config | B | ProjectSpecific |
| POM-01 | POM fields, base classes, inheritance | `PageObjectProperty`/`PageObjectLocator`; shallow inheritance | syntax + type graph (index) | C | PartiallyKnown |
| ASYNC-01 | sync Selenium → async Playwright | `async Task` lift + `await …Async()` | syntax + await-flow in file | D | PartiallyKnown |
| CTX-01 | frames / windows / tabs / popups (`SwitchTo`, `WindowHandles`) | none — TODO/unsupported (isolation) | syntax | E | Unknown |
| JS-01 | `IJavaScriptExecutor.ExecuteScript` | none — TODO/unsupported (isolation) | syntax | E | Unknown |
| ACTAPI-01 | `Actions` API chains | none — TODO/unsupported (isolation) | syntax | E | Unknown |
| SAFE-01 | unresolved symbols in generated statements | `[MIGRATOR:UNRESOLVED_SYMBOL]` TODO rather than active broken code | syntax + scope table | A | Unknown |
| CONF-01 | `SourceOnlyIdentifiers` / `SuppressedMethods` / `TargetKnown*` / `ScaffoldMethods` | suppression as comments / source-only never active / scaffold runtime blocker | config | B | ProjectSpecific |

## Where syntax-only vs Roslyn semantic info is used

- **Semantic (symbol/type)** is used in exactly **four places**:
  `TryRecognizeSemantic` — `.Click()` (IWebElement-like receiver), `.SendKeys/.InputText`
  (OpenQA Selenium), `Assert.That/AreEqual` (must be `NUnit.Framework.Assert`), and the
  *negative* rules `IsBuiltinSystemMethod` / `IsResolvedProjectMethod` (RoslynTestFileParser.cs:1153-1249).
- **Everything else is syntax-string based**: recognizer pipelines, regex extractors,
  suffix-stripping, method-name heuristics. `ProjectSemanticIndex` builds a real type/call
  graph for POM discovery and ownership, but the *transformations* rarely consume it —
  it feeds `IsResolvedProjectMethod` and `pom-index`/`helper-inventory` modes.
- Priority arbitration is **explicit and deterministic** (`RecognizerArbitrator`), with
  `AMBIGUOUS_RECOGNITION` blocking instead of silently choosing.

## The default (unconfigured) path is config-gated

Whole-corpus `analyze` (no adapter-config) on `corpus/stable/vertical-slice`:

```
31 files, 32 tests, 120 actions;  Semantic=35, SyntaxFallback=92
Mapped targets=2, Unmapped targets=31, TODO comments=117, UnsupportedActions=0
```

p01 (trivial `By.Id` login) unconfigured: generated test is all comments/TODOs, yet the
report says `SuccessfullyConvertedTests=1`; quality gate `[AssertionLoss]` fired
(`0 executable assertions of 3` preserved) and the run **failed** (not a false green).

Two consequences for the inventory:

1. Class **B** rules are the meat of the default experience: without a config the pipeline
   produces mostly `MISSING_MAPPING` TODOs. `% converted` cannot be read from
   `SuccessfullyConvertedTests`.
2. **Silent-loss candidates are caught by quality gates in `run` mode** (AssertionLoss,
   SemanticNoOp, MISSING_MAPPING, WAIT_REQUIRES_STATE_ASSERTION...), but `analyze`/report
   metrics (`SuccessfullyConvertedTests`) do not reflect them.

## Known unsafe / semantic-loss points (evidence-backed)

- **WAIT-03 (Unsafe)**: product-state wait direction (`Visible` vs `Hidden`) and *target
  element* are guessed from the method name and the wait receiver text. The receiver of a
  loader wait is usually the container, not the loader itself; `WaitForToast` guesses
  `Visible` though toasts commonly disappear. Corpus p17/p17b oracles exist; the heuristic
  is text-only.
- **ACT-02**: `SendKeys` → `FillAsync` always (never `TypeAsync`, no key events, no
  per-key semantics) — a real semantic difference outside the corpus's runtime-pass cases.
- **ASRT-03**: fluent text chains strip `.Replace(...)/.Trim()` normalization before
  finding the target, so the emitted assertion asserts on a different value than source.
- **LOC-06 / ACT-01**: `FindElement` and `FindElements` share one regex; multi-match
  locators used as single elements will throw Playwright strict-mode violations where
  Selenium returned the first.
- **LOC-01**: unconfigured inline action targets stay unresolved while the identical
  expression in a `var` declaration resolves — path-dependent behaviour (CONFIRMED on p01).
- **SAFE-01** mitigates compile-green semantic-red: unresolved symbols become TODO
  comments instead of active code.

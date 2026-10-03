# Correctness-Risk Areas — Evidence-Backed Findings

Phase A baseline, commit `734990c`. Only findings that are reproducible from code and/or
measured runs on `corpus/stable/vertical-slice` (p01–p29). Classification legend:
`CONFIRMED`, `LIKELY`, `HYPOTHESIS`, `ARCHITECTURAL TRADEOFF`, `NOT ENOUGH EVIDENCE`.

---

## 1. Locators

### 1.1 `FindElement` vs `FindElements` cardinality

```
Observed behavior / risk
  The recognizer and adapter treat FindElement and FindElements identically (single
  regex `FindElements?`). A stored list locator and a single locator produce the same
  Page.Locator. Collection semantics (count, per-index access beyond ElementAt(i),
  first-match policy) are only partially represented (Nth for literal index).

Source code location
  WebDriverFindElementRecognizer.cs:24 (MethodName check, both names);
  DefaultProjectAdapter.cs:836-878 (all FindElement* regexes use FindElements?),
  ResolveLocalElementAt/ResolveLocalIndexedAccess :1020-1065 (Nth only for literal index).

Mechanism
  Regex metacharacter `?` on the final `s` swallows the singular/plural distinction.

Concrete Selenium example
  var links = WebDriver.FindElements(By.CssSelector("a.nav"));
  links[0].Click();          // -> mapped as .Nth(0) only if literal index
  links.Count                // count semantics re-evaluated by Playwright lazily

Generated Playwright behavior
  Page.Locator("a.nav") (lazy, re-evaluated). Click on a multi-match locator without
  Nth throws a strict-mode violation in Playwright; Selenium returned the first element.

Why semantics may differ
  Selenium resolves FindElements at call time (snapshot); Playwright Locator re-resolves
  per action. Stale-element patterns, mutation between count and use, and first-vs-strict
  selection differ.

Existing test coverage
  corpus p04-findelements-count-text (runtime PASS in lab, which supplies config),
  p19-control-flow-loops.

Why current tests catch / do not catch it
  They cover indexed access and count with config; they do NOT cover a raw multi-match
  locator used as a single element (which would blow up at runtime).

Minimal regression fixture
  A fixture that stores FindElements list, mutates DOM, then asserts Count and clicks
  the first element without Nth.

Classification: CONFIRMED (structural conflation in code)
Recommended next action
  Distinguish cardinality in the recognizer/adapter; emit strict-mode-safe locators for
  single-element use or an explicit TODO when a multi-element source is used as single.
```

### 1.2 Path-dependent inline vs declaration resolution (default path)

```
Observed behavior / risk
  Without adapter-config, `WebDriver.FindElement(By.Id("result"))` inside a local
  declaration becomes an active `Page.Locator("#result")`, while the SAME expression used
  as an inline action target (SendKeys/Click) stays Unresolved -> MISSING_MAPPING TODO.

Source code location
  DefaultProjectAdapter.ResolveTargetWithLocalVars :981-1010 vs ResolveInlineFindElementTarget
  :884-935; local-variable mapping seeded by declaration handling.

Mechanism
  Local declarations are resolved through the inline FindElement resolver + local-vars
  map; inline invocation targets resolve through the config target map which is empty
  without config.

Concrete Selenium example
  WebDriver.FindElement(By.Id("username")).SendKeys("john");   // TODO (unmapped)
  var result = WebDriver.FindElement(By.Id("result"));          // Page.Locator("#result")

Generated Playwright behavior
  First line: `// TODO MISSING_MAPPING` + commented `FillAsync`. Second line: active
  `var result = Page.Locator("#result");`.

Why semantics may differ
  Not a semantic difference — a determinism/UX inconsistency: identical source expressions
  produce resolved vs unresolved depending on syntactic position.

Existing test coverage
  corpus p01 (measured), p07 (locator-in-variable with config).

Why current tests catch / do not catch it
  The lab runs p01 with a generated config, so `todoMax:0` is green there; the default
  unconfigured path is not asserted.

Minimal regression fixture
  p01 run without config must be recorded (it is). Add a contract test asserting the
  default path either resolves both or TODOs both.

Classification: CONFIRMED
Recommended next action
  Make inline action-target resolution use the same inline FindElement resolver first
  (or deliberately document that config is required and fail loudly).
```

---

## 2. Waits

### 2.1 Actionability elision is a blanket, not an analysis

```
Observed behavior / risk
  WaitPresence/WaitVisible/WaitEnabled/WaitClickable/WaitExists (and Async variants) are
  all elided into a single comment: "source wait elided ... Playwright actions and
  web-first assertions auto-wait". Presence (attached) is not actionability; visible,
  enabled, clickable are different states.

Source code location
  WaitInvocationRecognizer.cs ActionabilityWaitMethods :22-29; WaitPresenceRecognizer.cs;
  DotNetAssertionAndWaitRenderer.RenderWaitFor ActionabilityElided branch.

Mechanism
  One bucket -> one elide decision, regardless of state.

Concrete Selenium example
  driver.WaitPresence(By.Id("x"));     // element exists but may be hidden
  element.WaitClickable();             // wants enabled+visible+stable

Generated Playwright behavior
  Both become a comment and disappear.

Why semantics may differ
  Presence is weaker than actionability; a hidden-but-attached element passed the source
  wait but may still fail Playwright's actionability wait or the next action.

Existing test coverage
  corpus p15/p16 (visibility waits are assertion-mapped, not elided).

Why current tests catch / do not catch it
  Presence-vs-actionability distinction has no dedicated fixture.

Minimal regression fixture
  WaitPresence on an element that becomes present but stays hidden.

Classification: ARCHITECTURAL TRADEOFF (deliberate, documented elide policy) — with a
  LIKELY sub-finding that the single-bucket elision is coarser than the source states.
Recommended next action
  Split elision decisions by state; keep presence waits visible as TODOs.
```

### 2.2 Product-state wait direction is guessed from names (Unsafe)

```
Observed behavior / risk
  WaitForLoaded/WaitTableLoaded etc. map to ProductStateLoaded; widget spellings
  (Toast/Modal/Popup -> Visible, Loader/Spinner -> Hidden) and closing/opening verbs are
  inferred from the METHOD NAME and RECEIVER TEXT of the wait caller, not from what is
  actually waited on.

Source code location
  WaitInvocationRecognizer.cs IsProductStateWait :123-139, InferProductStateKind :151-170;
  renderer maps ProductState* to Expect(locator).ToBeVisible/Hidden/WaitForAsync.

Mechanism
  Heuristic classifier; conflicting signals -> ReviewRequired (good), but unambiguous
  guess -> hard-coded expectation.

Concrete Selenium example
  page.WaitForToast();        // name suggests "toast appears" but toasts usually hide
  WaitTableLoaded();           // waits LOADING state that the loader, not the table, shows
  target = <wait receiver>     // usually the container, not the state element

Generated Playwright behavior
  await Expect(<locator-of-receiver>).ToBeVisibleAsync();

Why semantics may differ
  The waited element and the desired state are both inferred, so a hidden-loader pattern
  migrates to the wrong assertion with full confidence.

Existing test coverage
  corpus p17-custom-wait-state, p17b-custom-wait-closing-state encode the intended
  direction as a runtime oracle (so the heuristic is pinned per fixture, not generally).

Why current tests catch / do not catch it
  They validate the *given* fixtures' direction; they cannot validate un-blessed names.

Minimal regression fixture
  A wait named by its widget (Toast) whose desired state is hidden.

Classification: CONFIRMED (mechanism in code) — heuristic semantics are unsafe by design
  unless the target element and direction come from analysis/config.
Recommended next action
  Route product-state waits through config/type analysis (which element/state) before
  emitting a positive assertion; keep ReviewRequired as the default confidence.
```

### 2.3 Custom wait -> ReviewRequired (good behaviour)

```
Observed behavior / risk
  Custom wait names degrade to WAIT_REQUIRES_STATE_ASSERTION TODO instead of a fixed
  timeout.

Source code location
  WaitInvocationRecognizer.cs LooksLikeCustomWait :141-149; RenderWaitFor ReviewRequired.

Classification: A (no semantic loss; explicit review).
```

---

## 3. Input

### 3.1 `SendKeys` → `FillAsync`

```
Observed behavior / risk
  SendKeys (and InputText) emit FillAsync always, never TypeAsync.

Source code location
  PlaywrightDotNetRenderer.RenderSendKeys :890-919 (single FillAsync branch).

Mechanism
  All text input is treated as value-setting.

Concrete Selenium example
  element.SendKeys("password");  // key events, focus, per-key
  element.SendKeys(Keys.Shift + "password");

Generated Playwright behavior
  await element.FillAsync("password");  // sets value directly

Why semantics may differ
  FillAsync fires a single input event and REPLACES the value; SendKeys types
  char-by-char (fires keydown/keypress/keyup), appends to the existing value, and can be
  intercepted by key handlers/autocomplete/IME. Shift+keys composition is lost.

Existing test coverage
  corpus p01/p02 (clear+fill runtime-pass asserts equality for the plain case),
  Keys.Enter -> PressAsync is covered.

Why current tests catch / do not catch it
  Runtime-pass covers only the no-key-event case; key-event-sensitive apps are absent.

Minimal regression fixture
  Input bound to keydown counters / incremental appending (Selenium types, Playwright
  would replace).

Classification: CONFIRMED (emitter is unconditional)
Recommended next action
  Keep FillAsync default but classify as PartiallyKnown; surface keyboard-sensitive
  patterns (non-quoted arg, modifiers) as review.
```

---

## 4. Assertions

### 4.1 Assertion loss is *detected*, not silent (good), but metrics lie

```
Observed behavior / risk
  p01 unconfigured: assert statements become comment + ASSERTION_CONSTRAINT TODO; the
  [AssertionLoss] gate counts 0/3 preserved and FAILS the run — so not a false green in
  `run` mode. HOWEVER report.json says SuccessfullyConvertedTests=1 for the same test.

Source code location
  ReportBuilder.Build :23-31 (convertedWithoutUnsupported = no UnsupportedAction in body);
  quality gates in Program.cs (AssertionLoss/SemanticNoOp).

Mechanism
  "Converted" is defined as "no UnsupportedAction", which TODO-constraints and
  comment-only assertions do not trigger.

Existing test coverage
  FinalGateProvenanceTests, ExecutableSemanticPreservationTests.

Classification: CONFIRMED — metric `SuccessfullyConvertedTests` is not a semantic-success
  metric; the run-level gate is the honest signal.
Recommended next action
  Add a referenced operation-preservation metric (see semantic-accounting harness); never
  gate product decisions on SuccessfullyConvertedTests.
```

### 4.2 Upgrade vs preservation are different rules (correct split)

```
Observed behavior / risk
  Web-first upgrade happens only for recognized fluent chains (ASRT-03/04/06/07);
  generic NUnit assertions are PRESERVED (ASRT-01/02) with NUnit semantics (snapshot,
  no auto-wait) — a slower-feedback but compile-safe fallback. This split is intentional
  and correct; the confidence differs (Known for preservation vs PartiallyKnown for
  upgrade).

Classification: A (split is sound); the residue is the semantic gap between snapshot NUnit
  asserts and web-first awaits, documented, not silent.
```

---

## 5. Page objects & helpers

### 5.1 Helper vs Selenium method distinction

```
Observed behavior / risk
  In the semantic path, project-owned methods resolve as authoritative negatives and are
  preserved (MethodInvocationAction) — so `MyHelper.Click()` is not treated as Selenium
  Click. In the syntax fallback path, any receiver.Click() becomes ClickAction regardless
  of whether the receiver is a real element.

Source code location
  RoslynTestFileParser.cs:731-743 (IsResolvedProjectMethod negative), ClickInvocationRecognizer
  :10 (any receiver, syntax).

Mechanism
  Resolution quality depends on whether the compilation resolves the symbol: with the
  lightweight per-file compilation (no project references) a project Click on a helper is
  NOT resolved and falls into the syntax Click recognizer.

Concrete Selenium example
  page.Navigation.Go();            // project method on page
  page.Table.Row(2).Click();       // project POM method that internally clicks

Generated Playwright behavior
  Go() -> MethodInvocationAction (preserved). Row(2).Click() -> ClickAction if the symbol
  did not resolve (wrong), else preserved. Without a project semantic index these are
  syntax-classified.

Existing test coverage
  corpus p09-p12 (helper/POM). Semantic index used when project root provided.

Classification: ARCHITECTURAL TRADEOFF with a real boundary: deterministic-only runs that
  lack project reference graph classify helper Click as Selenium Click. LIKELY symptom in
  single-file/standalone runs.
Recommended next action
  When ProjectSemanticIndex is unavailable, prefer preserving receiver invocations over
  action interpretation unless the receiver is a known WebDriver/WebElement root.
```

### 5.2 Helper-body migration is not performed

```
Observed behavior / risk
  helper -> helper -> Selenium chains: only the call site is mapped/surfaced; helper
  bodies are not analysed for waits/locators/alerts.

Source code location
  IR-01 rule (PageObjectMethodRecognizer + config method mapping).
Classification: ARCHITECTURAL TRADEOFF (explicit). D-rated for deep chains: helper body
  analysis requires project-wide call-graph + control-flow — deterministic candidates,
  not implemented.
```

---

## 6. Async propagation

```
Observed behavior / risk
  Sync->async lift works inside a file (renderer emits async Task + await); SetUp/base
  and cross-project callers are only partially lifted (p13/p14 cover same-file and
  setup-base). External/public boundaries and callers in OTHER projects cannot change
  their signatures deterministically without rewriting them.

Source code location
  ASYNC-01 rule; corpus p13, p14.

Why semantics may differ
  Blocking `.Result`/`.Wait()` in callers (not migrated) can deadlock; async lifecycle in
  NUnit SetUp changes thread-affinity semantics.

Classification: D (requires project-wide analysis) — LIKELY that current coverage stops at
  file/profile boundary.
```

---

## 7. Browser context, JS, Actions API, lifecycle/shared state

```
Frames/windows/tabs (CTX-01), JS (JS-01), Actions API (ACTAPI-01):
  NOT recognized; statements degrade to TODO/unsupported while neighbouring actions
  survive (isolation verified by p26/p27/p28 expected-unsupported fixtures). No silent
  translation — correct conservative behaviour. Classification E in Phase A for these,
  with the note that parts (window/tab, simple JS getters) are deterministic-analysis
  candidates (D), not necessarily LLM.

Lifecycle/shared state (SetUp/TearDown/OneTime*, shared driver, fixtures):
  The infra base class (LabSeleniumTestBase) is a non-test file, skipped from migration
  ("skipped non-test file"). WebDriver creation in [SetUp] is therefore never migrated;
  the target scaffold inherits PageTest which supplies Page. Shared mutable driver state,
  OneTimeSetUp, parameterized test data sources (p20/p21) are preserved structurally but
  their semantics (shared state, retry/order) are not audited for equivalence.

Why semantics may differ
  Deduplicating SetUp-driven shared state into Playwright fixtures is a project decision;
  keeping PageTest everywhere changes isolation/per-test browser lifecycle (NUnit
  [SetUp] per test = Playwright PageTest already per test; OneTimeSetUp shared driver has
  no direct equivalent and silently converts to per-test pages => different state sharing).

Existing test coverage
  corpus p21 (parallelizable/retry/order), p20 (TestCaseSource), p13/p14 (setup-lift).

Classification
  SHARED-DRIVER/OneTime* lifecycle: CONFIRMED unsupported boundary (LIKELY silent
  semantic difference if a project relies on sharing browser state across tests).
Recommended next action
  Detect OneTimeSetup/shared-driver and emit a review TODO instead of silently mapping to
  PageTest; add a regression fixture for shared-state waiting-on-state.
```

---

## 8. Silent semantic loss summary

| # | Path | Effect | Detected by | Classification |
|---|---|---|---|---|
| 1 | WAIT-02 elision bucket | wait disappears as comment | comment stays, no gate | ARCHITECTURAL TRADEOFF |
| 2 | WAIT-03 name heuristic | wrong positive assertion possible | only per-fixture oracles | CONFIRMED unsafe |
| 3 | ACT-02 SendKeys→Fill | key-event semantics lost | none (runtime oracle absent) | CONFIRMED |
| 4 | ASRT-03 text normalization stripped | asserted value differs from source | none | CONFIRMED |
| 5 | SuccessfullyConvertedTests | metric reports converted while ops lost | run-level AssertionLoss gate (not the metric) | CONFIRMED |
| 6 | OneTime/shared-driver Setup | state sharing silently changes | none | LIKELY |
| 7 | LOC-01 inline-vs-declaration | inconsistent resolution | LocatorPathConsistencyRegressionTests (By.Id/Css/XPath inline vs declaration reuse); default adapter forced by CLI | CONFIRMED |

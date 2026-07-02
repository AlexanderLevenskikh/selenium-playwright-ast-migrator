# Migrator Guardrails

These rules are mandatory.

## Mode boundary

Read `.agent-loops/13-loop-contract.md` before acting.

Default mode is `migration-artifact`.

In migration-artifact mode:

- do not edit migrator repository source code;
- do not search for migrator source when a compiled tool/artifact path was
  provided;
- work only on allowed config/profile/output/report paths;
- report migrator engine bugs as bounded findings or source-change ticket
  candidates.

Use `migrator-code` mode only when the prompt explicitly allows repository
source edits.

## Roslyn and semantic analysis

- Prefer Roslyn syntax and semantic model over regex/string parsing.
- Use syntax fallback only when semantic model cannot resolve the symbol.
- Do not transform text inside C# string literals, comments, URLs, CSS selectors, XPath selectors, or interpolated strings as executable code.
- Preserve source intent before optimizing generated output.
- Treat semantic ambiguity as a reason to emit an explicit TODO, not as a reason to silently guess unsafe behavior.

## Generated Playwright code

- Generated code must compile whenever possible.
- Do not emit `var x = ...` if the source code reassigns an existing variable.
- Do not introduce duplicate variable declarations.
- Do not silently drop Selenium actions.
- If an action cannot be safely migrated, emit an explicit TODO with a diagnostic reason.
- Prefer Playwright locator chains that preserve Selenium target semantics.
- Prefer stable generated code over clever but fragile transformations.
- Preserve async/await correctness.
- Do not create unused helper variables unless existing renderer conventions allow it.

## Tests

- Add or update regression tests for every behavior change.
- Do not weaken assertions merely to make tests pass.
- Snapshot changes must be intentional and explained.
- Compile-smoke coverage is required for structural generated-code changes when feasible.
- If the bug is reproduced by a small source snippet, keep the regression test minimal.
- If a broader scenario already exists, extend it instead of duplicating unnecessarily.

## Renderer

- Keep renderer output stable.
- Prefer local renderer changes over global formatting rewrites.
- Do not introduce broad formatting churn.
- Preserve existing using/namespace/class conventions.
- Preserve indentation/newline conventions used by current snapshots.
- Avoid changing unrelated snapshots.

## Adapter/config

- Do not hardcode project-specific mappings if they belong in adapter config.
- If a mapping cannot be inferred, classify it as adapter/config needed.
- Generated adapter drafts must not pretend unsupported cases are solved.
- Prefer reporting unmapped targets clearly over inventing locator mappings.

## POM/helper recovery

- Do not treat low target Playwright POM coverage as an automatic blocker.
- Before declaring POM coverage insufficient, run or inspect `--mode index-pom` on the Selenium project/POM directory.
- Before mapping, suppressing, or classifying project/POM helper wrappers, run or inspect `--mode helper-inventory`.
- Use Selenium POM selector evidence such as `ByTId("x")`, `CreateControlByTid(...)`, explicit `data-tid`, CSS, XPath, or resolved selector constants.
- Do not invent selectors. PageObject names and property names are not selectors.
- Prefer existing target POM member → generated POM scaffold/member in migration output → raw Playwright locator from proven selector → explicit TODO.
- Generate POM candidates only inside migration/output paths. Do not modify production target PageObjects unless explicitly allowed.
- If a selector or helper side effect cannot be proven, keep/report TODO instead of suppressing or guessing.

## Page objects and fields/properties

- Transfer simple class fields/properties only when initializer semantics are safe.
- Do not transfer fields/properties that require runtime Selenium driver state unless the migrator has an established pattern for it.
- Preserve existing variable/property usage semantics.
- Avoid duplicate page object declarations in generated Playwright classes.
- If a field/property maps to a Playwright page object wrapper, ensure generated code compiles.

## Strict ticket and path boundaries

When a task provides allowed input/write paths, those paths are mandatory boundaries.
Do not search parent directories, do not discover neighboring repositories, and do not edit source files outside allowed write paths.

If the task provides DLLs or artifacts, do not locate matching source code. Analyze the provided inputs and report source-change needs as findings.

Do not fix unrelated problems found during investigation. Keep every change tied to the current ticket.

## Autonomy

- Do not ask the user to choose between technical options.
- Make the safest local decision.
- Ask the user only when product/business semantics are required.
- Do not stop merely because there are multiple reasonable implementations.
- Do not ask "continue?" after a partial batch. Continue until the loop exit
  condition or stop policy is reached.

## Checkpoint is not completion

Do not treat a green build/project verify as completion of the whole migration when the board still has actionable TODOs, missing mappings, unresolved symbols, unsupported actions, empty tests, or blocked runtime candidates.

Treat green compile as a safe checkpoint.

Continue with the next highest-impact category unless the user explicitly asked only to fix compile/build errors.

## Source-only identifier safety

`SourceOnlyIdentifiers` are safe for compile preservation, but they may short-circuit mapping attempts.

Before moving a high-frequency root such as `page`, `pagef`, `modal`, `dialog`, or similar to source-only behavior, understand the effect on Method/ParameterizedMethod mapping.

A source-only root may trade compile errors for TODOs. That can be correct for compile-fix phase, but it is not a final migration-quality solution.

After such a trade, continue with category reduction or classification.

## Suppression safety

Do not reduce TODO count by unsafe broad suppression.

TODO removed via suppression is not migration progress. It is valid only when
the suppressed source operation is proven to be non-behavioral noise, and the
evidence is recorded.

Never suppress FluentAssertions, NUnit assertions, assertion-like helpers, or
business checks (`*.Should*`, `Should()`, `Assert*`, `Expect*`, `Equal*`,
`Be*`, `Contain*`, validation/check helpers) merely to make TODO metrics look
better.

`0 TODO` is not success if it was achieved by broad suppression, empty tests,
weakened assertions, deleted actions, dummy target-known declarations, or edits
to the real target/POM project.

Before adding more suppressions, inspect whether existing suppressions created `EMPTY_TEST_AFTER_SUPPRESSION`.

If a suppression or `MethodSemantics` decision touches project/POM helper wrappers, first run or request `--mode helper-inventory` and base the decision on helper body evidence. Do not infer helper semantics from names alone.

If tests became empty:

- trace representative generated tests back to source Selenium tests;
- distinguish technical wait/loader-only tests from accidentally suppressed meaningful tests;
- prefer upstream mapping/root-cause fixes over more suppression;
- document when suppression is intentionally preserving compile safety rather than migration completeness.

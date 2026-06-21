# Migrator Guardrails

These rules are mandatory.

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

## Page objects and fields/properties

- Transfer simple class fields/properties only when initializer semantics are safe.
- Do not transfer fields/properties that require runtime Selenium driver state unless the migrator has an established pattern for it.
- Preserve existing variable/property usage semantics.
- Avoid duplicate page object declarations in generated Playwright classes.
- If a field/property maps to a Playwright page object wrapper, ensure generated code compiles.

## Autonomy

- Do not ask the user to choose between technical options.
- Make the safest local decision.
- Ask the user only when product/business semantics are required.
- Do not stop merely because there are multiple reasonable implementations.

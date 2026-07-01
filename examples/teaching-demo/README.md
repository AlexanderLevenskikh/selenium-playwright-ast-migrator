# Teaching demo: AST migration explained

This demo is the small, readable companion to the public playground. The
playground is for trying the CLI quickly; this folder is for understanding how
an AST-based migration works.

## Files

- `input/LoginTeachingTest.cs` — Selenium C# / NUnit source test.
- `input/PageObjects/LoginPage.cs` — tiny PageObject with real `data-testid`
  selector evidence.
- `adapter-config.json` — reviewed source-expression → Playwright locator map.
- `expected/LoginTeachingTestPlaywright.generated.cs` — expected generated output
  after the mappings are applied.
- `reports/ast-action-map.md` — teaching map from Selenium source to AST action
  to Playwright output.

## Run it

From the repository root:

```bash
selenium-pw-migrator --mode analyze \
  --input examples/teaching-demo/input \
  --config examples/teaching-demo/adapter-config.json \
  --out teaching-demo-analyze \
  --format both

selenium-pw-migrator --mode migrate \
  --input examples/teaching-demo/input \
  --config examples/teaching-demo/adapter-config.json \
  --out teaching-demo-generated \
  --format both
```

The default migration workspace is `migration/`, so relative `--out` values are
created as `migration/teaching-demo-analyze` and `migration/teaching-demo-generated`.

## What to inspect

1. Open `input/LoginTeachingTest.cs` and notice that the test talks in project
   wrappers: `InputText`, `Click`, `ShouldBeVisible`, `Text`.
2. Open `input/PageObjects/LoginPage.cs` and find the real selector evidence:
   `[data-testid='login-email']`, `[data-testid='sign-in']`, and so on.
3. Open `adapter-config.json` and see how the reviewed source expressions map to
   Playwright locators.
4. Compare the generated output with
   `expected/LoginTeachingTestPlaywright.generated.cs`.
5. Read `reports/ast-action-map.md` for the action-by-action explanation.

## Why this is separate from `public-demo/` and `playground`

- `playground` is a disposable five-minute CLI experience.
- `public-demo/` is a broader copyable product demo.
- `teaching-demo/` is deliberately tiny and educational: it explains why the
  tool uses AST recognition, source-truth mappings, and reviewable TODOs.

Full article:

- [AST migration explained](../../docs/articles/ast-migration-explained.md)
- [AST migration explained — RU](../../docs/articles/ast-migration-explained.ru.md)

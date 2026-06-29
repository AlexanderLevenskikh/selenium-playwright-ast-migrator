# Simple end-to-end example

This directory contains a complete minimal migration example:

- `input/SimpleSeleniumTest.cs` — Selenium C# / NUnit source test.
- `adapter-config.json` — source-truth mappings for the sample PageObject expressions.
- `expected/SimplePlaywright.generated.cs` — expected Playwright .NET output.
- `report.example.txt` — example analysis report.

Run from the repository root:

```bash
selenium-pw-migrator --mode migrate \
  --input examples/simple/input \
  --config examples/simple/adapter-config.json \
  --out examples-simple-generated \
  --format both
```

See `docs/examples/end-to-end-simple.md` for the walkthrough.

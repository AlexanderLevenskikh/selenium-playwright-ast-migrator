# PROD-17 — Source auto-detect report

## Goal

Make source frontend selection safer and more ergonomic. When the user does not pass `--source`, or explicitly passes `--source auto`, the CLI detects the most likely Selenium source frontend and writes a diagnostic report before parsing.

## Supported signals

The detector currently scores three built-in source frontends:

- `selenium-csharp` / `csharp-selenium`
- `selenium-java` / `java-selenium`
- `selenium-python` / `python-selenium`

Detection is based on file extensions and Selenium/test-framework signals:

- C#: `.cs`, `OpenQA.Selenium`, `IWebDriver`, `IWebElement`, `FindElement`, NUnit/xUnit-style attributes.
- Java: `.java`, `org.openqa.selenium`, `WebDriver`, `WebElement`, `findElement`, `WebDriverWait`, `ExpectedConditions`, JUnit/TestNG signals.
- Python: `.py`, `selenium.webdriver`, `from selenium`, `find_element`, `WebDriverWait`, `expected_conditions`, pytest/unittest signals.

## CLI behavior

Explicit source still wins:

```bash
Migrator.Cli --mode migrate --source java-selenium --input ./JavaTests --target ts --out generated
```

Auto-detect runs when `--source` is omitted or set to `auto`:

```bash
Migrator.Cli --mode migrate --input ./JavaTests --target ts --out generated
Migrator.Cli --mode dump-ir --source auto --input ./tests --ir-version v2 --out ir-dump
```

Auto-detect applies to source-processing modes:

- `analyze`
- `dump-ir`
- `migrate`
- `verify`
- `verify-project`
- `doctor`
- `orchestrate`

Config/artifact-only modes keep backward-compatible defaults and do not scan source trees.

## Artifacts

When auto-detect runs, the CLI writes:

- `source-detection-report.json`
- `source-detection-report.md`

The report includes:

- selected source frontend;
- detected source frontend;
- confidence;
- files scanned;
- top reasons;
- candidate scores;
- sample matching files.

## Safety model

If no Selenium signals are found, the detector falls back to `selenium-csharp` for backwards compatibility and reports `none` confidence. This avoids breaking existing C#-default workflows while still surfacing the weak detection result.

If multiple frontends score closely, the report marks overall confidence as `ambiguous`; the current highest-scoring candidate is still selected, but users should pass `--source` explicitly for mixed-language repositories.

# MIG-XL-09..20 — Cross-language source/target milestone

This milestone turns the migrator from a Selenium C# -> Playwright tool into a domain-specific source-to-source compiler for UI tests.

## Scope delivered in this scaffold

| Ticket | Status | Notes |
|---|---|---|
| MIG-XL-09 IR V2 model skeleton | Scaffolded | `MigrationDocument`, source spans, locator/value/assertion/wait/navigation intents, IR statements. |
| MIG-XL-10 Legacy bridge | Scaffolded | `LegacyIrBridge` converts `TestFileModel` <-> `MigrationDocument`. |
| MIG-XL-11 Renderer reads IR V2 | Transitional | `ITargetBackend.RenderDocument(MigrationDocument)` lowers through the legacy bridge so current renderers stay behavior-compatible. |
| MIG-XL-12 Source frontend contract | Scaffolded | `ISourceFrontend`, `SourceParseResult`, `SourceFrontendRegistry`. |
| MIG-XL-13 C# Selenium frontend wrapper | Scaffolded | `CSharpSeleniumFrontend` wraps `RoslynTestFileParser`. |
| MIG-XL-14 Source plugin boundary | Partial | C# Selenium source identity and aliases are now isolated behind a frontend. Full recognizer namespace move should be a separate mechanical MR. |
| MIG-XL-15 ConfigNormalizer v1 -> v2 | Scaffolded | `ProjectAdapterConfigNormalizer.Normalize(...)` produces `MigrationProfile`. |
| MIG-XL-16 SourceProfile / TargetProfile split | Scaffolded | Source-only, recognizer/wait settings are separated from target host/import/helper settings. |
| MIG-XL-17 Schema v2 + migration warnings | Scaffolded | Added `migration-profile-v2.schema.json` and non-fatal config migration warnings. |
| MIG-XL-18 Java Selenium parser spike | Scaffolded | Regex-based Java parser for simple `findElement(By.*).click/sendKeys/getText/isDisplayed` idioms. |
| MIG-XL-19 Java source fixtures | Added | `CrossLanguageArchitectureTests` cover Java parser/frontend and rendering through existing backends. |
| MIG-XL-20 Decision document | Added | See below. |

## Decision: add Python Selenium as the next frequent source language

The official Selenium/WebDriver ecosystem has bindings for Java, Python, C#, JavaScript/Node.js and Ruby/R according to commonly referenced Selenium overviews. For prioritization, language popularity signals point strongly at Python and JavaScript/TypeScript alongside Java/C#:

- Selenium client APIs are commonly listed for Java, C#, Ruby, JavaScript, R and Python.
- TIOBE-style rankings in early 2026 place Python, Java and C# high in general language popularity, while JavaScript remains a major web ecosystem language.
- GitHub/Stack Overflow ecosystem signals often rank JavaScript/TypeScript and Python extremely high.

Roadmap update:

1. Keep Java Selenium as the first new source proof because it is closest to the current Selenium C# object model.
2. Add Python Selenium as the next source spike because it is a very common automation/testing language and exercises dynamic-language parsing risks.
3. Treat JavaScript/TypeScript Selenium as a later source spike because the target is already TypeScript Playwright; source/target AST ambiguity and async style need more care.

## Acceptance checklist before merging as a milestone

- Existing golden master snapshots still pass.
- Existing C# Selenium -> Playwright .NET behavior does not change.
- `CrossLanguageArchitectureTests` pass.
- Java parser spike is documented as experimental and not advertised as production-ready.
- `migration-profile-v2.schema.json` is treated as experimental until `adapter-config` loading can accept profile v2 directly.

## Follow-up tickets after this scaffold

### MIG-XL-21 Python Selenium source spike

Minimal supported idioms:

```python
browser.find_element(By.ID, "save").click()
browser.find_element(By.CSS_SELECTOR, ".name").send_keys("Alex")
assert browser.find_element(By.CSS_SELECTOR, ".status").text == "Saved"
```

Risks:

- dynamic imports and aliases;
- pytest fixtures;
- page objects with duck-typed helpers;
- chained waits via `WebDriverWait(...).until(...)`.

### MIG-XL-22 Real Java frontend

Replace regex spike with a parser-backed implementation. Candidate options:

- JavaParser / external CLI inventory;
- tree-sitter-java;
- Roslyn-like semantic support is not available by default, so config-driven helper inventory becomes more important.

### MIG-XL-23 Direct IR rendering

Move Playwright .NET and TS renderers from `TestFileModel` to `MigrationDocument` directly. Keep legacy lowering only for compatibility tests.

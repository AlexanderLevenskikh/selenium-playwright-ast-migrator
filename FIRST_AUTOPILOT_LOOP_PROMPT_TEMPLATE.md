Read all files in .agent-loops/.
Also read AGENTS.md, docs/autopilot-loop.md, .agent-loops/11-strict-ticket-boundaries.md, and .agent-loops/12-pom-helper-recovery-policy.md.

Start Migrator Autopilot Loop.

You are allowed and expected to make engineering decisions yourself.
Do not ask me to choose between implementation options.
Do not ask “continue?” after partial progress.
Do not stop after partial progress.
Continue until the selected migration block is fixed and verified, or until the stop policy requires a real stop.

Migration scope:

Source Selenium project:
<ABSOLUTE_OR_RELATIVE_PATH_TO_SOURCE_SELENIUM_PROJECT>

Target/generated Playwright project:
<ABSOLUTE_OR_RELATIVE_PATH_TO_TARGET_PLAYWRIGHT_PROJECT_OR_OUTPUT_DIR>

Migrator config/profile:
<PATH_TO_ADAPTER_CONFIG_OR_PROFILE, IF ANY>

Compiled migrator tool, if this is compiled-tool-only mode:
<PATH_TO_COMPILED_MIGRATOR_TOOL_OR_EMPTY>

Existing Playwright POM examples, if any:
<PATH_TO_ALLOWED_TARGET_POM_EXAMPLES_OR_EMPTY>

Verification output directory:
<PATH_TO_VERIFY_OR_ORCHESTRATION_OUTPUT>

Allowed input paths:
- <ALLOWED_INPUT_PATH_1>
- <ALLOWED_INPUT_PATH_2>

Allowed write paths:
- <ALLOWED_WRITE_PATH_1>

Forbidden paths:
- parent directories outside allowed input/write roots
- repository source folders unless explicitly listed as allowed write paths

Strict path rules:
- Do not search parent directories.
- Do not edit source files unless the repository source tree is listed as an allowed write path.
- If this task provides DLL/artifact folders, treat them as source of truth and do not locate matching source code.

POM/helper recovery rules:
- Before large POM/config work, run or inspect `index-pom` on the Selenium project/POM directory.
- Before mapping/suppressing project or POM helper wrappers, run or inspect `helper-inventory`.
- Missing target Playwright POM coverage is not automatically `TICKET_NEEDED`.
- If Selenium POM has real selector evidence such as `ByTId("value")`, `CreateControlByTid(...)`, `data-tid`, CSS, XPath, or resolved selector constants, use that source truth.
- Do not invent selectors. PageObject class/property names are not selectors.
- Prefer this order: existing target POM member → generated POM scaffold/member in migration output → raw Playwright locator from proven selector → explicit TODO.
- Generate POMs only inside migration/output paths; never modify production target PageObjects unless explicitly allowed.

Expected command shape:
- If repository source is allowed: `dotnet run --project Migrator.Cli -- --mode verify --input <SOURCE_SELENIUM_PROJECT> --config <CONFIG> --out <VERIFY_OUTPUT> --format both`
- If this is compiled-tool-only mode: `<PATH_TO_COMPILED_MIGRATOR_TOOL>\migrator.exe --mode verify --input <SOURCE_SELENIUM_PROJECT> --config <CONFIG> --out <VERIFY_OUTPUT> --format both`

In compiled-tool-only mode, do not search for `Migrator.Cli`, `.sln`, `.csproj`, or migrator source code.

Current task:
<ВСТАВЬ ТЕКУЩИЙ БЛОК / ОШИБКУ / ЛОГ / TODO-КАТЕГОРИЮ>

Use only allowed repository code, existing tests, snapshots, docs, CLI reports, generated output, source Selenium tests, target Playwright project conventions, `index-pom`/`helper-inventory` outputs, and command output as the source of truth.
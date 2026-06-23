Read all files in .agent-loops/.
Also read AGENTS.md, docs/autopilot-loop.md, and .agent-loops/11-strict-ticket-boundaries.md.

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

Expected command shape:
dotnet run --project Migrator.Cli -- --mode verify --input <SOURCE_SELENIUM_PROJECT> --config <CONFIG> --out <VERIFY_OUTPUT> --format both

Current task:
<ВСТАВЬ ТЕКУЩИЙ БЛОК / ОШИБКУ / ЛОГ / TODO-КАТЕГОРИЮ>

Use only allowed repository code, existing tests, snapshots, docs, CLI reports, generated output, source Selenium tests, target Playwright project conventions, and command output as the source of truth.
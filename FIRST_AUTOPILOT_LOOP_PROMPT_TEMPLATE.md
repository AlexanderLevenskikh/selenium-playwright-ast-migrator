Read all files in .agent-loops/.
Also read AGENTS.md and docs/autopilot-loop.md.

Start Migrator Autopilot Loop.

You are allowed and expected to make engineering decisions yourself.
Do not ask me to choose between implementation options.
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

Expected command shape:
dotnet run --project Migrator.Cli -- --mode verify --input <SOURCE_SELENIUM_PROJECT> --config <CONFIG> --out <VERIFY_OUTPUT> --format both

Current task:
<ВСТАВЬ ТЕКУЩИЙ БЛОК / ОШИБКУ / ЛОГ / TODO-КАТЕГОРИЮ>

Use repository code, existing tests, snapshots, docs, CLI reports, generated output, source Selenium tests, target Playwright project conventions, and command output as the source of truth.
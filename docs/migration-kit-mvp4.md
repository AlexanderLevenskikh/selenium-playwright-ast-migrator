# Migration Kit MVP-4: cross-platform kit commands

MVP-4 moves the migration workspace installer into the CLI so Windows, macOS and Linux use the same flow.

PowerShell scripts are still supported, but they are now convenience wrappers. The durable contract is:

```bash
selenium-pw-migrator kit init
selenium-pw-migrator kit update
selenium-pw-migrator kit doctor
selenium-pw-migrator kit next-ticket
```

## Install a workspace

```bash
selenium-pw-migrator kit init \
  --workspace migration \
  --source ./OldSeleniumTests \
  --config migration/profiles/adapter-config.json \
  --out migration/runs/run-001
```

This creates:

```text
migration/
  README.md
  QUICKSTART.md
  agent-state.md
  current-ticket.md
  profiles/adapter-config.json
  prompts/
  state/
  tickets/
  evidence/
  codex/
  .migration-kit/version.json
```

The command also copies `.agent-loops` and `.agent-state` into the project root when those files are available in the installed tool/bundle.

## Safe update

```bash
selenium-pw-migrator kit update --workspace migration --backup
```

Project-owned files are preserved:

```text
migration/profiles/adapter-config.json
migration/agent-state.md
migration/current-ticket.md
migration/state/handoff.md
migration/state/run-ledger.md
migration/state/decision-log.md
migration/runs/
migration/reports/
migration/logs/
```

Changed kit-owned files are written to:

```text
migration/.migration-kit/updates/<timestamp>/*.new
```

Use `--force` only when you intentionally want to overwrite kit-owned files.

## Doctor

```bash
selenium-pw-migrator kit doctor --workspace migration
```

This checks:

- workspace existence;
- `.migration-kit/version.json`;
- adapter config path;
- kickoff/resume/loop prompts;
- state handoff files;
- bundled JSON schema;
- optional Codex files;
- `dotnet` availability.

Reports are written to:

```text
migration/reports/kit-doctor/kit-doctor.md
migration/reports/kit-doctor/kit-doctor.json
```

## Next ticket prompt

```bash
selenium-pw-migrator kit next-ticket --workspace migration --input migration/runs/run-053
```

This generates:

```text
migration/prompts/generated-next-ticket-prompt.txt
```

The prompt asks the agent to inspect the latest run artifacts and produce one bounded, evidence-backed ticket instead of trying to continue from memory.

## Optional layers

Codex files are installed by default under `migration/codex/`.

OpenCode team files:

```bash
selenium-pw-migrator kit update --workspace migration --backup --with-team
```

Reusable loop library:

```bash
selenium-pw-migrator kit update --workspace migration --backup --with-loop-library
```

Skip Codex files:

```bash
selenium-pw-migrator kit init --workspace migration --no-codex-files
```

## Shell wrappers

Windows PowerShell:

```powershell
.\scripts\install-migration-kit.ps1 -Workspace migration -Update -Backup
```

Linux/macOS shell:

```bash
./scripts/install-migration-kit.sh --workspace migration --update --backup
```

Both wrappers should stay thin and delegate to the CLI `kit` command.

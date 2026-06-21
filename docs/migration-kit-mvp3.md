# Migration Kit MVP-3: optional agent team and Codex handoff

MVP-3 keeps the default install simple and adds optional helpers for teams that want stronger agent orchestration.

## Default install

```powershell
.\tool\scripts\install-migration-kit.ps1 `
  -Workspace migration `
  -Source C:\path\to\selenium-tests `
  -Config migration\profiles\adapter-config.json `
  -Output migration\runs\run-001
```

This creates the regular migration workspace and also installs Codex helper files under:

```text
migration/codex/
```

These files are project-local and do not configure any global agent tooling.

## Safe update

```powershell
.\tool\scripts\install-migration-kit.ps1 -Workspace migration -Update -Backup
```

Project-owned files are preserved. Changed kit-owned files are written as `.new` files under `migration/.migration-kit/updates/<timestamp>/` unless `-Force` is used.

## Codex handoff

Use Codex for a single bounded ticket:

```text
Read migration/codex/CODEX.md and migration/codex/prompts/ticket-fix-prompt.txt.
Fix only the current ticket in migration/current-ticket.md.
```

Use Codex as reviewer:

```text
Read migration/codex/CODEX.md and migration/codex/prompts/review-prompt.txt.
Review the current diff for blockers only.
```

## Optional OpenCode team files

Install project-local OpenCode team template files:

```powershell
.\tool\scripts\install-migration-kit.ps1 -Workspace migration -Update -Backup -WithTeam
```

This copies the team template to:

```text
migration/opencode-team/
```

It also writes `AGENTS.md` to the repository root using safe update rules.

The global OpenCode configuration is not installed automatically. To install it manually, read:

```text
migration/opencode-team/README.md
```

This keeps the default kit low-risk and avoids mutating user-level configuration unexpectedly.

## Optional loop library

Install reusable loop templates:

```powershell
.\tool\scripts\install-migration-kit.ps1 -Workspace migration -Update -Backup -WithLoopLibrary
```

This copies additional loop templates to:

```text
migration/loops-library/
```

These are documentation/templates only; the stateful migration loop still lives in `migration/state/` and `migration/prompts/`.

# Agent environments

Migrator Agent Harness Kit is agent-agnostic. OpenCode is the best-supported UI today, but the installed `migration/` workspace is the contract. Other agents can use the same contract as long as they read the installed files, stay inside allowed roots, and run the gates.

## One-command bootstrap

From the product repository root, prefer:

```powershell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --opencode-install auto
```

`--opencode-install auto` chooses the safest available setup for the current environment:

| Environment | Auto behavior | When to use |
|---|---|---|
| Windows + OpenCode Desktop | `project-desktop` | You open the repository folder directly in OpenCode Desktop. |
| macOS/Linux/WSL + OpenCode CLI | `project-local` | You start OpenCode CLI with `OPENCODE_CONFIG=.opencode-migrator/opencode.jsonc`. |
| CI/Codex/manual agent | Use `--opencode-install ci` or `none` | The agent reads prompts/contracts but no OpenCode config is installed. |

The legacy Windows shortcut is still supported:

```powershell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --project-desktop
```

## Install modes

```text
--opencode-install auto             Windows => project-desktop; macOS/Linux/WSL => project-local
--opencode-install project-desktop  Windows OpenCode Desktop project config in the repository root
--opencode-install project-local    Portable OpenCode CLI config in .opencode-migrator
--opencode-install ci               Install/update the workspace only; no OpenCode config
--opencode-install none             Same idea as ci/manual for non-OpenCode agents
--opencode-install global --force   Global OpenCode config; intentionally hard to call by accident
```

Prefer `project-desktop` for OpenCode Desktop and `project-local` for OpenCode CLI. Avoid `global` unless you intentionally want the migration roles to affect every OpenCode session for the current OS user.

## OpenCode Desktop on Windows

```powershell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --project-desktop
```

Then open the product repository root in OpenCode Desktop and run:

```text
/supervised-task
```

The orchestrator must create or resume `migration/runs/<run-id>/` with `migration/scripts/new-harness-run.ps1`; the user should not create run folders manually.

## OpenCode CLI on macOS/Linux/WSL

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --opencode-install project-local
OPENCODE_CONFIG=.opencode-migrator/opencode.jsonc opencode
```

Then run:

```text
/supervised-task
```

This mode does not touch global OpenCode config.

## Codex, CI, or another coding agent

Use the kit without installing OpenCode config:

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --opencode-install ci
```

Give the agent these files:

```text
migration/AGENT_CONTRACT.md
migration/prompts/kickoff-prompt.txt
migration/harness/README.md
migration/state/harness-policy.json
```

Tell the agent to create or resume a run through the shell wrapper:

```bash
./migration/scripts/new-harness-run.sh -TaskTitle "Pilot migration batch" -Goal "Run one bounded artifact-only migration batch."
```

Windows PowerShell can call the `.ps1` script directly:

```powershell
./migration/scripts/new-harness-run.ps1 -TaskTitle "Pilot migration batch" -Goal "Run one bounded artifact-only migration batch."
```

Before final success, the agent must run the installed gates. Bash users can stay in bash:

```bash
./migration/scripts/check-harness-policy.sh -Workspace migration -RepoRoot .
./migration/scripts/check-final-gate.sh -Workspace migration -RepoRoot .
```

PowerShell users can call the underlying scripts directly:

```powershell
./migration/scripts/check-harness-policy.ps1 -Workspace migration -RepoRoot .
./migration/scripts/check-final-gate.ps1 -Workspace migration -RepoRoot .
```

For CI, keep the agent output as artifacts: `migration/runs/**`, `migration/state/harness-events.jsonl`, and `migration/dashboard/harness/**`.

## Safety contract

All environments share the same rules:

- English docs are canonical; Russian docs are secondary localization.
- Machine-readable events and report status codes are language-neutral.
- The agent may continue autonomously only for actions allowed by `harness-policy.json` and `AGENT_CONTRACT.md`.
- The agent must ask before package installs, network access, broad shell operations, or edits outside allowed roots.
- Final success requires evidence and gates, not a confident chat answer.

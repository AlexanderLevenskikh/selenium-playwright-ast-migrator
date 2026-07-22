# Agent environments

> **Execution model:** one standard full-project run is supported. `pilot` is optional calibration; partition-specific planning and acceptance state are not used.

Migrator Agent Harness Kit is agent-agnostic. OpenCode is the best-supported UI today, but the installed `migration/` workspace is the contract. Other agents can use the same contract as long as they read the installed files, stay inside allowed roots, and run the gates.

## Recommended route

Start a product repository with the onboarding command, then choose the agent handoff that matches the environment:

```shell
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

`start` writes `migration/current-ticket.md`, `migration/next-commands.md`, and `migration/state/start-dispatch.json`. Agents should treat those files as the active bounded task. When the state is clear, they continue it directly instead of offering a broad menu or unrelated documentation/refactoring work. If the configured source is missing, they stop with `SOURCE_SCOPE_MISSING` rather than guessing.

## OpenCode Desktop or OpenCode CLI

Use the OpenCode-specific bootstrap when you want roles, commands, and low-noise permissions installed for OpenCode:

```shell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json --opencode-install auto
```

`--opencode-install auto` chooses the safest available setup for the current environment:

| Environment | Auto behavior | When to use |
|---|---|---|
| Windows + OpenCode Desktop | `project-desktop` | You open the repository folder directly in OpenCode Desktop. |
| macOS/Linux/WSL + OpenCode CLI | `project-local` | You start OpenCode CLI with `OPENCODE_CONFIG=.opencode-migrator/opencode.jsonc`. |
| CI / non-OpenCode | Use `bootstrap-agent` instead | The agent reads handoff docs/contracts; repository-root OpenCode command pack is only for OpenCode sessions. |

The legacy Windows shortcut is still supported:

```powershell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json --project-desktop
```

Then open the product repository root in OpenCode and run the one-command standard flow start:

```text
/supervised-task
```

`bootstrap-opencode` applies the repository-root command pack (`opencode.jsonc`, `.opencode/agents`, `.opencode/commands`, and `AGENTS.md` when missing) before OpenCode starts. The orchestrator resolves source/target/framework, runs doctor, optionally calibrates with a small pilot, then executes one complete `selenium-pw-migrator run` and real `verify-project`. The CLI creates the run directory; the agent does not reconstruct it by hand.

## Codex, CI, or another coding agent

Use the explicit agent handoff command. This is the primary non-OpenCode path:

```shell
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json
```

For a generic agent or CI runner:

```shell
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json
```

Give the agent these files:

```text
migration/AGENT_HANDOFF.md
migration/AGENT_CONTRACT.md
migration/current-ticket.md
migration/next-commands.md
migration/pilot/selected-tests.txt
migration/pilot/next-commands.md
migration/harness/README.md
migration/state/harness-policy.json
migration/state/start-dispatch.json
```

Tell the agent to work on the selected pilot input first:

```text
migration/pilot/selected-input
```

The generated `migration/pilot/next-commands.md` must analyze/migrate `selected-input`, not the full suite.

## Legacy compatibility mode

`bootstrap-opencode --opencode-install ci` is still supported as a compatibility alias for older docs/scripts. prefer `kit bootstrap-agent --agent codex` or `--agent generic` for new non-OpenCode setups.

```shell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json --opencode-install ci
```

## Install modes reference

```text
--opencode-install auto             Windows => project-desktop; macOS/Linux/WSL => project-local
--opencode-install project-desktop  Windows OpenCode Desktop project config in the repository root
--opencode-install project-local    Portable OpenCode CLI config in .opencode-migrator
--opencode-install ci               Legacy compatibility: no additional OpenCode launcher install; bootstrap-agent is preferred for non-OpenCode agents
--opencode-install none             Apply repository-root command pack only; no Desktop/global/project-local launcher install
--opencode-install global --force   Global OpenCode config; intentionally hard to call by accident
```

Prefer `project-desktop` for OpenCode Desktop and `project-local` for OpenCode CLI. Avoid `global` unless you intentionally want migration roles to affect every OpenCode session for the current OS user.

## Final gates and dashboard

Before final success, the agent must run the installed gates. Bash users can stay in bash:

```shell
./migration/scripts/check-harness-policy.sh
./migration/scripts/check-final-gate.sh
```

Windows PowerShell can call the `.ps1` scripts directly:

```powershell
./migration/scripts/check-harness-policy.ps1
./migration/scripts/check-final-gate.ps1
```

After a run exists, open the dashboard first:

```shell
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest --format both
```

## Safety contract

Rules are the same for every environment:

- English docs are canonical; Russian docs are secondary localization.
- Machine-readable events and report status codes stay language-neutral.
- The agent may perform only bounded actions allowed by `harness-policy.json` and `AGENT_CONTRACT.md`, then must stop with evidence.
- The agent must stop and request explicit user approval before package installs, network access, broad shell operations, or edits outside allowed roots; it must not guess or bypass the restriction.
- Final success requires evidence and gates, not a confident chat response.

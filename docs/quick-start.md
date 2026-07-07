# Quick start

This is the public first-run route. It keeps the user path short: install, diagnose, choose one of three entries, run a representative pilot, and open the dashboard only after real run artifacts exist.

## Install and diagnose

Recommended standalone install is still the most predictable no-runtime path for locked-down machines.


Recommended public install for most users:

```shell
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
```

Update the npm wrapper:

```shell
npm update -g selenium-pw-migrator
selenium-pw-migrator self update
```

`doctor install` is the first diagnostic command when something feels off. It tells you which executable your shell actually runs, which channel it looks like (`npm`, `standalone`, `dotnet-tool`, `source`, or `unknown`), which version is resolved, and what update command fits that channel.

Standalone remains the recommended no-runtime fallback when npm is unavailable:

```powershell
$installer = Join-Path $env:TEMP "install-standalone.ps1"
Invoke-WebRequest "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.ps1" -OutFile $installer
& $installer
selenium-pw-migrator doctor install
```

For a project-pinned .NET tool, see [Tool installation](tool-installation.md).

## Choose one of three entries

Harness run lifecycle is owned by the installed scripts such as `new-harness-run.ps1`, `write-harness-event.ps1`, and `check-final-gate.ps1`; agents should not hand-create run folders.


OpenCode auto install can choose `project-local` on macOS/Linux/WSL, `project-desktop` on Windows Desktop, or `ci` for workspace-only compatibility. Codex and other non-OpenCode agents should use `bootstrap-agent`, not the `ci` OpenCode compatibility path.


### 1. Try it without an agent

```shell
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
bash playground/commands.sh
selenium-pw-migrator playground verify --input playground --out playground-verify --format both
```

Use this playground first. For real migrations, keep the production promise focused on Selenium C# -> Playwright .NET; Java, Python, and Playwright TypeScript remain experimental preview paths.

### 2. Migrate with OpenCode

From the product repository root:

```shell
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json --opencode-install auto
# Windows OpenCode Desktop legacy shortcut:
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --project-desktop
```

Then open the repository in OpenCode and run:

```text
/supervised-task
```

`start` writes `migration/current-ticket.md` and `migration/state/start-dispatch.json`. `/supervised-task` must use those files as the active bounded task and must not ask the user to choose from a broad menu when the state is clear.

After a fresh FINAL/PASS checkpoint, `/supervised-task` stops once for review and prints one recommended `/supervised-task continue` command. On any later `/supervised-task` invocation where the workspace is already `FINAL_STOPPED_FOR_REVIEW`, it resumes post-final TODO/source-truth research, research-lead review, task slicing, change review, and one bounded executor task automatically; explicit continue remains supported but is not required. Implementation follows only after approved research/current-ticket, change-review approval, or a concrete implementation request.

### 3. Migrate with Codex, CI, or another agent

Do not route non-OpenCode agents through `bootstrap-opencode --opencode-install ci` as the main path. Use the explicit handoff command:

```shell
selenium-pw-migrator start --input ./SeleniumTests --agent codex --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json
```

For a generic agent or CI runner:

```shell
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json
```

`bootstrap-agent` writes `migration/AGENT_HANDOFF.md`, `migration/AGENT_CONTRACT.md`, prompts, state files, and harness docs. Give those files to the agent and tell it to follow the contract instead of inventing its own workflow.

## What `start` creates

```text
migration/
  current-ticket.md
  next-commands.md
  README.start.md
  profiles/adapter-config.start.json
  state/start-dispatch.json
```

`next-commands.md` is intentionally concrete. It should point to `doctor install`, `pilot`, `doctor`, agent bootstrap or manual migration, and a dashboard command that is clearly marked as usable only after a run exists.

## What `pilot` creates

```shell
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

Output:

```text
migration/pilot/
  pilot-selection.md
  pilot-selection.json
  selected-tests.txt
  selected-input/
  next-commands.md
```

`selected-input/` is the bounded pilot copy. The generated analyze/migrate commands must point at `migration/pilot/selected-input`, not at the full Selenium suite. That keeps a pilot run honest and prevents the next command from unexpectedly scaling to hundreds of tests.

## Open the dashboard after a run

A dashboard needs real run artifacts. After a harness or manual run writes `migration/runs/latest`, open this first:

```shell
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest --format both
```

Open `migration/dashboard/latest/report-dashboard.html` before reading raw JSON. The dashboard should be the review surface for readiness, TODO root causes, unsupported actions, generated files, evidence links, and agent run history.

## Manual CLI fallback

Use this when you are deliberately not using the product `start` route and only want a starter config/scaffold:

```shell
selenium-pw-migrator init --wizard --source ./SeleniumTests --target dotnet --target-test-framework nunit --workspace migration
selenium-pw-migrator --mode doctor --input ./SeleniumTests --config migration/profiles/adapter-config.json --out migration/doctor --format both
```

`init --wizard` remains supported, but it is now the legacy/manual scaffold path. New product-repo onboarding should start with `start` and `pilot`.

## Next steps

- [Agent environments](agent-environments.md)
- [Release UX Pack](release-ux-pack.md)
- [Report serve dashboard](report-serve-dashboard.md)
- [Migration workflow](user-guide/migration-workflow.md)
- [Config and profile guide](config-profile-guide.md)
- [Limitations](user-guide/limitations.md)

# OpenCode TrustedProject permissions

`TrustedProject` is an opt-in permission profile for trusted local dogfood runs where OpenCode approval prompts are more expensive than the remaining risk.

It intentionally allows routine OpenCode tools, file edits, subagents, and shell commands inside the repository working directory:

```jsonc
"permission": {
  "edit": "allow",
  "bash": "allow",
  "task": "allow",
  "external_directory": "deny"
}
```

`external_directory: deny` keeps tool calls scoped to the project directory where OpenCode was started. OpenCode applies `external_directory` when a tool touches paths outside the working directory, including `read`, `edit`, `glob`, `grep`, and many `bash` commands.

## When to use it

Use `TrustedProject` when:

- you are running a local dogfood migration in a disposable or version-controlled workspace;
- repeated approvals for `git status`, `git diff`, `where`, `dotnet tool list`, source inspection, and migration artifact writes are blocking the run;
- you still want OpenCode blocked from touching paths outside the project.

Keep the default `LowNoise` profile for users who want project writes limited to `migration/**`.

## Install with TrustedProject

Windows:

```powershell
migration/opencode-team/scripts/install-windows.ps1 `
  -Mode ProjectDesktop `
  -PermissionProfile TrustedProject `
  -Force
```

Linux/macOS/WSL:

```bash
migration/opencode-team/scripts/install-unix.sh   --mode ProjectLocal   --permission-profile TrustedProject
```

Then restart OpenCode so it reloads `opencode.jsonc`.

## Install with the default LowNoise profile

Windows:

```powershell
migration/opencode-team/scripts/install-windows.ps1 `
  -Mode ProjectDesktop `
  -PermissionProfile LowNoise
```

Linux/macOS/WSL:

```bash
migration/opencode-team/scripts/install-unix.sh   --mode ProjectLocal   --permission-profile LowNoise
```

## Safety notes

`TrustedProject` is not a replacement for the migration gates. Before accepting a run, still execute:

```powershell
migration/scripts/check-scope.ps1
migration/scripts/check-harness-policy.ps1
migration/scripts/check-final-gate.ps1
```

Do not use `TrustedProject` from a parent directory that contains unrelated repositories. Start OpenCode from the product repository root.

## Scope guard compatibility

Project-local OpenCode bootstrap files are migration harness configuration, not product source edits. The final gate allows these repository-root paths by default while continuing to block unrelated source changes outside `migration/**`:

```text
AGENTS.md
opencode.jsonc
.opencode/**
.opencode-migrator/**
```

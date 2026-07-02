# OpenCode Installation Safety

Recommended mode is project-local.

Do not install `global/.config/opencode` into the user-global OpenCode config
unless you intentionally want artifact-only migration behavior in all OpenCode
sessions for that user.

Use project-local mode for migration sessions:

```powershell
.\scripts\install-windows.ps1 -Mode ProjectLocal
$env:OPENCODE_CONFIG = "$PWD\.opencode-migrator\opencode.jsonc"
opencode
```

For OpenCode Desktop, install the project config into the repository root that
Desktop opens:

```powershell
Set-Location "C:\Users\levenskikh\Desktop\billy"
.\migration\opencode-team\scripts\install-windows.ps1 -Mode ProjectDesktop
```

`ProjectDesktop` is project-local. When the script is run from an installed kit
layout such as `migration\opencode-team\scripts\install-windows.ps1`, it
infers the repository root from that script path and writes only there. It must
not write to `$HOME`, `%USERPROFILE%`, or the user-global OpenCode config.

If inference is not possible, pass the repository root explicitly:

```powershell
.\migration\opencode-team\scripts\install-windows.ps1 -Mode ProjectDesktop -Target "C:\Users\levenskikh\Desktop\billy"
```

`ProjectDesktop` backs up existing `opencode.jsonc`, `.opencode\agents`, and
`.opencode\commands` under `.migration-kit\opencode-backups\<timestamp>` before
overwriting them. Use `-Force` only when overwriting without a backup is intended.

This copies:

- `opencode.jsonc` to the project root.
- agents to `.opencode\agents`.
- commands to `.opencode\commands`.

Equivalent manual/direct setup:

```powershell
Copy-Item "migration\opencode-team\global\.config\opencode\opencode.jsonc" ".\opencode.jsonc" -Force
New-Item -ItemType Directory -Force ".opencode\agents", ".opencode\commands"
Copy-Item "migration\opencode-team\global\.config\opencode\agents\*" ".opencode\agents" -Recurse -Force
Copy-Item "migration\opencode-team\global\.config\opencode\commands\*" ".opencode\commands" -Recurse -Force
```

```bash
bash ./scripts/install-unix.sh --mode ProjectLocal
OPENCODE_CONFIG="$PWD/.opencode-migrator/opencode.jsonc" opencode
```

Global mode is advanced:

```powershell
.\scripts\install-windows.ps1 -Mode Global
```

```bash
bash ./scripts/install-unix.sh --mode Global
```

Global mode may affect TUI, CLI, desktop, and automation sessions that load the
same user config.

Do not approve shell commands that write these files:

- `migration/scripts/check-scope.ps1`
- `migration/scripts/check-final-gate.ps1`
- `migration/.migration-kit/guard-checksums.json`

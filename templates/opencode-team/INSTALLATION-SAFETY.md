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

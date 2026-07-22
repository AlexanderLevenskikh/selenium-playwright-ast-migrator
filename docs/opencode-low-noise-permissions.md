# OpenCode low-noise permissions

The bundled project config exposes only four task roles: `orchestrator`, `executor`, `reviewer`, and `watchdog`.

The standard migration flow needs read access to repository files and write access to the configured migration workspace. Destructive cleanup, package publishing, unrestricted external-directory access, and broad dependency installation remain denied or review-required.

Useful read-only checks include:

```powershell
Get-Content migration/state/scope-contract.json
Get-Content migration/state/final-gate-result.json
Get-ChildItem migration/runs
```

The executor may write only the bounded files explicitly delegated by the orchestrator. The reviewer and watchdog inspect current reports, diffs, generated test bodies, and real verification logs; they do not create replacement evidence.

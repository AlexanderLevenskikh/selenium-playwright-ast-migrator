# Migration feedback bundles

A feedback bundle is a redacted project-local evidence pack for a noisy standard run, many TODOs, syntax fallbacks, unresolved symbols, or verification blockers.

```powershell
migration/scripts/create-feedback-bundle.ps1 -Workspace migration
```

```bash
migration/scripts/create-feedback-bundle.sh -Workspace migration
```

The bundle may include orchestration reports, mapping research memory, migration boards, TODO explanations, and project-verification reports. It excludes source and generated C# samples by default. Review `manifest.json` before sharing. Prefer `state/mapping-research-memory.*` and `state/mapping-research-candidates.jsonl` over private source.

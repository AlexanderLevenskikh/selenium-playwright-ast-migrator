# Config Authoring Assistant

`config author` proposes small, evidence-driven adapter-config changes from existing migration artifacts. It is a read-only assistant: it writes proposal files, not config changes.

```bash
selenium-pw-migrator config author \
  --input migration/runs/latest \
  --config ./adapter-config.json \
  --out config-proposals \
  --format both
```

Mode-compatible form:

```bash
selenium-pw-migrator --mode config-author \
  --input migration/runs/latest \
  --config ./adapter-config.json \
  --out config-proposals \
  --format both
```

## Inputs

The assistant looks for evidence from:

- `selector-evidence.md/json`
- `pom-index.generated.md/json`
- `helper-inventory.md/json`
- `discover-target` artifacts
- `explain-todo.md/json`
- `runtime-feedback-loop.md/json`
- `report-triage-decisions.md/json`
- `config-validate-report.md/json`

## Outputs

- `config-proposals.md` — human-readable proposals.
- `config-proposals.json` — machine-readable proposals for agents/review tools.
- `config-proposals.patch` — JSON snippets to copy into a reviewed candidate config layer.

## Safety model

The command never edits source tests, generated tests, or adapter-config files. It never invents selectors. Selector proposals are marked `review-required` and contain placeholders until `selector-evidence` or `index-pom` proves the real selector. Helper proposals require `helper-inventory` before mapping or suppression.

Recommended loop:

```bash
selenium-pw-migrator config author --input migration/runs/latest --config ./adapter-config.json --out config-proposals --format both
cp adapter-config.json adapter-config.authoring-candidate.json
# manually copy selected snippets
selenium-pw-migrator --mode config-diff --before adapter-config.json --after adapter-config.authoring-candidate.json --out config-authoring-diff --format both
selenium-pw-migrator --mode config-validate --config adapter-config.authoring-candidate.json --validation-mode production --out config-authoring-validate --format both
```

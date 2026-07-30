# Safety Checklist

## Must pass

- [ ] Generated syntax status is reported separately from project build and runtime status.
- [ ] Assertions/business checks were not silently removed.
- [ ] Empty tests are explicit TODO/inconclusive, not false-green.
- [ ] Generated files were not edited as the final fix.
- [ ] Source-only identifiers were not hidden by broad target-known declarations.
- [ ] TODO reduction came from source-backed mappings, safe helper classification, generated POM/raw locator evidence, or explicit classification.
- [ ] Scope guard passed for all changed files.
- [ ] New engine behavior has regression tests when possible.
- [ ] Config changes include representative evidence.
- [ ] Exactly one bounded change was made per remediation cycle.
- [ ] Every cycle has a stable fingerprint, baseline metrics, rerun evidence, and result.
- [ ] Progress reset the no-progress streak.
- [ ] A two-no-progress stop uses two distinct candidate fingerprints.
- [ ] `continuous` advanced after progress and rolled over each five-cycle checkpoint until a real mandatory stop.
- [ ] `state/autonomy-state.json` and `state/handoff.md` agree.
- [ ] `scripts/validate-handoff.ps1` passed before handoff.

## Cycle classification

- [ ] CONFIG_FIX
- [ ] ENGINE_FIX
- [ ] TARGET_PROJECT_INFRA
- [ ] SOURCE_TRUTH_MANUAL
- [ ] NEED_MORE_EVIDENCE
- [ ] UNSAFE_REVERTED

## Stop-policy gate

- [ ] `state/stop-policy-checklist.md` identifies the exact stop reason.
- [ ] Safe candidates remaining at budget boundary are ranked.
- [ ] In migration-artifact mode, no Migrator repository source code was edited.

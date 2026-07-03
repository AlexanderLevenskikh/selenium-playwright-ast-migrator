# Stop-Policy Checklist

Fill this before stopping or handing off a migration batch. This file is `state/stop-policy-checklist.md` in a generated migration kit.

## Batch context

- Status: <CONTINUE_AUTONOMOUSLY | READY_FOR_ACCEPTANCE | TICKET_NEEDED | BLOCKED_BY_ENVIRONMENT | BLOCKED_BY_MISSING_INPUT | MAX_ITERATIONS_REACHED | UNSAFE_REVERTED>
- Current mode: <migration-artifact | migrator-code | strict-ticket | other>
- Batch goal:
- Allowed input paths inspected:
- Allowed write paths used:
- Commands run:
- Artifacts inspected:
- Files changed:
- Scope guard command/result:
- Forbidden changed paths:
- TODO count before/after and why this is real migration progress:
- Suppression categories before/after:
- Meaningful generated actions/assertions preserved: yes/no/evidence

## Valid stop reason

At least one must be checked before stopping:

- [ ] Selected batch is complete and verified.
- [ ] Max autonomous batch budget or explicit fix-review cycle limit was reached after writing the next concrete ticket.
- [ ] Source truth / selector evidence / helper semantics is missing and cannot be proven from allowed inputs.
- [ ] Product or business semantics are required.
- [ ] Required tool/dependency is unavailable and no useful static verification remains.
- [ ] Required input files/configs are missing.
- [ ] Needed source edit is forbidden by the current mode/path contract.
- [ ] Max iterations reached.
- [ ] Unsafe change was reverted.
- [ ] Forbidden write path was detected and the batch was rejected.
- [ ] TODO decrease was caused by source-backed mappings or explicit classification, not by suppression/empty tests.

## Must not be true

- [ ] I am not asking the user whether to continue.
- [ ] I am not stopping only because the latest report says `NOT FINAL - INVESTIGATION RESULT ONLY` or `NOT RUNTIME READY` while an allowed next config/scaffold/evidence action exists.
- [ ] I am not stopping only because compile/project verify is green while actionable migration work remains.
- [ ] I did not edit migrator source code in `migration-artifact` mode.
- [ ] I did not search for migrator source in compiled-tool-only mode.
- [ ] I did not invent selectors from names.
- [ ] I did not hide behavior through broad suppressions or target-known declarations.
- [ ] I did not add or broaden FluentAssertions/NUnit/business assertion suppression.
- [ ] I did not count TODO removed by suppression as progress.
- [ ] I did not accept `0 TODO` without config-validate/quality gate evidence and meaningful generated actions/assertions.
- [ ] I did not change real target/POM project files, real Playwright tests, `.csproj`, `nuget.config`, or root-level generated files in artifact-only mode.
- [ ] I did not edit generated files as the final fix unless generated-output edits were explicitly allowed.

## Evidence


## One concrete next action

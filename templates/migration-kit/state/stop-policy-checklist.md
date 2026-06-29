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

## Valid stop reason

At least one must be checked before stopping:

- [ ] Selected batch is complete and verified.
- [ ] Source truth / selector evidence / helper semantics is missing and cannot be proven from allowed inputs.
- [ ] Product or business semantics are required.
- [ ] Required tool/dependency is unavailable and no useful static verification remains.
- [ ] Required input files/configs are missing.
- [ ] Needed source edit is forbidden by the current mode/path contract.
- [ ] Max iterations reached.
- [ ] Unsafe change was reverted.

## Must not be true

- [ ] I am not asking the user whether to continue.
- [ ] I am not stopping only because compile/project verify is green while actionable migration work remains.
- [ ] I did not edit migrator source code in `migration-artifact` mode.
- [ ] I did not search for migrator source in compiled-tool-only mode.
- [ ] I did not invent selectors from names.
- [ ] I did not hide behavior through broad suppressions or target-known declarations.
- [ ] I did not edit generated files as the final fix unless generated-output edits were explicitly allowed.

## Evidence


## One concrete next action



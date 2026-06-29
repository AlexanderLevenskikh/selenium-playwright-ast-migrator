# Stop-Policy Checklist

Use this checklist before any agent stops, hands off, or reports a blocker. It is a practical gate over `03-stop-policy.md`.

A stop is valid only when every applicable item is answered with evidence. If any answer is missing and the agent can still inspect files or run a cheaper check, continue instead of asking the user whether to continue.

## Required fields

- **Current mode:** `migration-artifact` / `migrator-code` / `strict-ticket` / other explicitly supplied mode.
- **Selected batch goal:** the concrete category, ticket, diagnostic, or failure being worked.
- **Allowed input paths checked:** exact paths.
- **Allowed write paths checked:** exact paths.
- **Commands run:** exact command lines or exact reason command execution was impossible.
- **Artifacts inspected:** reports, generated files, configs, POM/helper outputs, logs.
- **Files changed:** exact paths or `none`.

## Hard stop checklist

The agent may stop only when at least one item below is true and supported by evidence:

- [ ] `READY_FOR_ACCEPTANCE`: the selected batch objective is complete and the required verification/check command passed or was replaced by stronger evidence.
- [ ] `TICKET_NEEDED`: source truth, selector evidence, helper semantics, business semantics, or a generic migrator source change is required and cannot be proven or performed inside the allowed paths.
- [ ] `BLOCKED_BY_ENVIRONMENT`: a required tool/dependency is unavailable and no useful static or narrower verification remains.
- [ ] `BLOCKED_BY_MISSING_INPUT`: required files/configs/reports are absent and cannot be inferred from allowed paths.
- [ ] `MAX_ITERATIONS_REACHED`: the loop reached its explicit max iteration limit and includes the best current evidence.
- [ ] `UNSAFE_REVERTED`: the attempted batch made metrics worse or violated safety rules and was reverted.

## Mandatory negative checks

Before stopping, confirm all of these:

- [ ] The agent is **not** stopping merely because compile/project verify is green while actionable TODOs, unsupported actions, unmapped targets, empty tests, or runtime candidates remain.
- [ ] The agent is **not** stopping to ask “continue?” or to ask which technical option the user prefers.
- [ ] The agent is **not** stopping because there are several reasonable implementations; it chose the smallest safe reversible one or produced a bounded ticket.
- [ ] In `migration-artifact` mode, the agent did **not** edit migrator repository source code.
- [ ] In compiled-tool-only mode, the agent did **not** search for `Migrator.Cli`, `.sln`, `.csproj`, or migrator source folders.
- [ ] Any suppression, helper mapping, or POM decision has `helper-inventory` / `index-pom` evidence, or remains an explicit TODO/ticket.
- [ ] No selector was invented from a PageObject/member name.
- [ ] No generated file was edited as the final fix unless the current task explicitly allowed generated-output artifact edits.

## Output requirement

When a hard stop is valid, report exactly one final status and one concrete next action. Do not append an open-ended continuation question.

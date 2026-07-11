# Single validation host

Iteration 3 removes the agent-managed `validation-plan → execute shell command → record-validation` chain from the normal wave path.

## Command

```bash
selenium-pw-migrator migration validate \
  --out migration/runs/wave-001 \
  --validation-project ./Target.Tests/Target.Tests.csproj
```

The alias `migration validation-host` is also supported. `validation-plan` and `record-validation` remain available for recovery and importing externally executed evidence.

## What one invocation owns

1. validate the immutable wave/run context;
2. calculate changed-output impact and exact input fingerprint;
3. materialize an existing PASS only when both the exact inputs and exact validation contract match;
4. parse runtime JSON/JSONL artifacts;
5. syntax-check generated C# and sanity-check generated TypeScript/JavaScript;
6. execute the target project build or an explicit project validation command;
7. write per-process stdout/stderr, duration, timeout, command line, and peak-memory evidence;
8. record PASS/FAIL through the incremental validation contract;
9. create one validation checkpoint after a newly executed PASS.

A cache hit does not create another checkpoint.

## Project validation

For code changes, the host fails closed unless it can execute project-level evidence.

Use one of:

```bash
# .NET solution/project or TypeScript tsconfig
selenium-pw-migrator migration validate \
  --out migration/runs/wave-001 \
  --validation-project ./Target.Tests/Target.Tests.csproj

# Existing repository-specific build/test/gate command
selenium-pw-migrator migration validate \
  --out migration/runs/wave-001 \
  --validation-command "dotnet test ./Target.Tests/Target.Tests.csproj --no-restore"
```

`--validation-project` uses:

- `dotnet build <project> --no-restore` for `.sln`, `.slnx`, and `.csproj`;
- `npx --no-install tsc --noEmit -p <tsconfig>` for `tsconfig.json`.

Restore dependencies once before the wave loop. This avoids repeated restore/network work inside every validation cycle.

## Profiles and controls

- `--validation-profile auto|fast|standard|audit` — `auto` reads the immutable execution policy;
- `--validation-timeout-seconds <n>` — hard timeout for each external process;
- `--validation-dry-run true` — resolve internal checks and commands without executing external processes or recording PASS;
- `--force-validation true` — ignore an exact-input and exact-contract cache hit;
- `--checkpoint-on-pass true|false` — defaults to `true` for newly executed PASS only.

## Artifacts

```text
validation-plan.json
validation-result.json
validation-host-result.json
validation/processes/<invocation-id>/*.stdout.log
validation/processes/<invocation-id>/*.stderr.log
validation/host-runs/<invocation-id>.json
migration/.cache/validation/<input>.<validation-contract>.json
latest-checkpoint.json           # only after a newly executed PASS
```

`validation-host-result.json` uses `migration-validation-host-result/v1` and records internal checks, processes, cache decision, resource metrics, and safety invariants.

## Safety properties

- code changes cannot receive PASS from artifact-only checks;
- an empty or failed external command is never cached;
- C# syntax errors stop external execution and produce FAIL evidence;
- cached PASS must match both the exact inputs and the exact validation contract: profile, project, commands, and timeout;
- a checkpoint is recoverable progress, not `DONE`;
- final reviewer, sentinel, project gates, and final gate remain separate requirements.

## Test layers

- unit tests exercise `ValidationProcessExecutor` with fake process/clock/filesystem adapters;
- scenario smoke executes configuration-required, PASS, cache-hit, syntax-failure, and timeout-safe paths;
- full E2E remains in the scheduled full-validation workflow;
- performance budgets guard orchestration and validation-host wall-clock regressions.

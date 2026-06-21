# Ticket Needed Template

Use this only when the stop policy requires `TICKET_NEEDED`.

Do not create external GitHub issues automatically.
Do not upload source code.
Do not include proprietary code unless the user explicitly approves it.

Prepare a local ticket-ready summary.

## Template

```md
# <Suggested ticket title>

## Status

TICKET_NEEDED

## Category

<UnsupportedAction | TargetExpression | Renderer | AdapterConfig | CompileSafety | PageObjectTransfer | Other>

## Problem

Describe the migration case that cannot be solved safely in the current loop.

## Evidence

- Failing test:
- Build/test output:
- Report category:
- Generated output symptom:

## Minimal repro

Use sanitized or synthetic code when possible.

```csharp
// sanitized minimal source example
```

## Current behavior

Describe what the migrator currently generates or reports.

## Expected behavior

Describe the desired behavior if it is known.

If not known, say exactly what is ambiguous.

## Why autonomous fixing stopped

Explain which stop-policy condition applies.

## Suggested implementation direction

Give the safest likely technical direction, but do not pretend it is confirmed if it requires external knowledge.

## Acceptance criteria

- Regression test added.
- Build passes.
- Migrator.Tests pass.
- Generated output compiles when applicable.
- Report/TODO behavior is explicit when full migration is unsupported.
```

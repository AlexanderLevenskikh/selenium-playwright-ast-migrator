# Migrator Loop Continuation Rule

Drop-in files/patch snippets for the Migrator Autopilot Loop.

Purpose:

```text
compile errors fixed
→ project verify green
→ this is a safe checkpoint
→ continue with next migration category
```

This prevents the agent from stopping after compile/build becomes green while TODO/MISSING_MAPPING/UNSUPPORTED_ACTION work remains.

## Files

- `.agent-loops/08-continuation-rule.md` — new rule file.
- `.agent-loops/09-continue-after-compile-fix-prompt.txt` — prompt to continue after successful compile-fix batch.
- `.agent-loops/PATCH-01-autopilot-loop.md` — snippet to merge into `01-autopilot-loop.md`.
- `.agent-loops/PATCH-02-guardrails.md` — snippet to merge into `02-guardrails.md`.
- `.agent-loops/PATCH-03-stop-policy.md` — snippet to merge into `03-stop-policy.md`.

## Fast usage

Copy `.agent-loops/08-continuation-rule.md` into your existing `.agent-loops/` folder.

Then tell the agent:

```text
Read .agent-loops/08-continuation-rule.md.
Continue from .agent-loops/09-continue-after-compile-fix-prompt.txt.
```

## Recommended usage

Also merge the PATCH files into the corresponding existing loop docs.

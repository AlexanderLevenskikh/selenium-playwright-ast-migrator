# Audit Triage

Use when task is security/audit reduction.

## Classify each vulnerability

- severity;
- direct vs transitive;
- runtime vs dev dependency;
- fix availability;
- breaking vs non-breaking fix;
- affected package family;
- whether project likely ships/uses the vulnerable path.

## Priority order

1. Critical runtime vulnerabilities with safe fix.
2. High runtime vulnerabilities with safe fix.
3. Critical/high dev vulnerabilities affecting build/test tooling.
4. Transitive vulnerabilities fixable via direct parent update.
5. Transitive vulnerabilities fixable via targeted override/resolution.
6. Medium/low if task scope includes them.
7. No-fix vulnerabilities classified with evidence.

## Fix strategy priority

1. direct package update;
2. parent package update;
3. targeted transitive override/resolution;
4. package replacement only if unavoidable;
5. risk classification only when no safe fix exists.

## Verify

After each batch:

- audit count changed as expected;
- vulnerable version disappeared from dependency tree;
- build/tests still pass;
- lockfile does not contain unexpected old vulnerable versions.

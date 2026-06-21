# Dependency Security Guardrails

Mandatory rules.

## Safety

- Do not update all dependencies at once.
- Do not remove dependencies unless clearly unused and verified.
- Do not weaken tests, lint rules, type settings, or audit thresholds to pass checks.
- Do not delete lockfile and regenerate blindly.
- Do not change package manager unless explicitly requested.
- Do not change Node version unless required and documented.
- Do not silence security warnings without reducing or correctly classifying them.
- Do not replace libraries with alternatives unless unavoidable or explicitly requested.

## Security

- Prefer real vulnerability reduction over cosmetic audit changes.
- Confirm vulnerable versions disappeared from dependency tree.
- Treat `npm audit fix --force` and broad force upgrades as dangerous.
- Use force commands only if explicitly allowed.
- Do not accept a fix that reduces audit count by breaking build/test/runtime behavior.
- For no-fix vulnerabilities, classify them clearly.

## Lockfile

- Lockfile changes must correspond to package changes.
- Avoid unrelated lockfile churn.
- If lockfile churn is massive, investigate before proceeding.
- Prefer the install command used by CI.

## Code migration

- Make the minimal migration required by the package.
- Follow existing project patterns.
- Keep public behavior stable.
- Add/update tests when behavior is affected.
- Do not do unrelated refactoring.

## Autonomy

- Do not ask which package group to update next.
- Choose the safest next batch.
- Ask only when product behavior, manual QA scope, or externally owned policy is required.

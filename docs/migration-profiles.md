# Migration profiles

A migration profile is a reusable set of adapter config layers for a family of similar Selenium projects.

Recommended layout:

```text
profiles/
  infrastructure-base.adapter.json
  infrastructure-pom-rules.adapter.json
  projects/
    discounts.adapter.json
    billing.adapter.json
```

Use the common layers first and the project-specific layer last:

```powershell
--config profiles/infrastructure-base.adapter.json `
--config profiles/infrastructure-pom-rules.adapter.json `
--config profiles/projects/discounts.adapter.json
```

## What belongs in the base profile

- shared wrappers/helpers;
- shared source-only identifiers such as `page`, `pagef`, `Driver`, `WebDriver`;
- shared target-known types/identifiers;
- shared table/pagination mappings;
- shared method and parameterized method mappings;
- shared `LocatorSettings` and `TestHost` conventions.

## What belongs in the project profile

- concrete PageObject mappings;
- project-only selectors;
- project-only navigation helpers;
- project-specific `Verification.ProjectReferences`;
- local exceptions and overrides.

## Review policy

When an agent moves a rule into a base profile, it must explain why the rule is reusable across the project family.

If the rule is based on one project only and there is no evidence it is shared, keep it in the project profile.

## Profile match / reuse score

Для переиспользования профилей между похожими проектами используй режим `profile-match`:

```powershell
selenium-pw-migrator --mode profile-match --input "<tests>" --config "profiles/infrastructure-base.adapter.json" --config "profiles/projects/<project>.adapter.json" --out "profile-match-<project>" --format both
```

Он ничего не меняет, а оценивает, насколько текущий проект похож на уже готовый migration profile, какие правила профиля реально встречаются в source-коде и какие выражения остались не покрыты. Основной файл для агента: `agent-profile-reuse-task.md`.

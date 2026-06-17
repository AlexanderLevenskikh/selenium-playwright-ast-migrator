# Profile match / reuse score

`profile-match` helps decide whether an existing migration profile can be reused for a new Selenium test package or project.

Use it before a config-only migration pass when you have a base profile such as `profiles/infrastructure-base.adapter.json` and want to estimate how well it matches a new project.

```powershell
selenium-pw-migrator --mode profile-match `
  --input "C:\path\to\SeleniumTests" `
  --config "profiles/infrastructure-base.adapter.json" `
  --config "profiles/projects/discounts.adapter.json" `
  --out "profile-match-discounts" `
  --format both
```

The command is read-only. It does not modify source files, generated files, or adapter configs.

## Outputs

The command writes artifacts under the migration workspace:

```text
migration/profile-match-discounts/
  profile-match.md
  profile-match.json
  agent-profile-reuse-task.md
```

## What it checks

`profile-match` scans the source `.cs` files and compares them with each config layer:

- `UiTargets`
- `Methods`
- `ParameterizedMethods`
- `PageObjects`
- `Tables`
- `Pagination`
- `SourceOnlyIdentifiers`
- `TargetKnownTypes`
- `TargetKnownIdentifiers`
- scope-specific mappings

It reports:

- reuse score per layer;
- overall reuse score;
- matched rules;
- unused sample rules;
- high-frequency source expressions not covered by the profile;
- recommended next actions.

## How to read the score

| Score | Meaning | Suggested action |
|---:|---|---|
| `70%+` | High reuse potential | Use the base profile and add small project overrides. |
| `40–69%` | Partial reuse | Use the profile, then run an agent config-only pass for uncovered targets. |
| `1–39%` | Low signal | Treat the profile as reference material; start with `bootstrap-project` and `index-pom`. |
| `0%` | No meaningful signal | The profile may not match this project or the input path may be wrong. Run `doctor`. |

## Agent usage

Give the agent `agent-profile-reuse-task.md` after the command. The agent should:

1. keep common mappings in the base profile;
2. add only project-specific overrides to `profiles/projects/<project>.adapter.json`;
3. inspect POM/source truth for uncovered expressions;
4. run `config-validate`, `migrate`/`verify-project`, `guard`, and `config-diff` after changes;
5. escalate to a developer if reuse fails because of a generic migrator limitation.

## Important limitation

The score is heuristic. It is not a proof that generated Playwright tests will work. It answers a narrower question: “does this source project appear to use patterns covered by these profiles?”

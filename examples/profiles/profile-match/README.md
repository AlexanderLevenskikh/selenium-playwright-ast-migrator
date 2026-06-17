# Example: profile-match

```powershell
selenium-pw-migrator --mode profile-match `
  --input "./OldTests/DiscountsTests" `
  --config "../layering/infrastructure-base.adapter.json" `
  --config "../layering/projects/discounts.adapter.json" `
  --out "profile-match-discounts" `
  --format both
```

Open:

```text
migration/profile-match-discounts/profile-match.md
migration/profile-match-discounts/agent-profile-reuse-task.md
```

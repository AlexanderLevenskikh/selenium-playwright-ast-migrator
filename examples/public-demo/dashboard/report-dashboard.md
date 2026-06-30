# Public demo migration dashboard

This is a static sample of the dashboard produced by:

```bash
selenium-pw-migrator report serve --input migration/runs/public-demo-nunit --static-only --out migration/dashboard/public-demo
```

## Overview

| Metric | Value |
|---|---:|
| Source files | 1 |
| Tests | 1 |
| Generated files | 1 |
| Syntax errors | 0 |
| TODOs | 2 |
| Unsupported actions | 2 |
| Unmapped targets | 0 |

## Quality trend

| Run | Generated files | TODOs | Unsupported actions | Unmapped targets |
|---|---:|---:|---:|---:|
| public-demo-001 | 1 | 4 | 4 | 0 |
| public-demo-002 | 1 | 2 | 2 | 0 |

## TODO explorer

| Code | Count | Meaning | Next action |
|---|---:|---|---|
| `MIGRATOR:UNSUPPORTED_ACTION` | 2 | Navigation/setup helper calls need project semantics. | Add method mapping or target setup fixture. |

## Unsupported actions

| Source text | Likely owner |
|---|---|
| `Navigation.OpenLoginPage()` | config/profile |
| `page.Loader.ValidateLoading()` | config/profile |

## Unmapped targets

No unmapped UI targets in this demo. All field interactions use reviewed adapter config entries.

## What good looks like

A good first migration run does not need to be TODO-free. It should make uncertainty reviewable:

- no invented selectors;
- field interactions mapped from config/source evidence;
- setup/navigation uncertainty isolated as TODOs;
- generated output compiles after scaffold-specific package restore;
- next ticket is obvious and small.

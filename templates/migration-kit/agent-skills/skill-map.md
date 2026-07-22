# Migration agent skill map

| Situation | Skill | Owner |
|---|---|---|
| Continue through routine ambiguity | `plow-ahead` | orchestrator/executor |
| External API, SDK, framework, browser, CI, auth | `read-the-damn-docs` | orchestrator/executor |
| Audit claims and verification | `agent-watchdog` | watchdog/reviewer |
| Large TODO/log surface | `efficient-frontier` + `root-cause-prioritization` | orchestrator |
| Competing plans | `plan-arbiter` | orchestrator/reviewer |
| Final handoff | `quick-recap` | orchestrator/reviewer |

Always read `AGENT_CONTRACT.md` and `state/harness-policy.json`, and select only the skills needed for the current decision. Skill-use logging is optional diagnostics and is never acceptance evidence.

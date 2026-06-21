# Dependency Security Stop Policy

Continue autonomously unless a hard stop condition is met.

## Continue autonomously when

- several package groups exist and one can be safely prioritized;
- install/build/test errors are actionable;
- a failed batch can be split smaller;
- a transitive vulnerability can be fixed via parent update or targeted override;
- docs/config need small mechanical updates;
- type errors point to mechanical migration;
- audit still has unrelated remaining vulnerabilities.

## Stop only when

1. The same failure repeats after 3 serious attempts.
2. Fix requires product/business behavior decision.
3. Required private registry/network access is unavailable.
4. Package engine requirements conflict with project Node policy.
5. Peer dependency conflict has no safe resolution.
6. Vulnerability has no safe fix without major migration outside task scope.
7. Next step would require destructive or broad dependency churn.
8. Required commands/configs are missing and cannot be inferred.
9. Max iterations reached.
10. The selected task is complete and verified.

## Forbidden stop reasons

Do not stop with:

- "Which group should I update?"
- "Should I continue?"
- "There are multiple possible approaches."
- "I updated one package; want me to do the next?"
- "Tests failed; please advise."

Instead, read output, choose the safest next action, and continue.

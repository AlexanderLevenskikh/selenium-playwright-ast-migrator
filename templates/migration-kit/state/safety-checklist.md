# Safety Checklist

Use this checklist before keeping a batch.

## Must pass

- [ ] Generated project still compiles, or compile failure is the explicit ticket being fixed.
- [ ] Assertions/business checks were not silently removed.
- [ ] Empty tests are explicit TODO/inconclusive, not false-green.
- [ ] Generated files were not edited as the final fix.
- [ ] Source-only identifiers were not hidden by broad target-known declarations.
- [ ] New engine behavior has regression tests when possible.
- [ ] Config changes include representative evidence.

## Batch classification

- [ ] CONFIG_FIX
- [ ] ENGINE_FIX
- [ ] TARGET_PROJECT_INFRA
- [ ] SOURCE_TRUTH_MANUAL
- [ ] NEED_MORE_EVIDENCE
- [ ] UNSAFE_REVERTED

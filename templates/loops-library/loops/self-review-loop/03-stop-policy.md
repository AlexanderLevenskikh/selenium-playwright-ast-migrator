# Self Review Stop Policy

## Continue autonomously when

- a `BLOCKER` or `MAJOR` finding has a local safe fix;
- verification fails with actionable output;
- runtime smoke fails with actionable output;
- a test needs to be added/updated for the actual change;
- docs/config need small sync due to the change.

## Stop when

1. Maximum 2 review-fix cycles reached.
2. Remaining issue requires product/business decision.
3. Fix would broaden scope too much.
4. Environment blocks verification.
5. Final review has no `BLOCKER`/`MAJOR` and checks pass.
6. Serious issue remains but cannot be safely fixed locally.

## Do not loop forever

Do not create new improvement tasks from `MINOR` findings.

Report minor findings instead of repeatedly polishing.

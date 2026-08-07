# Suggested Config Patch

This is a review-first patch proposal. Do not auto-apply it blindly: check source truth, target POM/API, and generated evidence.

## Fix this profile mapping first

- **Root cause**: `ASSERTION_CONSTRAINT` / Review TODO [ASSERTION_CONSTRAINT]: convert constraint to Playwright assertion
- **Impact**: appears in `1` generated TODO/action site(s)
- **Confidence/evidence badge**: `low-impact` / `2` evidence link(s)
- **Suggested action**: Add reusable assertion mapping if this pattern appears often.

## UiTarget / mapping drafts

### Add mapping for button!

- Occurrences: `1`
- Confidence/evidence badge: `low-impact` / `2` evidence link(s)
- Suggested action: Find POM/source truth for this expression, then add a UiTarget/Method/ParameterizedMethod mapping in adapter-config.json.
```json
{
  "SourceExpression": "Add mapping for button!",
  "Target": "TODO_add mapping for button!",
  "RequiresReview": true
}
```

### Add source-backed mapping: map source expression to Playwright locator: button!

- Occurrences: `1`
- Confidence/evidence badge: `low-impact` / `2` evidence link(s)
- Suggested action: Find POM/source truth and add UiTarget/Method/ParameterizedMethod/Table/Pagination mapping.
```json
{
  "SourceExpression": "Add source-backed mapping: map source expression to Playwright locator: button!",
  "Target": "TODO_add source-backed mapping: map source expression to Playwright locator: button!",
  "RequiresReview": true
}
```

## Table/list mapping drafts

No table/list drafts were inferred.
## Method/helper drafts

No helper/method drafts were inferred.

# Suggested Config Patch

This is a review-first patch proposal. Do not auto-apply it blindly: check source truth, target POM/API, and generated evidence.

## Fix this profile mapping first

- **Root cause**: `MISSING_MAPPING` / Add mapping for WebDriver.FindElement(target)
- **Impact**: appears in `1` generated TODO/action site(s)
- **Confidence/evidence badge**: `low-impact` / `2` evidence link(s)
- **Suggested action**: Find POM/source truth for this expression, then add a UiTarget/Method/ParameterizedMethod mapping in adapter-config.json.

## UiTarget / mapping drafts

### Add mapping for WebDriver.FindElement(target)

- Occurrences: `1`
- Confidence/evidence badge: `low-impact` / `2` evidence link(s)
- Suggested action: Find POM/source truth for this expression, then add a UiTarget/Method/ParameterizedMethod mapping in adapter-config.json.
```json
{
  "SourceExpression": "Add mapping for WebDriver.FindElement(target)",
  "Target": "TODO_findElement(target)",
  "RequiresReview": true
}
```

## Table/list mapping drafts

No table/list drafts were inferred.
## Method/helper drafts

- `RAW_STATEMENT: method family `Id`` appears in `1` site(s). Confidence/evidence badge: `low-impact`. Next: Group all occurrences of this helper/method family; inspect source/helper body or run --mode helper-inventory before adding MethodSemantics/ParameterizedMethods.

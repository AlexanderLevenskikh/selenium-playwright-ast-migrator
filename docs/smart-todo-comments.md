# Smart TODO comments

Milestone 9 makes generated TODO comments more actionable while keeping backward compatibility with existing report counters.

The first line still starts with:

```csharp
// TODO:
```

so existing `TodoComments` metrics and tests keep working.

New comments append a stable machine-readable code and short guidance:

```csharp
// TODO: map source expression to Playwright locator: page.SaveButton [MIGRATOR:MISSING_MAPPING]
//   Reason: Source UI target has no adapter mapping yet.
//   Next: Find PageObject/source truth and add UiTarget/Table/Pagination mapping to adapter-config.
```

## Common codes

| Code | Meaning | Typical next step |
|---|---|---|
| `MISSING_MAPPING` | Source UI target was not mapped to a Playwright locator/action. | Find POM/source truth and add `UiTargets`, `Tables`, or `Pagination`. |
| `SOURCE_ONLY_IDENTIFIER` | Statement uses Selenium/source-only root such as `page`, `pagef`, `Driver`. | Map the whole expression or leave TODO; do not mark source-only roots target-known. |
| `UNRESOLVED_SYMBOL` | Statement depends on a variable/symbol blocked earlier. | Find the first TODO that blocked this symbol. |
| `UNAVAILABLE_SYMBOLS` | Statement references identifiers not known in target code. | Add real target-known type/identifier or map/comment the source expression. |
| `RAW_STATEMENT` | Source statement was not semantically recognized. | Add reusable Method/ParameterizedMethod mapping when safe. |
| `RAW_LOCAL_DECLARATION` | Local declaration depends on unresolved/source-side logic. | Map initializer or keep commented. |
| `MAPPED_REQUIRES_REVIEW` | Adapter config explicitly says mapping requires review. | Verify semantics and remove `RequiresReview` when safe. |
| `UNRESOLVED_PLACEHOLDER` | TargetStatements placeholder could not be substituted. | Fix `SourceMethodPattern`/placeholder names. |
| `ASSERTION_CONSTRAINT` | Assertion constraint was preserved because no target assertion mapping exists. | Add assertion mapping if pattern is common. |
| `TABLE_MAPPING_REQUIRED` | Table/list pattern lacks row target mapping. | Add `Tables` mapping with `RowTarget`. |
| `UNSUPPORTED_ACTION` | Recognizer/adapter cannot translate the action. | Classify as missing mapping, unsupported semantics, or generic migrator gap. |

## Agent rule

Agents should use the code to decide the next action. They must not hide a TODO by adding dummy declarations or by marking source-only symbols as target-known.

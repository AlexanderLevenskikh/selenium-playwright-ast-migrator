# Migrator Stop Policy

The agent must continue autonomously unless one of the hard stop conditions is met.

## Continue autonomously

Continue without asking the user when:

- multiple implementation options exist but one is safer or less invasive;
- naming can be inferred from existing code;
- formatting can be inferred from snapshots;
- tests fail with actionable output;
- a helper needs a small extension;
- a recognizer/resolver/renderer needs a local fix;
- generated output has TODOs outside the current selected category;
- adapter draft can represent the missing mapping;
- source behavior matches an existing pattern;
- build/test errors point to a clear fix;
- a regression test needs to be added;
- existing code has a nearby convention that can be followed;
- compile/build errors were fixed and the latest migration board still has actionable TODO/Unsupported/MissingMapping categories;
- a completed batch is ready for acceptance but the overall migration loop still has next work;
- the next phase involves migration-quality trade-offs that can be resolved with a small safe batch.

## Stop and ask/report

Stop only when:

1. Source behavior is genuinely ambiguous and unsafe to infer.
2. The fix requires product-specific or business-domain knowledge.
3. Required files or configs are missing and cannot be inferred.
4. The same failure repeats after 3 serious fix attempts.
5. A needed dependency/tool is unavailable.
6. The next step requires destructive action.
7. The task would require a broad architecture rewrite outside the selected block.
8. Maximum iterations are reached.
9. The selected migration block is complete and verified.

Migration-quality trade-off is not a stop reason by itself.
Stop only if the trade-off requires product/business semantics, unavailable source truth, destructive action, or another hard stop condition.

## Forbidden stop reasons

Do not stop with:

- "Which option do you prefer?"
- "Should I use approach A or B?"
- "Do you want me to continue?"
- "I can fix this next if you want."
- "There are several possible implementations."
- "This is probably enough."
- "I made partial progress."
- "Project verify is green, so the whole migration is done."
- "Generated code compiles, so I am finished."
- "Only warnings remain, so I should stop."
- "The next work is migration-quality improvement, so the user should decide."
- "There are TODO reduction trade-offs, so I should stop."

Instead, choose the safest approach and continue.

## Stop output

When stopping, always provide:

- exact status;
- exact blocker or completion reason;
- evidence from logs/tests/reports;
- changed files;
- commands run;
- recommended next step.

For `TICKET_NEEDED`, also provide a ticket-ready summary using `07-ticket-needed-template.md`.

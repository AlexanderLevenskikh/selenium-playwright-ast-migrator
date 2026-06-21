# Patch for .agent-loops/03-stop-policy.md

Add this to "Forbidden stop reasons".

Do not stop with:

- "Project verify is green, so the whole migration is done."
- "Generated code compiles, so I am finished."
- "Only warnings remain, so I should stop."

If the migration board still has actionable categories, continue.

Add this to "Continue autonomously".

Continue without asking when:

- compile/build errors were fixed and the latest migration board still has actionable TODO/Unsupported/MissingMapping categories;
- a completed batch is ready for acceptance but the overall migration loop still has next work.

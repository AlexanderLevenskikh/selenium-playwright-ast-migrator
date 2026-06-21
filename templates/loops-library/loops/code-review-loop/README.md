# Code Review Loop

Use this loop to review a diff, MR, PR, patch, or a set of changed files.

The goal is not to produce endless comments.
The goal is to give a useful engineering verdict.

## Success condition

The review is complete when:

- diff was inspected;
- intended behavior was understood;
- risks were classified;
- available checks were run or explicitly skipped with reason;
- comments are grouped by severity;
- final verdict is provided.

## This loop normally does not fix code

By default, this is review-only.

If the user asks to fix comments too, use `self-review-loop` or `ticket-fix-loop`.

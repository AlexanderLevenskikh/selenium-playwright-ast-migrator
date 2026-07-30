# `/supervised-task`

| Command | Behavior |
|---|---|
| `/supervised-task` | Start or resume the complete configured source and use up to five autonomous remediation cycles. |
| `/supervised-task continue` | Resume the latest evidence with a fresh five-cycle invocation budget; exhausted candidates stay exhausted without new evidence. |
| `/supervised-task continuous` | Automatically advance across five-cycle checkpoints until success or a real mandatory stop; no repeated `continue` command is required. |
| `/supervised-task continue continuous` | Explicit continuous continuation; equivalent cycle semantics to `continue`. |
| `/supervised-task <bounded request>` | Execute the requested cycle; add `continuous` to continue with other safe candidates afterward. |

One bounded change is allowed per cycle, not per invocation. Progress resets the no-progress streak. The first no-progress cycle tries a different independent candidate; two consecutive distinct no-progress cycles stop. Project-verification failures are tracked separately and block other work only when they prevent truthful measurement of every remaining candidate.

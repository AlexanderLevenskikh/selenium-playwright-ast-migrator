# `/supervised-task`

| Command | Behavior |
|---|---|
| `/supervised-task` | Start or resume the standard full-project run. |
| `/supervised-task continue` | Complete one bounded high-payoff improvement and rerun the same full pipeline. |
| `/supervised-task <bounded request>` | Apply the request without expanding source scope, then run the required checks. |

The command does not support partition planning or acceptance state. It stops on a concrete blocker, scope violation, required human decision, validation failure, or repeated no progress.

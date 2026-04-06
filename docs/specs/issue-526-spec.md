**Module**: docs/modules/session-runner.md (create new)

## Overview
This feature adds mechanical explanations for game combos to the playtest runner's output. To help playtesters understand what triggered a combo and what reward it provides, a descriptive explanation of the stat sequence is printed in two places: when a combo appears as a potential badge in the dialogue options list, and when the combo actually fires after a successful roll.

## Function Signatures

A new public method is added to `PlaytestFormatter`:

```csharp
namespace Pinder.SessionRunner
{
    public static class PlaytestFormatter
    {
        /// <summary>
        /// Returns a descriptive explanation of the sequence that triggers a given combo.
        /// </summary>
        public static string GetComboSequenceDescription(string comboName);
    }
}
```

## Input/Output Examples

| `comboName` (Input) | Return string |
|---------------------|---------------|
| `"The Setup"`       | `"You played Wit last turn, then Charm this turn — the sequence earns +1 bonus interest."` |
| `"The Reveal"`      | `"You played Charm last turn, then Honesty this turn — the sequence earns +1 bonus interest."` |
| `"The Read"`        | `"You played SA last turn, then Honesty this turn — the sequence earns +1 bonus interest."` |
| `"The Pivot"`       | `"You played Honesty last turn, then Chaos this turn — the sequence earns +1 bonus interest."` |
| `"The Escalation"`  | `"You played Chaos last turn, then Rizz this turn — the sequence earns +1 bonus interest."` |
| `"The Disarm"`      | `"You played Wit last turn, then Honesty this turn — the sequence earns +1 bonus interest."` |
| `"The Recovery"`    | `"You failed a roll last turn, then played SA this turn — the sequence earns +2 bonus interest."` |
| `"The Triple"`      | `"You played 3 different stats in 3 consecutive turns — your next roll gains +1 bonus."` |
| `null` or unknown   | `"Unknown combo sequence."` (Safe fallback) |

## Acceptance Criteria

### 1. Explanation in Option List
- When the `session-runner` prints the list of available dialogue options at the start of a turn, if an option has a pending combo (`opt.ComboName != null`), its explanation MUST be printed below the option text.
- The explanation must be prefixed with `> *Combo: ` and italicized.
- Example output:
  ```markdown
  **B)** CHARM +2 | 80% [Safe] | Combo: The Setup
  > "Is that so?"
  > *Combo: You played Wit last turn, then Charm this turn — the sequence earns +1 bonus interest.*
  ```

### 2. Explanation on Combo Trigger
- When a combo triggers after a roll (`result.ComboTriggered != null`), the session runner MUST output the explanation string immediately after the combo announcement blockquote.
- The explanation must be prefixed with `> *` to maintain the blockquote styling.
- Example output:
  ```markdown
  > *⭐ The Reveal combo fires!*
  > *You played Charm last turn, then Honesty this turn — the sequence earns +1 bonus interest.*
  ```

### 3. Separation of Concerns
- The mapping of combo string names to descriptive explanations MUST be fully contained within `session-runner/PlaytestFormatter.cs`.
- The core engine (`Pinder.Core.Conversation.ComboTracker` or `ComboResult`) MUST NOT be modified to return this descriptive text, keeping UI presentation logic out of the game engine.

## Edge Cases
- **Unknown/New Combos**: If a new combo name is added to the engine but not yet mapped in `PlaytestFormatter`, the method must return a safe fallback string (e.g. `"Unknown combo sequence."`) instead of crashing.
- **Missing Option Text**: If the option lacks `IntendedText`, the combo explanation must still be printed underneath the option's stat line.
- **Null Combo Name**: Handled gracefully and returns the fallback string.

## Error Conditions
- No exceptions should be thrown. Fallbacks must handle nulls or unrecognized strings cleanly.

## Dependencies
- Modifies `session-runner/Program.cs` printing loops (both the turn options block and the roll resolution block).

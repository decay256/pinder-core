# Contract: Issue #46 — Combo System (§15)

## Component
`ComboTracker` (Conversation/) + GameSession integration

## Dependencies
- #139 Wave 0: `RollEngine.Resolve(externalBonus)` for The Triple's +1 roll bonus

---

## ComboTracker

**File:** `src/Pinder.Core/Conversation/ComboTracker.cs`

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class ComboTracker
    {
        /// <summary>
        /// Record the result of a turn. Call once per Speak turn, in order.
        /// </summary>
        public void RecordTurn(StatType stat, bool success);

        /// <summary>
        /// Preview: what combo would complete if this stat is played and succeeds?
        /// Returns combo name or null. Does NOT mutate state.
        /// </summary>
        public string? PeekCombo(StatType stat);

        /// <summary>Name of last triggered combo, or null.</summary>
        public string? LastComboTriggered { get; }

        /// <summary>Interest bonus for last combo (0 if none or if The Triple).</summary>
        public int LastComboInterestBonus { get; }

        /// <summary>True if The Triple was triggered last turn → +1 to all rolls this turn.</summary>
        public bool TripleBonusActive { get; }

        /// <summary>Call at start of each turn to expire The Triple bonus.</summary>
        public void AdvanceTurn();
    }
}
```

---

## Combo Definitions

| Name | Sequence | Success Required | Bonus |
|---|---|---|---|
| The Setup | Wit → Charm | Completing stat | +1 Interest |
| The Reveal | Charm → Honesty | Completing stat | +1 Interest |
| The Read | SA → Honesty | Completing stat | +1 Interest |
| The Pivot | Honesty → Chaos | Completing stat | +1 Interest |
| The Recovery | Any fail → SA | SA must succeed | +2 Interest |
| The Escalation | Chaos → Rizz | Completing stat | +1 Interest |
| The Disarm | Wit → Honesty | Completing stat | +1 Interest |
| The Triple | 3 different stats in 3 turns | 3rd turn succeeds | +1 to ALL rolls next turn |

**Detection rules:**
- Two-stat combos: previous turn used stat A, current turn uses stat B and succeeds → combo fires
- The Recovery: previous turn was a failure (any stat), current turn uses SA and succeeds
- The Triple: last 3 turns used 3 distinct StatType values, 3rd turn succeeds
- A turn can trigger at most ONE combo (priority: specific combos > The Triple > The Recovery)
- Read/Recover/Wait turns do NOT contribute to combo tracking (only Speak)

---

## GameSession Integration

1. `StartTurnAsync`: Call `_comboTracker.AdvanceTurn()`. For each dialogue option, call `PeekCombo(option.Stat)` → set `DialogueOption.ComboName`
2. `ResolveTurnAsync`: After roll, call `_comboTracker.RecordTurn(stat, isSuccess)`. If combo triggered:
   - Add `LastComboInterestBonus` to interest delta
   - Set `TurnResult.ComboTriggered` = `LastComboTriggered`
3. Before rolling: if `TripleBonusActive`, add +1 to `externalBonus` param of `RollEngine.Resolve()`

---

## Behavioral Invariants
- Combo interest bonuses stack with SuccessScale, momentum, risk tier
- The Triple's +1 roll bonus is a one-turn effect applied via `externalBonus`
- Combo detection only fires on **success** of the completing stat
- `PeekCombo` assumes success (it previews "if you pick this and succeed")

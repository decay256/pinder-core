# Contract: Issue #46 — Combo System (§15)

## Component
`Pinder.Core.Conversation.ComboTracker` (new class)

## Depends on
- #139 Wave 0: `RollEngine.Resolve(externalBonus)` for The Triple's +1 roll bonus

## Maturity: Prototype

---

## ComboTracker

**File:** `src/Pinder.Core/Conversation/ComboTracker.cs`

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class ComboTracker
    {
        /// <summary>Record the stat used this turn and whether the roll succeeded.</summary>
        public void RecordTurn(StatType stat, bool succeeded);

        /// <summary>
        /// After RecordTurn, check if a combo completed this turn.
        /// Returns combo name and bonus, or null if no combo fired.
        /// Only returns a value when the completing roll succeeded.
        /// </summary>
        public ComboResult? CheckCombo();

        /// <summary>
        /// True if The Triple was completed last turn → +1 to all rolls this turn.
        /// Resets to false after being read (one-turn only).
        /// </summary>
        public bool HasTripleBonus { get; }

        /// <summary>
        /// Returns which combo(s) would be completed if the given stat were played and succeeded.
        /// Used by StartTurnAsync to preview combos on dialogue options.
        /// </summary>
        public string? PeekCombo(StatType stat);
    }

    public sealed class ComboResult
    {
        public string Name { get; }
        public int InterestBonus { get; }    // 0 for The Triple (roll bonus instead)
        public bool IsTriple { get; }        // true = +1 roll bonus next turn
    }
}
```

## Combo Definitions

| Name | Sequence | Interest Bonus |
|---|---|---|
| The Setup | Wit → Charm (success) | +1 |
| The Reveal | Charm → Honesty (success) | +1 |
| The Read | SA → Honesty (success) | +1 |
| The Pivot | Honesty → Chaos (success) | +1 |
| The Recovery | Any fail → SA (success) | +2 |
| The Escalation | Chaos → Rizz (success) | +1 |
| The Disarm | Wit → Honesty (success) | +1 |
| The Triple | 3 different stats in 3 turns (success on 3rd) | +1 roll bonus next turn |

## Behavioral Invariants
- Only fires when completing roll succeeds
- The Recovery: first element = any stat that failed (not a specific stat)
- The Triple: 3 distinct StatType values in last 3 turns; only 3rd must succeed
- ComboTracker is pure data — GameSession applies interest/roll bonus
- Multiple combos can match but only the highest-bonus one fires (or first if tied)

## Dependencies
- `StatType`

## Consumers
- `GameSession.ResolveTurnAsync()` and `GameSession.StartTurnAsync()` (peek)

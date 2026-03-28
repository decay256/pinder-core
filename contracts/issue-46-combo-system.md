# Contract: Issue #46 — Combo System

## Component
`Pinder.Core.Conversation.ComboTracker` — new class

## Dependencies
- #130 (`externalBonus` on RollEngine for The Triple)

## Files
- `Conversation/ComboTracker.cs` — new
- `Conversation/GameSession.cs` — wire into ResolveTurnAsync

## Interface

```csharp
namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Tracks stat history and detects combo completions.
    /// Stateful: call RecordTurn() after each roll to update history.
    /// </summary>
    public sealed class ComboTracker
    {
        public ComboTracker();

        /// <summary>
        /// Check if the given stat would complete a combo this turn.
        /// Call BEFORE recording the turn.
        /// </summary>
        /// <param name="stat">The stat about to be played.</param>
        /// <param name="isSuccess">Whether the roll succeeded.</param>
        /// <param name="lastRollFailed">Whether the previous turn was a failure (for The Recovery).</param>
        /// <returns>Combo name and interest bonus, or null if no combo.</returns>
        public ComboResult? CheckCombo(StatType stat, bool isSuccess, bool lastRollFailed);

        /// <summary>
        /// Record a completed turn. Updates stat history.
        /// </summary>
        public void RecordTurn(StatType stat, bool isSuccess);

        /// <summary>
        /// True if The Triple was triggered last turn, granting +1 to all rolls this turn.
        /// Cleared after the turn that benefits from it.
        /// </summary>
        public bool HasTripleBonusActive { get; }

        /// <summary>
        /// Consume the Triple bonus (call at start of turn that benefits).
        /// Returns 1 if active, 0 if not.
        /// </summary>
        public int ConsumeTripleBonus();

        /// <summary>
        /// Preview which combos each stat would complete (for UI ⭐ display).
        /// </summary>
        public Dictionary<StatType, string> GetPotentialCombos(bool lastRollFailed);
    }

    public sealed class ComboResult
    {
        /// <summary>Name of the combo (e.g. "The Setup").</summary>
        public string ComboName { get; }

        /// <summary>Interest bonus to add (+1 or +2). 0 for The Triple (roll bonus instead).</summary>
        public int InterestBonus { get; }

        /// <summary>True if this is The Triple (grants +1 roll bonus next turn instead of interest).</summary>
        public bool IsTriple { get; }

        public ComboResult(string comboName, int interestBonus, bool isTriple = false);
    }
}
```

## Combo Definitions (hardcoded constants)

| Name | History match | Bonus |
|---|---|---|
| The Setup | last=Wit, current=Charm, success | +1 interest |
| The Reveal | last=Charm, current=Honesty, success | +1 interest |
| The Read | last=SA, current=Honesty, success | +1 interest |
| The Pivot | last=Honesty, current=Chaos, success | +1 interest |
| The Recovery | lastFailed=true, current=SA, success | +2 interest |
| The Escalation | last=Chaos, current=Rizz, success | +1 interest |
| The Disarm | last=Wit, current=Honesty, success | +1 interest |
| The Triple | 3 different stats in last 3 turns, success on 3rd | +1 to all rolls next turn |

## Behavioral contracts
- Combos only fire on successful rolls (except checking prerequisites)
- The Triple checks the last 2 recorded stats + current stat = 3 distinct
- Multiple combos CAN trigger on the same turn (e.g. The Disarm and The Triple). Sum all interest bonuses.
- `HasTripleBonusActive` persists until consumed in the next turn's roll
- `RecordTurn()` must be called even for failed rolls (history tracking)

## Integration in GameSession.ResolveTurnAsync
1. Before roll: `int tripleBonus = _comboTracker.ConsumeTripleBonus();` → add to externalBonus
2. After roll: `var combo = _comboTracker.CheckCombo(stat, rollResult.IsSuccess, _lastRollFailed);`
3. If combo found: add `combo.InterestBonus` to interest delta; set `TurnResult.ComboTriggered`
4. After all processing: `_comboTracker.RecordTurn(stat, rollResult.IsSuccess);`
5. Store `_lastRollFailed = !rollResult.IsSuccess;`

## Integration in GameSession.StartTurnAsync
1. `var potentialCombos = _comboTracker.GetPotentialCombos(_lastRollFailed);`
2. For each returned DialogueOption, set `ComboName` if its stat matches a potential combo

## Consumers
GameSession

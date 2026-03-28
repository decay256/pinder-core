# Contract: Issue #46 — Combo System

## Component
`Pinder.Core.Conversation.ComboDetector` (new)

## Maturity
Prototype

---

## ComboDetector (`Pinder.Core.Conversation`)

Stateless detector that checks if the current stat play completes a combo, given recent stat history.

```csharp
namespace Pinder.Core.Conversation
{
    public static class ComboDetector
    {
        /// <summary>
        /// Check if playing `currentStat` after the given history completes a combo.
        /// Only triggers on successful rolls.
        /// </summary>
        /// <param name="statsHistory">Ordered list of stats played so far this session (oldest first).</param>
        /// <param name="currentStat">The stat being played this turn.</param>
        /// <param name="lastTurnWasFailure">True if the immediately preceding turn was a failure (for "The Recovery").</param>
        /// <returns>The completed combo, or null if none.</returns>
        public static ComboDefinition? Detect(
            IReadOnlyList<StatType> statsHistory,
            StatType currentStat,
            bool lastTurnWasFailure);

        /// <summary>
        /// Preview which combos would be completed by each possible stat choice.
        /// Used to annotate DialogueOptions before presenting to player.
        /// </summary>
        public static Dictionary<StatType, string?> PreviewCombos(
            IReadOnlyList<StatType> statsHistory,
            bool lastTurnWasFailure);
    }

    public sealed class ComboDefinition
    {
        public string Name { get; }
        public int InterestBonus { get; }       // +1 or +2 (immediate interest delta)
        public bool GrantsNextTurnBonus { get; } // true only for "The Triple" (+1 to all rolls next turn)

        public ComboDefinition(string name, int interestBonus, bool grantsNextTurnBonus = false);
    }
}
```

## Combo Table

| Combo | Sequence | Bonus | Notes |
|---|---|---|---|
| The Setup | Wit → Charm | +1 interest | 2-stat |
| The Reveal | Charm → Honesty | +1 interest | 2-stat |
| The Read | SA → Honesty | +1 interest | 2-stat |
| The Pivot | Honesty → Chaos | +1 interest | 2-stat |
| The Recovery | Any fail → SA | +2 interest | Requires prior failure |
| The Escalation | Chaos → Rizz | +1 interest | 2-stat |
| The Disarm | Wit → Honesty | +1 interest | 2-stat |
| The Triple | 3 different stats in 3 turns | +1 to all rolls next turn | 3-stat, NO immediate interest bonus |

## Behavioral Contract

- Combos only trigger on **successful** rolls. `GameSession` checks `rollResult.IsSuccess` before applying combo bonus.
- Multiple combos cannot fire on the same turn (first match wins, checked in table order).
- "The Triple" does NOT grant immediate interest bonus — it sets a flag that adds +1 to next turn's roll.
- `PreviewCombos` is called during `StartTurnAsync` to annotate `DialogueOption.ComboName`.

## Dependencies
- `StatType`, `SessionCounters.StatsUsedHistory` (from #44)

## Consumers
- `GameSession.ResolveTurnAsync` — calls `Detect()`, applies bonus to interest delta or sets next-turn flag
- `GameSession.StartTurnAsync` — calls `PreviewCombos()` to annotate options

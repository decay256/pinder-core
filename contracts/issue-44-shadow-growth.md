# Contract: Issue #44 — Shadow Growth Events

## Component
`Pinder.Core.Conversation.ShadowGrowthEvaluator` (new) + `GameSession` integration

## Maturity
Prototype

---

## ShadowGrowthEvaluator (`Pinder.Core.Conversation`)

Stateless evaluator that checks a turn's outcome and session state against the §7 growth table. Returns a list of shadow growth events to apply.

```csharp
namespace Pinder.Core.Conversation
{
    public static class ShadowGrowthEvaluator
    {
        /// <summary>
        /// Evaluate shadow growth events after a roll.
        /// </summary>
        /// <param name="result">The roll result from this turn.</param>
        /// <param name="tracker">Per-session tracking counters.</param>
        /// <returns>List of (ShadowStatType, amount, reason) tuples to apply.</returns>
        public static List<(ShadowStatType Shadow, int Amount, string Reason)> EvaluateAfterRoll(
            RollResult result,
            SessionCounters counters);

        /// <summary>
        /// Evaluate shadow growth events at end of game.
        /// </summary>
        public static List<(ShadowStatType Shadow, int Amount, string Reason)> EvaluateEndOfGame(
            GameOutcome outcome,
            SessionCounters counters);
    }
}
```

## SessionCounters (`Pinder.Core.Conversation`)

Per-session tracking state needed for shadow growth evaluation and other features.

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class SessionCounters
    {
        // Tracks number of TropeTrap activations this session
        public int TropeTrapsActivated { get; set; }

        // Whether any Honesty roll succeeded this session
        public bool AnyHonestySuccess { get; set; }

        // Stats used per turn (ordered list for combo/fixation tracking)
        public List<StatType> StatsUsedHistory { get; }

        // Number of SA uses this session
        public int SaUsageCount { get; set; }

        // First opener text (for "same opener twice" detection)
        public string? FirstOpenerText { get; set; }
        public string? SecondOpenerText { get; set; }

        // Number of turns where highest-modifier option was picked (consecutive)
        public int ConsecutiveHighestPickCount { get; set; }

        // Whether Chaos was ever picked this session
        public bool ChaosEverPicked { get; set; }

        // Read action failure count (for Overthinking growth)
        public int ReadFailCount { get; set; }

        // Recover action failure count (for Overthinking growth)
        public int RecoverFailCount { get; set; }

        public SessionCounters();
    }
}
```

## Growth Table (§7)

| Shadow | Trigger | Amount | When |
|---|---|---|---|
| Dread | Interest hits 0 (unmatch) | +2 | End of game |
| Dread | Getting ghosted | +1 | End of game |
| Dread | Catastrophic Wit fail (miss 10+) | +1 | After roll |
| Dread | Nat 1 on Wit | +1 | After roll |
| Madness | Nat 1 on Charm | +1 | After roll |
| Madness | 3+ trope traps in one conversation | +1 | After roll (when 3rd trap fires) |
| Madness | Same opener twice in a row | +1 | After turn 1 (when second opener matches first) |
| Denial | Date secured without any Honesty success | +1 | End of game |
| Denial | Nat 1 on Honesty | +1 | After roll |
| Fixation | Highest-% option picked 3 turns in a row | +1 | After roll |
| Fixation | Same stat used 3 turns in a row | +1 | After roll |
| Fixation | Never picked Chaos in whole conversation | +1 | End of game |
| Fixation | Nat 1 on Chaos | +1 | After roll |
| Fixation | 4+ different stats used (offset) | -1 | End of game |
| Overthinking | Read action failed | +1 | After roll (in ReadAsync) |
| Overthinking | Recover action failed | +1 | After roll (in RecoverAsync) |
| Overthinking | SA used 3+ times | +1 | After roll (when 3rd SA use) |
| Overthinking | Nat 1 on SA | +1 | After roll |

## Dependencies
- `SessionShadowTracker` (#139 Wave 0) — growth events are applied via `tracker.ApplyGrowth()`
- `RollResult`, `StatType`, `ShadowStatType`, `FailureTier`

## Consumers
- `GameSession.ResolveTurnAsync`, `ReadAsync`, `RecoverAsync` — call evaluator, apply results, populate `TurnResult.ShadowGrowthEvents`

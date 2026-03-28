# Contract: Issue #44 — Shadow Growth Events

## Component
Shadow growth trigger evaluation, wired into `GameSession`

## Dependencies
- #130 (`SessionShadowTracker`)
- #43 (Read/Recover actions — Overthinking triggers)

## Files
- `Conversation/ShadowGrowthProcessor.cs` — new, stateless evaluator
- `Conversation/SessionCounters.cs` — new, per-session tracking counters
- `Conversation/GameSession.cs` — wire growth evaluation

## Interface

### SessionCounters

```csharp
namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Per-session tracking counters for shadow growth trigger detection.
    /// Owned by GameSession, updated each turn.
    /// </summary>
    public sealed class SessionCounters
    {
        /// <summary>Number of TropeTrap tier activations this session.</summary>
        public int TropeTrapCount { get; set; }

        /// <summary>Whether any Honesty roll has succeeded this session.</summary>
        public bool HonestySucceeded { get; set; }

        /// <summary>Stats used each turn, in order. Index = turn number.</summary>
        public List<StatType> StatsUsedPerTurn { get; } = new List<StatType>();

        /// <summary>Number of SA rolls this session.</summary>
        public int SaUsageCount { get; set; }

        /// <summary>Whether Chaos was ever used this session.</summary>
        public bool ChaosUsed { get; set; }

        /// <summary>
        /// Track "highest-% option" picks. True if player picked the highest-modifier option.
        /// </summary>
        public List<bool> HighestOptionPicks { get; } = new List<bool>();
    }
}
```

### ShadowGrowthProcessor

```csharp
namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Evaluates shadow growth triggers. Stateless — all state passed in.
    /// Returns a list of growth events to apply.
    /// </summary>
    public static class ShadowGrowthProcessor
    {
        /// <summary>
        /// Evaluate per-turn growth triggers (called after each roll).
        /// </summary>
        /// <returns>List of (ShadowStatType, amount, reason) to apply.</returns>
        public static List<(ShadowStatType Shadow, int Amount, string Reason)> EvaluateTurnTriggers(
            RollResult roll,
            SessionCounters counters,
            InterestState currentInterestState);

        /// <summary>
        /// Evaluate end-of-game growth triggers.
        /// </summary>
        public static List<(ShadowStatType Shadow, int Amount, string Reason)> EvaluateEndOfGameTriggers(
            GameOutcome outcome,
            SessionCounters counters);
    }
}
```

### Growth trigger table (implemented in ShadowGrowthProcessor)

**Per-turn triggers** (EvaluateTurnTriggers):
| Condition | Shadow | Amount |
|-----------|--------|--------|
| Nat 1 on Charm | Madness | +1 |
| Nat 1 on Honesty | Denial | +1 |
| Nat 1 on Chaos | Fixation | +1 |
| Nat 1 on Wit | Dread | +1 |
| Nat 1 on SA | Overthinking | +1 |
| Nat 1 on Rizz | Horniness | +1 |
| Catastrophe on Wit (miss 10+) | Dread | +1 |
| TropeTrap count reaches 3 | Madness | +1 |
| Same stat 3 turns in a row | Fixation | +1 |
| Highest-% option 3 turns in a row | Fixation | +1 |
| SA used 3+ times this session | Overthinking | +1 |

**End-of-game triggers** (EvaluateEndOfGameTriggers):
| Condition | Shadow | Amount |
|-----------|--------|--------|
| Interest hit 0 (Unmatched) | Dread | +2 |
| Ghosted | Dread | +1 |
| Date secured with no Honesty success | Denial | +1 |
| Chaos never used | Fixation | +1 |
| 4+ different stats used → | Fixation | -1 (offset) |

**Note**: "Same opener twice in a row" (Madness +1) requires cross-session tracking → deferred to #56 ConversationRegistry.

### GameSession integration
1. Create `SessionCounters` in constructor
2. In `ResolveTurnAsync`:
   - Update counters (stat used, SA count, TropeTrap count, Honesty success, highest-% pick)
   - Call `ShadowGrowthProcessor.EvaluateTurnTriggers()`
   - Apply each event via `_shadowTracker.ApplyGrowth()`
   - Collect description strings for `TurnResult.ShadowGrowthEvents`
3. In end-of-game detection:
   - Call `ShadowGrowthProcessor.EvaluateEndOfGameTriggers()`
   - Apply events

### "Highest-% option" definition (prototype)
The "highest-%" option is the one whose stat has the highest effective modifier (`attacker.GetEffective(stat)`). In case of tie, it's the first in the array. The implementer compares the chosen option's stat modifier against all other options.

## Behavioral contracts
- ShadowGrowthProcessor is stateless and pure — easy to test
- SessionCounters is mutable, owned by GameSession
- Growth events applied via SessionShadowTracker (NOT StatBlock mutation)
- Multiple growth events can fire on a single turn (e.g. Nat 1 on Wit → Dread +1 AND Catastrophe on Wit → Dread +1 — though Nat 1 is Legendary tier not Catastrophe, so these are mutually exclusive)
- The -1 Fixation offset for 4+ different stats is evaluated at end-of-game only

## Consumers
GameSession, #45 (reads shadow values), #56 (cross-chat shadow bleed)

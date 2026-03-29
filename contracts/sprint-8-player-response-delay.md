# Contract: Issue #55 — PlayerResponseDelay

## Component
`Pinder.Core.Conversation.PlayerResponseDelayEvaluator` (new static class)

## Depends on
- #54: GameClock (provides delay duration)

## Maturity: Prototype

---

## PlayerResponseDelayEvaluator

**File:** `src/Pinder.Core/Conversation/PlayerResponseDelayEvaluator.cs`

```csharp
namespace Pinder.Core.Conversation
{
    public static class PlayerResponseDelayEvaluator
    {
        /// <summary>
        /// Pure function: evaluates interest penalty for player response delay.
        /// </summary>
        /// <param name="delay">Time since opponent's last message.</param>
        /// <param name="opponentStats">Opponent's stat block (Chaos modifies tolerance).</param>
        /// <param name="currentInterest">Current interest state.</param>
        /// <returns>Penalty result with interest delta and optional test trigger.</returns>
        public static DelayPenalty Evaluate(TimeSpan delay, StatBlock opponentStats, InterestState currentInterest);
    }

    public sealed class DelayPenalty
    {
        /// <summary>Interest delta to apply (0 or negative).</summary>
        public int InterestDelta { get; }

        /// <summary>Human-readable description of the penalty reason, or null if no penalty.</summary>
        public string? Reason { get; }

        /// <summary>True if the delay should trigger a "conversation test" event.</summary>
        public bool TriggersTest { get; }
    }
}
```

## Delay Penalty Table

| Delay | Base Penalty | Notes |
|---|---|---|
| < 1 minute | 0 | No penalty |
| 1–15 minutes | 0 | Normal reply speed |
| 15–60 minutes | −1 | Only if interest ≥ 16 (VeryIntoIt+) |
| 1–6 hours | −2 | Always applies |
| 6–24 hours | −3 | Always applies |
| 24+ hours | −5 | Always applies; triggers conversation test |

**Chaos modifier:** If opponent's Chaos stat ≥ 3, the penalty thresholds shift one tier more lenient (e.g., 15–60min becomes 0 regardless of interest). Implementation detail for #55 implementer.

## Dependencies
- `StatBlock` (from Stats/)
- `InterestState` (from Conversation/)

## Consumers
- `ConversationRegistry` (#56)
- `GameSession` (when host provides delay via clock)

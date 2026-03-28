# Contract: Issue #55 — PlayerResponseDelay

## Component
`Pinder.Core.Conversation.PlayerResponseDelayEvaluator` — pure stateless function

## Dependencies
- None (pure function). Uses `StatBlock` for reading opponent stats.

## Files
- `Conversation/PlayerResponseDelayEvaluator.cs` — new
- `Conversation/DelayPenalty.cs` — new

## Interface

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class DelayPenalty
    {
        /// <summary>Interest delta to apply (0 or negative).</summary>
        public int InterestDelta { get; }

        /// <summary>True if opponent sends a "thought you ghosted me" test message.</summary>
        public bool TriggerTest { get; }

        /// <summary>Prompt for the test message, or null.</summary>
        public string? TestPrompt { get; }

        public DelayPenalty(int interestDelta, bool triggerTest = false, string? testPrompt = null);
    }

    public static class PlayerResponseDelayEvaluator
    {
        /// <summary>
        /// Evaluate penalty for player response delay.
        /// Pure function — caller computes the TimeSpan.
        /// </summary>
        /// <param name="delay">Time between opponent's message and player's response.</param>
        /// <param name="opponentStats">Opponent's stat block (for Chaos base and shadow checks).</param>
        /// <param name="currentInterest">Current interest state (15-60min penalty only if ≥16).</param>
        public static DelayPenalty Evaluate(
            TimeSpan delay,
            StatBlock opponentStats,
            InterestState currentInterest);
    }
}
```

### Base penalty table
| Delay | Base penalty |
|-------|-------------|
| < 1 min | 0 |
| 1–15 min | 0 |
| 15–60 min | -1 (only if interest ≥ VeryIntoIt, i.e. value ≥ 16) |
| 1–6 hours | -2 |
| 6–24 hours | -3 |
| 24+ hours | -5 |

### Personality modifiers (applied after base penalty)
| Condition | Effect |
|-----------|--------|
| Opponent Chaos base stat ≥ 4 | Penalty = 0 (overrides everything) |
| Opponent Overthinking shadow ≥ 6 | Penalty += -1 (extra) |
| Opponent Fixation shadow ≥ 6 | Penalty *= 2 (doubled for irregular gaps) |
| Opponent Denial shadow ≥ 6 | Penalty unchanged (but TriggerTest = false — acts normal) |

### Application order
1. Compute base penalty from delay bucket
2. If Chaos base ≥ 4 → return penalty = 0
3. If Fixation shadow ≥ 6 → double penalty
4. If Overthinking shadow ≥ 6 → penalty -= 1
5. TriggerTest = true if delay is 1–6 hours AND penalty != 0

### Test trigger
When `TriggerTest = true`, the caller (GameSession or ConversationRegistry) should ask the LLM to generate a "testing" message from the opponent. This is a hint, not enforcement — the caller decides what to do.

## Behavioral contracts
- Pure function — no side effects, no state
- Penalty is always ≤ 0
- Chaos ≥ 4 overrides everything (penalty = 0)
- The 15–60min penalty only fires when interest ≥ VeryIntoIt (current interest value ≥ 16)
- InterestState enum is used for the check, not raw int (VeryIntoIt or AlmostThere)

## Consumers
GameSession (real-time mode), ConversationRegistry (async mode)

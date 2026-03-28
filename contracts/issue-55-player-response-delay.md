# Contract: Issue #55 — PlayerResponseDelay

## Component
`Pinder.Core.Conversation.PlayerResponseDelayEvaluator` (new), `DelayPenalty` (new)

## Maturity
Prototype

---

## PlayerResponseDelayEvaluator (`Pinder.Core.Conversation`)

Pure stateless function. Receives a pre-computed `TimeSpan` and opponent stats, returns penalty.

```csharp
namespace Pinder.Core.Conversation
{
    public static class PlayerResponseDelayEvaluator
    {
        /// <summary>
        /// Evaluate the interest penalty for a player's response delay.
        /// </summary>
        /// <param name="delay">Time the player took to respond (computed by caller).</param>
        /// <param name="opponentStats">Opponent's base StatBlock (for Chaos check).</param>
        /// <param name="opponentShadows">Opponent's shadow tracker (for Fixation/Overthinking checks). May be null.</param>
        /// <param name="currentInterestState">Current interest state (15–60min penalty only applies at ≥VeryIntoIt).</param>
        /// <returns>Penalty result.</returns>
        public static DelayPenalty Evaluate(
            TimeSpan delay,
            StatBlock opponentStats,
            SessionShadowTracker? opponentShadows,
            InterestState currentInterestState);
    }

    public sealed class DelayPenalty
    {
        /// <summary>Interest delta to apply (negative or zero).</summary>
        public int InterestDelta { get; }

        /// <summary>True if the delay should trigger a test message from opponent.</summary>
        public bool TriggerTest { get; }

        /// <summary>Optional test prompt flavor text, null if no test triggered.</summary>
        public string? TestPrompt { get; }

        public DelayPenalty(int interestDelta, bool triggerTest, string? testPrompt = null);
    }
}
```

## Penalty Table

| Delay | Base Penalty |
|---|---|
| < 1 min | 0 |
| 1–15 min | 0 |
| 15–60 min | -1 (only if Interest state ≥ VeryIntoIt, i.e., interest ≥ 16) |
| 1–6 hours | -2 |
| 6–24 hours | -3 |
| 24+ hours | -5 |

## Personality Modifiers (applied after base penalty, in order)

1. **Chaos base stat ≥ 4**: Penalty → 0. Chaos character doesn't care about delays.
2. **Fixation shadow ≥ 6**: Penalty is doubled.
3. **Overthinking shadow ≥ 6**: Penalty gets -1 extra.
4. **Denial shadow ≥ 6**: No penalty modification (penalty applies, but opponent acts like they didn't notice — flavor only, communicated via TestPrompt).

**Application order**: Check Chaos first (short-circuit to 0). Then apply Fixation doubling, then Overthinking extra.

## Test Trigger

`TriggerTest = true` when delay is 1–6 hours. Opponent may send a "thought you ghosted me" style message.
`TestPrompt` is a fixed string for prototype: "Thought you ghosted me..." (Denial variant: "I totally wasn't checking my phone...")

## Dependencies
- `StatBlock` (for `GetBase(StatType.Chaos)`)
- `SessionShadowTracker` (for shadow threshold checks — optional, nullable)
- `InterestState` (for 15–60min conditional)

## Consumers
- `ConversationRegistry` (#56) — calls after computing delay via `IGameClock`
- `GameSession` could also call directly if host provides the TimeSpan

# Contract: Issue #55 — PlayerResponseDelay

## Component
`PlayerResponseDelayEvaluator` (Conversation/) — pure stateless function

## Dependencies
- #54: `IGameClock` (caller uses clock to compute TimeSpan, but evaluator itself does not)

---

## PlayerResponseDelayEvaluator

**File:** `src/Pinder.Core/Conversation/PlayerResponseDelayEvaluator.cs`

```csharp
namespace Pinder.Core.Conversation
{
    public static class PlayerResponseDelayEvaluator
    {
        /// <summary>
        /// Evaluate the penalty for a player taking too long to respond.
        /// Pure function — does not measure time; receives computed delay.
        /// </summary>
        public static DelayPenalty Evaluate(
            TimeSpan delay,
            StatBlock opponentStats,
            InterestState currentInterest);
    }
}
```

---

## DelayPenalty

**File:** `src/Pinder.Core/Conversation/DelayPenalty.cs`

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class DelayPenalty
    {
        /// <summary>Interest delta to apply (negative or zero).</summary>
        public int InterestDelta { get; }

        /// <summary>True if delay is 1–6 hours → opponent may comment on the gap.</summary>
        public bool TriggerTest { get; }

        /// <summary>Optional prompt for the LLM test message. Null if no test.</summary>
        public string? TestPrompt { get; }

        public DelayPenalty(int interestDelta, bool triggerTest, string? testPrompt = null);
    }
}
```

---

## Penalty Table (base, before personality modifiers)

| Delay | Base Δ | Condition |
|---|---|---|
| < 1 min | 0 | — |
| 1–15 min | 0 | — |
| 15–60 min | -1 | Only if interest ≥ 16 (VeryIntoIt+) |
| 1–6 hours | -2 | TriggerTest = true |
| 6–24 hours | -3 | — |
| 24+ hours | -5 | — |

---

## Personality Modifiers

Applied to the base penalty:

| Condition | Check | Effect |
|---|---|---|
| Opponent Chaos base ≥ 4 | `opponentStats.GetBase(StatType.Chaos) >= 4` | Penalty = 0 (overrides everything) |
| Opponent Fixation shadow ≥ 6 | `opponentStats.GetShadow(ShadowStatType.Fixation) >= 6` | Penalty doubled |
| Opponent Overthinking shadow ≥ 6 | `opponentStats.GetShadow(ShadowStatType.Overthinking) >= 6` | Penalty +1 extra (more negative) |

**Application order:**
1. Compute base penalty from delay bucket
2. If Chaos ≥ 4 → return 0 (early exit)
3. If Fixation ≥ 6 → double the penalty
4. If Overthinking ≥ 6 → subtract 1 more
5. Return final penalty (always ≤ 0)

**Note:** Denial ≥ 6 is mentioned in the issue as "opponent acts like they didn't notice" — this is an LLM flavor instruction, not a mechanical effect. No code change needed for Denial.

---

## Behavioral Invariants
- Pure function: no state, no side effects
- Penalty is always ≤ 0 (or exactly 0)
- Chaos ≥ 4 completely nullifies all penalties
- Fixation doubling happens before Overthinking addition
- The 15–60 min penalty only applies when interest is high (≥16)
- TriggerTest is true only for the 1–6 hour bucket

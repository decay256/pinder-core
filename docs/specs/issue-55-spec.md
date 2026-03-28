# Spec: PlayerResponseDelay — Penalize Player for Slow Replies

**Issue:** #55  
**Component:** `Pinder.Core.Conversation`  
**Depends on:** #54 (GameClock — provides the delay duration)  
**Related:** #53 (OpponentTimingCalculator — similar pattern, opponent-side timing)

---

## 1. Overview

In Pinder's asynchronous messaging model, the player's reply speed affects the opponent's Interest. If the player takes too long to reply, the opponent loses interest — but the penalty is modified by the opponent's personality stats. `PlayerResponseDelayEvaluator` is a **pure function** that takes a delay duration, the opponent's stat block, and the current interest state, and returns a penalty result containing the interest delta and an optional conversational test trigger. It lives in `Pinder.Core.Conversation` and has zero side effects.

---

## 2. Function Signatures

All types are in `Pinder.Core.Conversation` unless otherwise noted. `StatBlock` is in `Pinder.Core.Stats`. `InterestState` is in `Pinder.Core.Conversation`.

### `PlayerResponseDelayEvaluator`

A static class (or class with a static method — matches the engine's stateless pattern used by `RollEngine`, `SuccessScale`, `FailureScale`).

```csharp
namespace Pinder.Core.Conversation
{
    public static class PlayerResponseDelayEvaluator
    {
        /// <summary>
        /// Evaluates the interest penalty for the player taking <paramref name="delay"/>
        /// to respond. The penalty is modified by the opponent's personality stats.
        /// </summary>
        /// <param name="delay">Time elapsed since the opponent's last message.
        ///     Provided by GameClock (issue #54). Must be non-negative.</param>
        /// <param name="opponentStats">The opponent's full StatBlock, used to check
        ///     Chaos base stat and shadow stat values (Denial, Fixation, Overthinking).</param>
        /// <param name="currentInterest">The current InterestState of the conversation,
        ///     used to gate the 15–60 min penalty bucket.</param>
        /// <returns>A DelayPenalty describing the interest delta and optional test trigger.</returns>
        public static DelayPenalty Evaluate(
            TimeSpan delay,
            StatBlock opponentStats,
            InterestState currentInterest);
    }
}
```

### `DelayPenalty`

A sealed class (NOT a record — netstandard2.0 / C# 8.0 constraint).

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class DelayPenalty
    {
        /// <summary>
        /// The interest change to apply. Always ≤ 0 (penalty) or 0 (no penalty).
        /// </summary>
        public int InterestDelta { get; }

        /// <summary>
        /// True when the delay is long enough to trigger a conversational test
        /// (e.g. opponent sends "thought you ghosted me").
        /// </summary>
        public bool TriggerTest { get; }

        /// <summary>
        /// Optional prompt hint for the LLM when TriggerTest is true.
        /// Null when TriggerTest is false.
        /// </summary>
        public string? TestPrompt { get; }

        public DelayPenalty(int interestDelta, bool triggerTest, string? testPrompt = null)
        {
            InterestDelta = interestDelta;
            TriggerTest = triggerTest;
            TestPrompt = testPrompt;
        }
    }
}
```

---

## 3. Input/Output Examples

### Example 1 — Short delay, no penalty
- **Input:** delay = 5 minutes, opponentStats = (Chaos base 2, all shadows 0), currentInterest = `Interested`
- **Output:** `DelayPenalty(interestDelta: 0, triggerTest: false, testPrompt: null)`

### Example 2 — 30-minute delay, interest below threshold
- **Input:** delay = 30 minutes, opponentStats = (Chaos base 2, all shadows 0), currentInterest = `Interested` (interest value somewhere in 5–15)
- **Output:** `DelayPenalty(interestDelta: 0, triggerTest: false, testPrompt: null)`
- **Rationale:** The 15–60 min bucket only applies when interest ≥ 16 (`VeryIntoIt` or `AlmostThere`).

### Example 3 — 30-minute delay, interest ≥ 16
- **Input:** delay = 30 minutes, opponentStats = (Chaos base 2, all shadows 0), currentInterest = `VeryIntoIt`
- **Output:** `DelayPenalty(interestDelta: -1, triggerTest: false, testPrompt: null)`

### Example 4 — 3-hour delay, triggers test
- **Input:** delay = 3 hours, opponentStats = (Chaos base 2, all shadows 0), currentInterest = `Interested`
- **Output:** `DelayPenalty(interestDelta: -2, triggerTest: true, testPrompt: <non-null string>)`

### Example 5 — 3-hour delay, high Chaos nullifies penalty
- **Input:** delay = 3 hours, opponentStats = (Chaos base 4, all shadows 0), currentInterest = `Interested`
- **Output:** `DelayPenalty(interestDelta: 0, triggerTest: false, testPrompt: null)`
- **Rationale:** Chaos base stat ≥ 4 → penalty = 0, which also means no test trigger (nothing to test if there's no penalty).

### Example 6 — 12-hour delay with Fixation doubling
- **Input:** delay = 12 hours, opponentStats = (Chaos base 2, Fixation shadow 6), currentInterest = `Interested`
- **Output:** `DelayPenalty(interestDelta: -6, triggerTest: false, testPrompt: null)`
- **Rationale:** Base penalty for 6–24h = −3. Fixation ≥ 6 doubles it → −6. TriggerTest only fires for 1–6h bucket.

### Example 7 — 2-hour delay with Overthinking +1
- **Input:** delay = 2 hours, opponentStats = (Chaos base 2, Overthinking shadow 6), currentInterest = `Interested`
- **Output:** `DelayPenalty(interestDelta: -3, triggerTest: true, testPrompt: <non-null string>)`
- **Rationale:** Base penalty for 1–6h = −2. Overthinking ≥ 6 adds +1 → total −3. Still in 1–6h bucket so TriggerTest = true.

### Example 8 — 2-hour delay with both Fixation AND Overthinking
- **Input:** delay = 2 hours, opponentStats = (Chaos base 2, Fixation shadow 7, Overthinking shadow 8), currentInterest = `Interested`
- **Output:** `DelayPenalty(interestDelta: -5, triggerTest: true, testPrompt: <non-null string>)`
- **Rationale:** Base = −2. Fixation doubles → −4. Overthinking adds +1 → −5. (Fixation applies first via doubling, then Overthinking adds.)

### Example 9 — 48-hour delay (24+ hours)
- **Input:** delay = 48 hours, opponentStats = (Chaos base 2, all shadows 0), currentInterest = `Bored`
- **Output:** `DelayPenalty(interestDelta: -5, triggerTest: false, testPrompt: null)`

---

## 4. Acceptance Criteria

### AC1: `PlayerResponseDelayEvaluator.Evaluate` exists
The static method `PlayerResponseDelayEvaluator.Evaluate(TimeSpan, StatBlock, InterestState)` must exist in `Pinder.Core.Conversation` and return a `DelayPenalty`.

### AC2: Correct penalty per delay bucket
The base interest delta (before personality modifiers) must follow this table:

| Delay Range | Base Interest Δ | Condition |
|---|---|---|
| < 1 minute | 0 | — |
| 1 minute to < 15 minutes | 0 | — |
| 15 minutes to < 60 minutes | −1 | **Only if** `currentInterest` is `VeryIntoIt` or `AlmostThere` (i.e., interest value ≥ 16). Otherwise 0. |
| 1 hour to < 6 hours | −2 | — |
| 6 hours to < 24 hours | −3 | — |
| 24 hours or more | −5 | — |

Boundary precision: use `TimeSpan` comparison. Delay of exactly 1 minute falls in the 1–15 min bucket (penalty 0). Delay of exactly 15 minutes falls in the 15–60 min bucket. Delay of exactly 60 minutes falls in the 1–6h bucket. Delay of exactly 6 hours falls in the 6–24h bucket. Delay of exactly 24 hours falls in the 24+ bucket.

### AC3: Chaos base stat ≥ 4 reduces penalty to 0
If the opponent's **base stat** for `StatType.Chaos` (via `opponentStats.GetBase(StatType.Chaos)`) is ≥ 4, the entire penalty is zeroed. The returned `DelayPenalty` has `InterestDelta = 0`, `TriggerTest = false`, `TestPrompt = null`. This check takes priority over all other personality modifiers — if Chaos ≥ 4, no other modifiers are evaluated.

### AC4: Fixation shadow ≥ 6 doubles penalty
If the opponent's shadow stat `ShadowStatType.Fixation` (via `opponentStats.GetShadow(ShadowStatType.Fixation)`) is ≥ 6, the base penalty is **doubled** (e.g. −2 becomes −4). This is applied before the Overthinking modifier.

### AC5: Overthinking shadow ≥ 6 applies +1 additional penalty
If the opponent's shadow stat `ShadowStatType.Overthinking` (via `opponentStats.GetShadow(ShadowStatType.Overthinking)`) is ≥ 6, the penalty magnitude increases by 1 (e.g. −2 becomes −3, or if Fixation already doubled to −4, becomes −5). Applied after Fixation doubling.

### AC6: Test trigger fires at 1–6h delay
When the delay falls in the 1-hour-to-less-than-6-hours bucket **and** the final penalty is non-zero (i.e., Chaos did not zero it), `TriggerTest` must be `true` and `TestPrompt` must be a non-null string. For all other buckets, `TriggerTest` must be `false` and `TestPrompt` must be `null`.

### AC7: `DelayPenalty` is a sealed class, NOT a record
`DelayPenalty` must be declared as `public sealed class` with a constructor, not as a `record` (records require C# 9+, which is unavailable on netstandard2.0 with LangVersion 8.0).

### AC8: Tests pass and build is clean
Unit tests must cover each delay bucket, each personality modifier in isolation, and the Chaos base stat boundary (value 3 → penalty applies; value 4 → penalty zeroed). The project must compile with `dotnet build` with zero warnings/errors.

---

## 5. Edge Cases

### Delay boundaries
- `TimeSpan.Zero` → 0 penalty (< 1 min bucket)
- `TimeSpan.FromSeconds(59)` → 0 penalty (< 1 min bucket)
- `TimeSpan.FromMinutes(1)` → 0 penalty (1–15 min bucket)
- `TimeSpan.FromMinutes(14.999)` → 0 penalty (1–15 min bucket)
- `TimeSpan.FromMinutes(15)` → −1 if interest ≥ 16, else 0
- `TimeSpan.FromMinutes(59.999)` → −1 if interest ≥ 16, else 0
- `TimeSpan.FromMinutes(60)` → −2 (1–6h bucket)
- `TimeSpan.FromHours(5.999)` → −2 (1–6h bucket)
- `TimeSpan.FromHours(6)` → −3 (6–24h bucket)
- `TimeSpan.FromHours(23.999)` → −3 (6–24h bucket)
- `TimeSpan.FromHours(24)` → −5 (24+ bucket)
- Very large delays (e.g. 30 days) → −5 (same as 24+)

### Interest state gating for 15–60 min bucket
- `InterestState.Unmatched` → 0 (not ≥ 16)
- `InterestState.Bored` → 0 (not ≥ 16)
- `InterestState.Interested` → 0 (not ≥ 16)
- `InterestState.VeryIntoIt` → −1 (interest 16–20)
- `InterestState.AlmostThere` → −1 (interest 21–24)
- `InterestState.DateSecured` → 0 (game is already over; however, the evaluator should still return −1 if called — or 0 because the game is over. **Recommendation:** treat `DateSecured` as ≥ 16, so return −1. The caller (GameSession) should not call this after DateSecured, but the evaluator should not special-case it.)

### Chaos base stat boundary
- `GetBase(StatType.Chaos)` = 3 → penalty applies normally
- `GetBase(StatType.Chaos)` = 4 → penalty = 0
- `GetBase(StatType.Chaos)` = 5 → penalty = 0
- Note: this checks the **base** stat, not the effective stat (shadow-reduced). `GetBase()`, not `GetEffective()`.

### Shadow stat boundaries
- `GetShadow(ShadowStatType.Fixation)` = 5 → no doubling
- `GetShadow(ShadowStatType.Fixation)` = 6 → doubling applies
- `GetShadow(ShadowStatType.Overthinking)` = 5 → no +1
- `GetShadow(ShadowStatType.Overthinking)` = 6 → +1 applies

### Multiple personality modifiers active simultaneously
- Fixation ≥ 6 AND Overthinking ≥ 6: Apply Fixation (double) first, then Overthinking (+1). E.g. base −2 → doubled to −4 → +1 = −5.
- Chaos ≥ 4 overrides everything: even if Fixation and Overthinking thresholds are met, result is 0.

### Denial shadow ≥ 6
Per the issue, Denial ≥ 6 means "penalty applies to Interest but opponent acts like they didn't notice." This is a **narrative** effect, not a mechanical modifier. The `InterestDelta` is unchanged. This could be signaled via `TestPrompt` or a separate field, but the issue does not define a mechanical change. **Recommendation for implementer:** Denial ≥ 6 does NOT modify `InterestDelta`. It could optionally set a flag or `TestPrompt` hint for the LLM, but this is not mechanically specified. For prototype maturity, ignore Denial — document it as a known gap for future narrative integration.

### Negative delay
- If `delay` is negative (`TimeSpan` can represent negative durations), treat as 0 penalty. The method should not throw; a negative delay simply means no time has passed.

### Zero-stat opponent
- An opponent with all base stats = 0 and all shadow stats = 0: all modifiers are inactive, pure base penalty applies.

---

## 6. Error Conditions

| Condition | Expected Behavior |
|---|---|
| `delay` is negative | Return `DelayPenalty(0, false, null)` — no penalty |
| `opponentStats` is null | Throw `ArgumentNullException` with parameter name `"opponentStats"` |
| `currentInterest` is an undefined enum value | Treat as "not ≥ 16" for the 15–60 min bucket (i.e., no special-case penalty). Alternatively, use the default switch arm. Do NOT throw. |

The method should never return null. It always returns a valid `DelayPenalty` instance.

---

## 7. Dependencies

### Internal (Pinder.Core)
| Dependency | Namespace | Usage |
|---|---|---|
| `StatBlock` | `Pinder.Core.Stats` | Read opponent's Chaos base stat and shadow stat values |
| `StatType` | `Pinder.Core.Stats` | Enum value `StatType.Chaos` for Chaos base stat lookup |
| `ShadowStatType` | `Pinder.Core.Stats` | Enum values `Fixation`, `Overthinking`, `Denial` for shadow lookups |
| `InterestState` | `Pinder.Core.Conversation` | Enum parameter to gate the 15–60 min penalty bucket |

### External
- **None.** This is a pure function with zero external dependencies, zero NuGet packages, and zero I/O.

### Upstream issues
- **#54 (GameClock):** Provides the `TimeSpan` delay value that is passed into `Evaluate()`. The evaluator does not measure time itself — it receives a pre-computed duration.
- **#53 (OpponentTimingCalculator):** Related but independent. Computes opponent reply delays. `PlayerResponseDelayEvaluator` computes penalties for player reply delays. They do not call each other.

---

## 8. Modifier Application Order

This section clarifies the exact order of operations inside `Evaluate`:

1. **Determine delay bucket** → look up base penalty from the table in AC2.
2. **Apply interest gate** for 15–60 min bucket: if `currentInterest` is not `VeryIntoIt` or `AlmostThere` (or `DateSecured`), base penalty for this bucket = 0.
3. **Check Chaos override**: if `opponentStats.GetBase(StatType.Chaos) >= 4`, return `DelayPenalty(0, false, null)` immediately.
4. **Apply Fixation doubling**: if `opponentStats.GetShadow(ShadowStatType.Fixation) >= 6`, multiply penalty by 2 (e.g. −2 → −4).
5. **Apply Overthinking addition**: if `opponentStats.GetShadow(ShadowStatType.Overthinking) >= 6`, subtract 1 more (e.g. −4 → −5).
6. **Determine TriggerTest**: set `TriggerTest = true` only if the delay is in the 1–6h bucket AND the final `InterestDelta` is non-zero. Set `TestPrompt` to a descriptive string (e.g. `"Opponent noticed the long gap"`) when triggering.
7. **Return** the `DelayPenalty`.

Note: If the base penalty from step 1–2 is already 0 (e.g. < 15 min delay), skip steps 4–5 and return `DelayPenalty(0, false, null)`.

---

## 9. Test Prompt Content

The `TestPrompt` string is a hint for the LLM layer. For prototype maturity, the exact string content is not critical — it just needs to be non-null when `TriggerTest` is true. A reasonable default: `"Opponent noticed the long gap between replies"`. The LLM adapter will use this to flavor the opponent's next message. Future iterations may make this more personality-specific.

# Spec: Issue #55 — PlayerResponseDelay — Penalize Player for Slow Replies

## Overview

`PlayerResponseDelayEvaluator` is a **pure, stateless function** that computes an interest penalty based on how long the player takes to respond to an opponent's message. The delay `TimeSpan` is computed externally by the caller (the Unity host in real-time mode, or `ConversationRegistry` via `IGameClock` in async mode). The evaluator receives the delay, the opponent's `StatBlock`, and the current `InterestState`, then returns a `DelayPenalty` containing the interest delta, whether a "test" message should fire, and an optional prompt for that test. Personality modifiers (Chaos base stat, Fixation/Overthinking shadow stats) alter the base penalty.

## Function Signatures

### `PlayerResponseDelayEvaluator` (static class)

**Namespace:** `Pinder.Core.Conversation`
**File:** `src/Pinder.Core/Conversation/PlayerResponseDelayEvaluator.cs`

```csharp
public static class PlayerResponseDelayEvaluator
{
    /// <summary>
    /// Evaluate the interest penalty for a player taking <paramref name="delay"/> to respond.
    /// Pure function — does not measure time; receives a pre-computed delay.
    /// </summary>
    /// <param name="delay">Elapsed time since the opponent's last message. Must be non-negative.</param>
    /// <param name="opponentStats">The opponent's StatBlock (used for personality modifier checks).</param>
    /// <param name="currentInterest">The current InterestState of the conversation.</param>
    /// <returns>A <see cref="DelayPenalty"/> with the computed interest delta and test trigger info.</returns>
    public static DelayPenalty Evaluate(
        TimeSpan delay,
        StatBlock opponentStats,
        InterestState currentInterest);
}
```

**Parameter types:**
- `delay`: `System.TimeSpan` — non-negative duration
- `opponentStats`: `Pinder.Core.Stats.StatBlock` — the opponent's stat block (immutable)
- `currentInterest`: `Pinder.Core.Conversation.InterestState` — enum value representing current interest band

**Return type:** `Pinder.Core.Conversation.DelayPenalty`

---

### `DelayPenalty` (sealed class)

**Namespace:** `Pinder.Core.Conversation`
**File:** `src/Pinder.Core/Conversation/DelayPenalty.cs`

```csharp
public sealed class DelayPenalty
{
    /// <summary>Interest delta to apply. Always ≤ 0.</summary>
    public int InterestDelta { get; }

    /// <summary>True when the delay is in the 1–6 hour bucket, signaling the opponent may comment on the gap.</summary>
    public bool TriggerTest { get; }

    /// <summary>Optional LLM prompt for the test message. Null when TriggerTest is false.</summary>
    public string? TestPrompt { get; }

    public DelayPenalty(int interestDelta, bool triggerTest, string? testPrompt = null);
}
```

`DelayPenalty` is a **sealed class** (not a record — netstandard2.0 / C# 8.0 constraint). It is immutable after construction.

---

## Penalty Table (Base Penalties)

The delay is classified into buckets. Boundary semantics use **inclusive lower, exclusive upper** unless stated otherwise:

| Bucket | Delay Range | Base Interest Δ | Condition | TriggerTest |
|--------|-------------|-----------------|-----------|-------------|
| Instant | `delay < 1 minute` | 0 | — | false |
| Quick | `1 min ≤ delay < 15 min` | 0 | — | false |
| Medium | `15 min ≤ delay < 60 min` | −1 | **Only if** `currentInterest` is `VeryIntoIt` or `AlmostThere` (i.e., interest value ≥ 16). Otherwise 0. | false |
| Long | `1 hour ≤ delay < 6 hours` | −2 | — | **true** |
| VeryLong | `6 hours ≤ delay < 24 hours` | −3 | — | false |
| Ghosting | `delay ≥ 24 hours` | −5 | — | false |

**Boundary precision:** Comparisons use `TimeSpan.TotalMinutes` and `TimeSpan.TotalHours`. Exact boundaries:
- 1 minute = `TimeSpan.FromMinutes(1)`
- 15 minutes = `TimeSpan.FromMinutes(15)`
- 60 minutes = `TimeSpan.FromMinutes(60)` = 1 hour
- 6 hours = `TimeSpan.FromHours(6)`
- 24 hours = `TimeSpan.FromHours(24)`

---

## Personality Modifiers

Personality modifiers are checked against the opponent's `StatBlock` and alter the base penalty. They are applied in a strict order:

### Application Order

1. **Compute base penalty** from the delay bucket table above.
2. **Chaos check (early exit):** If `opponentStats.GetBase(StatType.Chaos) >= 4`, return `DelayPenalty(interestDelta: 0, triggerTest: <from bucket>, testPrompt: null)`. Chaos nullifies all interest penalties but preserves the TriggerTest flag from the bucket.
3. **Fixation doubling:** If `opponentStats.GetShadow(ShadowStatType.Fixation) >= 6`, multiply the base penalty by 2 (e.g., −2 becomes −4).
4. **Overthinking addition:** If `opponentStats.GetShadow(ShadowStatType.Overthinking) >= 6`, subtract 1 more from the penalty (e.g., −4 becomes −5).
5. **Return** final `DelayPenalty` with the computed delta (always ≤ 0).

### Modifier Details

| Modifier | Stat Check | Threshold | Mechanical Effect |
|----------|-----------|-----------|-------------------|
| Chaos (base) | `opponentStats.GetBase(StatType.Chaos)` | ≥ 4 | Penalty forced to 0. Opponent doesn't care about response time. |
| Fixation (shadow) | `opponentStats.GetShadow(ShadowStatType.Fixation)` | ≥ 6 | Penalty is doubled (more negative). Opponent fixates on the delay. |
| Overthinking (shadow) | `opponentStats.GetShadow(ShadowStatType.Overthinking)` | ≥ 6 | Penalty worsened by −1. Opponent assumed the worst during the gap. |
| Denial (shadow) | `opponentStats.GetShadow(ShadowStatType.Denial)` | ≥ 6 | **No mechanical effect.** This is an LLM flavor instruction only — the opponent acts like they didn't notice. No code needed. |

**Important:** Fixation and Overthinking can stack. If both shadows are ≥ 6 with a base penalty of −2, the result is: −2 × 2 = −4, then −4 − 1 = −5.

---

## Input/Output Examples

### Example 1: Quick reply, no penalty
- **Input:** `delay = TimeSpan.FromMinutes(5)`, `opponentStats = {Chaos: 2, Fixation: 0, Overthinking: 0}`, `currentInterest = InterestState.Interested`
- **Output:** `DelayPenalty(interestDelta: 0, triggerTest: false, testPrompt: null)`

### Example 2: Medium delay, low interest — no penalty
- **Input:** `delay = TimeSpan.FromMinutes(30)`, `opponentStats = {Chaos: 2, Fixation: 0, Overthinking: 0}`, `currentInterest = InterestState.Interested`
- **Output:** `DelayPenalty(interestDelta: 0, triggerTest: false, testPrompt: null)`
- **Reason:** Interest is not ≥ 16 (Interested = 5–15), so the 15–60 min penalty does not apply.

### Example 3: Medium delay, high interest — penalty applies
- **Input:** `delay = TimeSpan.FromMinutes(30)`, `opponentStats = {Chaos: 2, Fixation: 0, Overthinking: 0}`, `currentInterest = InterestState.VeryIntoIt`
- **Output:** `DelayPenalty(interestDelta: -1, triggerTest: false, testPrompt: null)`
- **Reason:** Interest ≥ 16, so the −1 penalty applies.

### Example 4: Long delay — test trigger fires
- **Input:** `delay = TimeSpan.FromHours(3)`, `opponentStats = {Chaos: 2, Fixation: 0, Overthinking: 0}`, `currentInterest = InterestState.Interested`
- **Output:** `DelayPenalty(interestDelta: -2, triggerTest: true, testPrompt: <non-null string>)`

### Example 5: Very long delay
- **Input:** `delay = TimeSpan.FromHours(12)`, `opponentStats = {Chaos: 2, Fixation: 0, Overthinking: 0}`, `currentInterest = InterestState.Interested`
- **Output:** `DelayPenalty(interestDelta: -3, triggerTest: false, testPrompt: null)`

### Example 6: Ghosting delay (24+ hours)
- **Input:** `delay = TimeSpan.FromHours(48)`, `opponentStats = {Chaos: 2, Fixation: 0, Overthinking: 0}`, `currentInterest = InterestState.Interested`
- **Output:** `DelayPenalty(interestDelta: -5, triggerTest: false, testPrompt: null)`

### Example 7: Chaos ≥ 4 nullifies penalty
- **Input:** `delay = TimeSpan.FromHours(48)`, `opponentStats = {Chaos: 4, Fixation: 0, Overthinking: 0}`, `currentInterest = InterestState.Interested`
- **Output:** `DelayPenalty(interestDelta: 0, triggerTest: false, testPrompt: null)`
- **Reason:** Chaos base stat ≥ 4, so penalty is forced to 0.

### Example 8: Fixation doubles penalty
- **Input:** `delay = TimeSpan.FromHours(3)`, `opponentStats = {Chaos: 2, Fixation: 6, Overthinking: 0}`, `currentInterest = InterestState.Interested`
- **Output:** `DelayPenalty(interestDelta: -4, triggerTest: true, testPrompt: <non-null string>)`
- **Reason:** Base −2, doubled by Fixation to −4.

### Example 9: Fixation + Overthinking stack
- **Input:** `delay = TimeSpan.FromHours(3)`, `opponentStats = {Chaos: 2, Fixation: 6, Overthinking: 6}`, `currentInterest = InterestState.Interested`
- **Output:** `DelayPenalty(interestDelta: -5, triggerTest: true, testPrompt: <non-null string>)`
- **Reason:** Base −2, doubled to −4, then −1 more = −5.

### Example 10: Chaos boundary — 3 does NOT nullify
- **Input:** `delay = TimeSpan.FromHours(3)`, `opponentStats = {Chaos: 3, Fixation: 0, Overthinking: 0}`, `currentInterest = InterestState.Interested`
- **Output:** `DelayPenalty(interestDelta: -2, triggerTest: true, testPrompt: <non-null string>)`
- **Reason:** Chaos = 3 < 4, so the penalty applies normally.

### Example 11: Zero delay
- **Input:** `delay = TimeSpan.Zero`, `opponentStats = {any}`, `currentInterest = InterestState.Interested`
- **Output:** `DelayPenalty(interestDelta: 0, triggerTest: false, testPrompt: null)`

---

## Acceptance Criteria

### AC1: `PlayerResponseDelayEvaluator.Evaluate` exists as a pure function
- The method is `public static` on a `public static class`.
- It takes `TimeSpan delay`, `StatBlock opponentStats`, and `InterestState currentInterest`.
- It returns a `DelayPenalty`.
- It has no side effects: no fields, no state, no I/O.

### AC2: Correct penalty per delay bucket
- Each of the 6 delay buckets produces the correct base `InterestDelta` as specified in the penalty table.
- The 15–60 min bucket returns 0 when interest is below `VeryIntoIt`.
- Boundary values (exactly 1 min, exactly 15 min, exactly 60 min, exactly 6 hours, exactly 24 hours) fall into the correct bucket per inclusive-lower / exclusive-upper semantics.

### AC3: Chaos base stat ≥ 4 reduces penalty to 0
- When `opponentStats.GetBase(StatType.Chaos) >= 4`, the returned `InterestDelta` is always 0, regardless of delay bucket.
- When Chaos = 3, penalties apply normally.

### AC4: Fixation shadow ≥ 6 doubles penalty
- When `opponentStats.GetShadow(ShadowStatType.Fixation) >= 6` and Chaos < 4, the base penalty is multiplied by 2.
- When Fixation = 5, no doubling occurs.

### AC5: Overthinking shadow ≥ 6 applies +1 additional penalty
- When `opponentStats.GetShadow(ShadowStatType.Overthinking) >= 6` and Chaos < 4, 1 is subtracted from the (possibly Fixation-doubled) penalty.
- Fixation and Overthinking stack: base × 2 − 1.

### AC6: Test trigger fires at 1–6h delay
- `TriggerTest` is `true` when delay falls in the 1–6 hour bucket.
- `TriggerTest` is `false` for all other buckets.
- When `TriggerTest` is `true`, `TestPrompt` should be a non-null string suitable for prompting the LLM to generate a "thought you ghosted me" style message.

### AC7: `DelayPenalty` is a sealed class, NOT a record
- `DelayPenalty` must be declared as `public sealed class`.
- It must have a constructor `DelayPenalty(int interestDelta, bool triggerTest, string? testPrompt = null)`.
- Properties `InterestDelta`, `TriggerTest`, and `TestPrompt` must be get-only.

### AC8: Build clean
- The project compiles without errors or warnings under `dotnet build` targeting netstandard2.0.
- All existing tests (254+) continue to pass.

---

## Edge Cases

| Case | Expected Behavior |
|------|-------------------|
| `delay` is `TimeSpan.Zero` | Returns `DelayPenalty(0, false, null)` |
| `delay` is negative (e.g., clock skew) | Treat as 0 — return `DelayPenalty(0, false, null)`. Negative delays should not penalize the player. |
| `delay` is exactly 1 minute | Falls into the "Quick" bucket (1 min ≤ delay < 15 min) → penalty 0 |
| `delay` is exactly 15 minutes | Falls into the "Medium" bucket (15 min ≤ delay < 60 min) |
| `delay` is exactly 60 minutes | Falls into the "Long" bucket (1 hour ≤ delay < 6 hours) → penalty −2, TriggerTest true |
| `delay` is exactly 6 hours | Falls into the "VeryLong" bucket (6 hours ≤ delay < 24 hours) → penalty −3 |
| `delay` is exactly 24 hours | Falls into the "Ghosting" bucket (≥ 24 hours) → penalty −5 |
| `delay` is `TimeSpan.MaxValue` | Falls into "Ghosting" bucket → penalty −5 (with any applicable personality modifiers) |
| Base penalty is 0, Fixation ≥ 6 | 0 × 2 = 0 — no penalty even with Fixation |
| Base penalty is 0, Overthinking ≥ 6 | 0 − 1 = −1 — Overthinking adds its −1 even when base is 0 |
| Chaos ≥ 4 AND Fixation ≥ 6 | Chaos early-exit returns 0 — Fixation is never checked |
| `currentInterest` is `AlmostThere` with 15–60 min delay | Penalty −1 applies (AlmostThere implies interest 21–24, which is ≥ 16) |
| `currentInterest` is `DateSecured` with 15–60 min delay | `DateSecured` = interest 25. Since 25 ≥ 16, the −1 penalty applies. However, the game is effectively over at DateSecured, so this is a theoretical edge case. |
| `currentInterest` is `Unmatched` | Game is already over (interest = 0). The function still returns a penalty per the bucket table, but it has no practical effect. |

---

## Error Conditions

| Condition | Expected Behavior |
|-----------|-------------------|
| `opponentStats` is `null` | Throw `ArgumentNullException`. StatBlock is required for personality modifier checks. |
| `currentInterest` is an undefined enum value | Undefined behavior — callers must pass valid `InterestState` values. No explicit validation required. |
| `delay` is negative | Treat as zero delay (no penalty). See edge cases above. |

---

## Dependencies

| Dependency | Type | Usage |
|------------|------|-------|
| `Pinder.Core.Stats.StatBlock` | Internal class | Read opponent's Chaos base stat and Fixation/Overthinking shadow stats |
| `Pinder.Core.Stats.StatType` | Internal enum | `StatType.Chaos` for the Chaos base stat check |
| `Pinder.Core.Stats.ShadowStatType` | Internal enum | `ShadowStatType.Fixation`, `ShadowStatType.Overthinking` for shadow checks |
| `Pinder.Core.Conversation.InterestState` | Internal enum | Determines whether the 15–60 min conditional penalty applies |
| `System.TimeSpan` | .NET BCL | Input parameter for delay duration |
| **No external NuGet packages** | — | Zero-dependency constraint per project rules |

### Upstream Dependencies (not consumed by this component directly)
- **Issue #54 (`IGameClock`)**: The caller (`ConversationRegistry` or host) uses `IGameClock` to compute the `TimeSpan` delay before passing it to `Evaluate()`. The evaluator itself has no clock dependency.

### Downstream Consumers
- **`GameSession`**: Calls `PlayerResponseDelayEvaluator.Evaluate()` and applies the returned `InterestDelta` to `InterestMeter`.
- **`ConversationRegistry`**: In async/multi-session mode, computes the delay via `IGameClock` and calls the evaluator.

---

## Notes

- **Denial shadow ≥ 6** is mentioned in the issue but has **no mechanical effect**. It is purely an LLM flavor instruction ("opponent acts like they didn't notice"). No code handles Denial in this component.
- **TestPrompt content**: The exact string for `TestPrompt` when `TriggerTest` is `true` is implementation-discretion. A reasonable default is something like `"The opponent noticed you took a while to respond."` — it serves as context for the LLM to generate an in-character reaction.
- **Modifier application on zero base**: When the base penalty is 0 (e.g., quick reply), Fixation doubling has no effect (0 × 2 = 0). However, Overthinking still subtracts 1 (0 − 1 = −1). This means a player with a very fast reply can still receive a −1 penalty if the opponent has Overthinking ≥ 6. This is per the rules — Overthinking opponents assume the worst regardless.

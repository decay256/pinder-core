# Spec: Session Runner — Increase Max Turns and Report Projected Outcome on Cutoff

**Issue:** #417
**Module**: docs/modules/session-runner.md (create new)

---

## Overview

The session runner currently hard-codes a 15-turn cap, which cuts sessions short before they can reach a natural conclusion (e.g. session 008 ended at Interest 15/25 with Momentum 4 — likely winnable in 1–2 more turns). This feature raises the default cap to 20, and when the cap is reached before a natural game outcome (DateSecured / Unmatched / Ghost), outputs a projected-outcome block so playtest reviewers can assess whether the match was trending toward success or failure.

**Dependency:** This issue depends on #414 (CLI arg parsing), which owns the `--max-turns` CLI argument with default 20. This issue adds **only** the projection/reporting logic when the cap is hit.

---

## Function Signatures

### `OutcomeProjector` (new static class)

```csharp
namespace Pinder.SessionRunner
{
    /// <summary>
    /// Pure function: given final game state at max-turn cutoff,
    /// produces a human-readable projected outcome string.
    /// </summary>
    public static class OutcomeProjector
    {
        /// <summary>
        /// Project the likely game outcome given the state when the turn cap was hit.
        /// </summary>
        /// <param name="interest">Current interest value (0–25).</param>
        /// <param name="momentum">Current consecutive-success streak (0+).</param>
        /// <param name="turnsPlayed">Number of turns completed before cutoff.</param>
        /// <param name="maxTurns">The turn cap that was hit.</param>
        /// <returns>A human-readable projection string (never null or empty).</returns>
        public static string Project(
            int interest,
            int momentum,
            int turnsPlayed,
            int maxTurns);
    }
}
```

### `Program.cs` changes (session summary block)

No new public methods. The existing session-summary section in `Program.cs` is extended:

- When the game loop exits because `turn >= maxTurns` **and** `finalOutcome` is `null` (no natural game-over), the summary block changes from the current format to:

```markdown
## Session Summary
**⏸️ Incomplete ({turnsPlayed}/{maxTurns} turns) | Interest: {n}/25 | Total XP: {xp}**

Projected: {OutcomeProjector.Project(interest, momentum, turnsPlayed, maxTurns)}
```

- When the game ends naturally (DateSecured, Unmatched, Ghost), the summary format is **unchanged**.

---

## Input/Output Examples

### OutcomeProjector.Project examples

| interest | momentum | turnsPlayed | maxTurns | Output |
|----------|----------|-------------|----------|--------|
| 22 | 4 | 20 | 20 | `"Likely DateSecured"` |
| 18 | 1 | 20 | 20 | `"Probable DateSecured with continued play"` |
| 12 | 2 | 20 | 20 | `"Uncertain — could go either way"` |
| 7 | 0 | 15 | 15 | `"Trending toward Unmatched"` |
| 3 | 0 | 20 | 20 | `"Likely Unmatched or Ghost"` |
| 20 | 3 | 20 | 20 | `"Likely DateSecured"` |
| 16 | 0 | 20 | 20 | `"Probable DateSecured with continued play"` |
| 15 | 4 | 20 | 20 | `"Uncertain — could go either way"` |
| 9 | 0 | 20 | 20 | `"Trending toward Unmatched"` |
| 0 | 0 | 20 | 20 | `"Likely Unmatched or Ghost"` |

### Session summary output example (cutoff)

```
## Session Summary
**⏸️ Incomplete (20/20 turns) | Interest: 15/25 | Total XP: 143**

Projected: Uncertain — could go either way
```

### Session summary output example (natural end — unchanged)

```
## Session Summary
**✅ DateSecured | Turns: 14 | Total XP: 287**
```

---

## Acceptance Criteria

### AC1: Default max turns raised to 20

**Owned by #414.** This issue (#417) assumes `--max-turns` already exists with default 20. The game loop in `Program.cs` must use the parsed `maxTurns` value (from #414) instead of the hardcoded `15`.

**Verification:** Run the session runner with no `--max-turns` argument. The loop should allow up to 20 turns before cutting off.

### AC2: `--max-turns` arg accepted

**Owned by #414.** This issue assumes the arg already exists. No work required here.

### AC3: On cutoff — report Interest, Momentum, projected outcome text

When the game loop exits because `turn >= maxTurns` and no natural `GameOutcome` has been reached (`finalOutcome == null`):

1. The summary header uses the ⏸️ icon and the word "Incomplete" (not a `GameOutcome` enum value).
2. The header includes `{turnsPlayed}/{maxTurns} turns` (not just `Turns: {n}`).
3. The header includes `Interest: {n}/25`.
4. The header includes `Total XP: {xp}`.
5. A "Projected:" line follows, containing the output of `OutcomeProjector.Project(interest, momentum, turnsPlayed, maxTurns)`.

**Format:**
```
**⏸️ Incomplete ({turnsPlayed}/{maxTurns} turns) | Interest: {interest}/25 | Total XP: {xp}**

Projected: {projectionText}
```

### AC4: Build clean

All changes compile with zero warnings and zero errors. All existing tests pass.

---

## Projection Heuristics

`OutcomeProjector.Project` uses the following decision table, evaluated **top to bottom** (first match wins):

| Condition | Return value |
|-----------|-------------|
| `interest >= 20` **and** `momentum >= 3` | `"Likely DateSecured"` |
| `interest >= 16` | `"Probable DateSecured with continued play"` |
| `interest >= 10` **and** `interest <= 15` | `"Uncertain — could go either way"` |
| `interest >= 5` **and** `interest <= 9` | `"Trending toward Unmatched"` |
| `interest < 5` | `"Likely Unmatched or Ghost"` |

Notes:
- Momentum is only a differentiator in the top tier (`>= 20` interest). In all other brackets, momentum is ignored. This is intentionally simple for prototype maturity.
- The `turnsPlayed` and `maxTurns` parameters are accepted for future use (e.g. turns-remaining estimation) but are **not used** in the current heuristic.

---

## Edge Cases

1. **Interest exactly at boundary values (0, 5, 10, 16, 20, 25):**
   - `interest = 0` → `"Likely Unmatched or Ghost"`
   - `interest = 4` → `"Likely Unmatched or Ghost"`
   - `interest = 5` → `"Trending toward Unmatched"`
   - `interest = 9` → `"Trending toward Unmatched"`
   - `interest = 10` → `"Uncertain — could go either way"`
   - `interest = 15` → `"Uncertain — could go either way"`
   - `interest = 16` → `"Probable DateSecured with continued play"`
   - `interest = 19` → `"Probable DateSecured with continued play"`
   - `interest = 20, momentum = 2` → `"Probable DateSecured with continued play"` (momentum < 3, falls through to second rule)
   - `interest = 20, momentum = 3` → `"Likely DateSecured"`
   - `interest = 25` → should never occur at cutoff (DateSecured triggers at 25), but if it does: `"Likely DateSecured"` (matches first or second rule)

2. **Momentum values:**
   - `momentum = 0` with `interest = 22` → `"Probable DateSecured with continued play"` (momentum < 3)
   - `momentum = 5` with `interest = 22` → `"Likely DateSecured"`
   - Negative momentum should not occur (GameSession tracks streak ≥ 0), but treat as 0.

3. **Natural game-over before cutoff:** When the game ends via DateSecured, Unmatched, or Ghost (`finalOutcome != null`), the projection block is **not printed**. The existing summary format is used unchanged.

4. **`maxTurns = 0`:** Degenerate case. Loop never executes. `turnsPlayed = 0`, `interest = 10` (starting value). Projection: `"Uncertain — could go either way"`. Not a practical scenario but should not crash.

5. **`maxTurns = 1`:** Single-turn session. Interest may have moved ±1–5 from starting 10. Projection works normally based on resulting interest.

---

## Error Conditions

1. **`OutcomeProjector.Project` receives out-of-range interest:**
   - Interest is clamped to 0–25 by `InterestMeter`. If a value outside this range is somehow passed, the heuristic still returns a sensible result (negative values → `"Likely Unmatched or Ghost"`; values > 25 → `"Likely DateSecured"`). No exception is thrown.

2. **#414 not merged (missing `--max-turns` arg):**
   - This issue depends on #414. If #414 is not merged, the `maxTurns` variable does not exist in `Program.cs`. The implementer must wait for #414 or coordinate. This is a **build-time error**, not a runtime one.

3. **No crashes on projection:** `OutcomeProjector.Project` is a pure function with no I/O, no exceptions, and no null returns. It always returns a non-empty string.

---

## Dependencies

| Dependency | Type | Detail |
|------------|------|--------|
| #414 (CLI arg parsing + CharacterLoader) | **Hard — must merge first** | #414 adds the `--max-turns` CLI argument with default 20 and the `maxTurns` variable used by the game loop. #417 adds projection logic only. |
| `Pinder.Core.Conversation.GameSession` | Read-only | `session.TotalXpEarned` for summary output. No changes to GameSession. |
| `Pinder.Core.Conversation.GameOutcome` | Read-only | Used to detect natural end vs cutoff (`finalOutcome == null` means cutoff). |
| `Pinder.Core.Conversation.InterestMeter` | Conceptual | Interest range 0–25, starting at 10. No code dependency from OutcomeProjector. |

---

## Files Changed

| File | Change type | Description |
|------|-------------|-------------|
| `session-runner/OutcomeProjector.cs` | **New** | Static class with `Project(int, int, int, int) → string` |
| `session-runner/Program.cs` | **Modified** | Session summary block updated to show projection on cutoff; game loop uses `maxTurns` variable from #414 instead of hardcoded `15` |

---

## Non-Functional Requirements

- **Maturity:** Prototype — no performance, latency, or reliability targets.
- **Backward compatibility:** When the game ends naturally (not via cutoff), the output format is unchanged.
- **Determinism:** `OutcomeProjector.Project` is a pure function — same inputs always produce the same output.

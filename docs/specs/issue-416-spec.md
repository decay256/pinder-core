# Spec: ScoringPlayerAgent — Shadow Growth Risk Scoring

**Issue:** #416  
**Module:** docs/modules/session-runner.md (create new)

---

## Overview

The `ScoringPlayerAgent` currently scores dialogue options purely on expected interest gain, ignoring the shadow growth consequences of each pick. This spec adds four shadow-aware scoring adjustments: a **Fixation growth penalty** (avoid using the same stat three turns in a row), a **Denial growth penalty** (avoid skipping Honesty when available), a **Fixation threshold EV reduction** (Chaos options become less attractive as Fixation climbs), and a **stat variety bonus** (prefer stats not used in the last two turns). These adjustments make the scorer behave more like a skilled human player who manages shadow risk alongside interest gain.

Additionally, `PlayerAgentContext` gains three new fields (`LastStatUsed`, `SecondLastStatUsed`, `HonestyAvailableLastTurn`) to carry the stat history needed for these calculations. All new fields have defaults so existing constructor calls remain backward-compatible.

---

## Function Signatures

### PlayerAgentContext — New Properties

```csharp
namespace Pinder.SessionRunner
{
    public sealed class PlayerAgentContext
    {
        // ... existing properties unchanged ...

        /// <summary>
        /// The StatType used by the player on the previous turn.
        /// Null on the first turn or if unknown.
        /// </summary>
        public StatType? LastStatUsed { get; }

        /// <summary>
        /// The StatType used by the player two turns ago.
        /// Null on the first or second turn, or if unknown.
        /// </summary>
        public StatType? SecondLastStatUsed { get; }

        /// <summary>
        /// Whether at least one Honesty option was available on the previous turn.
        /// False on the first turn or if unknown.
        /// </summary>
        public bool HonestyAvailableLastTurn { get; }

        // Constructor gains three new optional parameters
        // (appended to existing params, all with defaults):
        public PlayerAgentContext(
            StatBlock playerStats,
            StatBlock opponentStats,
            int currentInterest,
            InterestState interestState,
            int momentumStreak,
            string[] activeTrapNames,
            int sessionHorniness,
            Dictionary<ShadowStatType, int>? shadowValues,
            int turnNumber,
            StatType? lastStatUsed = null,           // NEW
            StatType? secondLastStatUsed = null,     // NEW
            bool honestyAvailableLastTurn = false     // NEW
        );
    }
}
```

### ScoringPlayerAgent — No Signature Changes

The public interface remains `IPlayerAgent.DecideAsync(TurnStart, PlayerAgentContext) → Task<PlayerDecision>`. All changes are internal to the scoring logic within `DecideAsync`. Four new named constants are added:

```csharp
// New scoring constants (named, not magic numbers)
private const float FixationGrowthPenalty = 0.5f;
private const float DenialGrowthPenalty = 0.3f;
private const float FixationT1EvMultiplier = 0.8f;  // multiply expectedGainOnSuccess
private const float StatVarietyBonus = 0.1f;
```

---

## Input/Output Examples

### Example 1: Fixation Growth Penalty

**Context:**
- `LastStatUsed = StatType.Chaos`
- `SecondLastStatUsed = StatType.Chaos`
- Options: `[Charm, Chaos, Wit, Honesty]`

**Effect on Chaos option:**
- Base score computed normally (e.g., EV = 1.2)
- Fixation growth penalty applied: `score -= 0.5` → score becomes 0.7
- Other options unaffected by this penalty

### Example 2: Denial Growth Penalty

**Context:**
- Options: `[Charm, Honesty, Wit, Chaos]` (Honesty IS available)

**Effect on non-Honesty options:**
- Charm: `score -= 0.3`
- Wit: `score -= 0.3`
- Chaos: `score -= 0.3`
- Honesty: no penalty

### Example 3: Fixation Threshold — Tier 2

**Context:**
- `ShadowValues = { Fixation: 14 }` (≥12, Tier 2)
- Options include a Chaos option with `successChance = 0.65`

**Effect on Chaos option:**
- Disadvantage applied to success chance: roll twice, take lower
- Adjusted success chance ≈ `0.65 * 0.65 = 0.4225` (probability of both rolls succeeding)
- This replaces the normal `successChance` in the EV calculation for this option

### Example 4: Fixation Threshold — Tier 1

**Context:**
- `ShadowValues = { Fixation: 8 }` (≥6 and <12, Tier 1)
- Options include a Chaos option with `expectedGainOnSuccess = 2.5`

**Effect on Chaos option:**
- `expectedGainOnSuccess *= 0.8` → 2.0
- Success chance is NOT modified (no disadvantage at T1)

### Example 5: Stat Variety Bonus

**Context:**
- `LastStatUsed = StatType.Charm`
- `SecondLastStatUsed = StatType.Wit`
- Options: `[Charm, Rizz, Wit, Honesty]`

**Effect:**
- Charm: no variety bonus (used last turn)
- Rizz: `score += 0.1` (not used recently)
- Wit: no variety bonus (used two turns ago)
- Honesty: `score += 0.1` (not used recently)

### Example 6: Combined Adjustments

**Context:**
- `LastStatUsed = StatType.Chaos`, `SecondLastStatUsed = StatType.Chaos`
- `ShadowValues = { Fixation: 14 }`
- Options: `[Chaos, Honesty, Wit]`

**Effect on Chaos option:**
- Fixation growth penalty: `score -= 0.5`
- Denial growth penalty: `score -= 0.3` (Honesty is available, picking Chaos skips it)
- Fixation T2 disadvantage: `successChance` squared
- No variety bonus (Chaos used last two turns)
- Cumulative: significantly deprioritized

---

## Acceptance Criteria

### AC1: Fixation Growth Penalty

When `context.LastStatUsed` and `context.SecondLastStatUsed` are both non-null and both equal to the option's `Stat`, the option's score is reduced by `FixationGrowthPenalty` (0.5).

**Rationale:** Game rule §7 states that using the same stat 3 turns in a row triggers +1 Fixation growth. The scorer should avoid triggering this.

**Verification:** Given two consecutive Chaos turns (`LastStatUsed == SecondLastStatUsed == Chaos`), a Chaos option scores 0.5 lower than it would without the penalty, while a Charm option is unaffected.

### AC2: Denial Growth Penalty

When at least one option in the current turn has `Stat == StatType.Honesty`, every option whose `Stat != StatType.Honesty` has its score reduced by `DenialGrowthPenalty` (0.3).

**Rationale:** Game rule §7 states that skipping Honesty when it is available triggers +1 Denial growth. The scorer should prefer Honesty when present to avoid shadow growth.

**Note:** This penalty is evaluated based on the *current* turn's options, not the previous turn. The `HonestyAvailableLastTurn` field exists for potential future use (e.g., tracking whether the player already skipped Honesty last turn, which could compound the penalty). For this implementation, the penalty is applied based on current-turn Honesty availability only.

**Verification:** Given options `[Charm, Honesty, Wit]`, Charm and Wit each score 0.3 lower than they would without the penalty, while Honesty is unaffected.

### AC3: Fixation Threshold EV Reduction

When `context.ShadowValues` is non-null and contains a `ShadowStatType.Fixation` entry, Chaos options are penalized based on the Fixation value:

- **Fixation ≥ 12 (Tier 2+):** Apply simulated disadvantage to the Chaos option's `successChance`. Disadvantage means rolling twice and taking the worse result. For probability purposes: `adjustedSuccessChance = successChance * successChance` (probability both independent rolls succeed).
- **Fixation ≥ 6 and < 12 (Tier 1):** Multiply the Chaos option's `expectedGainOnSuccess` by `FixationT1EvMultiplier` (0.8). Do NOT modify `successChance`.
- **Fixation < 6 (Tier 0):** No adjustment.

Only options with `Stat == StatType.Chaos` are affected.

**Verification:**
1. Fixation = 14, Chaos option with `successChance = 0.6` → adjusted to `0.36`
2. Fixation = 8, Chaos option with `expectedGainOnSuccess = 2.0` → adjusted to `1.6`
3. Fixation = 3, Chaos option → no adjustment

### AC4: Stat Variety Bonus

When `context.LastStatUsed` or `context.SecondLastStatUsed` is non-null, any option whose `Stat` is NOT equal to either of them receives a `StatVarietyBonus` (+0.1) to its score.

- If both `LastStatUsed` and `SecondLastStatUsed` are null (first turn), no variety bonus is applied to any option.
- If `LastStatUsed == SecondLastStatUsed` (e.g., both Chaos), only options matching that stat miss the bonus — all others get +0.1.
- An option matching either `LastStatUsed` or `SecondLastStatUsed` does NOT get the bonus.

**Verification:** Given `LastStatUsed = Charm`, `SecondLastStatUsed = null`, options `[Charm, Rizz]`:
- Charm: no bonus (matches `LastStatUsed`)
- Rizz: `score += 0.1`

### AC5: Backward Compatibility

All existing tests and callers of `PlayerAgentContext` and `ScoringPlayerAgent` must continue to work without modification.

- `PlayerAgentContext` constructor: new parameters are optional with defaults (`null`, `null`, `false`).
- When `LastStatUsed` is null: Fixation growth penalty and stat variety bonus are skipped.
- When `ShadowValues` is null: Fixation threshold EV reduction is skipped.
- When no Honesty option is present: Denial growth penalty is not applied.
- Net effect: zero change to scoring when new context fields are at their defaults.

### AC6: Deterministic Output

The agent remains deterministic: identical inputs always produce identical outputs. No randomness, no LLM calls. The new adjustments are all pure arithmetic on the input values.

### AC7: Named Constants

All penalty/bonus magnitudes are defined as named `private const float` fields on `ScoringPlayerAgent` (not magic numbers in-line). This allows future tuning without searching through scoring logic.

---

## Edge Cases

1. **First turn (`LastStatUsed == null`, `SecondLastStatUsed == null`):**
   - Fixation growth penalty: skipped (no history)
   - Stat variety bonus: skipped (no history to compare against)
   - Denial growth penalty: still applies if Honesty is among current options
   - Fixation threshold: still applies if `ShadowValues` is non-null

2. **Second turn (`LastStatUsed` set, `SecondLastStatUsed == null`):**
   - Fixation growth penalty: skipped (need two consecutive same-stat turns)
   - Stat variety bonus: applied — options matching `LastStatUsed` miss the bonus

3. **All options are the same stat (e.g., forced Rizz from Horniness ≥18):**
   - Fixation growth penalty applies to all if `LastStatUsed == SecondLastStatUsed == Rizz`
   - Denial growth penalty: not applied (no Honesty option available)
   - Stat variety bonus: none awarded (all match recent history if Rizz was recent), or all awarded (if Rizz was NOT recent)
   - Scores remain differentiable via base EV differences

4. **`ShadowValues` contains Fixation = 0:**
   - Tier 0 → no Fixation threshold adjustment

5. **`ShadowValues` is non-null but missing `Fixation` key:**
   - Treat as Fixation = 0 (use `TryGetValue` with default 0)

6. **`ShadowValues` contains Fixation ≥ 18 (Tier 3):**
   - Same as Tier 2 treatment: apply disadvantage to Chaos `successChance`. The spec does not define a separate T3 effect beyond T2.

7. **Option has `Stat == Honesty` AND matches `LastStatUsed` and `SecondLastStatUsed`:**
   - Fixation growth penalty applies (same stat three times)
   - Denial growth penalty does NOT apply (it's Honesty)
   - Net: only the Fixation penalty hits

8. **Cumulative stacking:**
   - All four adjustments are independent and additive. A single option can receive:
     - Fixation growth penalty (−0.5)
     - Denial growth penalty (−0.3)
     - Fixation threshold EV reduction (modifies intermediate calc)
     - Stat variety bonus (+0.1)
   - The Fixation threshold reduction modifies `successChance` or `expectedGainOnSuccess` *before* the EV calculation. The other three adjust the final `score` *after* the EV calculation.

---

## Error Conditions

1. **`turn` is null:** Existing `ArgumentNullException` (no change).
2. **`context` is null:** Existing `ArgumentNullException` (no change).
3. **`turn.Options` is empty:** Existing `InvalidOperationException` (no change).
4. **`ShadowValues` dictionary with unexpected keys:** Ignored — only `ShadowStatType.Fixation` is read via `TryGetValue`.
5. **Negative shadow values in `ShadowValues`:** Treated as Tier 0 (< 6). No validation needed — the scorer is advisory, not authoritative.

No new exception types are introduced. The scorer is a pure scoring function — it does not throw on unusual input combinations, it just produces adjusted scores.

---

## Dependencies

- **`Pinder.Core.Stats.StatType`** — enum used for stat identification and comparison
- **`Pinder.Core.Stats.ShadowStatType`** — enum used as dictionary key in `ShadowValues`
- **`Pinder.Core.Conversation.TurnStart`** — input containing `DialogueOption[]`
- **`Pinder.Core.Conversation.DialogueOption`** — contains `Stat` property (StatType)
- **`Pinder.Core.Conversation.InterestState`** — enum for existing strategic adjustments
- **`Pinder.Core.Conversation.CallbackBonus`** — existing dependency (unchanged)
- **`session-runner/PlayerAgentContext.cs`** — modified (new optional fields)
- **`session-runner/ScoringPlayerAgent.cs`** — modified (new scoring terms)
- **`session-runner/IPlayerAgent.cs`** — unchanged interface
- **`session-runner/PlayerDecision.cs`** — unchanged
- **`session-runner/OptionScore.cs`** — unchanged
- **`session-runner/Program.cs`** — must wire new `PlayerAgentContext` fields when constructing context each turn (pass `LastStatUsed`, `SecondLastStatUsed`, `HonestyAvailableLastTurn` from turn history)

### Wiring Requirement (Program.cs)

`Program.cs` must track stat history across turns and populate the new `PlayerAgentContext` fields:
- After each `ResolveTurnAsync` call, record the chosen option's `Stat` as the new `LastStatUsed`, shifting the previous `LastStatUsed` into `SecondLastStatUsed`.
- Before constructing `PlayerAgentContext`, check if any option in the *previous* turn's `TurnStart.Options` had `Stat == Honesty` → set `HonestyAvailableLastTurn`.

This wiring is the responsibility of `Program.cs` (or whatever orchestrates the game loop), NOT of `ScoringPlayerAgent`.

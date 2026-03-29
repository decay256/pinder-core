# Spec: Shadow Thresholds — Implement §7 Threshold Effects on Gameplay

**Issue:** #45
**Sprint:** 7 — RPG Rules Complete
**Depends on:** #44 (shadow growth events), #139 (Wave 0 — `SessionShadowTracker`, `GameSessionConfig`)
**Contract:** `contracts/sprint-7-shadow-thresholds.md`
**Maturity:** Prototype

---

## 1. Overview

Rules v3.4 §7 defines behavioral effects that trigger when a player's shadow stats cross specific thresholds (6, 12, 18+). These effects range from cosmetic flavor injected into LLM prompts (Tier 1) to mechanical disadvantage on stat rolls (Tier 2) to hard gameplay restrictions like suppressed options and forced stats (Tier 3). This feature adds a `ShadowThresholdEvaluator` utility class, an `InterestMeter(int)` constructor overload, and integrates threshold checks into `GameSession` so that shadow corruption visibly degrades gameplay.

---

## 2. Function Signatures

### 2.1 ShadowThresholdEvaluator

**File:** `src/Pinder.Core/Stats/ShadowThresholdEvaluator.cs`
**Namespace:** `Pinder.Core.Stats`

```csharp
public static class ShadowThresholdEvaluator
{
    /// <summary>
    /// Returns the threshold tier (0, 1, 2, or 3) for the given shadow stat value.
    /// T0: 0–5 (no effect), T1: 6–11, T2: 12–17, T3: 18+
    /// </summary>
    /// <param name="shadowValue">Current shadow stat value (base + session delta). Must be ≥ 0.</param>
    /// <returns>0, 1, 2, or 3</returns>
    public static int GetThresholdLevel(int shadowValue);
}
```

**Logic:**
- `shadowValue >= 18` → return `3`
- `shadowValue >= 12` → return `2`
- `shadowValue >= 6` → return `1`
- otherwise → return `0`

The method accepts a raw `int` rather than a `ShadowStatType` because the threshold boundaries are the same for all shadow stats. The caller is responsible for obtaining the correct shadow value from `SessionShadowTracker`.

### 2.2 InterestMeter Constructor Overload

**File:** `src/Pinder.Core/Conversation/InterestMeter.cs`
**Namespace:** `Pinder.Core.Conversation`

```csharp
/// <summary>
/// Creates an InterestMeter with a custom starting value.
/// Used for Dread ≥18 effect (starting interest 8 instead of 10).
/// </summary>
/// <param name="startingValue">Starting interest value, clamped to [Min, Max].</param>
public InterestMeter(int startingValue);
```

**Logic:**
- Sets `Current` to `Math.Max(Min, Math.Min(Max, startingValue))`.
- The existing parameterless constructor `InterestMeter()` remains unchanged (starts at `StartingValue` = 10).

### 2.3 GameSession Integration (modified methods)

No new public methods are added. The following existing methods gain shadow threshold behavior:

#### GameSession Constructor (new overload)

**File:** `src/Pinder.Core/Conversation/GameSession.cs`

```csharp
public GameSession(
    CharacterProfile player,
    CharacterProfile opponent,
    ILlmAdapter llm,
    IDiceRoller dice,
    ITrapRegistry trapRegistry,
    GameSessionConfig? config = null);
```

At construction time, if `config?.PlayerShadowTracker` is provided:
1. Read the player's Dread shadow value via `config.PlayerShadowTracker.GetEffectiveShadow(ShadowStatType.Dread)`.
2. If `ShadowThresholdEvaluator.GetThresholdLevel(dreadValue) >= 3`, construct `InterestMeter(8)` instead of `InterestMeter()`.
3. Otherwise, construct `InterestMeter()` as usual.

#### StartTurnAsync

Before generating dialogue options:
1. Build a `Dictionary<ShadowStatType, int>` mapping each shadow stat to its threshold level (0–3) using `SessionShadowTracker.GetEffectiveShadow()` and `ShadowThresholdEvaluator.GetThresholdLevel()`.
2. Populate `DialogueContext.ShadowThresholds` with this dictionary.
3. Determine per-stat disadvantage from shadow thresholds at T2+ (see §3 for the pairing table). Merge with existing advantage/disadvantage from interest state. (Disadvantage does not stack — multiple sources of disadvantage still result in a single "roll twice, take lower".)
4. After the LLM returns dialogue options, apply T3 option restrictions (see §3.4).

If `SessionShadowTracker` is null, skip all threshold checks — `ShadowThresholds` is `null`, no disadvantage is added, no options are filtered.

---

## 3. Shadow Threshold Effects Table

Each shadow stat has effects at three threshold tiers. T0 (0–5) has no mechanical effect.

| Shadow Stat | Paired Stat | T1 (≥ 6) | T2 (≥ 12) | T3 (≥ 18) |
|---|---|---|---|---|
| Dread | Wit | Existential flavor in LLM options (cosmetic — via `ShadowThresholds` in `DialogueContext`) | **Wit rolls have disadvantage** | Starting Interest = 8 (applied at session construction) |
| Madness | Charm | UI glitches (cosmetic — host responsibility) | **Charm rolls have disadvantage** | One option/turn replaced with unhinged text (cosmetic — LLM uses `ShadowThresholds`) |
| Denial | Honesty | "I'm fine" leaks into messages (cosmetic — LLM) | **Honesty rolls have disadvantage** | **Honesty options removed** from returned options |
| Fixation | Chaos | Options start repeating patterns (cosmetic — LLM) | **Chaos rolls have disadvantage** | **Must pick same stat as last turn** (forced stat) |
| Overthinking | SelfAwareness | Always see Interest number (host responsibility) | **SA rolls have disadvantage** | See opponent's inner monologue (cosmetic — LLM) |
| Horniness | Rizz | Rizz options appear more (handled by issue #51) | One option always unwanted Rizz (handled by #51) | All options become Rizz (handled by #51) |

### 3.1 Cosmetic Effects (T1, some T3)

Cosmetic effects are achieved by passing `ShadowThresholds` to `DialogueContext`. The LLM adapter uses this dictionary to adjust tone and content. No engine-level enforcement is needed for cosmetic effects — the engine just provides the data.

### 3.2 Disadvantage Effects (T2)

When a shadow stat is at threshold ≥ 2, the **paired** positive stat rolls with disadvantage. The pairing is:

| Shadow at T2+ | Stat with Disadvantage |
|---|---|
| Dread ≥ 12 | Wit |
| Madness ≥ 12 | Charm |
| Denial ≥ 12 | Honesty |
| Fixation ≥ 12 | Chaos |
| Overthinking ≥ 12 | SelfAwareness |
| Horniness ≥ 12 | Rizz |

**Implementation in `StartTurnAsync`:** After computing advantage/disadvantage from interest state, check each shadow threshold. If the player's chosen stat (or any stat that options might use) is penalized, set `_currentHasDisadvantage = true` for rolls using that stat. Since disadvantage is resolved per-roll in `ResolveTurnAsync`, the session must track **which stats** have disadvantage, not just a global flag.

**Recommended approach:** Store a `HashSet<StatType> _shadowDisadvantagedStats` populated during `StartTurnAsync`. In `ResolveTurnAsync`, when resolving the chosen option's stat, check if that stat is in the disadvantaged set.

### 3.3 Starting Interest Override (Dread T3)

Applied once at construction time:
- If player's Dread shadow (via `SessionShadowTracker.GetEffectiveShadow(ShadowStatType.Dread)`) has threshold level ≥ 3 (i.e., value ≥ 18), the `InterestMeter` is constructed with starting value 8.
- This uses the new `InterestMeter(int startingValue)` overload.
- If `SessionShadowTracker` is null, default to `InterestMeter()` (starting value 10).

### 3.4 Hard Mechanical Restrictions (T3)

These are enforced in `GameSession` after the LLM returns dialogue options:

#### Denial ≥ 18: Remove Honesty Options

After `ILlmAdapter.GetDialogueOptionsAsync()` returns, filter out any `DialogueOption` where `option.Stat == StatType.Honesty`. If all options would be removed (unlikely but possible), leave one non-Honesty option. The LLM is not expected to comply with this restriction — it is enforced post-hoc.

#### Fixation ≥ 18: Force Same Stat as Last Turn

After options are returned, replace all options' stat with the stat used in the previous turn (`_lastStatUsed`). If this is the first turn (no previous stat), Fixation T3 has no forced-stat effect — use options as-is.

**Tracking `_lastStatUsed`:** Add a `private StatType? _lastStatUsed` field to `GameSession`. Set it in `ResolveTurnAsync` after resolving the roll. Initialized to `null`.

#### Horniness ≥ 18: All Options Become Rizz

This is handled by issue #51 (Horniness forced Rizz mechanic). This spec does NOT implement it. However, `ShadowThresholds` must be populated so #51 can read the Horniness threshold level.

---

## 4. Input/Output Examples

### Example 1: ShadowThresholdEvaluator — Basic Threshold Levels

| Input `shadowValue` | Output |
|---|---|
| 0 | 0 |
| 5 | 0 |
| 6 | 1 |
| 11 | 1 |
| 12 | 2 |
| 17 | 2 |
| 18 | 3 |
| 25 | 3 |

### Example 2: InterestMeter Custom Starting Value

```
new InterestMeter(8).Current == 8
new InterestMeter(0).Current == 0
new InterestMeter(30).Current == 25   // clamped to Max
new InterestMeter(-5).Current == 0    // clamped to Min
new InterestMeter().Current == 10     // unchanged default
```

### Example 3: Dread ≥ 18 → Starting Interest 8

**Setup:**
- Player `StatBlock` has Dread shadow = 20.
- `SessionShadowTracker` wraps this `StatBlock`.
- `GameSessionConfig` provides the tracker.

**Result:**
- `ShadowThresholdEvaluator.GetThresholdLevel(20)` returns 3.
- `GameSession` constructor creates `InterestMeter(8)`.
- First `TurnStart` shows `interest = 8`.

### Example 4: Denial ≥ 12 → Honesty Disadvantage

**Setup:**
- Player's Denial shadow = 14 (threshold 2).
- `StartTurnAsync` runs; LLM returns 3 options: Charm, Honesty, Wit.

**Result:**
- Honesty is flagged for disadvantage.
- If the player picks the Honesty option (index 1), `ResolveTurnAsync` resolves the roll with disadvantage (roll d20 twice, take lower).
- If the player picks Charm or Wit, no shadow disadvantage applies (unless those stats are also penalized).

### Example 5: Denial ≥ 18 → Honesty Options Removed

**Setup:**
- Player's Denial shadow = 19 (threshold 3).
- LLM returns 3 options: `[Charm, Honesty, Wit]`.

**Result:**
- Post-processing removes the Honesty option.
- `TurnStart.Options` contains 2 options: `[Charm, Wit]`.

### Example 6: Fixation ≥ 18 → Forced Stat

**Setup:**
- Player's Fixation shadow = 18 (threshold 3).
- Last turn, the player used `StatType.Charm` → `_lastStatUsed = Charm`.
- LLM returns 3 options: `[Wit, Honesty, Rizz]`.

**Result:**
- All options are replaced to use `StatType.Charm`.
- `TurnStart.Options` contains 3 options all with `Stat == StatType.Charm`.
- The player must pick one, but all resolve using Charm.

### Example 7: First Turn with Fixation ≥ 18

**Setup:**
- Player's Fixation shadow = 20 (threshold 3).
- This is the first turn — `_lastStatUsed == null`.

**Result:**
- No forced-stat effect applies. Options returned as-is from LLM.
- After the player resolves this turn, `_lastStatUsed` is set, and subsequent turns enforce the forced stat.

### Example 8: Multiple Shadow Thresholds Active

**Setup:**
- Dread = 14 (T2), Denial = 6 (T1), Fixation = 18 (T3).

**Result:**
- `ShadowThresholds` dict: `{ Dread: 2, Denial: 1, Fixation: 3, Madness: 0, Overthinking: 0, Horniness: 0 }`.
- Wit has disadvantage (Dread T2).
- Fixation T3 forces same stat as last turn.
- Denial T1 is cosmetic only (passed to LLM via context).

### Example 9: No SessionShadowTracker

**Setup:**
- `GameSession` constructed without `GameSessionConfig` (or config has null tracker).

**Result:**
- `ShadowThresholds` is `null` in `DialogueContext`.
- No disadvantage from shadows.
- No option filtering.
- `InterestMeter` starts at 10.
- All existing behavior is unchanged — full backward compatibility.

---

## 5. Acceptance Criteria

### AC1: ShadowThresholdEvaluator Computes Threshold Level (0/1/2/3)

`ShadowThresholdEvaluator.GetThresholdLevel(int shadowValue)` returns:
- `0` for values 0–5
- `1` for values 6–11
- `2` for values 12–17
- `3` for values 18+

This is a pure static function with no side effects and no dependencies.

### AC2: Disadvantage Applied to Correct Stat Rolls at Threshold ≥ 12

When `SessionShadowTracker` reports a shadow stat at threshold ≥ 2, the paired positive stat rolls with disadvantage in `ResolveTurnAsync`. The pairings are:
- Dread ≥ 12 → Wit disadvantage
- Madness ≥ 12 → Charm disadvantage
- Denial ≥ 12 → Honesty disadvantage
- Fixation ≥ 12 → Chaos disadvantage
- Overthinking ≥ 12 → SA disadvantage
- Horniness ≥ 12 → Rizz disadvantage

Disadvantage from shadows does not stack with other disadvantage sources — the player still only rolls twice and takes the lower value.

### AC3: Honesty Options Suppressed at Denial ≥ 18

When the player's Denial shadow is at threshold ≥ 3, any `DialogueOption` with `Stat == StatType.Honesty` is removed from the options returned by `StartTurnAsync`. The filtering happens after the LLM returns options (post-processing in `GameSession`).

### AC4: Forced Stat at Fixation ≥ 18

When the player's Fixation shadow is at threshold ≥ 3 and there is a previous turn (`_lastStatUsed != null`), all dialogue options have their `Stat` replaced with `_lastStatUsed`. On the first turn of a conversation (no previous stat), this effect does not apply.

### AC5: Starting Interest 8 When Dread ≥ 18

When the player's Dread shadow is at threshold ≥ 3, the `InterestMeter` is constructed with starting value 8 via the new `InterestMeter(int startingValue)` overload. This is checked once at `GameSession` construction time using `SessionShadowTracker.GetEffectiveShadow(ShadowStatType.Dread)`.

### AC6: Shadow Values Read from SessionShadowTracker

All shadow values used for threshold checks are read from `SessionShadowTracker.GetEffectiveShadow()` (which accounts for base value + in-session growth from #44). Raw `StatBlock.GetShadow()` is NOT used when a `SessionShadowTracker` is configured.

### AC7: DialogueContext.ShadowThresholds Populated Each Turn

`StartTurnAsync` populates `DialogueContext.ShadowThresholds` with a `Dictionary<ShadowStatType, int>` containing the threshold level (0–3) for each of the 6 shadow stats. If no `SessionShadowTracker` is configured, `ShadowThresholds` is `null`.

### AC8: Tests Cover Threshold Computation and Mechanical Effects

Tests must verify:
- `ShadowThresholdEvaluator` returns correct tier for boundary values (0, 5, 6, 11, 12, 17, 18, 25)
- `InterestMeter(int)` constructor sets `Current` correctly, including clamping
- Disadvantage is applied to the correct stat when threshold ≥ 2
- Honesty options are removed when Denial threshold ≥ 3
- Forced stat when Fixation threshold ≥ 3 (with and without a previous turn)
- Starting interest is 8 when Dread threshold ≥ 3
- No effects when `SessionShadowTracker` is null (backward compatibility)

### AC9: Build Clean

`dotnet build` succeeds with zero errors and zero warnings. All existing 254+ tests continue to pass.

---

## 6. Edge Cases

### 6.1 Negative Shadow Values

`ShadowThresholdEvaluator.GetThresholdLevel` should return `0` for any value < 0 (defensive guard). Shadow values should never be negative in practice, but the evaluator should not throw.

### 6.2 All Options Are Honesty at Denial T3

If the LLM returns options where every option uses `StatType.Honesty` and Denial is at T3, filtering all of them would leave zero options. In this case, the implementation should keep at least one option (the first one) to prevent an empty option set. Alternatively, the GameSession can request new options from the LLM without the Honesty constraint, but at prototype maturity, keeping one option is acceptable.

### 6.3 Fixation T3 on First Turn

`_lastStatUsed` is `null` on the first turn. The forced-stat restriction does not apply. After the first turn resolves, `_lastStatUsed` is set and the restriction takes effect on turn 2+.

### 6.4 Multiple Shadow Stats at T2+ Simultaneously

Multiple stats can have disadvantage at the same time. Each stat's disadvantage is independent. If the player picks an option using a stat that has disadvantage, the roll uses disadvantage. If they pick a stat that doesn't, no shadow-based disadvantage applies.

### 6.5 Disadvantage from Interest State AND Shadow Threshold

Disadvantage from being in the Bored interest state and disadvantage from a shadow threshold do not stack. Disadvantage is binary — you either have it or you don't. If both sources apply, the roll is still just "roll twice, take lower."

### 6.6 Advantage and Disadvantage Cancel

Per standard d20 rules: if a roll has both advantage (e.g., from VeryIntoIt interest state) and disadvantage (e.g., from a shadow threshold), they cancel out — the roll is normal (single d20). This is existing `RollEngine` behavior.

### 6.7 Shadow Growth During the Session Changes Thresholds

Shadow growth events from #44 modify `SessionShadowTracker` during the session. Threshold checks in `StartTurnAsync` always read the current effective value, so if a shadow crosses a threshold mid-session (e.g., Denial goes from 11 to 12 due to a growth event), the next turn will reflect the new threshold level. This is the intended behavior.

### 6.8 Horniness Thresholds Deferred to #51

Horniness threshold effects (T1: more Rizz options, T2: one forced Rizz, T3: all Rizz) are entirely handled by issue #51. This issue (#45) only ensures `ShadowThresholds` includes the Horniness threshold level so #51 can read it. No Horniness-specific option manipulation is implemented here.

### 6.9 InterestMeter(int) Overload Clamping

The `InterestMeter(int startingValue)` constructor clamps to `[Min, Max]` (i.e., `[0, 25]`). Values outside this range are clamped silently — no exception is thrown.

### 6.10 Existing Parameterless InterestMeter Constructor

The new overload does not affect the existing `InterestMeter()` constructor. All existing code that constructs `InterestMeter()` without arguments continues to start at 10.

---

## 7. Error Conditions

### 7.1 SessionShadowTracker is Null

**Not an error.** When no tracker is configured, all threshold effects are silently disabled. `ShadowThresholds` is `null`, no disadvantage is added, no options are filtered, interest starts at 10. This ensures backward compatibility.

### 7.2 GetEffectiveShadow Returns Unexpected Negative Value

If `SessionShadowTracker.GetEffectiveShadow()` returns a negative value (should not happen in normal operation), `ShadowThresholdEvaluator.GetThresholdLevel` returns 0 — no threshold effect.

### 7.3 DialogueOption.Stat is Not a Valid StatType

Should not happen since `StatType` is an enum. If it does, the disadvantage check simply won't match any shadow pairing and no disadvantage is applied.

### 7.4 GameSession Constructed with Config but Null Tracker

`GameSessionConfig` may be non-null but have a null `PlayerShadowTracker`. This is equivalent to no tracker — all threshold checks are skipped.

### 7.5 ResolveTurnAsync Called Without StartTurnAsync

Existing `GameSession` invariant — throws `InvalidOperationException`. Shadow threshold logic does not change this behavior.

---

## 8. Dependencies

| Dependency | Issue/Source | What's Needed |
|---|---|---|
| `SessionShadowTracker` | #139 (Wave 0) | `GetEffectiveShadow(ShadowStatType)` to read current shadow value (base + session delta) |
| `GameSessionConfig` | #139 (Wave 0) | Carrier for `SessionShadowTracker` injection into `GameSession` |
| Shadow growth events | #44 | Shadow values must be mutable during session for thresholds to reflect mid-session changes |
| `DialogueContext.ShadowThresholds` | PR #114 (merged) | `Dictionary<ShadowStatType, int>?` field already exists on `DialogueContext` |
| `InterestMeter` | Existing | Gains `InterestMeter(int startingValue)` overload |
| `ShadowStatType` enum | Existing (`Stats/`) | Used for threshold dictionary keys |
| `StatType` enum | Existing (`Stats/`) | Used for disadvantage pairing lookups |
| `StatBlock.ShadowPairs` | Existing (`Stats/StatBlock.cs`) | Maps `StatType → ShadowStatType` — can be used inversely to find which stat is penalized by which shadow |
| `RollEngine.Resolve()` | Existing (`Rolls/`) | Already supports advantage/disadvantage parameters — no changes needed |
| `DialogueOption` | Existing | Has `Stat` property of type `StatType` — used for option filtering |
| Horniness mechanic | #51 | Horniness T1/T2/T3 effects are NOT implemented here; only the threshold value is provided |

# Spec: OpponentTimingCalculator — Compute Simulated Reply Delay

**Issue:** #53  
**Component:** `Pinder.Core.Conversation`  
**Maturity:** Prototype  

---

## 1. Overview

`OpponentTimingCalculator` is a pure computation component that determines how long (in minutes) an opponent character takes to reply during a Pinder conversation. The delay is derived from the opponent's `TimingProfile`, the current `InterestState`, the opponent's shadow stat values, and randomness injected via `IDiceRoller`. This component has no side effects, no clock interaction, and no async behavior — it takes inputs and returns a `double` representing minutes.

A companion `JsonTimingRepository` loads pre-authored timing profiles from a JSON file, following the same pattern as the existing `JsonItemRepository`.

---

## 2. Function Signatures

### `OpponentTimingCalculator` (static class)

**Namespace:** `Pinder.Core.Conversation`

```csharp
public static class OpponentTimingCalculator
{
    /// <summary>
    /// Computes the opponent's reply delay in minutes.
    /// </summary>
    /// <param name="profile">The opponent's assembled TimingProfile.</param>
    /// <param name="interest">Current InterestState of the conversation.</param>
    /// <param name="shadows">
    ///   Map of the opponent's current shadow stat values.
    ///   Only keys with value ≥ 6 affect the result. Missing keys are treated as 0.
    /// </param>
    /// <param name="dice">Dice roller for randomness (variance, dry spell, madness outlier).</param>
    /// <returns>Delay in minutes (always ≥ 1.0).</returns>
    public static double ComputeDelayMinutes(
        TimingProfile profile,
        InterestState interest,
        Dictionary<ShadowStatType, int> shadows,
        IDiceRoller dice);
}
```

**Required usings:**
- `System.Collections.Generic`
- `Pinder.Core.Stats` (for `ShadowStatType`)
- `Pinder.Core.Interfaces` (for `IDiceRoller`)

### `JsonTimingRepository`

**Namespace:** `Pinder.Core.Data`

```csharp
public sealed class JsonTimingRepository
{
    /// <param name="json">Full JSON string — contents of response-profiles.json.</param>
    public JsonTimingRepository(string json);

    /// <summary>Returns the TimingProfile for a given profile ID, or null if not found.</summary>
    public TimingProfile? GetProfile(string profileId);

    /// <summary>Returns all loaded profiles.</summary>
    public IEnumerable<TimingProfile> GetAll();
}
```

> **Note:** The JSON schema for response profiles is not yet defined. The repository should parse a top-level JSON array of objects. Each object must have at minimum: `"id"` (string), `"baseDelayMinutes"` (int), `"varianceMultiplier"` (float), `"drySpellProbability"` (float), `"readReceipt"` (string: `"neutral"`, `"shows"`, or `"hides"`). The implementer should follow the exact pattern of `JsonItemRepository` using the existing `JsonParser`.

---

## 3. Input/Output Examples

### Example 1: Basic computation (no shadows active)

**Input:**
- `profile`: `BaseDelayMinutes = 10`, `VarianceMultiplier = 0.5f`, `DrySpellProbability = 0.0f`, `ReadReceipt = "neutral"`
- `interest`: `InterestState.Interested`
- `shadows`: empty dictionary
- `dice`: always returns 50 on `Roll(100)` (midpoint → variance factor ≈ 1.0)

**Computation:**
1. Base delay = 10.0 (roll d100=50, map to variance range: `10 * (1 + 0.5 * ((50-1)/99 - 0.5))` ≈ 10 * 0.9975 ≈ 9.975)
2. Interest multiplier for `Interested` = ×1.0 → delay = ~9.975
3. No shadow modifiers active
4. No dry spell (probability = 0.0)

**Output:** ~10.0 minutes (exact value depends on rounding)

### Example 2: Bored opponent with Overthinking ≥ 6

**Input:**
- `profile`: `BaseDelayMinutes = 10`, `VarianceMultiplier = 0.0f`, `DrySpellProbability = 0.0f`
- `interest`: `InterestState.Bored`
- `shadows`: `{ Overthinking: 8 }`
- `dice`: deterministic (Roll(100) → 50)

**Computation:**
1. Base delay = 10.0 (no variance since multiplier is 0.0)
2. Interest multiplier for `Bored` = ×2.0 → delay = 20.0
3. Overthinking ≥ 6: +50% → delay = 30.0
4. No dry spell

**Output:** 30.0 minutes

### Example 3: VeryIntoIt with Denial ≥ 6

**Input:**
- `profile`: `BaseDelayMinutes = 10`, `VarianceMultiplier = 0.0f`, `DrySpellProbability = 0.0f`
- `interest`: `InterestState.VeryIntoIt`
- `shadows`: `{ Denial: 7 }`
- `dice`: deterministic

**Computation:**
1. Base delay = 10.0
2. Interest multiplier for `VeryIntoIt` = ×0.5 → delay = 5.0
3. Denial ≥ 6: snap to nearest 5-minute interval → 5.0 (already aligned)

**Output:** 5.0 minutes

### Example 4: Dry spell triggers

**Input:**
- `profile`: `BaseDelayMinutes = 5`, `VarianceMultiplier = 0.0f`, `DrySpellProbability = 0.25f`
- `interest`: `InterestState.Interested`
- `shadows`: empty
- `dice`: Roll(100) → 20 (i.e. ≤ 25 → dry spell triggers)

**Computation:**
1. Base delay = 5.0, interest ×1.0 = 5.0
2. Dry spell check: roll d100, result 20 ≤ probability threshold (25) → dry spell!
3. Dry spell replaces computed delay with a value in range [120, 480] minutes (2–8 hours)
4. Dice determines exact duration within that range

**Output:** 120–480 minutes

---

## 4. Acceptance Criteria

### AC-1: `OpponentTimingCalculator.ComputeDelayMinutes` exists

A public static method with the exact signature above must exist in `Pinder.Core.Conversation.OpponentTimingCalculator`. It must accept `TimingProfile`, `InterestState`, `Dictionary<ShadowStatType, int>`, and `IDiceRoller`, and return `double`.

### AC-2: Interest multipliers applied correctly per InterestState

The method must apply the following multipliers to the computed base delay:

| InterestState | Timing Multiplier |
|---|---|
| Unmatched | N/A — see Edge Cases §5 |
| Bored | ×2.0 |
| Interested | ×1.0 |
| VeryIntoIt | ×0.5 |
| AlmostThere | ×0.3 |
| DateSecured | N/A — see Edge Cases §5 |

> **⚠️ Discrepancy note:** The issue owner's comment on #53 states `Bored=×5.0`, which contradicts the issue body table stating `Bored=×2.0`. The issue body table is treated as canonical for this spec since it is the complete table. The comment may have been referring to a different multiplier context. **The implementer should confirm with the PO which value is correct.** This spec uses ×2.0 from the issue body pending clarification.

### AC-3: No code references to `InterestState.Lukewarm`

The `InterestState` enum has exactly 6 values: `Unmatched`, `Bored`, `Interested`, `VeryIntoIt`, `AlmostThere`, `DateSecured`. The implementation must not reference, create, or assume a `Lukewarm` state. This was explicitly removed per VC-18.

### AC-4: Shadow modifiers applied for Overthinking, Denial, Fixation, Madness

Each shadow modifier activates only when the corresponding shadow stat value is **≥ 6**. Shadow modifiers are applied **after** the interest multiplier. Multiple shadow modifiers can stack.

| ShadowStatType | Threshold | Effect |
|---|---|---|
| Overthinking | ≥ 6 | Multiply current delay by 1.5 (+50% time) |
| Denial | ≥ 6 | Snap the delay to the nearest 5-minute interval (`Math.Round(delay / 5.0) * 5.0`). If the result is 0, use 5. |
| Fixation | ≥ 6 | Return the same delay as the previous call (rigid schedule). **Note:** Since `OpponentTimingCalculator` is stateless, the "previous delay" must be passed in or this modifier must be handled by the caller (e.g., `GameSession`). See Edge Cases §5 for design options. |
| Madness | ≥ 6 | 20% chance (roll d100, if ≤ 20) of an extreme outlier: either 1.0 minute OR 240+ minutes. Use dice to choose between them. |

**Application order (when multiple shadows active):**
1. Overthinking (+50%) — applied first as a multiplier
2. Madness (outlier check) — may override the computed value entirely
3. Denial (snap to 5-min) — applied to whatever value exists at this point
4. Fixation — overrides everything if active (returns previous delay)

### AC-5: Dry spell probability respected

After all modifiers are applied, perform a dry spell check:
1. Roll `dice.Roll(100)`.
2. If the roll ≤ `profile.DrySpellProbability * 100` (i.e., probability expressed as percentage), a dry spell occurs.
3. During a dry spell, the opponent disappears for 2–8 hours. Compute the dry spell duration: `120 + dice.Roll(361) - 1` minutes (giving range [120, 480]).
4. The dry spell duration replaces the computed delay.

If `DrySpellProbability` is 0.0, skip the check entirely.

### AC-6: `JsonTimingRepository` loads response profiles from JSON

`JsonTimingRepository` must:
- Accept a JSON string in its constructor
- Parse it using the existing `JsonParser` (no external dependencies)
- Expose `GetProfile(string profileId)` returning `TimingProfile?`
- Expose `GetAll()` returning `IEnumerable<TimingProfile>`
- Follow the same pattern as `JsonItemRepository`

The JSON data file should be located at `data/timing/response-profiles.json` (created by the implementer with at least one sample profile for testing).

### AC-7: Tests with deterministic dice

Tests must use a `FixedDice` (or equivalent deterministic `IDiceRoller`) to verify:
- Deterministic delay computation with known dice values
- Shadow Denial snaps delay to nearest 5-minute interval
- Shadow Madness outlier path (dice triggers the 20% chance)
- Each `InterestState` multiplier applied correctly (4 non-terminal states)
- Dry spell triggering and duration range

### AC-8: Build clean

`dotnet build` must complete with zero errors and zero warnings for the entire solution.

---

## 5. Edge Cases

### Terminal interest states (Unmatched, DateSecured)

`Unmatched` and `DateSecured` represent game-over conditions. The calculator should still return a valid delay if called with these states (defensive coding). Recommended behavior:
- **Unmatched:** Return a very large delay (e.g., `double.MaxValue` or a sentinel like `999999.0`) — the opponent has effectively ghosted.
- **DateSecured:** Return `1.0` (immediate response — they're locked in).

The implementer may alternatively throw `ArgumentException` for these states, but must document the choice.

### Empty or null shadows dictionary

If `shadows` is `null`, treat it as empty — no shadow modifiers apply. If `shadows` contains keys with values < 6, those keys have no effect.

### Shadow stats not in the modifier table

Only Overthinking, Denial, Fixation, and Madness have timing effects. The other two shadow stats (Horniness, Dread) are ignored by this calculator even if present in the dictionary with values ≥ 6.

### Fixation statefulness problem

The issue specifies Fixation ≥ 6 means "same as previous delay (rigid schedule)." However, `OpponentTimingCalculator` is a static, stateless class. Two design options:

**Option A (recommended):** Add an optional `double? previousDelay` parameter to `ComputeDelayMinutes`. When Fixation ≥ 6 and `previousDelay` has a value, return `previousDelay.Value`. When Fixation ≥ 6 but `previousDelay` is null (first call), compute normally and return that value. The caller (`GameSession`) is responsible for tracking and passing the previous delay.

**Option B:** Ignore Fixation in the calculator entirely and document it as a `GameSession` responsibility.

The implementer should choose Option A unless PO directs otherwise.

### Minimum delay floor

The result must never be less than 1.0 minute. After all modifiers (including Denial snapping and Madness outliers), clamp the result: `Math.Max(1.0, result)`.

Exception: Madness extreme outlier of 1.0 minute is exactly at the floor and is allowed.

### Very high shadow values

Shadow thresholds are binary (≥ 6 = active, < 6 = inactive). Values of 6, 10, or 100 all produce the same effect. There is no scaling with shadow value beyond the threshold.

### Variance computation

The base delay variance follows the same pattern as the existing `TimingProfile.ComputeDelay()`:
1. Roll `dice.Roll(100)` → value in [1, 100]
2. Normalize: `(roll - 1) / 99.0` → [0.0, 1.0]
3. Variance factor: `1.0 + profile.VarianceMultiplier * (normalized - 0.5)` 
4. Multiply base delay by variance factor

This produces a range of `[base * (1 - VM/2), base * (1 + VM/2)]` where VM is the `VarianceMultiplier`.

### Multiple shadow modifiers stacking

Overthinking and Denial can both be active. Example: base delay 10, interest ×1.0 = 10. Overthinking → 15. Denial snaps to nearest 5 → 15 (already aligned). If base were 7 and Overthinking → 10.5, Denial → 10.0.

Madness outlier, if it triggers, replaces the Overthinking-modified value before Denial snapping. So a Madness outlier of 240 minutes with Denial active would snap to 240 (already aligned).

---

## 6. Error Conditions

| Condition | Expected Behavior |
|---|---|
| `profile` is `null` | Throw `ArgumentNullException` with parameter name `"profile"` |
| `dice` is `null` | Throw `ArgumentNullException` with parameter name `"dice"` |
| `shadows` is `null` | Treat as empty dictionary (no modifiers apply) — do NOT throw |
| `interest` is an undefined enum value | Throw `ArgumentOutOfRangeException` with the invalid value |
| `profile.BaseDelayMinutes` is ≤ 0 | Clamp result to floor of 1.0 (don't throw — the formula handles it) |
| `profile.DrySpellProbability` > 1.0 or < 0.0 | Clamp to [0.0, 1.0] before checking (defensive) |
| `JsonTimingRepository` receives malformed JSON | Throw `FormatException` with a descriptive message |
| `JsonTimingRepository` receives JSON missing required fields | Throw `FormatException` indicating which field is missing |

---

## 7. Dependencies

### Internal (within Pinder.Core)

| Dependency | Location | Usage |
|---|---|---|
| `TimingProfile` | `Pinder.Core.Conversation.TimingProfile` | Input parameter — provides base delay, variance multiplier, dry spell probability |
| `InterestState` | `Pinder.Core.Conversation.InterestState` | Input parameter — determines timing multiplier |
| `ShadowStatType` | `Pinder.Core.Stats.ShadowStatType` | Dictionary key type for shadow modifiers |
| `IDiceRoller` | `Pinder.Core.Interfaces.IDiceRoller` | Injected randomness source |
| `JsonParser` | `Pinder.Core.Data.JsonParser` | Used by `JsonTimingRepository` for JSON parsing |

### External

None. Zero NuGet dependencies. Target: `netstandard2.0`, `LangVersion 8.0`.

### Consumers

| Consumer | How it uses this component |
|---|---|
| `GameSession` | Calls `ComputeDelayMinutes` after resolving each turn to determine opponent reply timing. Passes the result to the host/UI layer. Tracks `previousDelay` for Fixation shadow support. |
| Host (Unity) | Receives the delay value from `GameSession` / `TurnResult` and uses it to schedule the opponent's reply animation/appearance. |

---

## Appendix: Relationship to Existing `TimingProfile.ComputeDelay()`

The existing `TimingProfile.ComputeDelay(int interestLevel, IDiceRoller dice)` method computes delay using a linear interest formula (interest level as a 0–25 integer). The new `OpponentTimingCalculator.ComputeDelayMinutes` uses `InterestState` (enum) for a stepped multiplier table and adds shadow modifiers + dry spell logic. These are **different computation models**. The existing method remains for backward compatibility; the new calculator is the authoritative computation going forward. `GameSession` should use `OpponentTimingCalculator`, not `TimingProfile.ComputeDelay()`.

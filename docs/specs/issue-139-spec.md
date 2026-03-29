# Specification: Issue #139 — Wave 0 Infrastructure Prerequisites

## Overview

Wave 0 bundles the four foundational infrastructure components that every other Sprint 7 feature depends on: `SessionShadowTracker` (mutable shadow tracking wrapping immutable `StatBlock`), `IGameClock` (simulated in-game time interface), `RollEngine` extensions (`ResolveFixedDC` overload + `externalBonus`/`dcAdjustment` parameters), and `GameSessionConfig` (optional configuration carrier for `GameSession`). Two small additions — `InterestMeter(int)` constructor overload and `TrapState.HasActive` property — round out the wave. All changes must be backward-compatible with the existing 254 tests.

## Function Signatures

### 1. SessionShadowTracker (`Pinder.Core.Stats`)

**File:** `src/Pinder.Core/Stats/SessionShadowTracker.cs`

```csharp
namespace Pinder.Core.Stats
{
    public sealed class SessionShadowTracker
    {
        // Constructor: wraps an immutable StatBlock
        public SessionShadowTracker(StatBlock baseStats);

        // Returns base shadow value + in-session delta for the given shadow stat
        public int GetEffectiveShadow(ShadowStatType shadow);

        // Applies positive growth to a shadow stat.
        // Returns a description string: "{ShadowName} +{amount} ({reason})"
        // Throws ArgumentOutOfRangeException if amount <= 0.
        public string ApplyGrowth(ShadowStatType shadow, int amount, string reason);

        // Returns the effective stat modifier accounting for in-session shadow growth.
        // Formula: baseStats.GetBase(stat) - floor((baseStats.GetShadow(paired) + delta[paired]) / 3)
        public int GetEffectiveStat(StatType stat);

        // Returns only the in-session delta for a shadow stat (0 if no growth has occurred).
        public int GetDelta(ShadowStatType shadow);
    }
}
```

### 2. IGameClock + TimeOfDay (`Pinder.Core.Interfaces`)

**File:** `src/Pinder.Core/Interfaces/IGameClock.cs`

```csharp
namespace Pinder.Core.Interfaces
{
    public enum TimeOfDay
    {
        Morning,      // 06:00–11:59
        Afternoon,    // 12:00–17:59
        Evening,      // 18:00–21:59
        LateNight,    // 22:00–01:59
        AfterTwoAm    // 02:00–05:59
    }

    public interface IGameClock
    {
        DateTimeOffset Now { get; }
        void Advance(TimeSpan amount);
        void AdvanceTo(DateTimeOffset target);  // throws ArgumentException if target <= Now
        TimeOfDay GetTimeOfDay();
        int GetHorninessModifier();             // Morning=-2, Afternoon=0, Evening=+1, LateNight=+3, AfterTwoAm=+5
        int RemainingEnergy { get; }
        bool ConsumeEnergy(int amount);         // returns false if insufficient energy
    }
}
```

### 3. RollEngine Extensions (`Pinder.Core.Rolls`)

**File:** `src/Pinder.Core/Rolls/RollEngine.cs` (modify existing)

```csharp
// NEW overload — resolves against a fixed DC instead of computing from a defender
public static RollResult ResolveFixedDC(
    StatType stat,
    StatBlock attacker,
    int fixedDc,
    TrapState attackerTraps,
    int level,
    ITrapRegistry trapRegistry,
    IDiceRoller dice,
    bool hasAdvantage = false,
    bool hasDisadvantage = false,
    int externalBonus = 0);

// MODIFIED — two new optional params appended (backward-compatible defaults)
public static RollResult Resolve(
    StatType stat,
    StatBlock attacker,
    StatBlock defender,
    TrapState attackerTraps,
    int level,
    ITrapRegistry trapRegistry,
    IDiceRoller dice,
    bool hasAdvantage = false,
    bool hasDisadvantage = false,
    int externalBonus = 0,    // NEW: added to total before success check
    int dcAdjustment = 0);    // NEW: subtracted from DC (positive = easier)
```

### 4. RollResult Changes (`Pinder.Core.Rolls`)

**File:** `src/Pinder.Core/Rolls/RollResult.cs` (modify existing)

```csharp
// Constructor gains optional externalBonus parameter
public RollResult(
    int dieRoll,
    int? secondDieRoll,
    int usedDieRoll,
    StatType stat,
    int statModifier,
    int levelBonus,
    int dc,
    FailureTier tier,
    TrapDefinition? activatedTrap = null,
    int externalBonus = 0);         // NEW

// IsSuccess computation changes from:
//   IsSuccess = IsNatTwenty || (!IsNatOne && Total >= dc);
// to:
//   IsSuccess = IsNatTwenty || (!IsNatOne && FinalTotal >= dc);
// where FinalTotal = Total + ExternalBonus

// ExternalBonus is now set in the constructor (not only via AddExternalBonus)
```

### 5. GameSessionConfig (`Pinder.Core.Conversation`)

**File:** `src/Pinder.Core/Conversation/GameSessionConfig.cs`

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class GameSessionConfig
    {
        public IGameClock? Clock { get; }
        public SessionShadowTracker? PlayerShadows { get; }
        public SessionShadowTracker? OpponentShadows { get; }
        public int? StartingInterest { get; }

        public GameSessionConfig(
            IGameClock? clock = null,
            SessionShadowTracker? playerShadows = null,
            SessionShadowTracker? opponentShadows = null,
            int? startingInterest = null);
    }
}
```

### 6. GameSession Constructor Overload (`Pinder.Core.Conversation`)

**File:** `src/Pinder.Core/Conversation/GameSession.cs` (modify existing)

```csharp
// Existing constructor remains unchanged
public GameSession(
    CharacterProfile player,
    CharacterProfile opponent,
    ILlmAdapter llm,
    IDiceRoller dice,
    ITrapRegistry trapRegistry);

// NEW overload
public GameSession(
    CharacterProfile player,
    CharacterProfile opponent,
    ILlmAdapter llm,
    IDiceRoller dice,
    ITrapRegistry trapRegistry,
    GameSessionConfig? config = null);
```

### 7. InterestMeter Constructor Overload (`Pinder.Core.Conversation`)

**File:** `src/Pinder.Core/Conversation/InterestMeter.cs` (modify existing)

```csharp
// NEW overload — custom starting value, clamped to [Min, Max]
public InterestMeter(int startingValue);
```

### 8. TrapState.HasActive Property (`Pinder.Core.Traps`)

**File:** `src/Pinder.Core/Traps/TrapState.cs` (modify existing)

```csharp
// NEW property — true if any trap is currently active
public bool HasActive { get; }  // implemented as: _active.Count > 0
```

---

## Input/Output Examples

### SessionShadowTracker

**Setup:**
```
StatBlock with:
  Charm = 3, Rizz = 2, Honesty = 1, Chaos = 0, Wit = 4, SelfAwareness = 2
  Madness = 2, Horniness = 0, Denial = 0, Fixation = 0, Dread = 5, Overthinking = 1
```

| Call | Return Value | Explanation |
|------|-------------|-------------|
| `GetEffectiveShadow(Madness)` | `2` | base 2 + delta 0 |
| `GetDelta(Madness)` | `0` | no growth yet |
| `ApplyGrowth(Madness, 1, "Charm fail")` | `"Madness +1 (Charm fail)"` | delta becomes 1 |
| `GetEffectiveShadow(Madness)` | `3` | base 2 + delta 1 |
| `GetEffectiveStat(Charm)` | `2` | Charm(3) − floor(3/3) = 3 − 1 = 2 |
| `GetDelta(Madness)` | `1` | |
| `GetEffectiveStat(Wit)` | `2` | Wit(4) − floor(Dread(5+0)/3) = 4 − 1 = 3... wait, floor(5/3) = 1, so 4−1=3 |

Let me recalculate Wit example more carefully:
- Wit base = 4, Dread base = 5, Dread delta = 0
- floor(5 / 3) = 1
- GetEffectiveStat(Wit) = 4 − 1 = 3

After `ApplyGrowth(Dread, 1, "combo trigger")`:
- Dread effective = 5 + 1 = 6, floor(6 / 3) = 2
- GetEffectiveStat(Wit) = 4 − 2 = 2

### RollEngine.ResolveFixedDC

**Scenario: Read action, DC 12, SelfAwareness stat**
```
attacker StatBlock: SelfAwareness = 2, Overthinking = 0 → effective = 2
level = 3 → levelBonus = 1
dice rolls: 11
No active traps, no advantage/disadvantage, externalBonus = 0

Total = 11 + 2 + 1 = 14
FinalTotal = 14 + 0 = 14
DC = 12
IsSuccess = true (14 >= 12)
```

**Scenario: Read action fails**
```
attacker StatBlock: SelfAwareness = 1, Overthinking = 3 → effective = 0
level = 1 → levelBonus = 0
dice rolls: 8
No traps, no advantage/disadvantage

Total = 8 + 0 + 0 = 8
DC = 12
miss = 12 − 8 = 4 → FailureTier.Misfire
IsSuccess = false
```

### RollEngine.Resolve with externalBonus and dcAdjustment

**Scenario: Attack with callback bonus and weakness window**
```
stat = Charm, attacker Charm effective = 3, level = 2 (bonus = 0)
defender DC for Charm = 13 + defender.GetEffective(SelfAwareness) = 13 + 2 = 15
externalBonus = 2 (callback +2)
dcAdjustment = 2 (weakness window −2 DC)

dice rolls: 10
Total = 10 + 3 + 0 = 13
FinalTotal = 13 + 2 = 15
Adjusted DC = 15 − 2 = 13
IsSuccess = true (15 >= 13)
```

Without the external bonus and DC adjustment, Total 13 vs DC 15 → miss by 2 → Fumble.

### InterestMeter(int)

| Constructor Arg | Resulting Current | Explanation |
|-----------------|------------------|-------------|
| `InterestMeter(8)` | 8 | Normal value within range |
| `InterestMeter(0)` | 0 | Minimum boundary |
| `InterestMeter(25)` | 25 | Maximum boundary |
| `InterestMeter(-5)` | 0 | Clamped to Min |
| `InterestMeter(30)` | 25 | Clamped to Max |

### TrapState.HasActive

| State | HasActive |
|-------|-----------|
| Fresh TrapState (no traps activated) | `false` |
| After `Activate(trapDef)` | `true` |
| After all traps expire via `AdvanceTurn()` | `false` |

### GameSessionConfig

```
config = new GameSessionConfig(
    clock: null,
    playerShadows: new SessionShadowTracker(playerStats),
    opponentShadows: null,
    startingInterest: 8
);
session = new GameSession(player, opponent, llm, dice, trapRegistry, config);
// → InterestMeter starts at 8 instead of 10
// → PlayerShadows tracker available for shadow growth mechanics
// → No clock (time mechanics disabled)
// → No opponent shadows (opponent shadow growth disabled)
```

---

## Acceptance Criteria

### AC1: SessionShadowTracker construction, GetEffectiveShadow, ApplyGrowth, GetEffectiveStat, GetDelta

**Construction:**
- Accepts a non-null `StatBlock` as the sole constructor parameter.
- Throws `ArgumentNullException` if `baseStats` is null.
- All shadow deltas initialize to 0.

**GetEffectiveShadow(ShadowStatType):**
- Returns `baseStats.GetShadow(shadow) + delta[shadow]`.
- Returns the base shadow value when no growth has occurred (delta = 0).

**ApplyGrowth(ShadowStatType, int, string):**
- Adds `amount` to the internal delta for the given shadow stat.
- Returns a string formatted as `"{ShadowStatName} +{amount} ({reason})"`.
  - Example: `ApplyGrowth(ShadowStatType.Madness, 1, "Charm fail")` → `"Madness +1 (Charm fail)"`.
- Throws `ArgumentOutOfRangeException` if `amount <= 0`.
- Multiple calls accumulate: `ApplyGrowth(Madness, 1, ...)` then `ApplyGrowth(Madness, 2, ...)` → delta = 3.

**GetEffectiveStat(StatType):**
- Returns `baseStats.GetBase(stat) - floor((baseStats.GetShadow(paired) + delta[paired]) / 3)`.
- Uses `StatBlock.ShadowPairs` to determine the paired shadow stat.
- Integer division in C# naturally floors for positive values, which is correct here.

**GetDelta(ShadowStatType):**
- Returns the in-session delta only (not including base shadow value).
- Returns 0 if no growth has occurred for that shadow stat.

### AC2: IGameClock interface defined with TimeOfDay enum

**TimeOfDay enum** must have exactly 5 values: `Morning`, `Afternoon`, `Evening`, `LateNight`, `AfterTwoAm`.

**IGameClock interface** must define:
- `DateTimeOffset Now { get; }` — current simulated time.
- `void Advance(TimeSpan amount)` — move clock forward.
- `void AdvanceTo(DateTimeOffset target)` — move clock to specific time; `ArgumentException` if `target <= Now`.
- `TimeOfDay GetTimeOfDay()` — time-of-day bucket based on `Now.Hour`:
  - 6–11 → Morning
  - 12–17 → Afternoon
  - 18–21 → Evening
  - 22–23 or 0–1 → LateNight
  - 2–5 → AfterTwoAm
- `int GetHorninessModifier()` — Morning=−2, Afternoon=0, Evening=+1, LateNight=+3, AfterTwoAm=+5.
- `int RemainingEnergy { get; }` — energy left for current day.
- `bool ConsumeEnergy(int amount)` — deducts energy, returns `false` if insufficient (does not deduct on failure).

**Note:** This issue defines the interface only. The concrete `GameClock` implementation is built in issue #54.

### AC3: RollEngine.ResolveFixedDC works for DC 12 rolls

- New static method `ResolveFixedDC` on `RollEngine`.
- Uses `fixedDc` parameter directly instead of computing DC from a defender's stat block.
- No `defender` parameter — DC is entirely caller-specified.
- All other mechanics are identical to `Resolve()`: trap effects on attacker, advantage/disadvantage resolution, failure tier determination (Nat 1, Nat 20, miss margin tiers), trap activation on TropeTrap failure tier.
- The `externalBonus` parameter flows into the constructed `RollResult` and affects `FinalTotal` / `IsSuccess`.
- Works for any fixed DC value (not limited to 12), but primary use case is DC 12 for Read/Recover actions.

### AC4: RollEngine.Resolve accepts externalBonus and dcAdjustment (default 0)

- Two new optional `int` parameters appended to the existing `Resolve` signature: `int externalBonus = 0` and `int dcAdjustment = 0`.
- `externalBonus`: passed into the `RollResult` constructor. Affects `FinalTotal` and therefore `IsSuccess`.
- `dcAdjustment`: subtracted from the computed DC before the success/failure check. A positive `dcAdjustment` makes the roll easier (lower DC). The DC used in the `RollResult` should be the adjusted DC.
- Both default to 0, ensuring all existing callers (which pass no value for these) produce identical results.

### AC5: RollResult.IsSuccess uses FinalTotal (backward compatible when ExternalBonus=0)

- `IsSuccess` is now computed as: `IsNatTwenty || (!IsNatOne && FinalTotal >= DC)`.
- `FinalTotal` is already defined as `Total + ExternalBonus` (property exists in current code).
- The `RollResult` constructor gains an `int externalBonus = 0` parameter. This value is assigned to `ExternalBonus` during construction (in addition to the existing `AddExternalBonus` mutator which remains for backward compatibility but is deprecated).
- When `externalBonus = 0` (the default), `FinalTotal == Total`, so `IsSuccess` produces the same result as before.
- `AddExternalBonus()` is DEPRECATED. New code must NOT use it. External bonuses flow through `RollEngine.Resolve(externalBonus)` or `RollEngine.ResolveFixedDC(externalBonus)`.

### AC6: GameSessionConfig accepted by GameSession constructor

- `GameSessionConfig` is a new `sealed class` with four read-only properties, all nullable.
- `GameSession` gains a new constructor overload that accepts `GameSessionConfig? config = null` as the last parameter.
- When `config` is null or not provided, behavior is identical to the existing constructor.
- When `config.StartingInterest` has a value, the `InterestMeter` is constructed with `new InterestMeter(config.StartingInterest.Value)` instead of the default `new InterestMeter()`.
- When `config.PlayerShadows` / `config.OpponentShadows` are provided, they are stored as private fields for use by future Sprint 7 features (shadow growth, threshold checks).
- When `config.Clock` is provided, it is stored as a private field for use by future Sprint 7 features (Horniness modifier, delay evaluation).
- The existing parameterless-config constructor must continue to work unchanged.

### AC7: InterestMeter(int) overload works

- New constructor `InterestMeter(int startingValue)`.
- The `Current` property is set to `Math.Max(Min, Math.Min(Max, startingValue))` — i.e., clamped to [0, 25].
- The existing parameterless `InterestMeter()` constructor continues to set `Current = StartingValue` (10).
- All existing `InterestMeter` behavior (Apply, GetState, IsMaxed, IsZero, etc.) works identically regardless of which constructor was used.

### AC8: TrapState.HasActive works

- New read-only `bool` property `HasActive` on `TrapState`.
- Returns `true` if at least one trap is currently active; `false` if no traps are active.
- Implementation should use `_active.Count > 0` (no LINQ, no allocation).
- Correctly reflects state changes: `false` after construction, `true` after `Activate()`, `false` again after all traps expire or are cleared.

### AC9: All 254 existing tests still pass

- `dotnet test` must produce 0 failures and ≥254 passed tests.
- No existing test file is modified.
- All changes are additive (new methods, new overloads, new types) or backward-compatible modifications (IsSuccess formula change is identity when ExternalBonus=0).

### AC10: New tests for each component

- New test classes covering every public method/property of each new/modified component.
- See Edge Cases and Error Conditions sections below for specific scenarios that must be tested.

### AC11: Build clean, zero warnings

- `dotnet build` produces zero warnings.
- All new public members have XML doc comments.
- No unused imports or variables.

---

## Edge Cases

### SessionShadowTracker

- **Zero base shadow + zero delta:** `GetEffectiveShadow` returns 0, `GetEffectiveStat` returns the base stat unmodified.
- **Shadow penalty exactly on boundary:** base shadow = 2, delta = 1 → effective shadow = 3 → penalty = 1 (exactly at threshold).
- **Large accumulated delta:** Repeated `ApplyGrowth` calls accumulate correctly. E.g., 10 calls of amount=1 → delta=10.
- **All six shadow types independently tracked:** Growth on Madness does not affect Horniness delta.
- **GetEffectiveStat for stat with zero base:** base Chaos = 0, paired shadow Fixation = 3 → effective = 0 − 1 = −1 (negative values are valid modifiers).

### RollEngine

- **Nat 1 with externalBonus:** Still auto-fail (Legendary failure tier). ExternalBonus does not override Nat 1.
- **Nat 20 with negative externalBonus:** Still auto-success. Nat 20 overrides everything.
- **dcAdjustment larger than DC:** If DC is 15 and dcAdjustment is 20, adjusted DC is −5. Roll should auto-succeed (any non-Nat1 total ≥ −5). Verify this doesn't cause issues.
- **Negative externalBonus:** Valid — represents a penalty. Total might drop below DC even on a decent roll.
- **externalBonus pushes miss to success:** Total = 12, DC = 14, externalBonus = 3 → FinalTotal = 15 ≥ 14 → success. Failure tier = None.
- **ResolveFixedDC with fixedDc = 0:** Trivially succeeds (any non-Nat1 roll).
- **ResolveFixedDC with very high fixedDc:** E.g., DC 30 — only Nat 20 can succeed.
- **ResolveFixedDC TropeTrap activation:** Miss by 6–9 on fixed DC still activates traps if no trap is already active on that stat.
- **Both externalBonus and dcAdjustment applied together:** Their effects are independent. `FinalTotal >= adjustedDC`.

### RollResult

- **ExternalBonus set via constructor vs AddExternalBonus:** Both should update `ExternalBonus`. Constructor sets initial value; `AddExternalBonus` is additive on top. (Though `AddExternalBonus` is deprecated, it must still function for backward compatibility.)
- **MissMargin computation:** `MissMargin` uses `DC - Total` (not `DC - FinalTotal`). This is the raw margin before external bonuses. Verify this is consistent with expectations, or document if `MissMargin` should use `FinalTotal`. (Per existing code, `MissMargin => IsSuccess ? 0 : DC - Total` — this should remain unchanged for backward compat.)

### InterestMeter

- **Starting value exactly at boundaries:** `InterestMeter(0)` → Current = 0; `InterestMeter(25)` → Current = 25.
- **Negative starting value:** `InterestMeter(-100)` → Current = 0.
- **Starting value above max:** `InterestMeter(100)` → Current = 25.
- **State after custom start:** `InterestMeter(3)` → `GetState()` returns `Bored`.

### TrapState.HasActive

- **Empty state:** `HasActive` is `false` immediately after construction.
- **After clearing all traps via `Clear(stat)`:** Returns to `false` if that was the only active trap.
- **After `AdvanceTurn()` expires all traps:** Returns `false`.
- **Multiple active traps:** `HasActive` is `true`; removing one still leaves it `true`.

### GameSessionConfig

- **All-null config:** Equivalent to no config — all defaults apply.
- **StartingInterest = 0:** Valid — game starts in Unmatched state.
- **StartingInterest negative:** Clamped by InterestMeter constructor to 0.
- **StartingInterest > 25:** Clamped by InterestMeter constructor to 25.

---

## Error Conditions

| Component | Error Trigger | Expected Exception | Message Pattern |
|-----------|--------------|-------------------|-----------------|
| `SessionShadowTracker` | Constructor with `null` baseStats | `ArgumentNullException` | `"baseStats"` |
| `SessionShadowTracker.ApplyGrowth` | `amount <= 0` | `ArgumentOutOfRangeException` | `"amount"` — must be positive |
| `IGameClock.AdvanceTo` | `target <= Now` | `ArgumentException` | Target must be in the future |
| `IGameClock.ConsumeEnergy` | Insufficient energy | Returns `false` (no exception) | N/A |
| `GameSession` constructor | `null` player, opponent, llm, dice, or trapRegistry | `ArgumentNullException` | Parameter name |
| `RollEngine.ResolveFixedDC` | (no explicit error cases — all int values valid for fixedDc) | N/A | N/A |

**Note:** `RollEngine` methods do not perform parameter validation beyond what the existing `Resolve` does. Null `attacker`, `attackerTraps`, `trapRegistry`, or `dice` will throw `NullReferenceException` at usage point (existing behavior).

---

## Dependencies

### Internal Dependencies (within Pinder.Core)

| New Component | Depends On |
|---------------|-----------|
| `SessionShadowTracker` | `StatBlock`, `StatType`, `ShadowStatType`, `StatBlock.ShadowPairs` |
| `IGameClock` | `System.DateTimeOffset`, `System.TimeSpan` (BCL only) |
| `RollEngine.ResolveFixedDC` | `StatType`, `StatBlock`, `TrapState`, `ITrapRegistry`, `IDiceRoller`, `RollResult`, `FailureTier`, `LevelTable`, `TrapEffect`, `TrapDefinition` |
| `RollEngine.Resolve` (modified) | Same as current + passes `externalBonus`/`dcAdjustment` to `RollResult` |
| `RollResult` (modified) | No new dependencies |
| `GameSessionConfig` | `IGameClock`, `SessionShadowTracker` |
| `GameSession` (modified) | `GameSessionConfig`, `InterestMeter(int)` |
| `InterestMeter(int)` | No new dependencies |
| `TrapState.HasActive` | No new dependencies |

### External Dependencies

None. Pinder.Core has zero NuGet dependencies. This must be preserved.

### Target Framework

- .NET Standard 2.0
- C# Language Version 8.0
- No `record` types (use `sealed class`)
- Nullable reference types enabled (`?` annotations)

### Consumers (downstream Sprint 7 issues)

| Consumer Issue | Uses |
|----------------|------|
| #43 (Read/Recover/Wait) | `RollEngine.ResolveFixedDC`, `TrapState.HasActive` |
| #44 (Shadow Growth) | `SessionShadowTracker` |
| #45 (Shadow Thresholds) | `SessionShadowTracker.GetEffectiveShadow` |
| #46 (Combo System) | `GameSessionConfig`, `RollEngine.Resolve(externalBonus)` |
| #47 (Callback Bonus) | `RollEngine.Resolve(externalBonus)` |
| #48 (XP Tracking) | `GameSessionConfig` |
| #49 (Tells) | `RollEngine.Resolve(externalBonus)` |
| #50 (Weakness Windows) | `RollEngine.Resolve(dcAdjustment)` |
| #51 (Horniness/Forced Rizz) | `IGameClock`, `SessionShadowTracker`, `GameSessionConfig` |
| #54 (GameClock impl) | `IGameClock`, `TimeOfDay` |
| #55 (Player Response Delay) | `IGameClock` |
| #56 (Conversation Registry) | `IGameClock`, `GameSessionConfig` |

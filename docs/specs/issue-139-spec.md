# Specification: Issue #139 — Wave 0 Infrastructure Prerequisites

## Overview

Wave 0 bundles the foundational infrastructure components that every other Sprint 8 feature depends on: `SessionShadowTracker` (mutable shadow tracking wrapping immutable `StatBlock`), `IGameClock` (simulated in-game time interface with `TimeOfDay` enum), `RollEngine` extensions (`ResolveFixedDC` overload + `externalBonus`/`dcAdjustment` parameters), and `GameSessionConfig` (optional configuration carrier for `GameSession`). Two small additions — `InterestMeter(int)` constructor overload and `TrapState.HasActive` property — round out the wave. All changes must be backward-compatible with the existing 254 tests.

## Function Signatures

### 1. SessionShadowTracker (`Pinder.Core.Stats`)

**File:** `src/Pinder.Core/Stats/SessionShadowTracker.cs`

```csharp
namespace Pinder.Core.Stats
{
    /// <summary>
    /// Mutable shadow tracking layer wrapping an immutable StatBlock.
    /// Tracks in-session shadow growth deltas and provides effective stat values
    /// that account for session-accumulated shadow growth.
    /// </summary>
    public sealed class SessionShadowTracker
    {
        /// <summary>
        /// Wraps an immutable StatBlock for mutable shadow tracking.
        /// </summary>
        /// <param name="baseStats">Immutable StatBlock to wrap. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when baseStats is null.</exception>
        public SessionShadowTracker(StatBlock baseStats);

        /// <summary>
        /// Returns the effective shadow value: base shadow + in-session delta.
        /// </summary>
        public int GetEffectiveShadow(ShadowStatType shadow);

        /// <summary>
        /// Applies positive growth to a shadow stat. Stores the description for DrainGrowthEvents().
        /// </summary>
        /// <param name="shadow">The shadow stat to grow.</param>
        /// <param name="amount">Growth amount. Must be > 0.</param>
        /// <param name="reason">Human-readable reason for the growth.</param>
        /// <returns>Description string: "{ShadowStatName} +{amount} ({reason})"</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when amount is less than or equal to 0.</exception>
        public string ApplyGrowth(ShadowStatType shadow, int amount, string reason);

        /// <summary>
        /// Returns the effective stat modifier accounting for in-session shadow growth.
        /// Formula: baseStats.GetBase(stat) - floor((baseStats.GetShadow(pairedShadow) + delta[pairedShadow]) / 3)
        /// Uses StatBlock.ShadowPairs to determine the paired shadow stat.
        /// </summary>
        public int GetEffectiveStat(StatType stat);

        /// <summary>
        /// Returns only the in-session delta for a shadow stat (0 if no growth has occurred).
        /// </summary>
        public int GetDelta(ShadowStatType shadow);

        /// <summary>
        /// Returns all growth event description strings accumulated since last drain, then clears the internal log.
        /// Returns an empty list if no growth events have occurred since the last drain (or since construction).
        /// Added per #161 resolution — this is the canonical drain method, replacing the dropped CharacterState concept.
        /// </summary>
        public IReadOnlyList<string> DrainGrowthEvents();
    }
}
```

### 2. IGameClock + TimeOfDay (`Pinder.Core.Interfaces`)

**File:** `src/Pinder.Core/Interfaces/IGameClock.cs`

```csharp
namespace Pinder.Core.Interfaces
{
    /// <summary>
    /// Time-of-day buckets for the simulated game clock.
    /// Hour boundaries are inclusive-start, exclusive-end.
    /// </summary>
    public enum TimeOfDay
    {
        Morning,      // 06:00–11:59
        Afternoon,    // 12:00–17:59
        Evening,      // 18:00–21:59
        LateNight,    // 22:00–01:59
        AfterTwoAm    // 02:00–05:59
    }

    /// <summary>
    /// Simulated in-game clock. Injectable for testing via a FixedGameClock implementation.
    /// Concrete implementation (GameClock) is built in issue #54; this issue defines the interface only.
    /// </summary>
    public interface IGameClock
    {
        /// <summary>Current simulated time.</summary>
        DateTimeOffset Now { get; }

        /// <summary>Advance clock by the given amount.</summary>
        void Advance(TimeSpan amount);

        /// <summary>
        /// Advance clock to a specific point in time.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when target is less than or equal to Now.</exception>
        void AdvanceTo(DateTimeOffset target);

        /// <summary>
        /// Returns the current time-of-day bucket based on Now.Hour.
        /// </summary>
        TimeOfDay GetTimeOfDay();

        /// <summary>
        /// Returns the horniness modifier for the current time of day.
        /// Morning=-2, Afternoon=0, Evening=+1, LateNight=+3, AfterTwoAm=+5.
        /// </summary>
        int GetHorninessModifier();

        /// <summary>Energy remaining for the current day.</summary>
        int RemainingEnergy { get; }

        /// <summary>
        /// Attempt to consume energy. Returns false if insufficient (no deduction on failure).
        /// </summary>
        bool ConsumeEnergy(int amount);
    }
}
```

### 3. RollEngine Extensions (`Pinder.Core.Rolls`)

**File:** `src/Pinder.Core/Rolls/RollEngine.cs` (modify existing)

```csharp
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
    int externalBonus = 0,    // NEW: passed into RollResult, affects FinalTotal and IsSuccess
    int dcAdjustment = 0);    // NEW: subtracted from DC (positive value = easier roll)

// NEW overload — resolves against a fixed DC instead of computing DC from a defender
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
    int externalBonus = 0);         // NEW — sets ExternalBonus during construction

// IsSuccess computation changes FROM:
//   IsSuccess = IsNatTwenty || (!IsNatOne && Total >= dc);
// TO:
//   IsSuccess = IsNatTwenty || (!IsNatOne && FinalTotal >= dc);
//
// FinalTotal is already defined as Total + ExternalBonus.
// When externalBonus=0 (the default), FinalTotal == Total, so behavior is identical.

// AddExternalBonus() remains for backward compatibility but is DEPRECATED.
// All new external bonuses must flow through RollEngine.Resolve(externalBonus) or
// RollEngine.ResolveFixedDC(externalBonus).
```

### 5. GameSessionConfig (`Pinder.Core.Conversation`)

**File:** `src/Pinder.Core/Conversation/GameSessionConfig.cs`

```csharp
namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Optional configuration carrier for GameSession. All properties are nullable —
    /// null means "use the default behavior".
    /// </summary>
    public sealed class GameSessionConfig
    {
        public IGameClock? Clock { get; }
        public SessionShadowTracker? PlayerShadows { get; }
        public SessionShadowTracker? OpponentShadows { get; }
        public int? StartingInterest { get; }
        public string? PreviousOpener { get; }  // per #162 resolution

        public GameSessionConfig(
            IGameClock? clock = null,
            SessionShadowTracker? playerShadows = null,
            SessionShadowTracker? opponentShadows = null,
            int? startingInterest = null,
            string? previousOpener = null);
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

// NEW overload — accepts optional config
public GameSession(
    CharacterProfile player,
    CharacterProfile opponent,
    ILlmAdapter llm,
    IDiceRoller dice,
    ITrapRegistry trapRegistry,
    GameSessionConfig? config);
```

### 7. InterestMeter Constructor Overload (`Pinder.Core.Conversation`)

**File:** `src/Pinder.Core/Conversation/InterestMeter.cs` (modify existing)

```csharp
// NEW overload — custom starting value, clamped to [Min, Max]
public InterestMeter(int startingValue);
// Sets Current = Math.Max(Min, Math.Min(Max, startingValue))
```

### 8. TrapState.HasActive Property (`Pinder.Core.Traps`)

**File:** `src/Pinder.Core/Traps/TrapState.cs` (modify existing)

```csharp
/// <summary>True if any trap is currently active.</summary>
public bool HasActive => _active.Count > 0;
```

---

## Input/Output Examples

### SessionShadowTracker

**Setup StatBlock:**
```
Base stats:    Charm=3, Rizz=2, Honesty=1, Chaos=0, Wit=4, SelfAwareness=2
Shadow stats:  Madness=2, Horniness=0, Denial=0, Fixation=0, Dread=5, Overthinking=1
```

**Initial state (no growth applied):**

| Call | Return Value | Explanation |
|------|-------------|-------------|
| `GetEffectiveShadow(Madness)` | `2` | base 2 + delta 0 = 2 |
| `GetDelta(Madness)` | `0` | no growth yet |
| `GetEffectiveStat(Charm)` | `2` | Charm(3) − floor((Madness 2 + delta 0) / 3) = 3 − 0 = 3. Wait — floor(2/3) = 0, so 3 − 0 = 3 |

Correction — initial `GetEffectiveStat(Charm)` = **3** (floor(2/3) = 0, penalty = 0).

| Call | Return Value | Explanation |
|------|-------------|-------------|
| `GetEffectiveStat(Charm)` | `3` | 3 − floor(2/3) = 3 − 0 = 3 |
| `GetEffectiveStat(Wit)` | `3` | 4 − floor(5/3) = 4 − 1 = 3 |

**After `ApplyGrowth(Madness, 1, "Charm fail")`:**

| Call | Return Value | Explanation |
|------|-------------|-------------|
| Return value of `ApplyGrowth` | `"Madness +1 (Charm fail)"` | Format: `"{Name} +{amount} ({reason})"` |
| `GetEffectiveShadow(Madness)` | `3` | base 2 + delta 1 = 3 |
| `GetDelta(Madness)` | `1` | accumulated delta |
| `GetEffectiveStat(Charm)` | `2` | 3 − floor(3/3) = 3 − 1 = 2 |

**After `ApplyGrowth(Dread, 1, "combo trigger")`:**

| Call | Return Value | Explanation |
|------|-------------|-------------|
| Return value of `ApplyGrowth` | `"Dread +1 (combo trigger)"` | |
| `GetEffectiveShadow(Dread)` | `6` | base 5 + delta 1 = 6 |
| `GetEffectiveStat(Wit)` | `2` | 4 − floor(6/3) = 4 − 2 = 2 |

**DrainGrowthEvents after the two growths above:**

| Call | Return Value |
|------|-------------|
| `DrainGrowthEvents()` | `["Madness +1 (Charm fail)", "Dread +1 (combo trigger)"]` |
| `DrainGrowthEvents()` (second call) | `[]` (empty — log was cleared by first drain) |

### RollEngine.ResolveFixedDC

**Scenario A: Read action succeeds (DC 12, SelfAwareness stat)**
```
attacker StatBlock: SelfAwareness base=2, Overthinking=0 → GetEffective(SA) = 2
level = 3 → LevelTable.GetBonus(3) = 1
dice rolls: 11
No active traps, no advantage/disadvantage, externalBonus = 0

Total = 11 + 2 + 1 = 14
FinalTotal = 14 + 0 = 14
DC = 12
IsSuccess = true (14 >= 12)
RiskTier: need = 12 - (2 + 1) = 9 → Medium
```

**Scenario B: Read action fails (DC 12)**
```
attacker StatBlock: SelfAwareness base=1, Overthinking=3 → GetEffective(SA) = 0
level = 1 → LevelTable.GetBonus(1) = 0
dice rolls: 8

Total = 8 + 0 + 0 = 8
DC = 12
MissMargin = 12 − 8 = 4 → FailureTier.Misfire
IsSuccess = false
```

### RollEngine.Resolve with externalBonus and dcAdjustment

**Scenario: Attack with callback bonus (+2) and weakness window (DC −2)**
```
stat = Charm
attacker: Charm effective = 3, level = 2 → LevelTable.GetBonus(2) = 0
defender: DC for Charm = 13 + defender.GetEffective(SelfAwareness) = 13 + 2 = 15
externalBonus = 2 (callback)
dcAdjustment = 2 (weakness window)

dice rolls: 10
Total = 10 + 3 + 0 = 13
FinalTotal = 13 + 2 = 15
Adjusted DC = 15 − 2 = 13
IsSuccess = true (15 >= 13)
```

Without externalBonus and dcAdjustment: Total 13 vs DC 15 → miss by 2 → FailureTier.Fumble.

### InterestMeter(int)

| Constructor Argument | Resulting `Current` | Explanation |
|---------------------|---------------------|-------------|
| `InterestMeter()` | 10 | Default (unchanged behavior) |
| `InterestMeter(8)` | 8 | Normal value within range |
| `InterestMeter(0)` | 0 | Minimum boundary |
| `InterestMeter(25)` | 25 | Maximum boundary |
| `InterestMeter(-5)` | 0 | Clamped to Min (0) |
| `InterestMeter(30)` | 25 | Clamped to Max (25) |

### TrapState.HasActive

| State | `HasActive` |
|-------|-------------|
| Fresh TrapState (no traps activated) | `false` |
| After `Activate(trapDef)` on one stat | `true` |
| After `Clear(stat)` removes the only active trap | `false` |
| After `AdvanceTurn()` expires all traps | `false` |
| Two traps active, one cleared | `true` |

### GameSessionConfig

**Example: Custom starting interest with player shadow tracker**
```csharp
var config = new GameSessionConfig(
    clock: null,
    playerShadows: new SessionShadowTracker(playerStats),
    opponentShadows: null,
    startingInterest: 8,
    previousOpener: null
);
var session = new GameSession(player, opponent, llm, dice, trapRegistry, config);
// → InterestMeter starts at 8 instead of default 10
// → PlayerShadows tracker stored for shadow growth mechanics
// → No clock injected (time mechanics disabled for this session)
// → No opponent shadows (opponent shadow growth disabled)
// → No previous opener
```

**Example: All-null config (equivalent to no config)**
```csharp
var session = new GameSession(player, opponent, llm, dice, trapRegistry, new GameSessionConfig());
// Identical behavior to: new GameSession(player, opponent, llm, dice, trapRegistry)
```

---

## Acceptance Criteria

### AC1: SessionShadowTracker — construction, GetEffectiveShadow, ApplyGrowth, GetEffectiveStat, GetDelta, DrainGrowthEvents

**Construction:**
- Accepts a non-null `StatBlock` as the sole constructor parameter.
- Throws `ArgumentNullException` if `baseStats` is null.
- All shadow deltas initialize to 0.
- Internal growth event log starts empty.

**GetEffectiveShadow(ShadowStatType):**
- Returns `baseStats.GetShadow(shadow) + delta[shadow]`.
- Returns the base shadow value when no growth has occurred (delta = 0).

**ApplyGrowth(ShadowStatType, int, string):**
- Adds `amount` to the internal delta for the given shadow stat.
- Returns a string formatted as `"{ShadowStatName} +{amount} ({reason})"`.
  - Example: `ApplyGrowth(ShadowStatType.Madness, 1, "Charm fail")` → `"Madness +1 (Charm fail)"`.
- Stores the returned description string in an internal log (for `DrainGrowthEvents()`).
- Throws `ArgumentOutOfRangeException` if `amount <= 0`.
- Multiple calls accumulate: `ApplyGrowth(Madness, 1, ...)` then `ApplyGrowth(Madness, 2, ...)` → delta = 3.

**GetEffectiveStat(StatType):**
- Returns `baseStats.GetBase(stat) - floor((baseStats.GetShadow(pairedShadow) + delta[pairedShadow]) / 3)`.
- Uses `StatBlock.ShadowPairs` to determine the paired shadow stat for the given stat.
- Integer division in C# naturally floors for non-negative values, which is correct here since shadow values are non-negative.

**GetDelta(ShadowStatType):**
- Returns the in-session delta only (not including base shadow value).
- Returns 0 if no growth has occurred for that shadow stat.

**DrainGrowthEvents():**
- Returns an `IReadOnlyList<string>` of all growth event descriptions accumulated since the last drain (or since construction).
- Clears the internal log after copying the list.
- Returns an empty list if no growth events have occurred since the last drain.
- Order of events is preserved (first growth first in the list).

### AC2: IGameClock interface defined with TimeOfDay enum

**TimeOfDay enum** must have exactly 5 values: `Morning`, `Afternoon`, `Evening`, `LateNight`, `AfterTwoAm`.

**IGameClock interface** must define all 7 members listed in the Function Signatures section above.

**Time-of-day bucket mapping (based on `Now.Hour`):**
- Hours 6–11 → `Morning`
- Hours 12–17 → `Afternoon`
- Hours 18–21 → `Evening`
- Hours 22–23, 0–1 → `LateNight`
- Hours 2–5 → `AfterTwoAm`

**Horniness modifier mapping:**
- `Morning` → −2
- `Afternoon` → 0
- `Evening` → +1
- `LateNight` → +3
- `AfterTwoAm` → +5

**Note:** This issue defines the **interface** and **enum** only. The concrete `GameClock` implementation is built in issue #54. A test-only `FixedGameClock` implementation may be needed for testing other Wave 0 components.

### AC3: RollEngine.ResolveFixedDC works for DC 12 rolls

- New static method `ResolveFixedDC` added to `RollEngine`.
- Uses `fixedDc` parameter directly instead of computing DC from a defender's stat block.
- No `defender` parameter — DC is entirely caller-specified.
- All other mechanics are identical to `Resolve()`:
  - Trap effects on attacker (disadvantage, stat penalty).
  - Advantage/disadvantage resolution (disadvantage overrides advantage).
  - Failure tier determination: Nat 1 → Legendary, Nat 20 → auto-success, miss margin tiers (1–2 Fumble, 3–5 Misfire, 6–9 TropeTrap, 10+ Catastrophe).
  - TropeTrap failure tier activates a trap if one is defined and not already active on that stat.
- The `externalBonus` parameter flows into the constructed `RollResult`.
- Works for any fixed DC value, not limited to 12.

### AC4: RollEngine.Resolve accepts externalBonus and dcAdjustment (default 0)

- Two new optional `int` parameters appended to the existing `Resolve` signature.
- `externalBonus` (default 0): Passed into the `RollResult` constructor. Affects `FinalTotal` and therefore `IsSuccess`.
- `dcAdjustment` (default 0): Subtracted from the computed DC before the success/failure check. A positive `dcAdjustment` makes the roll easier (lower DC). The adjusted DC is stored in the `RollResult`.
- Both default to 0, ensuring all existing callers produce identical results.

### AC5: RollResult.IsSuccess uses FinalTotal (backward compatible when ExternalBonus=0)

- `IsSuccess` computation changes to: `IsNatTwenty || (!IsNatOne && FinalTotal >= DC)`.
- `FinalTotal` is defined as `Total + ExternalBonus` (property already exists).
- The constructor gains an `int externalBonus = 0` parameter that sets `ExternalBonus` during construction.
- When `externalBonus = 0` (the default), `FinalTotal == Total`, producing identical results to current behavior.
- `MissMargin` remains `DC - Total` (not `DC - FinalTotal`) for backward compatibility.
- `AddExternalBonus()` is **DEPRECATED** but remains functional for backward compatibility. New code must use the `externalBonus` constructor parameter (via `RollEngine`).

### AC6: GameSessionConfig accepted by GameSession constructor

- `GameSessionConfig` is a new `sealed class` with five read-only properties, all nullable.
- Properties: `Clock` (`IGameClock?`), `PlayerShadows` (`SessionShadowTracker?`), `OpponentShadows` (`SessionShadowTracker?`), `StartingInterest` (`int?`), `PreviousOpener` (`string?`).
- `GameSession` gains a new constructor overload accepting `GameSessionConfig? config` as the last parameter.
- When `config` is null or not provided, behavior is identical to the existing constructor.
- When `config.StartingInterest` has a value, the `InterestMeter` is constructed with `new InterestMeter(config.StartingInterest.Value)`.
- When `config.PlayerShadows`, `config.OpponentShadows`, `config.Clock`, or `config.PreviousOpener` are provided, they are stored as private fields for use by downstream Sprint 8 features.
- The existing parameterless-config constructor continues to work unchanged.

### AC7: InterestMeter(int) overload works

- New constructor `InterestMeter(int startingValue)`.
- Sets `Current = Math.Max(Min, Math.Min(Max, startingValue))` — clamped to [0, 25].
- The existing parameterless `InterestMeter()` constructor remains unchanged (sets `Current = 10`).
- All existing behavior (`Apply`, `GetState`, `IsMaxed`, `IsZero`, `GrantsAdvantage`, `GrantsDisadvantage`) works identically regardless of which constructor was used.

### AC8: TrapState.HasActive works

- New read-only `bool` property `HasActive` on `TrapState`.
- Returns `true` if at least one trap is currently active; `false` otherwise.
- Implementation: `_active.Count > 0` (no LINQ, no allocation).
- Correctly reflects state changes after `Activate()`, `Clear()`, `ClearAll()`, and `AdvanceTurn()`.

### AC9: All 254 existing tests still pass

- `dotnet test` must produce 0 failures and ≥254 passed tests.
- No existing test file is modified.
- All changes are additive (new methods, new overloads, new types) or backward-compatible (IsSuccess formula change is identity when ExternalBonus=0).

### AC10: New tests for each component

- New test classes covering every public method/property of each new/modified component.
- See Edge Cases and Error Conditions sections for specific scenarios that must be tested.

### AC11: Build clean, zero warnings

- `dotnet build` produces zero warnings.
- All new public members have XML doc comments.
- No unused imports or variables.

---

## Edge Cases

### SessionShadowTracker

| Scenario | Expected Behavior |
|----------|-------------------|
| Zero base shadow + zero delta | `GetEffectiveShadow` returns 0; `GetEffectiveStat` returns base stat unmodified (floor(0/3)=0 penalty) |
| Shadow penalty exactly on boundary | base shadow=2, delta=1 → effective shadow=3 → floor(3/3)=1 → penalty=1 |
| Large accumulated delta | 10 calls of `ApplyGrowth(Madness, 1, ...)` → delta=10, `GetEffectiveShadow(Madness)` = base+10 |
| All six shadow types independent | Growth on Madness does not affect Horniness delta or any other shadow type |
| Negative effective stat | base Chaos=0, paired Fixation effective=3 → 0 − floor(3/3) = −1 (valid — negative modifiers are legal) |
| DrainGrowthEvents with no events | Returns empty `IReadOnlyList<string>` |
| DrainGrowthEvents clears log | Second call after drain returns empty list even if events were present before first drain |
| Multiple growths to same shadow | All descriptions are captured individually in DrainGrowthEvents |

### RollEngine

| Scenario | Expected Behavior |
|----------|-------------------|
| Nat 1 with positive externalBonus | Still auto-fail (Legendary). ExternalBonus does not override Nat 1. |
| Nat 20 with negative externalBonus | Still auto-success. Nat 20 overrides everything. |
| dcAdjustment larger than DC | If DC=15 and dcAdjustment=20, adjusted DC=−5. Any non-Nat1 roll succeeds. |
| Negative externalBonus | Valid penalty. Total might drop below DC on an otherwise-passing roll. |
| externalBonus pushes miss to success | Total=12, DC=14, externalBonus=3 → FinalTotal=15 ≥ 14 → success, Tier=None. |
| ResolveFixedDC with fixedDc=0 | Trivially succeeds (any non-Nat1 roll). |
| ResolveFixedDC with very high fixedDc | E.g., DC 30 — only Nat 20 can succeed. |
| ResolveFixedDC TropeTrap activation | Miss by 6–9 on fixed DC activates a trap if defined and not already active on that stat. |
| Both externalBonus and dcAdjustment | Effects are independent: `FinalTotal >= adjustedDC`. |
| externalBonus=0 and dcAdjustment=0 | Identical behavior to current `Resolve()` — no regression. |

### RollResult

| Scenario | Expected Behavior |
|----------|-------------------|
| ExternalBonus via constructor | Sets `ExternalBonus` property. `FinalTotal = Total + ExternalBonus`. |
| ExternalBonus via deprecated AddExternalBonus | Still additive — adds on top of constructor-set value. Must remain functional. |
| MissMargin with ExternalBonus | `MissMargin` uses `DC - Total` (not `DC - FinalTotal`). This preserves backward compatibility. |
| Constructor externalBonus=0 | `FinalTotal == Total`, `IsSuccess` unchanged from current behavior. |

### InterestMeter

| Scenario | Expected Behavior |
|----------|-------------------|
| `InterestMeter(0)` | Current=0, `GetState()` → `Unmatched` |
| `InterestMeter(25)` | Current=25, `GetState()` → `DateSecured` |
| `InterestMeter(-100)` | Current=0 (clamped to Min) |
| `InterestMeter(100)` | Current=25 (clamped to Max) |
| `InterestMeter(3)` | Current=3, `GetState()` → `Bored` |
| `Apply(-5)` after `InterestMeter(3)` | Current=0 (clamped), `GetState()` → `Unmatched` |

### TrapState.HasActive

| Scenario | Expected Behavior |
|----------|-------------------|
| Empty state after construction | `false` |
| After `Activate(trapDef)` | `true` |
| After `Clear(stat)` removes only active trap | `false` |
| After `ClearAll()` | `false` |
| After `AdvanceTurn()` expires all traps | `false` |
| Two active traps, one removed | `true` |

### GameSessionConfig

| Scenario | Expected Behavior |
|----------|-------------------|
| All-null config | Equivalent to no config — all defaults apply |
| `StartingInterest = 0` | Valid — game starts in Unmatched state |
| `StartingInterest` negative | Clamped by InterestMeter constructor to 0 |
| `StartingInterest > 25` | Clamped by InterestMeter constructor to 25 |
| `PreviousOpener` set | Stored for downstream feature (#44 shadow growth / #47 callback) |
| Config is null | `new GameSession(p, o, llm, dice, tr, null)` behaves identically to 5-param constructor |

---

## Error Conditions

| Component | Error Trigger | Expected Exception | Message / Behavior |
|-----------|--------------|-------------------|---------------------|
| `SessionShadowTracker` constructor | `null` baseStats | `ArgumentNullException` | Parameter name: `"baseStats"` |
| `SessionShadowTracker.ApplyGrowth` | `amount <= 0` | `ArgumentOutOfRangeException` | Parameter name: `"amount"` |
| `IGameClock.AdvanceTo` | `target <= Now` | `ArgumentException` | Target must be in the future |
| `IGameClock.ConsumeEnergy` | Insufficient energy | Returns `false` (no exception, no deduction) | N/A |
| `GameSession` constructor (existing) | `null` for player, opponent, llm, dice, or trapRegistry | `ArgumentNullException` | Respective parameter name |
| `RollEngine` methods | Null attacker, attackerTraps, trapRegistry, or dice | `NullReferenceException` at usage point | Existing behavior — no explicit validation |

---

## Dependencies

### Internal Dependencies (within Pinder.Core)

| New/Modified Component | Depends On |
|------------------------|-----------|
| `SessionShadowTracker` | `StatBlock`, `StatType`, `ShadowStatType`, `StatBlock.ShadowPairs` |
| `IGameClock` / `TimeOfDay` | `System.DateTimeOffset`, `System.TimeSpan` (BCL only) |
| `RollEngine.ResolveFixedDC` | `StatType`, `StatBlock`, `TrapState`, `ITrapRegistry`, `IDiceRoller`, `RollResult`, `FailureTier`, `LevelTable`, `TrapEffect`, `TrapDefinition` |
| `RollEngine.Resolve` (modified) | Same as current + passes `externalBonus` / `dcAdjustment` to `RollResult` |
| `RollResult` (modified) | No new dependencies |
| `GameSessionConfig` | `IGameClock`, `SessionShadowTracker` |
| `GameSession` (modified) | `GameSessionConfig`, `InterestMeter(int)` |
| `InterestMeter(int)` | No new dependencies |
| `TrapState.HasActive` | No new dependencies |

### External Dependencies

**None.** Pinder.Core has zero NuGet dependencies. This must be preserved.

### Target Framework

- **.NET Standard 2.0** — no `record` types (use `sealed class`)
- **C# Language Version 8.0** — nullable reference types enabled (`?` annotations)
- `Task<T>` available from BCL

### Downstream Consumers (Sprint 8 issues that depend on Wave 0)

| Consumer Issue | Components Used |
|----------------|----------------|
| #43 (Read/Recover/Wait) | `RollEngine.ResolveFixedDC`, `TrapState.HasActive` |
| #44 (Shadow Growth) | `SessionShadowTracker`, `GameSessionConfig.PlayerShadows` |
| #45 (Shadow Thresholds) | `SessionShadowTracker.GetEffectiveShadow` |
| #46 (Combo System) | `GameSessionConfig`, `RollEngine.Resolve(externalBonus)` |
| #47 (Callback Bonus) | `RollEngine.Resolve(externalBonus)`, `GameSessionConfig.PreviousOpener` |
| #48 (XP Tracking) | `GameSessionConfig` |
| #49 (Tells) | `RollEngine.Resolve(externalBonus)` |
| #50 (Weakness Windows) | `RollEngine.Resolve(dcAdjustment)` |
| #51 (Horniness/Forced Rizz) | `IGameClock`, `SessionShadowTracker`, `GameSessionConfig` |
| #54 (GameClock impl) | `IGameClock`, `TimeOfDay` |
| #55 (Player Response Delay) | `IGameClock` |
| #56 (Conversation Registry) | `IGameClock`, `GameSessionConfig` |

### Architecture Contract Reference

Primary contract: `contracts/sprint-8-wave0-infrastructure.md`
ADR #161: `SessionShadowTracker` is canonical — `CharacterState` dropped.
ADR #162: `PreviousOpener` goes into `GameSessionConfig`, not a dedicated constructor param.

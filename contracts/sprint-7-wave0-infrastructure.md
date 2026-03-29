# Contract: Issue #139 — Wave 0 Infrastructure Prerequisites

## Components
- `SessionShadowTracker` (Stats/)
- `IGameClock` + `TimeOfDay` (Interfaces/)
- `RollEngine` extensions (Rolls/)
- `GameSessionConfig` (Conversation/)
- `InterestMeter` overload (Conversation/)
- `TrapState.HasActive` (Traps/)

---

## 1. SessionShadowTracker

**File:** `src/Pinder.Core/Stats/SessionShadowTracker.cs`

```csharp
namespace Pinder.Core.Stats
{
    /// <summary>
    /// Mutable shadow tracking layer that wraps an immutable StatBlock.
    /// Tracks in-session shadow growth deltas without modifying the underlying StatBlock.
    /// </summary>
    public sealed class SessionShadowTracker
    {
        /// <param name="baseStats">The immutable StatBlock to wrap.</param>
        public SessionShadowTracker(StatBlock baseStats);

        /// <summary>
        /// Returns the effective shadow value: base shadow + in-session delta.
        /// </summary>
        public int GetEffectiveShadow(ShadowStatType shadow);

        /// <summary>
        /// Apply shadow growth. Returns a human-readable description string.
        /// Example: "Overthinking +1 (Read failed)"
        /// </summary>
        /// <param name="shadow">Which shadow stat to grow.</param>
        /// <param name="amount">Positive integer to add.</param>
        /// <param name="reason">Human-readable reason for the growth.</param>
        /// <returns>Description string: "{ShadowName} +{amount} ({reason})"</returns>
        public string ApplyGrowth(ShadowStatType shadow, int amount, string reason);

        /// <summary>
        /// Returns the effective stat modifier accounting for in-session shadow growth.
        /// Formula: baseStat - floor((baseShadow + sessionDelta) / 3)
        /// </summary>
        public int GetEffectiveStat(StatType stat);

        /// <summary>
        /// Returns only the in-session delta for a shadow stat (0 if no growth).
        /// </summary>
        public int GetDelta(ShadowStatType shadow);
    }
}
```

**Invariants:**
- `GetEffectiveShadow(s)` = `baseStats.GetShadow(s) + delta[s]`
- `GetEffectiveStat(t)` = `baseStats.GetBase(t) - floor(GetEffectiveShadow(ShadowPairs[t]) / 3)`
- `amount` must be positive in `ApplyGrowth`; throw `ArgumentOutOfRangeException` if ≤ 0
- Thread safety: not required (single-threaded game loop)

**Dependencies:** `StatBlock`, `StatType`, `ShadowStatType`

---

## 2. IGameClock + TimeOfDay

**File:** `src/Pinder.Core/Interfaces/IGameClock.cs`

```csharp
namespace Pinder.Core.Interfaces
{
    public enum TimeOfDay
    {
        Morning,      // 6:00–11:59
        Afternoon,    // 12:00–17:59
        Evening,      // 18:00–21:59
        LateNight,    // 22:00–01:59
        AfterTwoAm    // 02:00–05:59
    }

    public interface IGameClock
    {
        /// <summary>Current in-game time.</summary>
        DateTimeOffset Now { get; }

        /// <summary>Advance the clock by the given amount.</summary>
        void Advance(TimeSpan amount);

        /// <summary>Advance the clock to a specific time. Must be in the future.</summary>
        /// <exception cref="ArgumentException">If target is before Now.</exception>
        void AdvanceTo(DateTimeOffset target);

        /// <summary>Get the time-of-day bucket for the current time.</summary>
        TimeOfDay GetTimeOfDay();

        /// <summary>Horniness modifier based on time of day.</summary>
        /// <returns>Morning=-2, Afternoon=0, Evening=+1, LateNight=+3, AfterTwoAm=+5</returns>
        int GetHorninessModifier();

        /// <summary>Remaining energy for the current in-game day.</summary>
        int RemainingEnergy { get; }

        /// <summary>Consume energy. Returns false if insufficient.</summary>
        bool ConsumeEnergy(int amount);
    }
}
```

**Note:** `GameClock` implementation (#54) is a separate issue. This contract defines the interface only. `IGameClock` does NOT include `ReplenishAtMidnight()` — the implementation handles midnight crossing internally when `Advance`/`AdvanceTo` is called.

**Dependencies:** `System.DateTimeOffset`, `System.TimeSpan`

---

## 3. RollEngine Extensions

**File:** `src/Pinder.Core/Rolls/RollEngine.cs` (modify)

### 3a. New overload: ResolveFixedDC

```csharp
/// <summary>
/// Resolve a roll against a fixed DC (e.g., DC 12 for Read/Recover).
/// Same mechanics as Resolve() but uses a fixed DC instead of computing from defender stats.
/// </summary>
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
    int externalBonus = 0)
```

**Behavior:** Identical to `Resolve()` except DC is `fixedDc` instead of `defender.GetDefenceDC(stat)`. No `defender` parameter. All trap effects, advantage/disadvantage, failure tier logic apply normally.

### 3b. Existing Resolve gains optional params

```csharp
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
    int externalBonus = 0,    // NEW — added to Total before success check
    int dcAdjustment = 0)     // NEW — subtracted from DC (positive = easier)
```

**Behavior of new params:**
- `externalBonus`: Added to `Total` (becomes part of `RollResult.Total` or applied via constructor to compute `FinalTotal`). Represents callback/tell/triple bonuses.
- `dcAdjustment`: Subtracted from the computed DC. Positive value = easier roll (e.g., weakness window −2 means `dcAdjustment = 2`).
- **Backward compatible**: Both default to 0. All existing callers unchanged.

### 3c. RollResult.IsSuccess uses FinalTotal

**File:** `src/Pinder.Core/Rolls/RollResult.cs` (modify)

Change `IsSuccess` computation:
```csharp
// Before:
IsSuccess = IsNatTwenty || (!IsNatOne && Total >= dc);

// After:
IsSuccess = IsNatTwenty || (!IsNatOne && FinalTotal >= dc);
```

Where `FinalTotal = Total + ExternalBonus`. When `ExternalBonus = 0` (default), behavior is identical.

The `externalBonus` parameter from `RollEngine.Resolve()` flows into the `RollResult` constructor. The constructor signature gains `int externalBonus = 0`.

**Backward compatibility:** Since `ExternalBonus` already exists (added by PR #135) and defaults to 0, and `FinalTotal = Total + ExternalBonus`, changing `IsSuccess` to use `FinalTotal` is backward-compatible when no external bonus is applied.

---

## 4. GameSessionConfig

**File:** `src/Pinder.Core/Conversation/GameSessionConfig.cs`

```csharp
namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Optional configuration for GameSession. All fields nullable — defaults used when null.
    /// </summary>
    public sealed class GameSessionConfig
    {
        /// <summary>Game clock for time-dependent mechanics. Null = time mechanics disabled.</summary>
        public IGameClock? Clock { get; }

        /// <summary>Player's session shadow tracker. Null = shadow growth disabled.</summary>
        public SessionShadowTracker? PlayerShadows { get; }

        /// <summary>Opponent's session shadow tracker. Null = shadow growth disabled.</summary>
        public SessionShadowTracker? OpponentShadows { get; }

        /// <summary>Override starting interest value. Null = use InterestMeter.StartingValue (10).</summary>
        public int? StartingInterest { get; }

        public GameSessionConfig(
            IGameClock? clock = null,
            SessionShadowTracker? playerShadows = null,
            SessionShadowTracker? opponentShadows = null,
            int? startingInterest = null)
        {
            Clock = clock;
            PlayerShadows = playerShadows;
            OpponentShadows = opponentShadows;
            StartingInterest = startingInterest;
        }
    }
}
```

### GameSession constructor overload

```csharp
// Existing constructor remains unchanged (backward compat)
public GameSession(CharacterProfile player, CharacterProfile opponent,
    ILlmAdapter llm, IDiceRoller dice, ITrapRegistry trapRegistry);

// New overload
public GameSession(CharacterProfile player, CharacterProfile opponent,
    ILlmAdapter llm, IDiceRoller dice, ITrapRegistry trapRegistry,
    GameSessionConfig? config = null);
```

When `config` is provided:
- `config.StartingInterest` → used as `InterestMeter(startingValue)` instead of default 10
- `config.PlayerShadows` / `config.OpponentShadows` → stored for shadow growth/threshold use
- `config.Clock` → stored for time-dependent mechanics (Horniness modifier, etc.)

---

## 5. InterestMeter Constructor Overload

**File:** `src/Pinder.Core/Conversation/InterestMeter.cs` (modify)

```csharp
/// <summary>Construct with custom starting value. Clamped to [Min, Max].</summary>
public InterestMeter(int startingValue)
{
    Current = Math.Max(Min, Math.Min(Max, startingValue));
}
```

Existing parameterless constructor unchanged.

---

## 6. TrapState.HasActive

**File:** `src/Pinder.Core/Traps/TrapState.cs` (modify)

```csharp
/// <summary>True if any trap is currently active.</summary>
public bool HasActive => _active.Count > 0;
```

Uses dictionary count instead of LINQ to avoid allocation.

---

## Acceptance Criteria (from issue #139)
- [ ] SessionShadowTracker: construction, GetEffectiveShadow, ApplyGrowth, GetEffectiveStat, GetDelta
- [ ] IGameClock interface defined with TimeOfDay enum
- [ ] RollEngine.ResolveFixedDC works for DC 12 rolls
- [ ] RollEngine.Resolve accepts externalBonus and dcAdjustment (default 0)
- [ ] RollResult.IsSuccess uses FinalTotal (backward compatible when ExternalBonus=0)
- [ ] GameSessionConfig accepted by GameSession constructor
- [ ] InterestMeter(int) overload works
- [ ] TrapState.HasActive works
- [ ] All 254 existing tests still pass
- [ ] New tests for each component
- [ ] Build clean, zero warnings

## Consumers
All other Sprint 7 issues: #43, #44, #45, #46, #47, #48, #49, #50, #51, #54, #55, #56

# Contract: Issue #139 — Wave 0 Infrastructure Prerequisites (Sprint 8)

## Components
- `SessionShadowTracker` (Stats/) — with `DrainGrowthEvents()` addition per #161 resolution
- `IGameClock` + `TimeOfDay` (Interfaces/)
- `RollEngine` extensions (Rolls/)
- `RollResult` changes (Rolls/)
- `GameSessionConfig` (Conversation/) — with `PreviousOpener` per #162 resolution
- `InterestMeter` overload (Conversation/)
- `TrapState.HasActive` (Traps/)

## Maturity: Prototype
## NFR: latency target — all methods < 1ms (pure computation, no I/O)

---

## 1. SessionShadowTracker

**File:** `src/Pinder.Core/Stats/SessionShadowTracker.cs`

```csharp
namespace Pinder.Core.Stats
{
    public sealed class SessionShadowTracker
    {
        /// <param name="baseStats">Immutable StatBlock to wrap. Throws ArgumentNullException if null.</param>
        public SessionShadowTracker(StatBlock baseStats);

        /// <summary>Returns base shadow + in-session delta.</summary>
        public int GetEffectiveShadow(ShadowStatType shadow);

        /// <summary>
        /// Apply shadow growth. Returns "{ShadowName} +{amount} ({reason})".
        /// Also stores the description internally for DrainGrowthEvents().
        /// Throws ArgumentOutOfRangeException if amount <= 0.
        /// </summary>
        public string ApplyGrowth(ShadowStatType shadow, int amount, string reason);

        /// <summary>
        /// Effective stat modifier with session shadow growth.
        /// Formula: baseStats.GetBase(stat) - floor((baseStats.GetShadow(paired) + delta[paired]) / 3)
        /// </summary>
        public int GetEffectiveStat(StatType stat);

        /// <summary>In-session delta only (0 if no growth).</summary>
        public int GetDelta(ShadowStatType shadow);

        /// <summary>
        /// Returns all growth event description strings accumulated since last drain.
        /// Clears internal log after copying. Returns empty list if none.
        /// Added per #161 resolution — replaces CharacterState.DrainGrowthEvents().
        /// </summary>
        public IReadOnlyList<string> DrainGrowthEvents();
    }
}
```

**Dependencies:** `StatBlock`, `StatType`, `ShadowStatType`, `StatBlock.ShadowPairs`
**Consumers:** #43, #44, #45, #48, #51, #56

---

## 2. IGameClock + TimeOfDay

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
        bool ConsumeEnergy(int amount);         // false if insufficient (no deduction on failure)
    }
}
```

**Dependencies:** System.DateTimeOffset, System.TimeSpan (BCL only)
**Consumers:** #51, #54, #55, #56

---

## 3. RollEngine Extensions

**File:** `src/Pinder.Core/Rolls/RollEngine.cs` (modify existing)

```csharp
// MODIFIED — two new optional params (backward-compatible)
public static RollResult Resolve(
    StatType stat, StatBlock attacker, StatBlock defender,
    TrapState attackerTraps, int level, ITrapRegistry trapRegistry, IDiceRoller dice,
    bool hasAdvantage = false, bool hasDisadvantage = false,
    int externalBonus = 0,    // added to total before success check
    int dcAdjustment = 0);    // subtracted from DC (positive = easier)

// NEW overload — fixed DC instead of computing from defender
public static RollResult ResolveFixedDC(
    StatType stat, StatBlock attacker, int fixedDc,
    TrapState attackerTraps, int level, ITrapRegistry trapRegistry, IDiceRoller dice,
    bool hasAdvantage = false, bool hasDisadvantage = false,
    int externalBonus = 0);
```

**Behavioral notes:**
- `externalBonus` flows into `RollResult` constructor → affects `FinalTotal` and `IsSuccess`
- `dcAdjustment` subtracted from DC before the check. Adjusted DC used in `RollResult`.
- Nat 1 = auto-fail regardless of bonuses. Nat 20 = auto-success regardless of penalties.
- `ResolveFixedDC` has identical trap/advantage logic to `Resolve`, just no defender param.

---

## 4. RollResult Changes

**File:** `src/Pinder.Core/Rolls/RollResult.cs` (modify existing)

```csharp
// Constructor gains optional externalBonus
public RollResult(..., int externalBonus = 0);

// IsSuccess changes from: Total >= dc
// to:                      FinalTotal >= dc
// where FinalTotal = Total + ExternalBonus
// When externalBonus=0: identical behavior (backward-compatible)

// AddExternalBonus() remains but is DEPRECATED
```

**MissMargin:** stays as `DC - Total` (not FinalTotal) for backward compat.

---

## 5. GameSessionConfig

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

**Dependencies:** `IGameClock`, `SessionShadowTracker`
**Consumers:** GameSession, #44, #48, #51, #56

---

## 6. InterestMeter Overload

**File:** `src/Pinder.Core/Conversation/InterestMeter.cs` (modify existing)

```csharp
// NEW — custom starting value, clamped to [Min, Max]
public InterestMeter(int startingValue);
// Current = Math.Max(Min, Math.Min(Max, startingValue))
```

---

## 7. TrapState.HasActive

**File:** `src/Pinder.Core/Traps/TrapState.cs` (modify existing)

```csharp
/// <summary>True if any trap is currently active.</summary>
public bool HasActive => _active.Count > 0;
```

---

## 8. GameSession Constructor Overload

**File:** `src/Pinder.Core/Conversation/GameSession.cs` (modify existing)

```csharp
// Existing constructor unchanged
public GameSession(CharacterProfile player, CharacterProfile opponent,
    ILlmAdapter llm, IDiceRoller dice, ITrapRegistry trapRegistry);

// NEW overload — stores config fields for use by feature issues
public GameSession(CharacterProfile player, CharacterProfile opponent,
    ILlmAdapter llm, IDiceRoller dice, ITrapRegistry trapRegistry,
    GameSessionConfig? config);
```

When `config?.StartingInterest` is set → `new InterestMeter(value)`.
Stores `config.PlayerShadows`, `config.OpponentShadows`, `config.Clock`, `config.PreviousOpener` as private fields.

---

## Backward Compatibility

All changes are additive or use default parameter values. Existing 254 tests must pass without modification.

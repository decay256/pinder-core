# Contract: Issue #130 — Wave 0 Prerequisites

## Overview
Infrastructure changes required before any Wave 1/2 feature work can begin. Four pieces that multiple issues depend on.

## Component 1: SessionShadowTracker

**Location**: `Pinder.Core.Stats.SessionShadowTracker`

**Purpose**: Mutable shadow tracking layer that wraps an immutable `StatBlock`. Provides read access to effective shadow values (base + in-session deltas) and write access to apply growth. Does NOT modify `StatBlock._shadow`.

### Interface

```csharp
namespace Pinder.Core.Stats
{
    /// <summary>
    /// Tracks in-session shadow stat mutations without modifying the underlying StatBlock.
    /// Shadow values for roll resolution: base (from StatBlock) + delta (from this tracker).
    /// </summary>
    public sealed class SessionShadowTracker
    {
        /// <param name="baseStats">The character's immutable StatBlock.</param>
        public SessionShadowTracker(StatBlock baseStats);

        /// <summary>
        /// Get effective shadow value: StatBlock.GetShadow(shadow) + in-session delta.
        /// </summary>
        public int GetEffectiveShadow(ShadowStatType shadow);

        /// <summary>
        /// Apply shadow growth. Adds `amount` to the in-session delta.
        /// Amount must be positive (growth) or negative (reduction, e.g. Fixation offset).
        /// </summary>
        /// <returns>Description string for TurnResult.ShadowGrowthEvents, e.g. "Dread +2 (interest hit 0)".</returns>
        public string ApplyGrowth(ShadowStatType shadow, int amount, string reason);

        /// <summary>
        /// Get the in-session delta only (not including base). For end-of-session reporting.
        /// </summary>
        public int GetDelta(ShadowStatType shadow);

        /// <summary>
        /// Get effective stat modifier accounting for in-session shadow growth.
        /// Replaces StatBlock.GetEffective() when session-level shadow tracking is active.
        /// Formula: baseStatValue - floor((baseShadow + delta) / 3)
        /// </summary>
        public int GetEffectiveStat(StatType stat);
    }
}
```

### Behavioral contracts
- `GetEffectiveShadow` never returns negative (clamp to 0).
- `ApplyGrowth` with amount=0 is a no-op, returns empty string.
- Thread safety: NOT thread-safe. GameSession is single-threaded.
- The underlying `StatBlock` is never modified.

### Consumers
#43, #44, #45, #51, #56

---

## Component 2: IGameClock Interface

**Location**: `Pinder.Core.Interfaces.IGameClock`

**Purpose**: Abstraction for simulated in-game time. Implementations provide `GameClock` (real) and `FixedGameClock` (test).

### Interface

```csharp
namespace Pinder.Core.Interfaces
{
    public enum TimeOfDay
    {
        Morning,     // 6:00–11:59
        Afternoon,   // 12:00–17:59
        Evening,     // 18:00–21:59
        LateNight,   // 22:00–1:59
        AfterTwoAm   // 2:00–5:59
    }

    public interface IGameClock
    {
        /// <summary>Current simulated time.</summary>
        DateTimeOffset Now { get; }

        /// <summary>Advance clock by the given amount.</summary>
        void Advance(TimeSpan amount);

        /// <summary>Advance clock to the given time. Must be >= Now.</summary>
        /// <exception cref="ArgumentException">If target is before Now.</exception>
        void AdvanceTo(DateTimeOffset target);

        /// <summary>Derive time-of-day from current hour.</summary>
        TimeOfDay GetTimeOfDay();

        /// <summary>
        /// Horniness modifier based on time of day.
        /// Morning=-2, Afternoon=0, Evening=+1, LateNight=+3, AfterTwoAm=+5.
        /// </summary>
        int GetHorninessModifier();

        /// <summary>Remaining energy for today.</summary>
        int RemainingEnergy { get; }

        /// <summary>Consume energy. Returns false if insufficient.</summary>
        bool ConsumeEnergy(int amount);
    }
}
```

### Behavioral contracts
- `Advance(negative)` throws `ArgumentException`.
- `AdvanceTo(past)` throws `ArgumentException`.
- Energy resets when clock crosses midnight (handled internally by Advance/AdvanceTo).
- Initial energy: roll `dice.Roll(6) + 14` (range 15–20) at construction and each midnight.

### Consumers
#51, #54, #55, #56

---

## Component 3: RollEngine Extensions

**Location**: `Pinder.Core.Rolls.RollEngine`

### 3a. ResolveFixedDC overload

```csharp
/// <summary>
/// Resolve a roll against a fixed DC (no defender stat block needed).
/// Used for Read (DC 12) and Recover (DC 12).
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
    int externalBonus = 0);
```

**Behavioral contract**: Same as `Resolve()` but uses `fixedDc` directly instead of computing from defender. Failure tier boundaries use `fixedDc - total` as the miss margin. TropeTrap activation still works normally.

### 3b. externalBonus parameter on existing Resolve

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
    int externalBonus = 0);  // NEW — default 0, backward compatible
```

**Behavioral contract**: `externalBonus` is added to `Total` in `RollResult`. `IsSuccess` checks `Total + externalBonus >= DC`. The bonus is reflected in `RollResult.Total` (or alternatively in `RollResult.FinalTotal` — see existing `ExternalBonus` property). 

**IMPORTANT**: `RollResult` already has `AddExternalBonus(int)` and `FinalTotal`. The decision here is: should `RollEngine.Resolve` call `AddExternalBonus` before returning, or should the caller do it? **Decision**: `RollEngine.Resolve` applies it internally via `AddExternalBonus()` so that `IsSuccess` uses `FinalTotal`. This changes `IsSuccess` to check `FinalTotal >= dc` instead of `Total >= dc`.

**Wait — checking existing code**: `IsSuccess` is computed in the constructor as `IsNatTwenty || (!IsNatOne && Total >= dc)`. Since `ExternalBonus` is set post-construction via `AddExternalBonus()`, `IsSuccess` does NOT reflect it. This is a **known issue** (#129).

**Revised decision**: For this sprint, `RollEngine.Resolve` calls `result.AddExternalBonus(externalBonus)` before returning. `IsSuccess` must be changed to a computed property: `public bool IsSuccess => IsNatTwenty || (!IsNatOne && FinalTotal >= DC);`. This is a **breaking change** to `RollResult` — but since `ExternalBonus` defaults to 0, existing behavior is unchanged.

### Consumers
#43, #46, #47, #50

---

## Component 4: GameSessionConfig

**Location**: `Pinder.Core.Conversation.GameSessionConfig`

```csharp
namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Optional configuration for GameSession. Carries optional dependencies
    /// that were not part of the original constructor.
    /// All properties are nullable — null means "not configured / use default".
    /// </summary>
    public sealed class GameSessionConfig
    {
        public IGameClock? GameClock { get; }
        public SessionShadowTracker? PlayerShadowTracker { get; }
        public SessionShadowTracker? OpponentShadowTracker { get; }
        public int? StartingInterest { get; }

        public GameSessionConfig(
            IGameClock? gameClock = null,
            SessionShadowTracker? playerShadowTracker = null,
            SessionShadowTracker? opponentShadowTracker = null,
            int? startingInterest = null)
        {
            GameClock = gameClock;
            PlayerShadowTracker = playerShadowTracker;
            OpponentShadowTracker = opponentShadowTracker;
            StartingInterest = startingInterest;
        }
    }
}
```

### GameSession constructor addition (backward compatible)

```csharp
public GameSession(
    CharacterProfile player,
    CharacterProfile opponent,
    ILlmAdapter llm,
    IDiceRoller dice,
    ITrapRegistry trapRegistry,
    GameSessionConfig? config = null)  // NEW optional param
```

**Behavioral contract**: When `config` is null, behavior is identical to current. When provided:
- `config.GameClock` is stored for time-of-day queries
- `config.PlayerShadowTracker` is used for shadow reads/writes instead of raw StatBlock
- `config.StartingInterest` overrides `InterestMeter.StartingValue` (for Dread ≥18 → start at 8)

### InterestMeter constructor overload

```csharp
public InterestMeter(int startingValue)
{
    Current = Math.Max(Min, Math.Min(Max, startingValue));
}
```

### TrapState.HasActive property

```csharp
public bool HasActive => _active.Count > 0;
```

### Consumers
All GameSession features (#43–#56)

---

## Dependencies

```
#130 (this) → no dependencies (ships first)
#43 → #130
#44 → #130, #43
#45 → #130, #44
#46 → #130
#47 → #130
#48 → #130, #43
#49 → #130
#50 → #130
#51 → #130, #45, #54
#52 → already merged
#54 → #130
#55 → (none — pure function, but needs #130 for IGameClock type definition)
#56 → #130, #54, #44
#38 → all features landed
```

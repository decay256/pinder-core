# Contract: Issue #139 — Wave 0 Infrastructure Prerequisites

## Component
`Pinder.Core.Stats.SessionShadowTracker`, `Pinder.Core.Interfaces.IGameClock`, `Pinder.Core.Conversation.TimeOfDay`,
`Pinder.Core.Conversation.GameSessionConfig`, `RollEngine` extensions, `InterestMeter` overload, `TrapState.HasActive`

## Maturity
Prototype

## NFR
- latency_p99_ms: N/A (all in-process, no I/O)

---

## 1. SessionShadowTracker (`Pinder.Core.Stats`)

Mutable shadow tracking layer that wraps an immutable `StatBlock` for a single conversation session. Session-local shadow growth is tracked as deltas on top of the base shadow values. RollEngine and other consumers use this to get effective stats with in-session growth applied.

```csharp
namespace Pinder.Core.Stats
{
    public sealed class SessionShadowTracker
    {
        // Constructor: takes the character's base StatBlock
        public SessionShadowTracker(StatBlock baseStats);

        // Read the base StatBlock (immutable, never modified)
        public StatBlock BaseStats { get; }

        // Get effective shadow value = base shadow + session delta
        // Pre: shadow is a valid ShadowStatType
        // Post: returns >= 0
        public int GetEffectiveShadow(ShadowStatType shadow);

        // Get in-session delta only (how much shadow has grown this session)
        public int GetDelta(ShadowStatType shadow);

        // Apply shadow growth. Returns a human-readable description string.
        // Pre: amount > 0, reason is non-null
        // Post: delta for this shadow increases by amount; description returned
        // Example: ApplyGrowth(ShadowStatType.Dread, 2, "Interest hit 0 (unmatch)") → "Dread +2: Interest hit 0 (unmatch)"
        public string ApplyGrowth(ShadowStatType shadow, int amount, string reason);

        // Get effective stat modifier accounting for session shadow growth.
        // Equivalent to: baseStats.GetBase(stat) - (GetEffectiveShadow(paired shadow) / 3)
        public int GetEffectiveStat(StatType stat);

        // Get the shadow stat paired with a given stat type (delegates to StatBlock.ShadowPairs)
        public ShadowStatType GetPairedShadow(StatType stat);
    }
}
```

**Dependencies**: `StatBlock`, `StatType`, `ShadowStatType`
**Consumers**: `GameSession` (#43, #44, #45, #51), `ConversationRegistry` (#56)
**Does NOT own**: The base StatBlock (read-only reference). Does NOT persist across sessions.

---

## 2. IGameClock + TimeOfDay (`Pinder.Core.Interfaces` / `Pinder.Core.Conversation`)

```csharp
namespace Pinder.Core.Conversation
{
    public enum TimeOfDay
    {
        Morning,     // 6:00–11:59
        Afternoon,   // 12:00–17:59
        Evening,     // 18:00–21:59
        LateNight,   // 22:00–01:59
        AfterTwoAm   // 02:00–05:59
    }
}

namespace Pinder.Core.Interfaces
{
    public interface IGameClock
    {
        // Current simulated game time
        DateTimeOffset Now { get; }

        // Advance clock by a relative amount
        // Pre: amount >= TimeSpan.Zero
        void Advance(TimeSpan amount);

        // Advance clock to an absolute target time
        // Pre: target >= Now (do not go backward)
        void AdvanceTo(DateTimeOffset target);

        // Derive TimeOfDay from Now.Hour
        TimeOfDay GetTimeOfDay();

        // Horniness modifier per time of day:
        // Morning → -2, Afternoon → 0, Evening → +1, LateNight → +3, AfterTwoAm → +5
        int GetHorninessModifier();

        // Daily energy remaining
        int RemainingEnergy { get; }

        // Consume energy. Returns true if sufficient, false if not (no partial consume).
        // Pre: amount > 0
        bool ConsumeEnergy(int amount);

        // Reset energy to a new random daily amount (15-20). Called when clock crosses midnight.
        void ReplenishAtMidnight();
    }
}
```

**Dependencies**: None (interface only)
**Consumers**: `GameSession` (#51 Horniness), `ConversationRegistry` (#56), `PlayerResponseDelayEvaluator` (#55 — indirect, caller computes TimeSpan)
**Implementations**: `GameClock` (#54 — production impl), `FixedGameClock` (test helper)

---

## 3. RollEngine Extensions (`Pinder.Core.Rolls`)

### 3a. New overload: `ResolveFixedDC`

For Read/Recover actions that roll against a fixed DC (not opponent stats):

```csharp
public static RollResult ResolveFixedDC(
    StatType stat,
    StatBlock attacker,
    int fixedDc,              // e.g. 12 for Read/Recover
    TrapState attackerTraps,
    int level,
    ITrapRegistry trapRegistry,
    IDiceRoller dice,
    bool hasAdvantage = false,
    bool hasDisadvantage = false);
```

Behavior: Identical to `Resolve` except DC is the provided `fixedDc` instead of `defender.GetDefenceDC(stat)`. Does NOT require a defender StatBlock.

### 3b. Existing `Resolve` gains optional params

```csharp
public static RollResult Resolve(
    ..., // existing params unchanged
    int externalBonus = 0,   // added to Total after roll
    int dcAdjustment = 0);   // subtracted from computed DC (weakness windows)
```

Both default to 0, so existing callers are unaffected.

### 3c. `RollResult.IsSuccess` uses `FinalTotal`

**Current**: `IsSuccess = IsNatTwenty || (!IsNatOne && Total >= dc)`
**New**: `IsSuccess = IsNatTwenty || (!IsNatOne && FinalTotal >= dc)`

When `ExternalBonus == 0`, `FinalTotal == Total`, so this is backward-compatible.

**Dependencies**: None new
**Consumers**: All roll-based features

---

## 4. GameSessionConfig (`Pinder.Core.Conversation`)

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class GameSessionConfig
    {
        public IGameClock? GameClock { get; }
        public SessionShadowTracker? PlayerShadows { get; }
        public SessionShadowTracker? OpponentShadows { get; }
        public int? StartingInterest { get; }

        public GameSessionConfig(
            IGameClock? gameClock = null,
            SessionShadowTracker? playerShadows = null,
            SessionShadowTracker? opponentShadows = null,
            int? startingInterest = null);
    }
}
```

`GameSession` gains a new constructor overload:
```csharp
public GameSession(
    CharacterProfile player,
    CharacterProfile opponent,
    ILlmAdapter llm,
    IDiceRoller dice,
    ITrapRegistry trapRegistry,
    GameSessionConfig? config = null);
```

When `config` is null, behavior is identical to the existing constructor.

**Dependencies**: `IGameClock`, `SessionShadowTracker`
**Consumers**: All feature issues that need clock or shadow tracking

---

## 5. Small Additions

### InterestMeter overload
```csharp
public InterestMeter(int startingValue)
{
    Current = Math.Max(Min, Math.Min(Max, startingValue));
}
```

### TrapState.HasActive
```csharp
public bool HasActive => _active.Count > 0;
```

---

## Backward Compatibility

All existing 254 tests MUST pass unchanged. All new parameters have defaults. No existing public signatures change — only additions.

# Spec: Issue #54 — GameClock — Simulated In-Game Time with Time-of-Day Effects

## Overview

`GameClock` is a simulated in-game clock that tracks time-of-day in the Pinder RPG engine. It provides horniness modifiers based on time-of-day, manages a daily energy budget, and supports deterministic time advancement. The clock is injectable via the `IGameClock` interface so that consumers (GameSession, ConversationRegistry, PlayerResponseDelayEvaluator) can be tested with a `FixedGameClock` stub.

---

## Function Signatures

### `TimeOfDay` Enum

**File:** `src/Pinder.Core/Interfaces/IGameClock.cs`  
**Namespace:** `Pinder.Core.Interfaces`

```csharp
public enum TimeOfDay
{
    Morning,      // Hour 6–11 (06:00–11:59)
    Afternoon,    // Hour 12–17 (12:00–17:59)
    Evening,      // Hour 18–21 (18:00–21:59)
    LateNight,    // Hour 22–23 and 0–1 (22:00–01:59)
    AfterTwoAm    // Hour 2–5 (02:00–05:59)
}
```

### `IGameClock` Interface

**File:** `src/Pinder.Core/Interfaces/IGameClock.cs`  
**Namespace:** `Pinder.Core.Interfaces`

```csharp
public interface IGameClock
{
    /// <summary>Current simulated time.</summary>
    DateTimeOffset Now { get; }

    /// <summary>
    /// Advance the clock forward by the given amount.
    /// Throws ArgumentOutOfRangeException if amount is negative or zero.
    /// If the advance crosses midnight, energy is replenished automatically.
    /// </summary>
    void Advance(TimeSpan amount);

    /// <summary>
    /// Advance the clock to the specified target time.
    /// Throws ArgumentException if target &lt;= Now.
    /// If the advance crosses midnight, energy is replenished automatically.
    /// </summary>
    void AdvanceTo(DateTimeOffset target);

    /// <summary>Returns the TimeOfDay based on Now.Hour.</summary>
    TimeOfDay GetTimeOfDay();

    /// <summary>
    /// Returns the horniness modifier for the current time of day.
    /// Morning=-2, Afternoon=0, Evening=+1, LateNight=+3, AfterTwoAm=+5.
    /// </summary>
    int GetHorninessModifier();

    /// <summary>Remaining energy for the current in-game day.</summary>
    int RemainingEnergy { get; }

    /// <summary>
    /// Attempt to consume the given amount of energy.
    /// Returns true and deducts if sufficient energy remains.
    /// Returns false and does NOT deduct if insufficient.
    /// Throws ArgumentOutOfRangeException if amount &lt;= 0.
    /// </summary>
    bool ConsumeEnergy(int amount);
}
```

> Note: The issue lists a `ReplenishAtMidnight()` method on the interface. Per the contract (`sprint-8-game-clock.md`), midnight replenishment is handled automatically when the clock crosses midnight via `Advance`/`AdvanceTo`. A public `ReplenishAtMidnight()` method is NOT required on the interface — it is an internal implementation detail. However, if the implementer chooses to expose it for edge-case manual calls, it should be on `GameClock` only, not on `IGameClock`.

### `GameClock` Sealed Class

**File:** `src/Pinder.Core/Conversation/GameClock.cs`  
**Namespace:** `Pinder.Core.Conversation`

```csharp
public sealed class GameClock : IGameClock
{
    /// <param name="startTime">Initial simulated time. Stored as Now.</param>
    /// <param name="dailyEnergy">
    ///   Energy budget for the starting day. Default: 10.
    ///   Must be >= 0. Throws ArgumentOutOfRangeException if negative.
    /// </param>
    public GameClock(DateTimeOffset startTime, int dailyEnergy = 10);

    public DateTimeOffset Now { get; }
    public int RemainingEnergy { get; }

    public void Advance(TimeSpan amount);
    public void AdvanceTo(DateTimeOffset target);
    public TimeOfDay GetTimeOfDay();
    public int GetHorninessModifier();
    public bool ConsumeEnergy(int amount);
}
```

### `FixedGameClock` Test Helper

**File:** `tests/Pinder.Core.Tests/Helpers/FixedGameClock.cs` (or equivalent test project location)  
**Namespace:** Test project namespace

```csharp
public sealed class FixedGameClock : IGameClock
{
    /// <summary>
    /// Creates a fully controllable game clock for testing.
    /// </summary>
    /// <param name="now">Fixed starting time.</param>
    /// <param name="energy">Fixed starting energy. Default: 10.</param>
    public FixedGameClock(DateTimeOffset now, int energy = 10);

    public DateTimeOffset Now { get; }
    public int RemainingEnergy { get; }

    public void Advance(TimeSpan amount);
    public void AdvanceTo(DateTimeOffset target);
    public TimeOfDay GetTimeOfDay();
    public int GetHorninessModifier();
    public bool ConsumeEnergy(int amount);
}
```

The `FixedGameClock` follows the same pattern as the existing `FixedDice` helper in the test project. It should have identical behavior to `GameClock` for `GetTimeOfDay()` and `GetHorninessModifier()` (derived from `Now.Hour`), but allows full control over initial state. Time advancement is deterministic (just sets `Now`). Energy does not auto-replenish on midnight crossing — tests control energy explicitly.

---

## Input/Output Examples

### GetTimeOfDay Examples

| `Now.Hour` | Returns |
|---|---|
| 0 | `TimeOfDay.LateNight` |
| 1 | `TimeOfDay.LateNight` |
| 2 | `TimeOfDay.AfterTwoAm` |
| 5 | `TimeOfDay.AfterTwoAm` |
| 6 | `TimeOfDay.Morning` |
| 11 | `TimeOfDay.Morning` |
| 12 | `TimeOfDay.Afternoon` |
| 17 | `TimeOfDay.Afternoon` |
| 18 | `TimeOfDay.Evening` |
| 21 | `TimeOfDay.Evening` |
| 22 | `TimeOfDay.LateNight` |
| 23 | `TimeOfDay.LateNight` |

### GetHorninessModifier Examples

| `Now.Hour` | TimeOfDay | Modifier |
|---|---|---|
| 8 | Morning | −2 |
| 14 | Afternoon | 0 |
| 19 | Evening | +1 |
| 23 | LateNight | +3 |
| 3 | AfterTwoAm | +5 |

### Advance / Midnight Crossing Example

```
clock = new GameClock(startTime: 2024-01-15T23:00:00Z, dailyEnergy: 10)
clock.ConsumeEnergy(7)     → true, RemainingEnergy = 3
clock.Advance(2 hours)     → Now = 2024-01-16T01:00:00Z
                           → Crossed midnight → energy replenished
                           → RemainingEnergy = 10 (reset to dailyEnergy)
```

### ConsumeEnergy Examples

```
clock = new GameClock(startTime: 2024-01-15T10:00:00Z, dailyEnergy: 15)
clock.RemainingEnergy      → 15
clock.ConsumeEnergy(5)     → true,  RemainingEnergy = 10
clock.ConsumeEnergy(10)    → true,  RemainingEnergy = 0
clock.ConsumeEnergy(1)     → false, RemainingEnergy = 0 (no deduction)
```

### AdvanceTo Example

```
clock = new GameClock(startTime: 2024-01-15T10:00:00Z, dailyEnergy: 10)
clock.AdvanceTo(2024-01-15T14:00:00Z)  → Now = 2024-01-15T14:00:00Z
clock.AdvanceTo(2024-01-15T10:00:00Z)  → throws ArgumentException (target <= Now)
```

---

## Acceptance Criteria

### AC1: `IGameClock` interface exists in `Pinder.Core.Interfaces`

The `IGameClock` interface must be defined in `src/Pinder.Core/Interfaces/IGameClock.cs` with all members listed in the Function Signatures section above. The `TimeOfDay` enum must be in the same file and namespace.

### AC2: `GameClock` sealed class implements `IGameClock`

`GameClock` must be a `sealed class` in `Pinder.Core.Conversation` that implements `IGameClock`. It must have the constructor `GameClock(DateTimeOffset startTime, int dailyEnergy = 10)`.

### AC3: `FixedGameClock` test helper in test project

A `FixedGameClock` class must exist in the test project. It must implement `IGameClock` and support deterministic testing by allowing full control over `Now` and `RemainingEnergy`. It follows the same pattern as the existing `FixedDice` test helper.

### AC4: `TimeOfDay` enum with correct hour ranges

The enum must have exactly five values: `Morning`, `Afternoon`, `Evening`, `LateNight`, `AfterTwoAm`. The hour-to-enum mapping must match the table in the Input/Output Examples section. Boundary hours are inclusive on the low end (e.g., hour 6 is Morning, hour 12 is Afternoon).

### AC5: `GetHorninessModifier()` returns correct value per time of day

The modifier lookup must be:

| TimeOfDay | Modifier |
|---|---|
| Morning | −2 |
| Afternoon | 0 |
| Evening | +1 |
| LateNight | +3 |
| AfterTwoAm | +5 |

This is a pure derivation from `GetTimeOfDay()`.

### AC6: DailyEnergy system with consume/replenish

- `RemainingEnergy` starts at `dailyEnergy` (constructor param, default 10).
- `ConsumeEnergy(amount)` returns `true` and deducts when `RemainingEnergy >= amount`; returns `false` and does NOT deduct when `RemainingEnergy < amount`.
- Energy is replenished to `dailyEnergy` when the clock crosses midnight (checked during `Advance` and `AdvanceTo`).
- Energy state is owned solely by `IGameClock` — no other component duplicates it.

### AC7: Consumers inject `IGameClock`, not concrete `GameClock`

This is a design constraint for issues #51, #55, #56. The `GameSessionConfig.Clock` property is typed as `IGameClock?`. No consumer should reference `GameClock` directly.

### AC8: Tests cover time-of-day boundaries, midnight replenish, horniness modifier mapping

Tests must verify:
- All 24 hours map to the correct `TimeOfDay` (especially boundaries: 0, 1, 2, 5, 6, 11, 12, 17, 18, 21, 22, 23).
- Midnight crossing triggers energy replenishment.
- Each `TimeOfDay` value maps to the correct horniness modifier.

### AC9: Build clean

All existing tests (254+) continue to pass. No new compiler warnings. No NuGet dependencies added.

---

## Edge Cases

### Time Boundaries

- **Hour 0** → `LateNight` (not AfterTwoAm — the range wraps: 22–01)
- **Hour 1** → `LateNight` (last hour before AfterTwoAm at 02)
- **Hour 2** → `AfterTwoAm` (boundary transition)
- **Hour 5** → `AfterTwoAm` (last hour before Morning)
- **Hour 6** → `Morning` (boundary transition)
- **Hour 11** → `Morning` (last hour before Afternoon)
- **Hour 12** → `Afternoon` (boundary transition)
- **Hour 17** → `Afternoon` (last hour before Evening)
- **Hour 18** → `Evening` (boundary transition)
- **Hour 21** → `Evening` (last hour before LateNight)
- **Hour 22** → `LateNight` (boundary transition)

### Energy Edge Cases

- **Zero energy at start**: `GameClock(time, dailyEnergy: 0)` → `RemainingEnergy = 0`, all `ConsumeEnergy` calls return `false`.
- **Consume exactly remaining**: `ConsumeEnergy(RemainingEnergy)` → `true`, `RemainingEnergy = 0`.
- **Consume zero**: `ConsumeEnergy(0)` → throws `ArgumentOutOfRangeException` (amount must be > 0).
- **Multiple midnight crossings**: `Advance(TimeSpan.FromHours(50))` — crosses midnight twice. Energy is replenished (final state: `dailyEnergy`). The intermediate day's energy is irrelevant since no consumption happens during advance.

### Advance Edge Cases

- **Advance by zero**: `Advance(TimeSpan.Zero)` → throws `ArgumentOutOfRangeException`.
- **Advance by negative**: `Advance(TimeSpan.FromHours(-1))` → throws `ArgumentOutOfRangeException`. Time cannot go backward.
- **AdvanceTo same time**: `AdvanceTo(Now)` → throws `ArgumentException` (target must be strictly greater than Now).
- **AdvanceTo past time**: `AdvanceTo(Now - 1 hour)` → throws `ArgumentException`.
- **Very large advance**: Crossing multiple midnights in one call still results in a single replenishment to `dailyEnergy`.

### Midnight Crossing Detection

Midnight crossing occurs when the calendar date of `Now` before the advance differs from the calendar date after. This must use UTC offset-aware comparison (i.e., based on the offset of the `DateTimeOffset`, not raw UTC). For simplicity, if using `Now.Date` (local date portion), crossing midnight means `oldNow.Date != newNow.Date`.

---

## Error Conditions

| Method | Condition | Exception Type | Message (approximate) |
|---|---|---|---|
| `GameClock(startTime, dailyEnergy)` | `dailyEnergy < 0` | `ArgumentOutOfRangeException` | "dailyEnergy must be non-negative" |
| `Advance(amount)` | `amount <= TimeSpan.Zero` | `ArgumentOutOfRangeException` | "amount must be positive" |
| `AdvanceTo(target)` | `target <= Now` | `ArgumentException` | "target must be after Now" |
| `ConsumeEnergy(amount)` | `amount <= 0` | `ArgumentOutOfRangeException` | "amount must be positive" |

No exceptions are thrown for normal operation. `ConsumeEnergy` returns `false` (not an exception) when energy is insufficient.

---

## Dependencies

### Internal Dependencies

- **`TimeOfDay` enum** — defined alongside `IGameClock` in `Pinder.Core.Interfaces`. Both are part of Wave 0 infrastructure (#139).
- **`IDiceRoller`** — existing interface in `Pinder.Core.Interfaces.IRollDataProvider.cs`. The issue mentions rolling `15–20` for daily energy. However, the contract (`sprint-8-game-clock.md`) specifies `dailyEnergy` as a constructor parameter (default 10), meaning the caller (host or `ConversationRegistry`) rolls and passes the value. `GameClock` itself does NOT depend on `IDiceRoller`.

### External Dependencies

- **None.** .NET Standard 2.0 BCL only (`System.DateTimeOffset`, `System.TimeSpan`).

### Consumers (downstream)

| Issue | Component | How it uses IGameClock |
|---|---|---|
| #51 | GameSession (horniness logic) | `GetHorninessModifier()`, `GetTimeOfDay()` |
| #55 | PlayerResponseDelayEvaluator | `Now` for elapsed-time calculation |
| #56 | ConversationRegistry | `Now`, `Advance()`, `AdvanceTo()`, `ConsumeEnergy()` |

### Test Infrastructure

- `FixedGameClock` follows the pattern of `FixedDice` (existing test helper for `IDiceRoller`).
- Tests should use `FixedGameClock` to control time deterministically.

---

## Design Notes

### Why `IGameClock` is an Interface

The game clock must be injectable for two reasons:
1. **Deterministic testing** — tests need exact control over time without real-time waits.
2. **Host flexibility** — a Unity host might tie game-time to frame updates differently than a CLI host.

### Energy Ownership

Energy state is owned **solely** by `IGameClock`. The `ConversationRegistry` (#56) calls `ConsumeEnergy()` but does NOT track energy itself. This avoids state duplication.

### DailyEnergy Roll

The issue states "15–20 energy per in-game day (roll once at day start via `IDiceRoller`)". The contract resolves this: the caller rolls and passes the result as `dailyEnergy`. On midnight replenishment within `GameClock`, the energy resets to the `dailyEnergy` value provided at construction. If the host wants variable daily energy, it should create a new `GameClock` instance or the design could be extended in a future sprint. For Sprint 8, `dailyEnergy` is fixed at construction time.

### .NET Standard 2.0 Constraints

- No `record` types — use `sealed class`.
- `DateTimeOffset` is available in netstandard2.0.
- Nullable reference types are enabled (use `?` annotations where applicable).
- No external NuGet packages.

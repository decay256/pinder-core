# Spec: GameClock — Simulated In-Game Time with Time-of-Day Effects

**Issue:** #54  
**Component:** `Pinder.Core.Conversation.GameClock`, `Pinder.Core.Interfaces.IGameClock`  
**Target:** .NET Standard 2.0, C# 8.0, zero NuGet dependencies  

---

## 1. Overview

The game has an internal clock that advances independently of real (wall-clock) time. `GameClock` tracks a simulated `DateTimeOffset` that moves forward only when explicitly told to. Time-of-day affects a Horniness stat modifier (ranging from −2 to +5), and an energy system gates how many actions a player can take per in-game day, replenishing at midnight.

Per VC-67, an `IGameClock` interface must be defined so that tests and other consumers can inject a stub clock.

---

## 2. Types

### 2.1 `TimeOfDay` Enum

**Namespace:** `Pinder.Core.Conversation`  
**File:** `src/Pinder.Core/Conversation/TimeOfDay.cs`

```
public enum TimeOfDay
{
    Morning,      // 06:00–11:59 (hour 6 inclusive to hour 12 exclusive)
    Afternoon,    // 12:00–17:59 (hour 12 inclusive to hour 18 exclusive)
    Evening,      // 18:00–21:59 (hour 18 inclusive to hour 22 exclusive)
    LateNight,    // 22:00–01:59 (hour 22 inclusive to hour 2 exclusive, wraps midnight)
    AfterTwoAm    // 02:00–05:59 (hour 2 inclusive to hour 6 exclusive)
}
```

Hour ranges are based on the **local hour** of the `DateTimeOffset` stored in `GameClock.Now` (i.e. `Now.Hour`).

### 2.2 `IGameClock` Interface

**Namespace:** `Pinder.Core.Interfaces`  
**File:** `src/Pinder.Core/Interfaces/IGameClock.cs`

```csharp
public interface IGameClock
{
    DateTimeOffset Now { get; }
    void Advance(TimeSpan amount);
    void AdvanceTo(DateTimeOffset target);
    TimeOfDay GetTimeOfDay();
    int GetHorninessModifier();
    int DailyEnergy { get; }
    bool ConsumeEnergy(int amount);
}
```

All members are documented in detail in section 3 below. The interface enables test stubs and alternative implementations (e.g. a clock driven by Unity's game loop).

### 2.3 `GameClock` Class

**Namespace:** `Pinder.Core.Conversation`  
**File:** `src/Pinder.Core/Conversation/GameClock.cs`

```csharp
public sealed class GameClock : IGameClock
```

`GameClock` is a mutable, stateful class. It holds:
- The current simulated time (`Now`).
- The current day's energy pool (`DailyEnergy`).
- The calendar date of the last energy replenishment (to detect midnight crossings).

---

## 3. Function Signatures

### Constructor

```csharp
public GameClock(DateTimeOffset startTime, IDiceRoller dice)
```

- `startTime`: The initial simulated time. Stored as-is (offset preserved).
- `dice`: Used to roll daily energy (see `ReplenishEnergy` below). The existing `IDiceRoller` interface from `Pinder.Core.Interfaces` is reused.
- On construction, energy is rolled for the starting day via the replenish logic.

### `DateTimeOffset Now { get; }`

Returns the current simulated time. Read-only to external callers; mutated only by `Advance` and `AdvanceTo`.

### `void Advance(TimeSpan amount)`

Advances the clock by `amount`. After advancing, checks whether midnight was crossed (i.e. the calendar date of `Now` changed) and if so, calls the internal replenish logic.

- **Precondition:** `amount` must be non-negative (`amount >= TimeSpan.Zero`). If negative, throw `ArgumentOutOfRangeException` with message `"amount must be non-negative"`.

### `void AdvanceTo(DateTimeOffset target)`

Jumps the clock to `target`. Equivalent to `Advance(target - Now)` but expressed as an absolute timestamp.

- **Precondition:** `target` must be >= `Now`. If `target < Now`, throw `ArgumentOutOfRangeException` with message `"target must not be in the past"`.
- Midnight crossing detection applies (same as `Advance`).

### `TimeOfDay GetTimeOfDay()`

Returns the `TimeOfDay` enum value corresponding to the current hour of `Now.Hour`:

| Hour range | Value |
|---|---|
| 6 ≤ hour < 12 | `Morning` |
| 12 ≤ hour < 18 | `Afternoon` |
| 18 ≤ hour < 22 | `Evening` |
| 22 ≤ hour ≤ 23 **or** 0 ≤ hour < 2 | `LateNight` |
| 2 ≤ hour < 6 | `AfterTwoAm` |

Note: `LateNight` wraps around midnight. The implementation should use `Now.Hour` (0–23 integer).

### `int GetHorninessModifier()`

Returns the Horniness modifier for the current time of day, per §async-time:

| `TimeOfDay` | Modifier |
|---|---|
| `Morning` | −2 |
| `Afternoon` | 0 |
| `Evening` | +1 |
| `LateNight` | +3 |
| `AfterTwoAm` | +5 |

This is a pure derivation from `GetTimeOfDay()`. No side effects.

### `int DailyEnergy { get; }`

The remaining energy for the current in-game day. Starts at a rolled value (15–20 inclusive) at each new day. Decremented by `ConsumeEnergy`. Never negative.

### `bool ConsumeEnergy(int amount)`

Attempts to consume `amount` energy.

- If `amount <= 0`: throw `ArgumentOutOfRangeException` with message `"amount must be positive"`.
- If `DailyEnergy >= amount`: subtracts `amount` from `DailyEnergy`, returns `true`.
- If `DailyEnergy < amount`: does **not** modify `DailyEnergy`, returns `false`.

### Internal: Midnight Replenish Logic

When `Advance` or `AdvanceTo` causes the calendar date (i.e. `Now.Date`) to change from the previously tracked date:

1. Roll new daily energy: `dice.Roll(6) + 14` — this yields a uniform value in [15, 20].
2. Set `DailyEnergy` to the rolled value.
3. Update the tracked date to the new `Now.Date`.

If multiple midnights are crossed in a single `Advance`/`AdvanceTo` call (e.g. advancing by 48 hours), energy is rolled **once** for the final day only. Intermediate days are not simulated.

---

## 4. Input/Output Examples

### Example 1: Time-of-day at various hours

```
Clock starts at 2025-06-15T08:00:00+00:00
  GetTimeOfDay() → Morning
  GetHorninessModifier() → -2

Advance(TimeSpan.FromHours(6))  → Now = 14:00
  GetTimeOfDay() → Afternoon
  GetHorninessModifier() → 0

Advance(TimeSpan.FromHours(5))  → Now = 19:00
  GetTimeOfDay() → Evening
  GetHorninessModifier() → +1

Advance(TimeSpan.FromHours(4))  → Now = 23:00
  GetTimeOfDay() → LateNight
  GetHorninessModifier() → +3

Advance(TimeSpan.FromHours(4))  → Now = 03:00 (next day)
  GetTimeOfDay() → AfterTwoAm
  GetHorninessModifier() → +5
```

### Example 2: Midnight energy replenish

```
Clock starts at 2025-06-15T22:00:00+00:00
  DailyEnergy → (rolled on construction, e.g. 17)

ConsumeEnergy(5) → true, DailyEnergy → 12
ConsumeEnergy(15) → false, DailyEnergy → 12 (unchanged)

Advance(TimeSpan.FromHours(3)) → Now = 01:00 on 2025-06-16
  Midnight crossed → DailyEnergy re-rolled (e.g. 19)
```

### Example 3: AdvanceTo

```
Clock at 2025-06-15T10:00:00+00:00
AdvanceTo(2025-06-15T14:00:00+00:00) → Now = 14:00, no midnight crossing
AdvanceTo(2025-06-15T10:00:00+00:00) → throws ArgumentOutOfRangeException (past)
```

### Example 4: ConsumeEnergy edge

```
DailyEnergy = 3
ConsumeEnergy(3) → true, DailyEnergy → 0
ConsumeEnergy(1) → false, DailyEnergy → 0
```

---

## 5. Acceptance Criteria

### AC-1: `GameClock` with `Now`, `Advance`, `AdvanceTo`

- `GameClock` is a `sealed class` in `Pinder.Core.Conversation`.
- Constructor accepts `DateTimeOffset startTime` and `IDiceRoller dice`.
- `Now` returns the current simulated time.
- `Advance(TimeSpan)` moves time forward; throws on negative input.
- `AdvanceTo(DateTimeOffset)` jumps to target; throws if target is before `Now`.

### AC-2: `TimeOfDay` enum with correct hour ranges

- Enum `TimeOfDay` in `Pinder.Core.Conversation` with five values: `Morning`, `Afternoon`, `Evening`, `LateNight`, `AfterTwoAm`.
- `GetTimeOfDay()` returns the correct value for every hour 0–23, per the mapping in section 3.

### AC-3: `GetHorninessModifier()` returns correct value per time of day

- Morning → −2, Afternoon → 0, Evening → +1, LateNight → +3, AfterTwoAm → +5.
- Verified at boundary hours (e.g. hour 6, 12, 18, 22, 0, 2).

### AC-4: `DailyEnergy` system with consume/replenish

- Energy is rolled on construction (15–20 inclusive via `dice.Roll(6) + 14`).
- `ConsumeEnergy(int)` returns `true` and decrements on success, `false` with no change on insufficient energy.
- Crossing midnight (calendar date change) re-rolls energy.

### AC-5: `IGameClock` interface exists (per VC-67)

- `IGameClock` interface in `Pinder.Core.Interfaces` exposes: `Now`, `Advance`, `AdvanceTo`, `GetTimeOfDay`, `GetHorninessModifier`, `DailyEnergy`, `ConsumeEnergy`.
- `GameClock` implements `IGameClock`.

### AC-6: Tests inject a stub clock (per VC-67)

- Tests that depend on time use a stub/mock implementing `IGameClock`, not `GameClock` directly.
- `GameClock` itself is tested with a deterministic `IDiceRoller` stub.

### AC-7: Build clean

- `dotnet build` succeeds with zero warnings and zero errors on the solution.

---

## 6. Edge Cases

| Scenario | Expected behavior |
|---|---|
| Hour exactly 0 (midnight) | `GetTimeOfDay()` → `LateNight` |
| Hour exactly 1 | `GetTimeOfDay()` → `LateNight` |
| Hour exactly 2 | `GetTimeOfDay()` → `AfterTwoAm` |
| Hour exactly 6 | `GetTimeOfDay()` → `Morning` |
| Hour exactly 12 | `GetTimeOfDay()` → `Afternoon` |
| Hour exactly 18 | `GetTimeOfDay()` → `Evening` |
| Hour exactly 22 | `GetTimeOfDay()` → `LateNight` |
| `Advance(TimeSpan.Zero)` | No-op; `Now` unchanged, no midnight check triggered (date hasn't changed) |
| `AdvanceTo(Now)` | No-op; valid (target == Now is not "in the past") |
| Advance crossing multiple midnights (e.g. +72h) | Energy rolled once for the final day |
| `ConsumeEnergy(0)` or negative | Throws `ArgumentOutOfRangeException` |
| `DailyEnergy` is 0, then midnight replenish | Energy re-rolled to 15–20 |
| `DateTimeOffset` with non-UTC offset | `Now.Hour` uses the offset's local hour; behavior is consistent |

---

## 7. Error Conditions

| Method | Condition | Exception | Message |
|---|---|---|---|
| `Advance` | `amount < TimeSpan.Zero` | `ArgumentOutOfRangeException` | `"amount must be non-negative"` |
| `AdvanceTo` | `target < Now` | `ArgumentOutOfRangeException` | `"target must not be in the past"` |
| `ConsumeEnergy` | `amount <= 0` | `ArgumentOutOfRangeException` | `"amount must be positive"` |

No other methods throw. `GetTimeOfDay()` and `GetHorninessModifier()` are pure derivations that always succeed.

---

## 8. Dependencies

| Dependency | Type | Location | Notes |
|---|---|---|---|
| `IDiceRoller` | Interface (injected) | `Pinder.Core.Interfaces` | Used for energy roll (`Roll(6)`) |
| `DateTimeOffset` | BCL type | `System` | Available in .NET Standard 2.0 |
| `TimeSpan` | BCL type | `System` | Available in .NET Standard 2.0 |

No external NuGet packages. No dependency on `GameSession`, `InterestMeter`, or any other Pinder.Core component. `GameClock` is a standalone leaf component.

---

## 9. File Placement

| File | Namespace |
|---|---|
| `src/Pinder.Core/Conversation/TimeOfDay.cs` | `Pinder.Core.Conversation` |
| `src/Pinder.Core/Conversation/GameClock.cs` | `Pinder.Core.Conversation` |
| `src/Pinder.Core/Interfaces/IGameClock.cs` | `Pinder.Core.Interfaces` |

---

## 10. Constants Sync Table Addition

When implementing, add the following rows to the Rules-to-Code Sync Table in `docs/architecture.md`:

| Rules Section | Rule Value | C# Location | C# Constant/Expression |
|---|---|---|---|
| §async-time Time-of-day ranges | Morning 6–12, Afternoon 12–18, Evening 18–22, LateNight 22–2, AfterTwoAm 2–6 | `Conversation/GameClock.cs` | `GetTimeOfDay()` hour boundaries |
| §async-time Horniness modifiers | Morning −2, Afternoon 0, Evening +1, LateNight +3, AfterTwoAm +5 | `Conversation/GameClock.cs` | `GetHorninessModifier()` return values |
| §async-time Daily energy | 15–20 per day | `Conversation/GameClock.cs` | `dice.Roll(6) + 14` in replenish logic |

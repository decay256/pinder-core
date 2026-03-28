# Contract: Issue #54 — GameClock

## Component
`Pinder.Core.Interfaces.IGameClock` (new interface)
`Pinder.Core.Conversation.GameClock` (new class)
`Pinder.Core.Conversation.TimeOfDay` (new enum)

## Maturity
Prototype

---

## Interface

**File**: `src/Pinder.Core/Interfaces/IGameClock.cs`

```csharp
public interface IGameClock
{
    DateTimeOffset Now { get; }
    void Advance(TimeSpan amount);
    void AdvanceTo(DateTimeOffset target);
    TimeOfDay GetTimeOfDay();
    int GetHorninessModifier();
}
```

### TimeOfDay Enum

**File**: `src/Pinder.Core/Conversation/TimeOfDay.cs`

```csharp
public enum TimeOfDay
{
    Morning,      // 6:00–11:59
    Afternoon,    // 12:00–17:59
    Evening,      // 18:00–21:59
    LateNight,    // 22:00–01:59
    AfterTwoAm   // 02:00–05:59
}
```

### Horniness Modifier by TimeOfDay

| TimeOfDay | Modifier |
|-----------|----------|
| Morning | -2 |
| Afternoon | 0 |
| Evening | +1 |
| LateNight | +3 |
| AfterTwoAm | +1 |

---

## Default Implementation: GameClock

**File**: `src/Pinder.Core/Conversation/GameClock.cs`

```csharp
public sealed class GameClock : IGameClock
{
    private DateTimeOffset _now;

    public GameClock(DateTimeOffset startTime)
    {
        _now = startTime;
    }

    public DateTimeOffset Now => _now;

    public void Advance(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(amount));
        _now = _now.Add(amount);
    }

    public void AdvanceTo(DateTimeOffset target)
    {
        if (target < _now) throw new ArgumentOutOfRangeException(nameof(target));
        _now = target;
    }

    public TimeOfDay GetTimeOfDay()
    {
        int hour = _now.Hour;
        if (hour >= 6 && hour < 12)  return TimeOfDay.Morning;
        if (hour >= 12 && hour < 18) return TimeOfDay.Afternoon;
        if (hour >= 18 && hour < 22) return TimeOfDay.Evening;
        if (hour >= 22 || hour < 2)  return TimeOfDay.LateNight;
        return TimeOfDay.AfterTwoAm;
    }

    public int GetHorninessModifier()
    {
        return GetTimeOfDay() switch
        {
            TimeOfDay.Morning    => -2,
            TimeOfDay.Afternoon  => 0,
            TimeOfDay.Evening    => 1,
            TimeOfDay.LateNight  => 3,
            TimeOfDay.AfterTwoAm => 1,
            _ => 0
        };
    }
}
```

---

## Behavioural Contract
- Clock only advances forward — `Advance` and `AdvanceTo` throw on negative/past values
- `IGameClock` is injectable via `GameSessionConfig` — null means "no time tracking"
- GameSession advances clock by opponent reply delay after each turn
- Energy system is NOT part of this issue (deferred per VC-75)

## Dependencies
None

## Consumers
- GameSession (advances clock per turn)
- OpponentTimingCalculator (reads time-of-day for horniness modifier)
- ConversationRegistry (future — fast-forward uses clock)

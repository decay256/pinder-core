# Contract: Issue #54 — GameClock

## Component
`Pinder.Core.Conversation.GameClock` (implements `IGameClock` from #139 Wave 0)

## Maturity
Prototype

## NFR
- latency_p99_ms: N/A (in-process, no I/O)

---

## GameClock (`Pinder.Core.Conversation`)

Production implementation of `IGameClock`. Tracks simulated in-game time, derives time-of-day, provides Horniness modifier, and manages daily energy.

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class GameClock : IGameClock
    {
        // Constructor: initial game time + dice roller for energy randomization
        // Pre: startTime is a valid DateTimeOffset, dice is non-null
        // Post: Now == startTime, energy is rolled as dice.Roll(6) + 14 (range 15–20)
        public GameClock(DateTimeOffset startTime, IDiceRoller dice);

        public DateTimeOffset Now { get; }

        // Advance by relative amount. Checks for midnight crossing → auto-replenish.
        // Pre: amount >= TimeSpan.Zero
        // Post: Now = old Now + amount; if midnight crossed, energy replenished
        public void Advance(TimeSpan amount);

        // Advance to absolute target. Checks for midnight crossing → auto-replenish.
        // Pre: target >= Now
        // Throws: ArgumentException if target < Now
        public void AdvanceTo(DateTimeOffset target);

        // Derive from Now.Hour:
        // 6–11 → Morning, 12–17 → Afternoon, 18–21 → Evening, 22–23 or 0–1 → LateNight, 2–5 → AfterTwoAm
        public TimeOfDay GetTimeOfDay();

        // Morning → -2, Afternoon → 0, Evening → +1, LateNight → +3, AfterTwoAm → +5
        public int GetHorninessModifier();

        public int RemainingEnergy { get; }

        // Returns false if amount > RemainingEnergy. Otherwise deducts and returns true.
        public bool ConsumeEnergy(int amount);

        // Rolls new daily energy (15–20) using the injected dice.
        public void ReplenishAtMidnight();
    }
}
```

## FixedGameClock (test helper — in test project)

```csharp
namespace Pinder.Core.Tests
{
    public sealed class FixedGameClock : IGameClock
    {
        // Constructor: fixed time, fixed energy
        public FixedGameClock(DateTimeOffset now, int energy = 20);

        // All methods deterministic. Advance/AdvanceTo update Now.
        // ConsumeEnergy deducts from fixed pool.
        // ReplenishAtMidnight sets energy to constructor value.
    }
}
```

## TimeOfDay Hour Boundaries

| TimeOfDay | Hour Range (inclusive) |
|---|---|
| Morning | 6–11 |
| Afternoon | 12–17 |
| Evening | 18–21 |
| LateNight | 22–23, 0–1 |
| AfterTwoAm | 2–5 |

## Horniness Modifier Table

| TimeOfDay | Modifier |
|---|---|
| Morning | -2 |
| Afternoon | 0 |
| Evening | +1 |
| LateNight | +3 |
| AfterTwoAm | +5 |

## Dependencies
- `IGameClock` (from #139 Wave 0)
- `IDiceRoller` (for energy randomization)

## Consumers
- `GameSession` (via `GameSessionConfig`)
- `ConversationRegistry` (#56)
- `HorninessForcedRizz` logic (#51)
- `PlayerResponseDelayEvaluator` (#55 — indirect, caller uses clock to compute TimeSpan)

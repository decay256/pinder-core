# Contract: Issue #54 — GameClock

## Component
`Pinder.Core.Conversation.GameClock` implementing `Pinder.Core.Interfaces.IGameClock`

## Dependencies
- #130 (IGameClock interface defined there)
- `IDiceRoller` (for daily energy roll)

## What it owns
- Simulated game time (Unix-like, DateTimeOffset)
- Time-of-day derivation
- Horniness modifier by time of day
- Daily energy pool (15–20, replenishes at midnight)

## What it does NOT own
- Real wall-clock time
- Energy consumption policy (consumers call ConsumeEnergy)
- Conversation scheduling (that's ConversationRegistry)

## Implementation

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class GameClock : IGameClock
    {
        private DateTimeOffset _now;
        private readonly IDiceRoller _dice;
        private int _remainingEnergy;
        private int _currentDay;  // day number for midnight detection

        /// <param name="startTime">Initial game time.</param>
        /// <param name="dice">For rolling daily energy (d6 + 14 → 15–20).</param>
        public GameClock(DateTimeOffset startTime, IDiceRoller dice);

        public DateTimeOffset Now => _now;
        public int RemainingEnergy => _remainingEnergy;

        public void Advance(TimeSpan amount);      // throws if negative
        public void AdvanceTo(DateTimeOffset target); // throws if before Now
        public TimeOfDay GetTimeOfDay();
        public int GetHorninessModifier();
        public bool ConsumeEnergy(int amount);

        // Internal: called by Advance/AdvanceTo when crossing midnight
        private void CheckMidnightCrossing(DateTimeOffset oldTime, DateTimeOffset newTime);
    }
}
```

### TimeOfDay mapping
```
Hour  0– 1 → LateNight
Hour  2– 5 → AfterTwoAm
Hour  6–11 → Morning
Hour 12–17 → Afternoon
Hour 18–21 → Evening
Hour 22–23 → LateNight
```

### Horniness modifier
| TimeOfDay | Modifier |
|-----------|----------|
| Morning | -2 |
| Afternoon | 0 |
| Evening | +1 |
| LateNight | +3 |
| AfterTwoAm | +5 |

### Energy
- Initial: `dice.Roll(6) + 14` (range 15–20)
- On midnight crossing: re-roll `dice.Roll(6) + 14`
- `ConsumeEnergy(n)`: if `_remainingEnergy >= n`, subtract and return true; else return false

## Test helper: FixedGameClock

```csharp
// In test project (not shipped in Pinder.Core)
public sealed class FixedGameClock : IGameClock
{
    public FixedGameClock(DateTimeOffset now, int energy = 20);
    // All methods work deterministically, Advance/AdvanceTo update Now
    // Energy does NOT auto-replenish (test controls it)
}
```

## Consumers
#51 (Horniness modifier), #55 (time source for delay), #56 (ConversationRegistry)

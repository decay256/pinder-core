# Contract: Issue #54 — GameClock Implementation

## Component
`Pinder.Core.Conversation.GameClock` — concrete implementation of `IGameClock`

## Maturity: Prototype
## NFR: all methods < 1ms

---

## GameClock

**File:** `src/Pinder.Core/Conversation/GameClock.cs`

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class GameClock : IGameClock
    {
        /// <param name="startTime">Initial simulated time.</param>
        /// <param name="dailyEnergy">Energy budget per day. Default: 10.</param>
        public GameClock(DateTimeOffset startTime, int dailyEnergy = 10);

        public DateTimeOffset Now { get; }
        public void Advance(TimeSpan amount);               // amount must be positive
        public void AdvanceTo(DateTimeOffset target);        // target > Now, else ArgumentException
        public TimeOfDay GetTimeOfDay();                     // based on Now.Hour
        public int GetHorninessModifier();                   // Morning=-2, Afternoon=0, Evening=+1, LateNight=+3, AfterTwoAm=+5
        public int RemainingEnergy { get; }
        public bool ConsumeEnergy(int amount);               // false if insufficient
    }
}
```

**Time-of-Day mapping (Now.Hour):**
- 6–11 → Morning
- 12–17 → Afternoon
- 18–21 → Evening
- 22–23, 0–1 → LateNight
- 2–5 → AfterTwoAm

**Energy:** resets when day changes (crossing midnight). `ConsumeEnergy` returns false and does NOT deduct when insufficient.

**Testing helper (same file or separate):**

```csharp
/// <summary>Fixed clock for deterministic testing.</summary>
public sealed class FixedGameClock : IGameClock
{
    // Constructor takes all values as params for full control
}
```

## Dependencies
- `IGameClock`, `TimeOfDay` (from #139 Wave 0)

## Consumers
- #51 (Horniness), #55 (PlayerResponseDelay), #56 (ConversationRegistry)

# Contract: Issue #54 — GameClock Implementation

## Component
`GameClock` (Conversation/) — concrete `IGameClock` implementation + `FixedGameClock` test helper

## Dependencies
- `IGameClock`, `TimeOfDay` (from #139 Wave 0)
- `IDiceRoller` (for daily energy roll)

---

## GameClock

**File:** `src/Pinder.Core/Conversation/GameClock.cs`

```csharp
namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Simulated in-game clock. Tracks game time, time-of-day, and daily energy.
    /// Energy is rolled at construction and replenished on midnight crossing.
    /// </summary>
    public sealed class GameClock : IGameClock
    {
        /// <param name="startTime">Initial in-game time.</param>
        /// <param name="dice">For rolling daily energy (15 + dice.Roll(6) → 16–20).</param>
        public GameClock(DateTimeOffset startTime, IDiceRoller dice);

        public DateTimeOffset Now { get; }
        public void Advance(TimeSpan amount);
        public void AdvanceTo(DateTimeOffset target);
        public TimeOfDay GetTimeOfDay();
        public int GetHorninessModifier();
        public int RemainingEnergy { get; }
        public bool ConsumeEnergy(int amount);
    }
}
```

**Energy rules:**
- Daily energy = `15 + dice.Roll(6)` → range 16–20 (note: issue says 15–20, this gives 16–20; if PO wants 15–20, use `14 + dice.Roll(6)`)
- Actually per the issue: "15–20 energy per in-game day (roll once at day start via IDiceRoller)" → simplest: `dice.Roll(6) + 14` gives 15–20
- Energy replenishes when clock crosses midnight (detected during `Advance`/`AdvanceTo`)
- `ConsumeEnergy(amount)` returns `false` and does NOT deduct if insufficient

**TimeOfDay boundaries (hour of day):**
| Hour range | TimeOfDay |
|---|---|
| 6–11 | Morning |
| 12–17 | Afternoon |
| 18–21 | Evening |
| 22–23, 0–1 | LateNight |
| 2–5 | AfterTwoAm |

**Horniness modifier table:**
| TimeOfDay | Modifier |
|---|---|
| Morning | -2 |
| Afternoon | 0 |
| Evening | +1 |
| LateNight | +3 |
| AfterTwoAm | +5 |

---

## FixedGameClock (test helper)

**File:** `tests/Pinder.Core.Tests/Helpers/FixedGameClock.cs`

```csharp
/// <summary>
/// Deterministic game clock for testing. No dice dependency.
/// </summary>
public sealed class FixedGameClock : IGameClock
{
    public FixedGameClock(DateTimeOffset startTime, int initialEnergy = 20);
    // All IGameClock methods implemented deterministically
    // Advance/AdvanceTo update Now, energy replenishes on midnight crossing
}
```

---

## Consumers
- #51 (Horniness-forced Rizz — uses `GetHorninessModifier()`)
- #55 (PlayerResponseDelay — caller computes delay from clock, not the evaluator itself)
- #56 (ConversationRegistry — `FastForward` advances clock, `ConsumeEnergy` pass-through)

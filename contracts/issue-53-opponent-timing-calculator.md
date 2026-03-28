# Contract: Issue #53 — OpponentTimingCalculator

## Component
`Pinder.Core.Conversation.OpponentTimingCalculator` (new static class)

## Maturity
Prototype

## NFR
- Latency: < 1ms (pure computation)

---

## Interface

**File**: `src/Pinder.Core/Conversation/OpponentTimingCalculator.cs`

```csharp
public static class OpponentTimingCalculator
{
    /// <summary>
    /// Compute reply delay in minutes, accounting for shadow effects.
    /// Extends the existing TimingProfile.ComputeDelay with shadow-aware modifiers.
    /// </summary>
    /// <param name="profile">Opponent's timing profile.</param>
    /// <param name="interestLevel">Current interest (0–25).</param>
    /// <param name="shadows">Session shadow tracker for shadow-aware modifiers. Null = no shadow effects.</param>
    /// <param name="dice">Dice roller for variance.</param>
    /// <returns>Delay in whole minutes (minimum 1).</returns>
    public static int ComputeDelay(
        TimingProfile profile,
        int interestLevel,
        SessionShadowTracker? shadows,
        IDiceRoller dice)
    {
        // Base computation same as TimingProfile.ComputeDelay
        // Shadow modifiers:
        //   Overthinking ≥ 6: +50% delay (they overthink their replies)
        //   Fixation ≥ 6: -25% delay (they reply fast, obsessively)
        //   Madness ≥ 12: variance doubles (erratic timing)
        // Floor at 1 minute
    }
}
```

**Note**: `TimingProfile.ComputeDelay` already exists and works. This calculator adds shadow-aware modifiers on top. GameSession should switch from calling `_opponent.Timing.ComputeDelay()` to calling `OpponentTimingCalculator.ComputeDelay()` once shadow tracking is available.

---

## Behavioural Contract
- Returns `int` (minutes), minimum 1
- Pure function — no side effects
- Without shadows (null tracker), behaves identically to `TimingProfile.ComputeDelay`
- Shadow modifiers are multiplicative on the base delay
- Does NOT advance the game clock — that's the caller's responsibility

## Dependencies
- SessionShadowTracker (from #44)
- TimingProfile (existing)
- IDiceRoller (existing)

## Consumers
- GameSession.ResolveTurnAsync (replaces direct TimingProfile.ComputeDelay call)

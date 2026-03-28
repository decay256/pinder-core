# Contract: Issue #55 — PlayerResponseDelay

## Component
`Pinder.Core.Conversation.PlayerResponseDelay` (new static class)

## Maturity
Prototype

---

## Interface

**File**: `src/Pinder.Core/Conversation/PlayerResponseDelay.cs`

```csharp
public static class PlayerResponseDelay
{
    /// <summary>
    /// Compute the interest penalty for the player's response delay.
    /// </summary>
    /// <param name="delay">Wall-clock time the player took to respond (measured by host).</param>
    /// <param name="opponentStats">Opponent's stat block for personality modifiers.</param>
    /// <param name="opponentShadows">Opponent's shadow tracker for Denial threshold.</param>
    /// <param name="currentInterest">Current interest level.</param>
    /// <returns>Interest delta (0 or negative).</returns>
    public static int ComputePenalty(
        TimeSpan delay,
        StatBlock opponentStats,
        SessionShadowTracker? opponentShadows,
        int currentInterest)
    {
        // 1. Compute base penalty from delay duration
        // 2. Apply personality modifiers
        // 3. Return
    }
}
```

---

## Base Penalty Table

| Player delay | Base Interest Δ |
|-------------|----------------|
| < 1 min | 0 |
| 1–15 min | 0 |
| 15–60 min | -1 (only if interest ≥ 16) |
| 1–6 hours | -2 |
| 6–24 hours | -3 |
| 24+ hours | -5 |

## Personality Modifiers

| Condition | Effect |
|-----------|--------|
| Opponent Chaos base stat ≥ 4 | Penalty = 0 (doesn't care) |
| Opponent Denial shadow ≥ 6 | Penalty applied but also triggers shadow test (Denial growth) |
| Opponent Fixation shadow ≥ 6 | Penalty doubled |

**Application order**: Check Chaos ≥ 4 first (overrides to 0). Then check Fixation (doubles). Denial is a side-effect trigger, not a penalty modifier.

---

## Time Source (resolves VC-81)

The `TimeSpan delay` parameter is **wall-clock time measured by the host**. The host tracks when it last called `ResolveTurnAsync` (or session start) and when the player submits their next action. This is NOT game-clock time.

GameSession can optionally accept a `TimeSpan playerDelay` parameter on `StartTurnAsync` or `ResolveTurnAsync`. For prototype, the host passes it in.

---

## Behavioural Contract
- Pure function — no side effects (except Denial growth trigger which is a returned flag)
- Returns 0 or negative int
- Personality modifiers apply to the opponent's stats, not the player's
- Wall-clock time, not game-clock time
- The host is responsible for measuring and passing the delay

## Dependencies
- #44 (SessionShadowTracker for opponent shadow values — nullable, skip if not available)

## Consumers
- GameSession (calls on turn start, applies penalty before turn)
- Host (measures and passes wall-clock delay)

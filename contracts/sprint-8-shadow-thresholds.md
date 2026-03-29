# Contract: Issue #45 — Shadow Thresholds (§7 Effects)

## Component
- `Pinder.Core.Stats.ShadowThresholdEvaluator` (new static class)
- `Pinder.Core.Conversation.GameSession` (extend)

## Depends on
- #44: Shadow growth events (threshold effects require grown shadow values)
- #139: `SessionShadowTracker`, `GameSessionConfig`

## Maturity: Prototype

---

## ShadowThresholdEvaluator

**File:** `src/Pinder.Core/Stats/ShadowThresholdEvaluator.cs`

```csharp
namespace Pinder.Core.Stats
{
    public static class ShadowThresholdEvaluator
    {
        /// <summary>
        /// Returns threshold tier: 0 (0–5), 1 (6–11), 2 (12–17), 3 (18+).
        /// </summary>
        public static int GetThresholdLevel(int shadowValue);
    }
}
```

## Threshold Effects (applied in GameSession)

| Tier | Shadow Value | Effect |
|------|-------------|--------|
| T0 | 0–5 | None |
| T1 | 6–11 | Flavor text injected into LLM prompt (DialogueContext gains shadow flavor field) |
| T2 | 12–17 | Disadvantage on rolls using the paired stat |
| T3 | 18+ | **Dread ≥18**: Starting interest drops to 8 (via GameSessionConfig.StartingInterest). **Horniness ≥18**: All options forced to Rizz (handled by #51). **Others**: Stat suppressed — options using that stat are removed. |

## GameSession Integration

1. In `StartTurnAsync()`: evaluate all 6 shadow thresholds via `_playerShadows.GetEffectiveShadow(shadow)`
2. T2 effects: set `hasDisadvantage = true` for the paired stat's rolls
3. T3 Dread: handled at construction time via `GameSessionConfig.StartingInterest = 8`
4. T3 suppression: filter LLM-returned options to exclude suppressed stats (if all suppressed, keep one Chaos option as fallback)

## Dependencies
- `SessionShadowTracker.GetEffectiveShadow()` (#139)

## Consumers
- #51 (Horniness T3 check)
- GameSession (all threshold effects)

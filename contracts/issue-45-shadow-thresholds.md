# Contract: Issue #45 — Shadow Thresholds

## Component
`Pinder.Core.Stats.ShadowThresholdEvaluator` + GameSession integration

## Dependencies
- #130 (`SessionShadowTracker`)
- #44 (shadow growth — so values are mutable during session)

## Files
- `Stats/ShadowThresholdEvaluator.cs` — new, stateless
- `Conversation/GameSession.cs` — wire threshold effects

## Interface

### ShadowThresholdEvaluator

```csharp
namespace Pinder.Core.Stats
{
    /// <summary>
    /// Evaluates shadow threshold level from a shadow stat value.
    /// Threshold 0: value < 6
    /// Threshold 1: 6 ≤ value < 12
    /// Threshold 2: 12 ≤ value < 18
    /// Threshold 3: value ≥ 18
    /// </summary>
    public static class ShadowThresholdEvaluator
    {
        public static int GetThresholdLevel(int shadowValue)
        {
            if (shadowValue >= 18) return 3;
            if (shadowValue >= 12) return 2;
            if (shadowValue >= 6)  return 1;
            return 0;
        }

        /// <summary>
        /// Get all threshold levels for a character's shadow stats.
        /// Returns dictionary of ShadowStatType → threshold level (0–3).
        /// Only includes non-zero thresholds.
        /// </summary>
        public static Dictionary<ShadowStatType, int> GetAllThresholds(SessionShadowTracker tracker);
    }
}
```

### Threshold effects table

| Shadow | T1 (≥6) | T2 (≥12) | T3 (≥18) |
|--------|---------|----------|----------|
| Dread | Existential flavor (LLM context) | Wit has disadvantage | Starting Interest 8 |
| Madness | UI glitches (cosmetic, LLM) | Charm has disadvantage | One option replaced with unhinged text (LLM) |
| Denial | "I'm fine" in messages (LLM) | Honesty has disadvantage | Honesty options removed |
| Fixation | Repeating patterns (LLM) | Chaos has disadvantage | Must pick same stat as last turn |
| Overthinking | Always see Interest number | SA has disadvantage | See opponent's inner monologue (LLM) |
| Horniness | Rizz appears more (LLM) | One option always Rizz | ALL options Rizz |

### Mechanical effects (code-enforced)

Only these effects need code implementation (others are LLM context):

**T2 (≥12) — Disadvantage**:
- Dread → `hasDisadvantage = true` when rolling Wit
- Madness → disadvantage on Charm
- Denial → disadvantage on Honesty
- Fixation → disadvantage on Chaos
- Overthinking → disadvantage on SA
- Horniness → (handled by #51, one option forced Rizz)

**T3 (≥18) — Hard constraints**:
- Dread ≥18 → starting interest 8 (via `GameSessionConfig.StartingInterest`)
- Denial ≥18 → remove all Honesty options from dialogue
- Fixation ≥18 → force player to pick same stat as last turn (remove others)
- Horniness ≥18 → all options become Rizz (handled by #51)

**T1 (≥6) — LLM flavor only**:
- Pass threshold data via `DialogueContext.ShadowThresholds`
- LLM adapter adjusts tone/content based on thresholds
- Overthinking T1: `TurnStart` or `GameStateSnapshot` could expose interest value (currently already available)

### GameSession integration

In `StartTurnAsync`:
1. Compute thresholds: `var thresholds = ShadowThresholdEvaluator.GetAllThresholds(_shadowTracker);`
2. Apply T2 disadvantage: for each shadow at T2+, set `hasDisadvantage = true` for the paired stat
3. Pass thresholds to `DialogueContext.ShadowThresholds`
4. After LLM returns options: filter/constrain based on T3 effects
   - Denial ≥18: remove options where `stat == StatType.Honesty`
   - Fixation ≥18: remove options where `stat != lastStatUsed` (if lastStatUsed is set)
5. If options are empty after filtering → game design edge case. For prototype: keep at least one option. If all would be removed, keep the first.

### InterestMeter overload

```csharp
public InterestMeter(int startingValue)
{
    Current = Math.Max(Min, Math.Min(Max, startingValue));
}
```

Used by GameSession when `Dread ≥ 18` at session start.

## Behavioral contracts
- Threshold evaluation is pure and stateless
- Disadvantage from shadows stacks with disadvantage from interest state (Bored) — but disadvantage doesn't "double", it's boolean
- Disadvantage from shadows also stacks with trap disadvantage — same: boolean
- Option filtering happens AFTER LLM returns options (GameSession post-processes)
- At least one option must always remain after filtering

## Consumers
GameSession, #51 (Horniness thresholds)

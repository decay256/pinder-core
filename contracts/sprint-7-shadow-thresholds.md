# Contract: Issue #45 — Shadow Thresholds (§7)

## Component
`ShadowThresholdEvaluator` (Stats/) + GameSession integration

## Dependencies
- #44: Shadow growth events (so thresholds reflect in-session growth)
- #139 Wave 0: `SessionShadowTracker`, `InterestMeter(int)`, `GameSessionConfig`

---

## ShadowThresholdEvaluator

**File:** `src/Pinder.Core/Stats/ShadowThresholdEvaluator.cs`

```csharp
namespace Pinder.Core.Stats
{
    /// <summary>
    /// Computes threshold level (0–3) for a shadow stat value.
    /// T0: 0–5, T1: 6–11, T2: 12–17, T3: 18+
    /// </summary>
    public static class ShadowThresholdEvaluator
    {
        /// <returns>0, 1, 2, or 3</returns>
        public static int GetThresholdLevel(int shadowValue)
        {
            if (shadowValue >= 18) return 3;
            if (shadowValue >= 12) return 2;
            if (shadowValue >= 6) return 1;
            return 0;
        }
    }
}
```

---

## Threshold Effects

### At session start (constructor):
- **Dread ≥ 18**: Starting interest = 8 instead of 10 (via `InterestMeter(8)`)

### Per turn (StartTurnAsync):
- **Any shadow at T2 (≥12)**: The penalized stat rolls with **disadvantage**
  - Dread ≥12 → Wit disadvantage
  - Madness ≥12 → Charm disadvantage
  - Denial ≥12 → Honesty disadvantage
  - Fixation ≥12 → Chaos disadvantage
  - Overthinking ≥12 → SA disadvantage
  - Horniness ≥12 → Rizz disadvantage

- **Denial ≥ 18**: Honesty options **removed** from dialogue options (filter out after LLM returns)
- **Fixation ≥ 18**: Player **must** use same stat as last turn (replace all options with that stat, or add constraint to DialogueContext)
- **Horniness ≥ 18**: All options become Rizz (handled by #51 Horniness mechanic)

### LLM context:
- `DialogueContext.ShadowThresholds` populated as `Dictionary<ShadowStatType, int>` mapping each shadow to its threshold level (0–3)
- LLM uses this for flavor text (Dread T1 = existential flavor, Madness T3 = unhinged text, etc.)

---

## GameSession Integration Points

1. **Constructor**: Check Dread shadow value via `SessionShadowTracker`. If T3, use `InterestMeter(8)`.
2. **StartTurnAsync**: Build `shadowThresholds` dict. Apply per-stat disadvantage flags. Apply option filters (Denial T3, Fixation T3).
3. **ResolveTurnAsync**: Shadow thresholds affect the roll via disadvantage flags already set in step 2.

---

## Behavioral Invariants
- Threshold checks use `SessionShadowTracker.GetEffectiveShadow()` (base + delta)
- Disadvantage from shadows stacks with disadvantage from interest state (Bored) — but disadvantage doesn't stack in d20 (roll twice, take lower is the same regardless of source)
- If `SessionShadowTracker` is null, all thresholds are 0 (no effects)
- T3 option restrictions happen after the LLM returns options (post-processing in GameSession, not a constraint on the LLM)

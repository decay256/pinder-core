# Contract: Issue #45 — Shadow Thresholds

## Component
`Pinder.Core.Stats.ShadowThresholdEvaluator` (new) + `GameSession` integration

## Maturity
Prototype

---

## ShadowThresholdEvaluator (`Pinder.Core.Stats`)

Stateless utility that computes threshold levels from shadow values.

```csharp
namespace Pinder.Core.Stats
{
    public static class ShadowThresholdEvaluator
    {
        /// <summary>
        /// Get threshold level: 0 (none), 1 (≥6), 2 (≥12), 3 (≥18).
        /// </summary>
        public static int GetThresholdLevel(int shadowValue);

        /// <summary>
        /// Get all stats that should have disadvantage applied due to threshold ≥12.
        /// Returns empty if none.
        /// Dread≥12→Wit, Madness≥12→Charm, Denial≥12→Honesty,
        /// Fixation≥12→Chaos, Overthinking≥12→SA, Horniness≥12→Rizz.
        /// </summary>
        public static List<StatType> GetDisadvantagedStats(SessionShadowTracker tracker);

        /// <summary>
        /// Get stats whose options should be suppressed (threshold ≥18 effects).
        /// Denial≥18: suppress Honesty.
        /// </summary>
        public static List<StatType> GetSuppressedStats(SessionShadowTracker tracker);

        /// <summary>
        /// Check if Fixation ≥18: player must pick same stat as last turn.
        /// </summary>
        public static bool MustRepeatLastStat(SessionShadowTracker tracker);

        /// <summary>
        /// Get starting interest override. Returns null if no override.
        /// Dread ≥18: starting interest = 8.
        /// </summary>
        public static int? GetStartingInterestOverride(SessionShadowTracker tracker);

        /// <summary>
        /// Build shadow threshold dictionary for DialogueContext.
        /// Maps each ShadowStatType to its threshold level (0/1/2/3).
        /// </summary>
        public static Dictionary<ShadowStatType, int> BuildThresholdMap(SessionShadowTracker tracker);
    }
}
```

## Threshold Table

| Level | Shadow Value | General Effect |
|---|---|---|
| 0 | 0–5 | None |
| 1 | 6–11 | Flavor/cosmetic (LLM instruction) |
| 2 | 12–17 | Stat has disadvantage |
| 3 | 18+ | Hard mechanical restriction |

## Per-Shadow Threshold 3 Effects (Hard Mechanical)

| Shadow | At ≥18 |
|---|---|
| Dread | Starting Interest 8 (not 10) |
| Madness | One option/turn replaced with unhinged text (LLM instruction) |
| Denial | Honesty options stop appearing (suppressed from option list) |
| Fixation | Must pick same stat as last turn (enforce in ResolveTurnAsync) |
| Overthinking | See opponent's inner monologue (LLM instruction) |
| Horniness | ALL options become Rizz (handled by #51) |

## Integration with GameSession

1. **Construction**: If `GameSessionConfig.PlayerShadows` provided, check `GetStartingInterestOverride()` → pass to `InterestMeter(int)` overload.
2. **StartTurnAsync**: Call `GetDisadvantagedStats()` → apply disadvantage flags. Call `GetSuppressedStats()` → filter available stats. Call `BuildThresholdMap()` → set `DialogueContext.ShadowThresholds`.
3. **ResolveTurnAsync**: If `MustRepeatLastStat()` and chosen stat differs from last turn → throw `InvalidOperationException`.

## Dependencies
- `SessionShadowTracker` (#139 Wave 0)
- `InterestMeter(int startingValue)` overload (#139 Wave 0)

## Consumers
- `GameSession` (reads thresholds each turn)
- `ILlmAdapter` consumers (receive threshold map via `DialogueContext.ShadowThresholds`)

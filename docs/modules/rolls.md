# Rolls

## Overview
The `Pinder.Core.Rolls` namespace implements dice-rolling mechanics, failure/success classification, and interest-delta computation for the Pinder dating-sim game engine. It determines how player rolls translate into game-state changes (interest gain/loss, risk tiers, failure severity).

## Key Components

| File / Class | Description |
|---|---|
| `src/Pinder.Core/Rolls/FailureScale.cs` | Static class mapping `FailureTier` values to negative interest deltas per rules-v3.4 §5. |
| `src/Pinder.Core/Rolls/FailureTier.cs` | Enum classifying failure severity by miss margin: None, Fumble (1–2), Misfire (3–5), TropeTrap (6–9), Catastrophe (10+), Legendary (Nat 1). |
| `src/Pinder.Core/Rolls/SuccessScale.cs` | Maps successful rolls to positive interest deltas. |
| `src/Pinder.Core/Rolls/RollEngine.cs` | Core dice-rolling logic — resolves rolls against DC, applies modifiers. |
| `src/Pinder.Core/Rolls/RollResult.cs` | Value object holding the outcome of a single roll (total, DC, tier, success flag, etc.). |
| `src/Pinder.Core/Rolls/RiskTier.cs` | Enum for risk tiers (e.g., Safe, Bold, Risky). |
| `src/Pinder.Core/Rolls/RiskTierBonus.cs` | Maps risk tiers to bonus modifiers. |
| `src/Pinder.Core/Rolls/SystemRandom.cs` | Default `IDice` implementation wrapping `System.Random`. |
| `tests/Pinder.Core.Tests/RulesSpec/RulesSpecTests.cs` | 54 xUnit tests (37 passing, 17 skipped) auto-generated from `rules-v3-enriched.yaml`, verifying `FailureScale`, `SuccessScale`, `RiskTier`, `RiskTierBonus`, `InterestMeter`, `ShadowThresholdEvaluator`, and `LevelTable` against authoritative rules. |

## API / Public Interface

### FailureScale

```csharp
public static class FailureScale
{
    /// Returns the interest delta for a roll result.
    /// Successes (FailureTier.None) return 0.
    public static int GetInterestDelta(RollResult result);
}
```

**Failure tier → interest delta mapping (rules-v3.4 §5):**

| FailureTier | Interest Delta |
|---|---|
| None | 0 |
| Fumble | −1 |
| Misfire | −1 |
| TropeTrap | −2 |
| Catastrophe | −3 |
| Legendary | −4 |

### FailureTier

```csharp
public enum FailureTier
{
    None,           // Not a failure
    Fumble,         // Missed by 1–2 (using FinalTotal)
    Misfire,        // Missed by 3–5 (using FinalTotal)
    TropeTrap,      // Missed by 6–9 (using FinalTotal, activates a Trap)
    Catastrophe,    // Missed by 10+ (using FinalTotal)
    Legendary       // Nat 1 (regardless of DC)
}
```

### RollResult (selected members)

```csharp
public sealed class RollResult
{
    /// <summary>By how much the roll missed the DC (using FinalTotal). 0 on success.</summary>
    public int MissMargin => IsSuccess ? 0 : DC - FinalTotal;
}
```

### SuccessScale

```csharp
public static class SuccessScale
{
    /// Returns the positive interest delta for a successful roll.
    /// Uses FinalTotal (includes external bonuses) to compute margin over DC.
    public static int GetInterestDelta(RollResult result);
}
```

## Architecture Notes

- **FailureScale is pure logic** — no state, no dependencies. It takes a `RollResult` and returns an `int`.
- **Trap activation on failure**: `RollEngine` activates traps for both `TropeTrap` (miss 6–9) and `Catastrophe` (miss 10+) tiers per rules §5. If a trap is already active on the stat, no new trap is activated. Trap lookup uses `ITrapRegistry.GetTrap(stat)`.
- **Side effects (shadow growth) are handled by `GameSession`**, not by FailureScale. The scale only computes the interest delta.
- **Interest is clamped to [0, 25] by GameSession** — FailureScale itself does not clamp.
- The delta values were updated in issue #266 to match rules-v3.4 §5. The previous (prototype) values from issue #28 were steeper: Misfire −2, TropeTrap −3, Catastrophe −4, Legendary −5.
- **Rules-to-code verification**: `RulesSpecTests.cs` is auto-generated from `rules/extracted/rules-v3-enriched.yaml` (via `rules/tools/generate_tests.py`). It should not be hand-edited; regenerate from the YAML source. The file includes three private helpers (`MakeFailureResult`, `MakeSuccessResult`, `MakeRiskResult`) for constructing `RollResult` fixtures. Tests span §5 (roll resolution), §6 (interest states), §7 (shadow thresholds), and §10 (progression/level table). 17 qualitative/LLM-taint rules are marked `[Fact(Skip = "Qualitative/LLM rule — not unit-testable")]`.
- **FinalTotal is the canonical value for all margin calculations** (issue #309). `SuccessScale`, `RollEngine` failure tier assignment, `RollResult.MissMargin`, and `GameSession.beatDcBy` all use `FinalTotal` (which includes `externalBonus`) rather than `Total`. This ensures external bonuses affect outcome quality, not just the pass/fail threshold.

## Change Log

| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-02 | #266 | Initial creation — documented FailureScale interest deltas updated to rules-v3.4 §5: Misfire −2→−1, TropeTrap −3→−2, Catastrophe −4→−3, Legendary −5→−4. Tests updated across GameSessionTests, ComboGameSessionTests, FullConversationIntegrationTest, ShadowGrowthEventTests, ShadowGrowthSpecTests. |
| 2026-04-02 | #267 | Bug fix: Catastrophe tier (miss 10+) now also activates a trap via `RollEngine`, matching rules §5 (miss 10+ = −3 + trap). Previously only TropeTrap activated traps. Added `SingleTrapRegistry` test helper and three new tests in `RollEngineTests`. |
| 2026-04-03 | #309 | Bug fix: All margin calculations now use `FinalTotal` (includes `externalBonus`) instead of `Total`. Affected: `SuccessScale.GetInterestDelta` margin, `RollEngine` failure tier assignment, `RollResult.MissMargin`, and `GameSession.beatDcBy`. Added `Issue309_FinalTotalTests.cs` (12 tests). Updated existing tests in `RollEngineExtensionTests` and `Wave0SpecTests`. |
| 2026-04-04 | #445 | Added `tests/Pinder.Core.Tests/RulesSpec/RulesSpecTests.cs` — 54 xUnit tests (37 passing, 17 skipped) generated from `rules-v3-enriched.yaml`. Covers FailureScale (§5), SuccessScale (§5), RiskTier/RiskTierBonus (§5), InterestMeter/InterestState (§6), ShadowThresholdEvaluator (§7), and LevelTable (§10). All generated stub API paths corrected to real Pinder.Core APIs. No new dependencies. |

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
    Fumble,         // Missed by 1–2
    Misfire,        // Missed by 3–5
    TropeTrap,      // Missed by 6–9 (activates a Trap)
    Catastrophe,    // Missed by 10+
    Legendary       // Nat 1 (regardless of DC)
}
```

## Architecture Notes

- **FailureScale is pure logic** — no state, no dependencies. It takes a `RollResult` and returns an `int`.
- **Trap activation on failure**: `RollEngine` activates traps for both `TropeTrap` (miss 6–9) and `Catastrophe` (miss 10+) tiers per rules §5. If a trap is already active on the stat, no new trap is activated. Trap lookup uses `ITrapRegistry.GetTrap(stat)`.
- **Side effects (shadow growth) are handled by `GameSession`**, not by FailureScale. The scale only computes the interest delta.
- **Interest is clamped to [0, 25] by GameSession** — FailureScale itself does not clamp.
- The delta values were updated in issue #266 to match rules-v3.4 §5. The previous (prototype) values from issue #28 were steeper: Misfire −2, TropeTrap −3, Catastrophe −4, Legendary −5.

## Change Log

| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-02 | #266 | Initial creation — documented FailureScale interest deltas updated to rules-v3.4 §5: Misfire −2→−1, TropeTrap −3→−2, Catastrophe −4→−3, Legendary −5→−4. Tests updated across GameSessionTests, ComboGameSessionTests, FullConversationIntegrationTest, ShadowGrowthEventTests, ShadowGrowthSpecTests. |
| 2026-04-02 | #267 | Bug fix: Catastrophe tier (miss 10+) now also activates a trap via `RollEngine`, matching rules §5 (miss 10+ = −3 + trap). Previously only TropeTrap activated traps. Added `SingleTrapRegistry` test helper and three new tests in `RollEngineTests`. |

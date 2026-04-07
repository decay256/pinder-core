# Spec Document for Issue #481

## 1. Module
**Module**: `docs/modules/session-runner.md`

## 2. Overview
The `ScoringPlayerAgent` currently uses a pure expected value (EV) formula (`P(Success)*Gain - P(Fail)*Cost`), which ignores human risk-reward trade-offs and treats all failure/success probabilities symmetrically. This update resolves CPO Vision Concern #667 by independently weighting the success gain and failure cost components based on the current game state (position-aware risk appetite). Additionally, it factors in character personality via an archetype-based risk modifier and models the human impulse for narrative appeal ("Too cool not to try"). 

## 3. Function Signatures

```csharp
// In Pinder.SessionRunner.PlayerAgentContext:
public string? ActiveArchetype { get; }
public string? TextingStyleFragment { get; }
public string? History { get; }
// (Constructor updated to accept these as optional parameters with null defaults)

// In Pinder.SessionRunner.ScoringPlayerAgent:
private static (float SuccessWeight, float FailureWeight) ComputePositionWeights(int currentInterest);
private static float GetArchetypeBias(string? activeArchetype, RiskTierCategory riskTier, StatType stat);
private static float GetNarrativeAppealBonus(RiskTierCategory riskTier, DialogueOption option);
```

## 4. Input/Output Examples

**Example 1: Position-Aware Weighting (Near Win)**
- **State**: Current Interest = 22.
- **Option**: Risk Tier = Bold. Success Gain = 3.0, Failure Cost = 3.5. Success Chance = 0.4.
- **Weights**: `SuccessWeight` = 0.8, `FailureWeight` = 1.5.
- **EV Output**: `(0.4 * 3.0 * 0.8) - (0.6 * 3.5 * 1.5)` = `0.96 - 3.15` = `-2.19`.
- **Result**: The option is heavily penalized, correctly reflecting that a player at 22 interest should not take bold risks.

**Example 2: Archetype Risk Bias**
- **State**: `ActiveArchetype` = "Chaos", Option Risk = Bold.
- **Output**: `GetArchetypeBias` returns `0.3f`. 
- **Application**: The bias is applied to the final score to favor the option.

**Example 3: "Too Cool Not To Try"**
- **State**: Option Risk = Bold, Has Combo = True.
- **Output**: `GetNarrativeAppealBonus` returns `0.3f`, which is added as a flat bonus to the final score.

## 5. Acceptance Criteria

- **AC 1: Independent Risk-Reward Weighting**
  - Replace the flat EV calculation with independent weights: `(SuccessChance * ExpectedGain * SuccessWeight) - (FailChance * FailCost * FailureWeight)`.
  - **Near Win (Interest ≥ 20)**: `SuccessWeight` is penalized (e.g., `0.8f`), and `FailureWeight` is amplified (e.g., `1.5f`).
  - **Desperate (Interest ≤ 8)**: `SuccessWeight` is amplified (e.g., `1.2f`), and `FailureWeight` is reduced (e.g., `0.5f`).
  - **Neutral**: Weights remain `1.0f`.
  - *Note: Trope Trap miss range (6-9) is already factored into `ComputeWeightedFailCost` (Sprint 14).*

- **AC 2: Archetype-Based Risk Appetite**
  - Add an `archetypeBias` to the score based on the character's `ActiveArchetype`.
  - `Philosopher`: `+0.2` bias on `Hard` and `Bold` options.
  - `Peacock`: `+0.2` bias on `Safe` and `Medium` options.
  - `Chaos` / `2AM Texter`: `+0.3` bias on `Bold` options.
  - `Love Bomber`: `+0.2` bias on `Honesty` and `Rizz` options (regardless of risk tier).
  - `Sniper`: High variance penalty. (Treat as `+0.2` bias on `Hard` options but `-0.2` on `Bold` options unless success chance is very high).

- **AC 3: "Too Cool Not To Try"**
  - If an option is `Bold` AND has either a `Combo` or a `Tell` bonus, add a flat `+0.3` narrative appeal bonus.

- **AC 4: Context DTO Extension**
  - `PlayerAgentContext` must expose `ActiveArchetype`, `TextingStyleFragment`, and `History` (all `string?`, optional in constructor). This enables both the `ScoringPlayerAgent` and `LlmPlayerAgent` to access character persona data.

- **AC 5: Testing**
  - Add tests verifying that at Interest 20, the agent prefers a `Safe` option over a `Bold` option even if the unweighted EV of the `Bold` option is slightly higher.
  - Add tests verifying archetype bias applied to the correct Risk Tiers.

## 6. Edge Cases
- **Negative EV and Archetype Multiplier**: The CPO comment suggests `baseEV * (1.0f + archetypeBias)`. However, if an option has a negative EV, applying this multiplier makes it *more* negative, penalizing the archetype's preferred behavior. Implementation must ensure archetype bias strictly *increases* the score of preferred options (e.g., `score += Math.Abs(score) * archetypeBias` or applying a flat addition).
- **Missing Archetype**: If `ActiveArchetype` is null or unrecognized, the bias defaults to `0.0f`.
- **Strategic Bonus Sync**: Existing `NearWinBias` (flat +2.0) and `BoredBoldBias` (flat +1.0) should be removed or scaled back, as the independent EV weighting now handles position-aware risk mathematically.

## 7. Error Conditions
- `PlayerAgentContext` with null required parameters continues to throw `ArgumentNullException`. The new string parameters must be nullable and default to `null`.
- No exception should be thrown for unknown archetypes; the agent must fail open to a `0.0f` bias.

## 8. Dependencies
- Depends on `CharacterProfile` (or the session runner host) passing the `ActiveArchetype` into `PlayerAgentContext`.

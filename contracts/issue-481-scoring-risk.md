# Contract: Issue #481 & #667 — ScoringPlayerAgent Variance

## Component: `Pinder.SessionRunner.ScoringPlayerAgent`

### Math Update
Modify the expected value calculation to independently weight the failure penalty instead of scaling the final EV.

```csharp
// 1. Position-based risk appetite
float failureWeight = context.CurrentInterest >= 20 ? 1.5f : (context.CurrentInterest <= 8 ? 0.5f : 1.0f);

// 2. Trap cost valuation
float expectedTrapPenalty = failChance * (missIsInTropeTrapRange ? 2.5f : 0f);

// 3. Expected Value calculation
float expectedGain = (successChance * successValue) - (failChance * failCost * failureWeight) - expectedTrapPenalty;

// 4. Momentum banking
float momentumBonus = (context.MomentumStreak == 2 && successChance >= 0.5f) ? 0.3f : 0f;
float finalScore = expectedGain + momentumBonus;
```

This ensures high-variance (risky) options become relatively more attractive when desperate, because the failure cost is down-weighted.

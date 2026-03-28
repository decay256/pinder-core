# Contract: Issue #42 — Risk Tier Interest Bonus

## Component
`Pinder.Core.Rolls.RollResult` (modified — add RiskTier property)
`Pinder.Core.Conversation.GameSession.ResolveTurnAsync` (modified — add risk bonus)

## Maturity
Prototype

---

## RiskTier Computation

**Added to `RollResult` as a computed property:**

```csharp
/// <summary>
/// Risk tier based on what the player needed to roll on the d20.
/// Need = DC - (StatModifier + LevelBonus). Clamped to 1 minimum.
/// </summary>
public RiskTier RiskTier
{
    get
    {
        int need = DC - (StatModifier + LevelBonus);
        if (need <= 5)  return RiskTier.Safe;
        if (need <= 10) return RiskTier.Medium;
        if (need <= 15) return RiskTier.Hard;
        return RiskTier.Bold;
    }
}
```

## Interest Bonus in GameSession

In `ResolveTurnAsync`, after computing base interest delta from SuccessScale:

```csharp
if (rollResult.IsSuccess)
{
    int riskBonus = rollResult.RiskTier switch
    {
        RiskTier.Hard => 1,
        RiskTier.Bold => 2,
        _ => 0
    };
    interestDelta += riskBonus;
}
```

**Note**: C# 8.0 switch expression is supported with LangVersion 8.0. If not, use if/else.

## Behavioural Contract
- Risk tier is computed from `DC - (StatModifier + LevelBonus)` — the "need" to roll
- Hard success: +1 bonus interest on top of SuccessScale
- Bold success: +2 bonus interest on top of SuccessScale
- Safe/Medium success: no bonus
- Failure: no risk bonus regardless of tier
- `TurnResult.RiskTier` is populated from `rollResult.RiskTier`

## Dependencies
- #78 (RiskTier enum must exist)

## Consumers
GameSession, TurnResult

# Contract: Issue #78 — TurnResult Expansion (Prerequisite)

## Component
`Pinder.Core.Conversation.TurnResult` (modified)
`Pinder.Core.Rolls.RiskTier` (new enum)

## Maturity
Prototype

---

## New Type: RiskTier

**File**: `src/Pinder.Core/Rolls/RiskTier.cs`

```csharp
namespace Pinder.Core.Rolls
{
    /// <summary>
    /// Risk tier based on how much the player needs to roll.
    /// Need = DC - (statMod + levelBonus). Higher need = riskier.
    /// </summary>
    public enum RiskTier
    {
        Safe,    // Need ≤ 5
        Medium,  // Need 6–10
        Hard,    // Need 11–15
        Bold     // Need ≥ 16
    }
}
```

---

## TurnResult Expansion

**File**: `src/Pinder.Core/Conversation/TurnResult.cs`

Add fields with defaults so existing constructor calls compile:

```csharp
public sealed class TurnResult
{
    // EXISTING fields — unchanged
    public RollResult Roll { get; }
    public string DeliveredMessage { get; }
    public string OpponentMessage { get; }
    public string? NarrativeBeat { get; }
    public int InterestDelta { get; }
    public GameStateSnapshot StateAfter { get; }
    public bool IsGameOver { get; }
    public GameOutcome? Outcome { get; }

    // NEW fields
    public IReadOnlyList<string> ShadowGrowthEvents { get; }  // #44 — list of "Dread +1" descriptions
    public string? ComboTriggered { get; }                      // #46 — combo name or null
    public int CallbackBonusApplied { get; }                    // #47 — 0 if none
    public int TellReadBonus { get; }                           // #50 — 0 if none
    public string? TellReadMessage { get; }                     // #50 — null if no tell read
    public RiskTier RiskTier { get; }                           // #42 — computed from roll
    public int XpEarned { get; }                                // #48 — XP this turn

    public TurnResult(
        RollResult roll,
        string deliveredMessage,
        string opponentMessage,
        string? narrativeBeat,
        int interestDelta,
        GameStateSnapshot stateAfter,
        bool isGameOver,
        GameOutcome? outcome,
        // New params with defaults
        IReadOnlyList<string>? shadowGrowthEvents = null,
        string? comboTriggered = null,
        int callbackBonusApplied = 0,
        int tellReadBonus = 0,
        string? tellReadMessage = null,
        RiskTier riskTier = RiskTier.Safe,
        int xpEarned = 0)
    {
        // ... assign all
        ShadowGrowthEvents = shadowGrowthEvents ?? Array.Empty<string>();
    }
}
```

---

## Behavioural Contract
- All new fields have safe defaults (null, 0, empty list, RiskTier.Safe)
- Existing TurnResult construction sites compile without changes
- Feature issues (#42, #44, #46, #47, #48, #50) will populate their respective fields when implemented
- `RiskTier` is derived from `RollResult` — the computation is: `need = DC - (statMod + levelBonus)`, then: ≤5→Safe, 6–10→Medium, 11–15→Hard, ≥16→Bold

## Dependencies
None (prerequisite — goes first, or parallel with #63)

## Consumers
#42 (RiskTier field), #44 (ShadowGrowthEvents), #46 (ComboTriggered), #47 (CallbackBonusApplied), #48 (XpEarned), #50 (TellReadBonus, TellReadMessage)

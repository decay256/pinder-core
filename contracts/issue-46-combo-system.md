# Contract: Issue #46 — Combo System

## Component
`Pinder.Core.Conversation.ComboTracker` (new)
`Pinder.Core.Conversation.GameSession` (modified — wire combo detection)

## Maturity
Prototype

---

## ComboTracker

**File**: `src/Pinder.Core/Conversation/ComboTracker.cs`

```csharp
public sealed class ComboTracker
{
    // Circular buffer of last 3 stats played
    private readonly StatType?[] _recentStats = new StatType?[3];
    private readonly bool[] _recentSuccess = new bool[3];
    private int _count;

    /// <summary>
    /// Record a stat play and check for combo completion.
    /// Returns (comboName, bonusInterest) if a combo completed, or (null, 0).
    /// </summary>
    public (string? ComboName, int Bonus) RecordAndCheck(StatType stat, bool wasSuccess)
    {
        // Shift buffer, add new entry
        // Check combo table
        // Return result
    }

    /// <summary>
    /// True if "The Triple" was triggered last turn (3 different stats in 3 turns).
    /// Grants +1 to ALL rolls next turn via externalBonus.
    /// </summary>
    public bool TripleActiveNextTurn { get; private set; }
}
```

## Combo Table

| Combo | Sequence | Bonus | Condition |
|-------|----------|-------|-----------|
| The Setup | Wit → Charm | +1 | Charm succeeds |
| The Reveal | Charm → Honesty | +1 | Honesty succeeds |
| The Read | SA → Honesty | +1 | Honesty succeeds |
| The Pivot | Honesty → Chaos | +1 | Chaos succeeds |
| The Recovery | Any fail → SA | +2 | SA succeeds |
| The Escalation | Chaos → Rizz | +1 | Rizz succeeds |
| The Disarm | Wit → Honesty | +1 | Honesty succeeds |
| The Triple | 3 different stats in 3 turns | +1 to ALL rolls next turn | All 3 succeed |

**Combo detection**: Combos only trigger when the completing stat SUCCEEDS. The preceding stat(s) can be success or fail, EXCEPT The Recovery (requires a fail before SA).

**The Triple**: Special — doesn't give an interest bonus directly. Instead, it sets a flag that adds +1 as `externalBonus` to the next turn's roll (any stat). Uses `RollEngine.Resolve(..., externalBonus: 1)`.

---

## Integration into GameSession

In `ResolveTurnAsync`, after the roll:
```csharp
// Combo detection
var (comboName, comboBonus) = _comboTracker.RecordAndCheck(chosenOption.Stat, rollResult.IsSuccess);
if (comboName != null && rollResult.IsSuccess)
{
    interestDelta += comboBonus;
}
```

Before the roll (for Triple bonus):
```csharp
int externalBonus = 0;
if (_comboTracker.TripleActiveNextTurn)
    externalBonus += 1;
// ... also add callback bonus, tell bonus
```

---

## Behavioural Contract
- ComboTracker is per-session, owned by GameSession
- Combos only trigger on success of the completing stat
- Multiple combos cannot trigger on the same turn (first match wins)
- The Triple bonus lasts one turn, then resets
- Combo interest bonus is added AFTER SuccessScale + RiskTier bonus + momentum
- `TurnResult.ComboTriggered` is set to the combo name (or null)

## Dependencies
- #78 (TurnResult.ComboTriggered field)
- #43 or parallel (RollEngine externalBonus param, from ADR-2)

## Consumers
- GameSession

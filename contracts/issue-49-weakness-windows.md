# Contract: Issue #49 — Weakness Windows

## Component
`Pinder.Core.Conversation.GameSession` (modified — apply DC reduction from weakness)

## Maturity
Prototype

---

## How Weakness Windows Work

1. After each opponent response, `OpponentResponse.DetectedWeakness` may contain a `WeaknessWindow`
2. GameSession stores it: `_activeWeakness = opponentResponse.DetectedWeakness`
3. On the NEXT turn's roll, if the chosen option's stat matches the weakness's `DefendingStat`:
   - Apply DC reduction: pass `fixedDc: defender.GetDefenceDC(stat) - weakness.DcReduction` to RollEngine
   - Or equivalently, adjust the computed DC before passing to RollEngine
4. After the roll, clear the weakness: `_activeWeakness = null` (one-turn window)

## Crack Triggers → DC Reduction (defined by LLM)

| Opponent behavior | Defending stat | DC reduction |
|-------------------|---------------|-------------|
| Contradicts themselves | Honesty | -2 |
| Laughs genuinely | Charm | -2 |
| Shares something personal | SA | -3 |
| Gets flustered | Wit | -2 |
| Asks personal question | Honesty | -2 |
| Makes risky joke | Chaos | -2 |

**Note**: The LLM adapter detects the crack and returns the `WeaknessWindow`. The engine just applies the DC reduction.

---

## Integration

In `ResolveTurnAsync`, before calling `RollEngine.Resolve`:

```csharp
int? dcOverride = null;
if (_activeWeakness != null && _activeWeakness.DefendingStat == chosenOption.Stat)
{
    int normalDc = _opponent.Stats.GetDefenceDC(chosenOption.Stat);
    dcOverride = normalDc - _activeWeakness.DcReduction;
}
// After roll:
_activeWeakness = null;  // consumed for this turn
```

The UI shows 🔓 on options that match the active weakness stat.

---

## Behavioural Contract
- Weakness windows last exactly 1 turn after the opponent's response
- DC reduction is subtracted from the normal DC
- If the player picks a different stat, the weakness is wasted
- `WeaknessWindow` comes from `OpponentResponse.DetectedWeakness` — set by LLM adapter
- Engine doesn't detect cracks — it just applies the DC reduction it's given

## Dependencies
- #63 (OpponentResponse.DetectedWeakness, WeaknessWindow type)

## Consumers
- GameSession

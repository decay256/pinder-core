# Contract: Issue #49 — Weakness Windows (§15 DC Reduction)

## Component
`Pinder.Core.Conversation.GameSession` (extend)

## Depends on
- #139 Wave 0: `RollEngine.Resolve(dcAdjustment)`

## Maturity: Prototype

---

## Mechanism

1. `ILlmAdapter.GetOpponentResponseAsync()` returns `OpponentResponse` with optional `WeaknessWindow? WeaknessWindow`
2. GameSession stores `_activeWeakness` (from most recent opponent response)
3. On next `ResolveTurnAsync()`: if chosen option's stat's defence pairing matches `_activeWeakness.DefendingStat`:
   - `dcAdjustment = _activeWeakness.DcReduction` (positive value = DC lowered)
4. Passed to `RollEngine.Resolve(dcAdjustment:)`
5. After one turn, `_activeWeakness` is cleared (used or not)

## WeaknessWindow (already exists)

```csharp
public sealed class WeaknessWindow
{
    public StatType DefendingStat { get; }    // the stat whose DC is reduced
    public int DcReduction { get; }           // 2 or 3
}
```

## Crack Trigger Table

| Opponent Behaviour | Defending Stat | DC Reduction |
|---|---|---|
| Contradicts themselves | Honesty | −2 |
| Laughs genuinely | Charm | −2 |
| Shares something personal | SelfAwareness | −3 |
| Gets flustered | Wit | −2 |
| Asks personal question | Honesty | −2 |
| Makes risky joke | Chaos | −2 |

## Behavioral Invariants
- Weakness window is one-turn-only: expires after one turn whether used or not
- DC reduction is applied by checking if the chosen stat attacks via the weakened defence stat
- The match logic: `StatBlock.DefenceTable[chosenOption.Stat] == _activeWeakness.DefendingStat`

## Dependencies
- `OpponentResponse.WeaknessWindow` (already exists)
- `RollEngine.Resolve(dcAdjustment)` (#139)
- `StatBlock.DefenceTable` (already exists)

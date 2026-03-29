# Contract: Issue #49 — Weakness Windows (§15)

## Component
Weakness window detection, storage, and DC reduction in `GameSession`

## Dependencies
- #139 Wave 0: `RollEngine.Resolve(dcAdjustment)` for flowing DC reduction into roll

---

## Weakness Window (already defined)

`WeaknessWindow` class already exists in `Conversation/WeaknessWindow.cs`:
```csharp
public sealed class WeaknessWindow
{
    public StatType DefendingStat { get; }
    public int DcReduction { get; }
}
```

---

## Data Flow

1. **Previous turn**: `ILlmAdapter.GetOpponentResponseAsync()` returns `OpponentResponse` with `WeaknessWindow?` (field already exists)
2. **GameSession** stores `_activeWeaknessWindow` from previous turn's opponent response
3. **StartTurnAsync**: For each dialogue option, check if `option.Stat` matches `_activeWeaknessWindow.DefendingStat` → set `DialogueOption.HasWeaknessWindow = true` (need to add this field)
4. **ResolveTurnAsync**: If chosen option stat matches active window, pass `dcAdjustment = window.DcReduction` to `RollEngine.Resolve()`
5. **After resolution**: Clear `_activeWeaknessWindow` (one-turn only)
6. **Store new window**: After this turn's opponent response, store any new `WeaknessWindow` for next turn

---

## Crack → DC Reduction Mapping

| Opponent behavior | Defending stat | DC reduction |
|---|---|---|
| Contradicts themselves | Honesty | -2 |
| Laughs genuinely | Charm | -2 |
| Shares something personal | SA | -3 |
| Gets flustered / responds too fast | Wit | -2 |
| Asks personal question | Honesty | -2 |
| Makes a risky joke | Chaos | -2 |

Detection is done by the LLM — `OpponentResponse.WeaknessWindow` carries the result. GameSession does not detect cracks; it only applies the DC reduction.

---

## DialogueOption Extension

Add property to `DialogueOption`:
```csharp
public bool HasWeaknessWindow { get; }
```

Constructor gains `bool hasWeaknessWindow = false` (backward-compatible default).

---

## Behavioral Invariants
- Window lasts exactly one turn (the turn after the crack was detected)
- DC reduction flows through `dcAdjustment` param, not post-hoc
- The displayed DC on options already reflects the reduction (GameSession computes adjusted DC for display)
- Only one window can be active at a time (latest overwrites)

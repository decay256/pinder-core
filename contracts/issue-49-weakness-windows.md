# Contract: Issue #49 — Weakness Windows

## Component
Weakness window storage and DC reduction in `GameSession`

## Dependencies
- #130 (Wave 0 — for GameSessionConfig, though weakness windows only need existing types)
- `WeaknessWindow` type already exists in `Conversation/WeaknessWindow.cs`
- `OpponentResponse.WeaknessWindow` field already exists

## Files modified
- `Conversation/GameSession.cs` — store weakness, apply DC reduction
- `Conversation/DialogueOption.cs` — add `HasWeaknessWindow` property

## Interface

### DialogueOption addition

```csharp
/// <summary>Whether this option benefits from a weakness window (DC reduced). UI shows 🔓.</summary>
public bool HasWeaknessWindow { get; }
```

Add to constructor as optional parameter: `bool hasWeaknessWindow = false`

### GameSession changes

1. Add field: `private WeaknessWindow? _activeWeakness;`
2. In `ResolveTurnAsync` (end): after getting opponent response, store `opponentResponse.WeaknessWindow` as `_activeWeakness`
3. In `StartTurnAsync`:
   - If `_activeWeakness != null`, annotate the matching `DialogueOption` with `HasWeaknessWindow = true`
   - The DC reduction is applied via the roll: reduce the effective DC by `_activeWeakness.DcReduction` when the chosen option's stat matches the weakness stat
4. In `ResolveTurnAsync` (start):
   - If `_activeWeakness != null` and chosen option stat matches weakness stat:
     - Reduce the DC for this roll by `_activeWeakness.DcReduction`
     - **Implementation**: Pass a `dcAdjustment` to RollEngine? No — RollEngine computes DC from defender stats. Instead, apply the reduction **after** RollEngine by adjusting the effective DC. **Better**: Create a temporary `StatBlock` wrapper that reduces the defender's stat. **Simplest for prototype**: Post-adjust — if the weakness matches and the roll failed, re-check if `total >= (dc - reduction)` and override to success.
     - **Decision**: For prototype, compute the weakness-adjusted DC separately and pass it to the `RollResult` check. The cleanest approach: modify `RollEngine.Resolve` to accept an optional `dcAdjustment` parameter (default 0). This avoids post-hoc correction.
5. Clear `_activeWeakness` after the turn it was applied (one-turn window)

### RollEngine.Resolve — dcAdjustment parameter (alternative to modifying defender)

```csharp
public static RollResult Resolve(
    ...,
    int dcAdjustment = 0  // NEW — subtracted from computed DC
);
```

**Wait**: This adds yet another parameter to Resolve. For prototype, it's acceptable. The DC becomes `defender.GetDefenceDC(stat) + dcAdjustment` (negative dcAdjustment reduces DC).

**Revised approach**: Actually, the simplest is for GameSession to compute the adjusted DC and use `ResolveFixedDC` when a weakness is active. But that loses the trap-on-stat mechanics that come from the full Resolve. 

**Final decision for prototype**: Add `int dcAdjustment = 0` to `RollEngine.Resolve()`. Applied as: `int dc = defender.GetDefenceDC(stat) + dcAdjustment;`. Since weakness gives -2/-3, pass `dcAdjustment = -2` when weakness matches.

## Behavioral contracts
- Weakness lasts exactly one turn (the turn after the opponent's cracking message)
- Only one weakness window active at a time (latest overwrites)
- DC reduction only applies if chosen stat matches `WeaknessWindow.DefendingStat`
- If no stat matches, window expires unused
- The UI's displayed DC already includes the reduction (via HasWeaknessWindow flag)

## Consumers
GameSession

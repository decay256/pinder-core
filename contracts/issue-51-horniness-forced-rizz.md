# Contract: Issue #51 — Horniness-Forced Rizz Option

## Component
Post-processing logic in `GameSession.StartTurnAsync`

## Maturity
Prototype

---

## Behavioral Contract

When Horniness is high, Rizz options are forced into the dialogue option list.

### Horniness Level Computation

```
horninessLevel = sessionShadowTracker.GetEffectiveShadow(ShadowStatType.Horniness)
               + gameClock.GetHorninessModifier()
```

Computed each turn in `StartTurnAsync`. If no `GameSessionConfig` / no clock / no shadow tracker, horniness level = 0 (no forced Rizz).

### Threshold Effects

| Horniness Level | Effect |
|---|---|
| < 6 | No effect |
| 6–11 | If no Rizz option in the 4 returned: replace lowest-priority option with a Rizz option. Mark `IsHorninessForced = true`. |
| 12–17 | One option is ALWAYS Rizz (replace if needed). Mark `IsHorninessForced = true`. |
| ≥ 18 | ALL options become Rizz. All marked `IsHorninessForced = true`. |

### "Lowest-priority" Definition

For prototype: the last option in the array (index 3). No priority ranking needed.

### DialogueOption Addition

```csharp
// Add to DialogueOption:
public bool IsHorninessForced { get; }

public DialogueOption(
    StatType stat,
    string intendedText,
    int? callbackTurnNumber = null,
    string? comboName = null,
    bool hasTellBonus = false,
    bool hasWeaknessWindow = false,
    bool isHorninessForced = false);  // NEW
```

### Integration with DialogueContext

`DialogueContext` already has `HorninessLevel` and `RequiresRizzOption` fields.

In `StartTurnAsync`:
1. Compute `horninessLevel`.
2. Set `DialogueContext.HorninessLevel = horninessLevel`.
3. Set `DialogueContext.RequiresRizzOption = horninessLevel >= 6`.
4. Call `ILlmAdapter.GetDialogueOptionsAsync(context)`.
5. Post-process returned options:
   - At ≥6: if no Rizz option, replace last option with `new DialogueOption(StatType.Rizz, "🔥 ...", isHorninessForced: true)`.
   - At ≥12: ensure at least one Rizz option exists.
   - At ≥18: replace all non-Rizz options.

At ≥18, the LLM should already return all Rizz options (because `RequiresRizzOption` is set), but post-processing enforces it.

### Rizz Option Generation for Forced Replacement

When replacing an option with a forced Rizz option:
- If the LLM already returned a Rizz option, duplicate it (or request an additional one — for prototype, duplicate is acceptable).
- Use text prefix "🔥" to mark it visually.
- Set `IsHorninessForced = true`.

## Dependencies
- `SessionShadowTracker` (#139 Wave 0)
- `IGameClock.GetHorninessModifier()` (#54)
- `ShadowStatType.Horniness`
- `DialogueContext.HorninessLevel`, `RequiresRizzOption` (already exist)

## Consumers
- `GameSession.StartTurnAsync` (post-processing logic)
- UI host (reads `IsHorninessForced` to show 🔥 marker)

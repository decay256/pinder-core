# Contract: Issue #51 — Horniness-Forced Rizz (§15)

## Component
Horniness level computation + Rizz option enforcement in `GameSession.StartTurnAsync`

## Dependencies
- #45: Shadow thresholds (Horniness shadow stat value)
- #54: `IGameClock.GetHorninessModifier()` (time-of-day component)
- #139 Wave 0: `SessionShadowTracker`, `GameSessionConfig`

---

## Horniness Level Computation

```
horninessLevel = sessionShadowTracker.GetEffectiveShadow(ShadowStatType.Horniness)
                + gameClock.GetHorninessModifier()
```

If `sessionShadowTracker` is null → use `_player.Stats.GetShadow(ShadowStatType.Horniness)`.
If `gameClock` is null → modifier = 0.

---

## Threshold Effects on Options

| Horniness Level | Effect |
|---|---|
| < 6 | No effect |
| 6–11 | At least one Rizz option must be present. If LLM didn't provide one, replace lowest-priority option. |
| 12–17 | One option is **always** forced Rizz, marked `IsHorninessForced = true`. |
| ≥ 18 | **ALL** options become Rizz. |

---

## Implementation in GameSession.StartTurnAsync

1. Compute `horninessLevel` after receiving LLM options
2. Set `DialogueContext.HorninessLevel` and `DialogueContext.RequiresRizzOption` before calling LLM
3. After receiving options from LLM:
   - If level ≥ 18: replace all options with Rizz stat (re-request from LLM or mutate stat)
   - If level ≥ 12: ensure at least one option is Rizz and marked `IsHorninessForced`
   - If level ≥ 6: ensure at least one option is Rizz (if none present, replace last option)
4. For Rizz replacement: create `new DialogueOption(StatType.Rizz, originalText, isHorninessForced: true)`

---

## DialogueOption Extension

Add property:
```csharp
public bool IsHorninessForced { get; }
```

Constructor gains `bool isHorninessForced = false` (backward-compatible).

---

## Behavioral Invariants
- Horniness level is computed EACH TURN (not once at session start) — time-of-day changes
- Forced Rizz options use Rizz stat for the roll regardless of original stat
- The 🔥 UI marker is set via `IsHorninessForced = true`
- At level ≥ 18, the player has NO non-Rizz choices
- Horniness forced options DO still get normal roll resolution (not auto-success/fail)

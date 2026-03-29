# Contract: Issue #51 — Horniness-Forced Rizz (§15 🔥 Mechanic)

## Component
`Pinder.Core.Conversation.GameSession` (extend)

## Depends on
- #45: Shadow thresholds (Horniness T2/T3 effects)
- #54: GameClock (time-of-day Horniness modifier)
- #139: `SessionShadowTracker`, `IGameClock`, `GameSessionConfig`

## Maturity: Prototype

---

## Horniness Calculation (at session start)

```
horninessBase   = dice.Roll(10)                                // 1d10 → 1–10
timeModifier    = gameClock?.GetHorninessModifier() ?? 0       // -2/0/+1/+3/+5
shadowHorniness = playerShadows?.GetEffectiveShadow(Horniness) ?? player.Stats.GetShadow(Horniness)
horniness       = Math.Max(0, horninessBase + timeModifier + shadowHorniness)
```

Computed once in GameSession constructor. Stored as `private readonly int _horniness`. Does NOT change during conversation.

## Forced Rizz Rules

| Horniness Level | Effect |
|---|---|
| < 6 | No effect |
| 6–11 | At least 1 of 4 options must be Rizz. If LLM returned none, replace the last option. Marked with 🔥. |
| 12–17 | Exactly 1 option is always Rizz (forced). Others can be anything. |
| ≥ 18 | ALL options become Rizz. |

Applied in `StartTurnAsync()` after receiving LLM options and before returning `TurnStart`.

## Option Replacement Logic

When forcing Rizz options:
1. Count existing Rizz options in the LLM response
2. If count < required: replace non-Rizz options from the end of the array
3. Replacement option: keep original `IntendedText` but change `Stat` to `StatType.Rizz`
4. Mark replaced options with `IsForced = true` (or similar — for UI 🔥 display)

**Note:** `DialogueOption` may need an `IsForced` bool property added. If not feasible without breaking changes, the 🔥 indicator can be inferred from `Stat == Rizz` when `_horniness >= 6`.

## Dependencies
- `IGameClock.GetHorninessModifier()` (#54)
- `SessionShadowTracker.GetEffectiveShadow(Horniness)` (#139)
- `IDiceRoller.Roll(10)` (already exists)

## Consumers
- `TurnStart.Options` (modified options returned to host)

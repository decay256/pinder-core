# Contract: Issue #51 — Horniness-Forced Rizz Option

## Component
`Pinder.Core.Conversation.GameSession` (modified — option injection in StartTurnAsync)

## Maturity
Prototype

---

## Mechanic

After receiving options from `ILlmAdapter.GetDialogueOptionsAsync`:

1. Compute effective Horniness = `SessionShadowTracker.GetEffectiveShadow(ShadowStatType.Horniness)`
   - Plus time-of-day modifier from GameClock if available

2. Apply thresholds:

| Horniness Level | Effect |
|----------------|--------|
| < 6 | No modification |
| ≥ 6 | If no Rizz option exists, replace lowest-priority option with a Rizz option |
| ≥ 12 | Ensure at least one option is always Rizz (replace if needed) |
| ≥ 18 | ALL options become Rizz |

### How to get Rizz options

For thresholds 1 and 2: Set `DialogueContext.RequiresRizzOption = true` and `DialogueContext.HorninessLevel = effectiveHorniness`. The LLM adapter should return at least one Rizz option when `RequiresRizzOption` is true.

If the LLM adapter returns no Rizz option despite the flag (defensive), GameSession post-processes:
- Find the option with the lowest-priority stat (implementation-defined — e.g., last in array)
- Replace it with a hardcoded Rizz option: `new DialogueOption(StatType.Rizz, "🔥 [Horniness-forced option]")`

For threshold 3 (≥ 18): Replace ALL options:
```csharp
for (int i = 0; i < options.Length; i++)
    options[i] = new DialogueOption(StatType.Rizz, options[i].IntendedText);
```
(Keep the text but force the stat to Rizz.)

### Horniness source (resolves VC-74)

Horniness for this mechanic = `SessionShadowTracker.GetEffectiveShadow(ShadowStatType.Horniness)`.

Per rules: Horniness is rolled fresh each conversation (1d10) as the initial shadow value. That rolling happens at session construction, NOT in this issue. This issue just reads the effective value.

---

## Behavioural Contract
- Options are modified in-place after LLM returns them, before returning TurnStart to host
- 🔥 marker on Rizz options is cosmetic — the host/UI reads `option.Stat == StatType.Rizz` to display the icon
- At Horniness ≥ 18, the player has NO choice — all 4 options use Rizz stat
- Horniness grows via shadow growth (#44) and time-of-day (#54)

## Dependencies
- #44 (SessionShadowTracker for Horniness value)
- #54 (GameClock for time-of-day horniness modifier — optional, skip if no clock)
- #63 (DialogueContext.HorninessLevel, RequiresRizzOption fields)

## Consumers
- GameSession.StartTurnAsync (option post-processing)

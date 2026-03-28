# Contract: Issue #51 — Horniness-Forced Rizz Option

## Component
Option post-processing in `GameSession.StartTurnAsync`

## Dependencies
- #45 (shadow thresholds — Horniness shadow value)
- #54 (IGameClock.GetHorninessModifier())
- #130 (SessionShadowTracker)

## Files modified
- `Conversation/GameSession.cs` — post-process options in StartTurnAsync

## Interface

No new types needed. Uses existing:
- `SessionShadowTracker.GetEffectiveShadow(ShadowStatType.Horniness)`
- `IGameClock.GetHorninessModifier()`
- `DialogueOption` constructor (with `IsHorninessForced` — needs adding)

### DialogueOption addition

```csharp
/// <summary>Whether this option was forced by the Horniness mechanic. UI shows 🔥.</summary>
public bool IsHorninessForced { get; }
```

Add to constructor: `bool isHorninessForced = false`

### Horniness level computation

```csharp
int horninessLevel = _shadowTracker?.GetEffectiveShadow(ShadowStatType.Horniness) ?? 0;
if (_gameClock != null)
    horninessLevel += _gameClock.GetHorninessModifier();
```

### Option post-processing (in StartTurnAsync after LLM returns options)

```
if horninessLevel >= 18:
    Replace ALL options with Rizz options (request from LLM via RequiresRizzOption flag,
    or post-process: set all option stats to Rizz, mark IsHorninessForced)
    
elif horninessLevel >= 12:
    Ensure at least 1 option is Rizz + IsHorninessForced.
    If no Rizz option exists, replace the last option.
    
elif horninessLevel >= 6:
    If no Rizz option in the 4 returned, replace lowest-priority (last) option
    with a Rizz option. Mark IsHorninessForced.
```

**"Replace with Rizz"**: For prototype, GameSession asks the LLM for Rizz options by setting `DialogueContext.RequiresRizzOption = true` and `DialogueContext.HorninessLevel = horninessLevel`. The LLM is expected to include Rizz options. If it doesn't, GameSession forces it by replacing options with `new DialogueOption(StatType.Rizz, "[Horniness takes over]", isHorninessForced: true)`.

### Integration order in StartTurnAsync
1. Compute horniness level
2. Set `DialogueContext.HorninessLevel` and `RequiresRizzOption`
3. Get options from LLM
4. Post-process: annotate tells, weakness windows, combos
5. Post-process: horniness forcing (replace options as needed)
6. Return TurnStart

## Behavioral contracts
- Horniness level = shadow stat + time-of-day modifier (NO dice roll)
- Horniness < 6: no effect
- At least one option must always exist after all post-processing
- IsHorninessForced is a display hint for the UI (🔥 icon)
- The forced Rizz option uses StatType.Rizz for the roll — the full roll mechanics apply normally

## Consumers
GameSession (internal), UI (reads IsHorninessForced)

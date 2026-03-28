# Contract: Issue #50 — Tells

## Component
Tell storage and roll bonus in `GameSession`

## Dependencies
- #130 (`externalBonus` on RollEngine.Resolve)
- `Tell` type already exists in `Conversation/Tell.cs`
- `OpponentResponse.DetectedTell` field already exists
- `DialogueOption.HasTellBonus` field already exists
- `TurnResult.TellReadBonus` and `TurnResult.TellReadMessage` fields already exist

## Files modified
- `Conversation/GameSession.cs` — store tell, apply bonus

## Interface

### GameSession changes

1. Add field: `private Tell? _activeTell;`
2. In `ResolveTurnAsync` (end): after opponent response, store `opponentResponse.DetectedTell` as `_activeTell`
3. In `StartTurnAsync`:
   - If `_activeTell != null`, for each DialogueOption where `option.Stat == _activeTell.Stat`, set `HasTellBonus = true`
4. In `ResolveTurnAsync` (start):
   - If `_activeTell != null` and chosen option stat matches `_activeTell.Stat`:
     - Add +2 to `externalBonus` parameter on `RollEngine.Resolve`
     - Set `tellReadBonus = 2` and `tellReadMessage = "📖 You read the moment. +2 bonus."`
   - Else: `tellReadBonus = 0`, `tellReadMessage = null`
5. Clear `_activeTell` after the turn (one-turn window — same as weakness)

### DialogueOption.HasTellBonus

Already exists as a constructor parameter (`bool hasTellBonus = false`). GameSession sets it when building options from LLM output.

**Problem**: GameSession gets options from `ILlmAdapter.GetDialogueOptionsAsync()` which returns `DialogueOption[]`. The LLM adapter constructs these objects. GameSession cannot set `HasTellBonus` on them because properties are read-only.

**Solution for prototype**: GameSession wraps/replaces the returned options. After receiving options from LLM, GameSession creates new `DialogueOption` objects with the same data plus `HasTellBonus = true` for matching stats.

```csharp
// In StartTurnAsync, after getting options from LLM:
if (_activeTell != null)
{
    for (int i = 0; i < options.Length; i++)
    {
        var o = options[i];
        if (o.Stat == _activeTell.Stat)
        {
            options[i] = new DialogueOption(
                o.Stat, o.IntendedText, o.CallbackTurnNumber, o.ComboName,
                hasTellBonus: true);
        }
    }
}
```

Similarly for `HasWeaknessWindow` and `ComboName` — GameSession reconstructs options with additional metadata after LLM returns them.

## Behavioral contracts
- Tell bonus is +2 to the **d20 roll** (via externalBonus), NOT interest delta
- Bonus is hidden from UI display but reflected in `RollResult.FinalTotal` and `IsSuccess`
- Tell lasts exactly one turn
- Only one tell active at a time
- If player doesn't use the tell stat, it expires unused
- `TellReadBonus` on TurnResult is 0 or 2 (never other values)

## Consumers
GameSession

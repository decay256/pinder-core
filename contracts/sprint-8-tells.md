# Contract: Issue #50 — Tells (§15 Hidden Roll Bonus)

## Component
`Pinder.Core.Conversation.GameSession` (extend)

## Depends on
- #139 Wave 0: `RollEngine.Resolve(externalBonus)`

## Maturity: Prototype

---

## Mechanism

1. `ILlmAdapter.GetOpponentResponseAsync()` returns `OpponentResponse` with optional `Tell? DetectedTell`
2. GameSession stores `_activeTell` (the Tell from the most recent opponent response)
3. On next `ResolveTurnAsync()`: if `chosenOption.HasTellBonus` or `chosenOption.Stat == _activeTell.Stat`:
   - Tell bonus = +2
   - Summed into `externalBonus` with callback + triple combo
4. After use (or after one turn passes without use), `_activeTell` is cleared

## Tell Types (from Tell.cs — already exists)

```csharp
public sealed class Tell
{
    public StatType Stat { get; }
    public string Description { get; }
}
```

## TurnResult Fields (already exist)
- `TellReadBonus` (int) — 0 or +2
- `TellReadMessage` (string?) — description of the tell that was read

## Behavioral Invariants
- Tell bonus is **hidden** — not shown in success % preview
- Tell is one-shot: consumed on the turn it's used, or expires if not used
- +2 is a constant (not variable per tell type)

## Dependencies
- `OpponentResponse.DetectedTell` (already exists)
- `RollEngine.Resolve(externalBonus)` (#139)

## Consumers
- `TurnResult.TellReadBonus`, `TurnResult.TellReadMessage`

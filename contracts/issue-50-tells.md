# Contract: Issue #50 — Tell Detection and Bonus

## Component
`Pinder.Core.Conversation.GameSession` (modified — apply tell bonus)

## Maturity
Prototype

---

## How Tells Work

1. After each opponent response, `OpponentResponse.DetectedTell` may contain a `Tell`
2. GameSession stores it: `_activeTell = opponentResponse.DetectedTell`
3. On the NEXT turn's roll, if the chosen option's stat matches `_activeTell.Stat`:
   - Add +2 to `externalBonus` for `RollEngine.Resolve`
   - This bonus is **hidden** — not shown in option probability
4. After the roll, clear: `_activeTell = null`
5. If the tell was read (stat matched):
   - Set `TurnResult.TellReadBonus = 2`
   - Set `TurnResult.TellReadMessage = "📖 You read the moment. +2 bonus."`
   - Also flag on `DialogueOption.HasTellBonus = true` (already exists on the type)

---

## Integration

In `ResolveTurnAsync`, before calling `RollEngine.Resolve`:

```csharp
int tellBonus = 0;
if (_activeTell != null && _activeTell.Stat == chosenOption.Stat)
{
    tellBonus = 2;
    externalBonus += tellBonus;
}
// After roll:
_activeTell = null;  // consumed
```

---

## Behavioural Contract
- Tell bonus is +2 to the roll (not to interest)
- Hidden from pre-roll probability display
- Revealed post-roll via TurnResult.TellReadMessage
- Lasts exactly 1 turn
- `Tell` comes from `OpponentResponse.DetectedTell` — set by LLM adapter
- Engine doesn't detect tells — it applies the bonus when stat matches

## Dependencies
- #63 (OpponentResponse.DetectedTell, Tell type)
- #78 (TurnResult.TellReadBonus, TellReadMessage)
- #43 (RollEngine externalBonus parameter)

## Consumers
- GameSession

# Contract: Issue #47 — Callback Bonus

## Component
`Pinder.Core.Conversation.GameSession` (modified — callback detection and bonus application)

## Maturity
Prototype

---

## How Callbacks Work

1. GameSession tracks `CallbackOpportunity` objects — topics introduced in conversation
2. When building `DialogueContext`, GameSession passes the list of callback opportunities
3. The LLM adapter may tag a `DialogueOption` with `CallbackTurnNumber` (the turn the referenced topic was introduced)
4. In `ResolveTurnAsync`, if the chosen option has a `CallbackTurnNumber`, compute the hidden bonus:

```csharp
int callbackBonus = 0;
if (chosenOption.CallbackTurnNumber.HasValue)
{
    int distance = _turnNumber - chosenOption.CallbackTurnNumber.Value;
    if (distance >= 4) callbackBonus = 2;       // 4+ turns ago
    else if (distance >= 2) callbackBonus = 1;  // 2 turns ago
    // Special case: turn 0 (opener) → +3
    if (chosenOption.CallbackTurnNumber.Value == 0) callbackBonus = 3;
}
```

5. Add `callbackBonus` to `externalBonus` for `RollEngine.Resolve`

---

## Callback Bonus Table

| When introduced | Hidden roll bonus |
|-----------------|-------------------|
| 2 turns ago | +1 |
| 4+ turns ago | +2 |
| Opener (turn 0) | +3 |

**Hidden**: The bonus is NOT shown in the option's displayed probability. It's applied to the actual roll.

---

## CallbackOpportunity Tracking

GameSession maintains a `List<CallbackOpportunity>`:
- After each opponent response, extract any new topics (via OpponentResponse metadata from future LLM work — for now, just pass existing list)
- For prototype: the LLM adapter is responsible for both detecting callback opportunities in opponent messages and tagging options with callback references

---

## Behavioural Contract
- Callback bonus is added to `externalBonus` passed to RollEngine — it affects the roll, not interest directly
- `TurnResult.CallbackBonusApplied` reports the bonus used (0 if none)
- The UI does NOT show the callback bonus in the option probability (it's hidden)
- Post-roll, a message could indicate "📎 Callback bonus: +{bonus}"

## Dependencies
- #63 (DialogueContext.CallbackOpportunities field)
- #78 (TurnResult.CallbackBonusApplied field)
- #43 (RollEngine externalBonus parameter)

## Consumers
- GameSession

# Contract: Issue #47 — Callback Bonus

## Component
Topic tracking logic added to `GameSession` + new `ConversationTopic` type.

## Dependencies
- #130 (externalBonus on RollEngine — **WAIT**: re-reading #47 issue, the callback bonus is added to **interest delta**, NOT the d20 roll. So it does NOT need externalBonus on RollEngine.)

**Correction**: Per the spec `docs/specs/issue-47-spec.md`, the callback bonus is an **interest delta bonus** on success, not a roll modifier. It does NOT flow through RollEngine. It stacks with success scale + risk tier + momentum.

## Files
- `Conversation/ConversationTopic.cs` — new (already exists as stub? checking... no, `CallbackOpportunity` exists)
- `Conversation/GameSession.cs` — add topic tracking + callback bonus calculation

## Interface

### ConversationTopic (reuse existing CallbackOpportunity)

`CallbackOpportunity` already exists with `TopicKey` and `TurnIntroduced`. Use it directly.

### Callback bonus calculation (pure function, in GameSession or extracted)

```csharp
/// <summary>
/// Compute callback interest bonus from turn distance.
/// Only applied on successful rolls where the chosen option has a CallbackTurnNumber.
/// </summary>
internal static int ComputeCallbackBonus(int callbackTurnNumber, int currentTurn)
{
    if (callbackTurnNumber == 0) return 3;  // opener reference
    int distance = currentTurn - callbackTurnNumber;
    if (distance >= 4) return 2;
    if (distance >= 2) return 1;
    return 0;  // too recent, no bonus
}
```

### GameSession changes

1. Add `private readonly List<CallbackOpportunity> _topics = new List<CallbackOpportunity>();`
2. In `StartTurnAsync`: pass `_topics` as `DialogueContext.CallbackOpportunities`
3. In `ResolveTurnAsync`: if `chosenOption.CallbackTurnNumber != null` and roll is success:
   - `int callbackBonus = ComputeCallbackBonus(chosenOption.CallbackTurnNumber.Value, _turnNumber);`
   - Add `callbackBonus` to interest delta (NOT to roll externalBonus)
   - Set `TurnResult.CallbackBonusApplied = callbackBonus`
4. After opponent response: extract new topics from LLM response (the LLM flags new topics). For prototype: the LLM adapter returns topics as part of `OpponentResponse`. Alternatively, topics are seeded by the dialogue options themselves (each option that introduces a topic adds it). **Decision**: For prototype, topics are **manually tracked** — each DialogueOption can have a `TopicKey` field, and if the player picks it, the topic is added to `_topics`. The LLM is responsible for flagging callbacks via `CallbackTurnNumber`.

### Distance-to-bonus table
| Condition | Interest bonus |
|-----------|---------------|
| References opener (turn 0) | +3 |
| 4+ turns ago | +2 |
| 2–3 turns ago | +1 |
| < 2 turns ago | 0 |

## Behavioral contracts
- Bonus is **interest delta** only, NOT a roll modifier
- Bonus only on success
- Hidden from UI (not shown in displayed percentage)
- Stacks additively with success scale, risk tier, momentum, combo

## Consumers
GameSession

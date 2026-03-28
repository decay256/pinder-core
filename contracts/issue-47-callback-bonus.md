# Contract: Issue #47 — Callback Bonus

## Component
`Pinder.Core.Conversation.CallbackEvaluator` (new)

## Maturity
Prototype

---

## CallbackEvaluator (`Pinder.Core.Conversation`)

Stateless evaluator that computes the hidden interest bonus for callback references.

```csharp
namespace Pinder.Core.Conversation
{
    public static class CallbackEvaluator
    {
        /// <summary>
        /// Compute callback bonus based on how far back the referenced topic was introduced.
        /// </summary>
        /// <param name="callbackTurnNumber">Turn when the topic was introduced (from DialogueOption.CallbackTurnNumber).</param>
        /// <param name="currentTurn">Current turn number.</param>
        /// <returns>Hidden bonus: +1 (2 turns ago), +2 (4+ turns ago), +3 (turn 0 / opener).</returns>
        public static int GetBonus(int callbackTurnNumber, int currentTurn);
    }
}
```

## Bonus Table

| Topic Introduced | Distance | Bonus |
|---|---|---|
| 2 turns ago | currentTurn - callbackTurn == 2 or 3 | +1 |
| 4+ turns ago | currentTurn - callbackTurn >= 4 | +2 |
| Opener (turn 0) | callbackTurn == 0 | +3 |

**Precedence**: Opener check first (turn 0 always gets +3 regardless of distance).

## Behavioral Contract

- Bonus is applied as an **external bonus on the roll** via `RollResult.AddExternalBonus()`. It affects `FinalTotal` and thus `IsSuccess`.
- Bonus is additive with other external bonuses (tell bonus, combo next-turn bonus).
- Callback bonus is invisible to the player pre-roll (not reflected in displayed percentage).
- `DialogueOption.CallbackTurnNumber` is set by the LLM adapter when generating options.
- `GameSession` passes callback opportunities to the LLM via `DialogueContext.CallbackOpportunities`.
- `GameSession` tracks topics introduced each turn (extracted from opponent responses) in a `List<CallbackOpportunity>`.

## Dependencies
- `CallbackOpportunity` (already exists)
- `DialogueOption.CallbackTurnNumber` (already exists)
- `RollResult.AddExternalBonus()` (from #139 Wave 0 / #135)

## Consumers
- `GameSession.ResolveTurnAsync` — calls `GetBonus()` when `chosenOption.CallbackTurnNumber` is non-null

# Contract: Issue #47 — Callback Bonus (§15)

## Component
Callback distance detection + bonus computation in `GameSession`

## Dependencies
- #139 Wave 0: `RollEngine.Resolve(externalBonus)` for flowing bonus into roll

---

## Callback Bonus Table

| When topic introduced | Distance | Hidden roll bonus |
|---|---|---|
| 2 turns ago | 2 | +1 |
| 4+ turns ago | 4+ | +2 |
| Opener (turn 0) | any (first message) | +3 |

---

## Data Flow

1. **GameSession** maintains `List<CallbackOpportunity> _topics` — topics extracted from conversation
2. **LLM** signals topics: `OpponentResponse` or `DeliverMessageAsync` may return topic keys (to be added to `_topics` with current turn number)
3. **StartTurnAsync**: passes `_topics` as `DialogueContext.CallbackOpportunities` (field already exists)
4. **LLM returns**: `DialogueOption.CallbackTurnNumber` is set by the LLM when an option references a prior topic
5. **ResolveTurnAsync**: If `chosenOption.CallbackTurnNumber != null`:
   - Compute distance = `_turnNumber - callbackTurnNumber`
   - If `callbackTurnNumber == 0`: bonus = +3 (opener)
   - Else if distance >= 4: bonus = +2
   - Else if distance >= 2: bonus = +1
   - Else: bonus = 0
   - Pass bonus as part of `externalBonus` to `RollEngine.Resolve()`
6. **TurnResult.CallbackBonusApplied** = computed bonus value

---

## Topic Tracking

For prototype maturity, topic tracking is simple:
- Topics are seeded by the LLM via `OpponentResponse` (extend with `List<string>? NewTopics` if needed)
- Or manually: after each LLM call, GameSession extracts topics from the response
- For now, the LLM's `CallbackTurnNumber` on `DialogueOption` is sufficient — GameSession just computes the bonus from the distance. No validation that the topic actually existed.

---

## Behavioral Invariants
- Callback bonus is **hidden** — displayed success percentage does NOT include it
- Bonus flows through `RollEngine.Resolve(externalBonus)` so `RollResult.IsSuccess` and `MissMargin` are accurate
- Multiple bonuses (callback + tell + triple) are summed into a single `externalBonus`
- Callback bonus = 0 if `CallbackTurnNumber` is null

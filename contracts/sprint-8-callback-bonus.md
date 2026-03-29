# Contract: Issue #47 — Callback Bonus (§15)

## Component
Callback distance detection + bonus computation in `GameSession`

## Depends on
- #139 Wave 0: `RollEngine.Resolve(externalBonus)` for flowing bonus into roll

## Maturity: Prototype

---

## Callback Bonus Table

| When topic introduced | Distance | Hidden roll bonus |
|---|---|---|
| 2 turns ago | 2 | +1 |
| 4+ turns ago | 4+ | +2 |
| Opener (turn 0) | any (first message) | +3 |

---

## Data Flow

1. `DialogueOption.CallbackTurnNumber` set by LLM when option references a prior topic
2. In `ResolveTurnAsync`: if `chosenOption.CallbackTurnNumber != null`:
   - `callbackTurnNumber == 0` → bonus = +3 (opener)
   - `distance >= 4` → bonus = +2
   - `distance >= 2` → bonus = +1
   - else → bonus = 0
3. Bonus summed with tell + triple combo into single `externalBonus` param to `RollEngine.Resolve()`
4. `TurnResult.CallbackBonusApplied` = computed bonus value

## Behavioral Invariants
- Callback bonus is **hidden** — displayed success % does NOT include it
- Bonus = 0 if `CallbackTurnNumber` is null
- Multiple bonuses (callback + tell + triple) summed into single `externalBonus`

## Dependencies
- `RollEngine.Resolve(externalBonus)` (#139)
- `DialogueOption.CallbackTurnNumber` (already exists)

## Consumers
- `TurnResult.CallbackBonusApplied` (already exists on TurnResult)

# Spec: Issue #47 — Callback Bonus (§15 Callback Distance Detection)

## Overview

When a player selects a dialogue option that references a topic introduced earlier in the conversation, they receive a hidden **interest delta bonus** based on how far back that topic was introduced. This implements Rules v3.4 §15. On a successful roll, the callback bonus is added to the interest delta (alongside the base success delta, risk bonus, and momentum bonus) — it does **not** modify the d20 roll itself or affect whether the roll succeeds or fails. The bonus is invisible to the player (not reflected in displayed UI) and stacks additively with other interest bonuses (e.g. risk tier bonus from #42).

## Key Concepts

- **Conversation Topic**: A named topic (e.g. `"fear-of-commitment"`, `"cheese-obsession"`) paired with the turn number it was first introduced.
- **Callback**: A dialogue option that references a previously introduced topic. The LLM flags this by setting `DialogueOption.CallbackTurnNumber` to the turn the topic was introduced.
- **Callback Distance**: `currentTurn - CallbackTurnNumber`. Determines the bonus magnitude.
- **Hidden Bonus**: The callback bonus is added to the **interest delta** (not the d20 roll total). It increases how much interest the player gains on a successful roll, but does not affect whether the roll succeeds or fails. It is not shown to the player in UI.

## New Type: `ConversationTopic`

### Location
`Pinder.Core.Conversation.ConversationTopic`

### Signature
```csharp
namespace Pinder.Core.Conversation
{
    public sealed class ConversationTopic
    {
        public string TopicKey { get; }
        public int TurnIntroduced { get; }

        public ConversationTopic(string topicKey, int turnIntroduced);
    }
}
```

### Constraints
- **Must be a `sealed class`**, NOT a `record` (netstandard2.0 / C# 8.0 constraint).
- `TopicKey` must not be null; constructor throws `ArgumentNullException` if null.
- `TurnIntroduced` is a zero-based turn index (matching `GameSession._turnNumber`).

## New Property on `DialogueContext`: `CallbackOpportunities`

### Location
`Pinder.Core.Conversation.DialogueContext`

### Change
Add a new constructor parameter and readonly property:

```csharp
public IReadOnlyList<ConversationTopic> CallbackOpportunities { get; }
```

This list is passed to `ILlmAdapter.GetDialogueOptionsAsync()` so the LLM knows which past topics are available for callbacks. The LLM may then set `DialogueOption.CallbackTurnNumber` on options it generates that reference one of these topics.

### Constraints
- Must not be null; throw `ArgumentNullException` if null.
- May be empty (no topics introduced yet, e.g. turn 0).

## Callback Distance-to-Bonus Mapping

| Condition | Distance (turns ago) | Hidden Bonus |
|-----------|---------------------|-------------|
| Opener (turn 0) | `currentTurn - 0 = currentTurn` (any distance, but topic was from turn 0) | **+3** |
| 4+ turns ago | `currentTurn - CallbackTurnNumber >= 4` | **+2** |
| 2–3 turns ago | `currentTurn - CallbackTurnNumber >= 2` and `< 4` | **+1** |
| 1 turn ago | `currentTurn - CallbackTurnNumber == 1` | **+0** (too recent, no bonus) |
| Same turn | `currentTurn - CallbackTurnNumber == 0` | **+0** (not a callback) |

### Priority Rule
The "opener" check takes priority: if `CallbackTurnNumber == 0` AND `currentTurn >= 2`, the bonus is always **+3** regardless of distance. If `currentTurn < 2` (i.e. still on turn 0 or 1), then a reference to turn 0 follows the normal distance table (which would yield +0 or +1).

### Static Helper Method

#### Location
`Pinder.Core.Conversation.CallbackBonus` (new static class)

#### Signature
```csharp
namespace Pinder.Core.Conversation
{
    public static class CallbackBonus
    {
        /// <summary>
        /// Compute the hidden callback bonus given the current turn number
        /// and the turn the referenced topic was introduced.
        /// Returns 0 if no bonus applies.
        /// </summary>
        public static int Compute(int currentTurn, int callbackTurnNumber);
    }
}
```

#### Behavior
```
Compute(currentTurn, callbackTurnNumber):
    if callbackTurnNumber is null → return 0  (handled by caller; method takes int)
    distance = currentTurn - callbackTurnNumber
    if distance < 2 → return 0
    if callbackTurnNumber == 0 → return 3   (opener bonus)
    if distance >= 4 → return 2
    return 1                                  (distance 2 or 3)
```

## Changes to `GameSession`

### New State: Topic List

`GameSession` maintains a `List<ConversationTopic>` field (e.g. `_topics`). This list is:
- Populated from LLM-extracted topics or seeded by host/player actions.
- Passed to `DialogueContext` as `CallbackOpportunities` during `StartTurnAsync`.
- Topics are append-only within a session; they are never removed.

### `StartTurnAsync` Changes

When building the `DialogueContext`, pass `_topics` (or a read-only copy) as `CallbackOpportunities`:

```
var context = new DialogueContext(
    ...,
    callbackOpportunities: _topics.AsReadOnly()
);
```

### `ResolveTurnAsync` Changes

After computing the base interest delta (from `SuccessScale`/`FailureScale`) and before applying momentum, compute and add the callback bonus:

```
// Between steps 2 and 3 in current ResolveTurnAsync:
int callbackBonus = 0;
if (rollResult.IsSuccess && chosenOption.CallbackTurnNumber.HasValue)
{
    callbackBonus = CallbackBonus.Compute(_turnNumber, chosenOption.CallbackTurnNumber.Value);
    interestDelta += callbackBonus;
}
```

**Important**: The callback bonus applies **only on success**. A failed roll with a callback option gets no callback bonus.

### Topic Seeding

The issue states topics are "extracted by LLM or seeded by player action." For this implementation:
- `GameSession` should expose a method to add topics:
  ```csharp
  public void AddTopic(ConversationTopic topic);
  ```
- The LLM adapter or the host is responsible for calling this after each turn to register new topics.
- This keeps topic extraction out of the engine (the engine doesn't parse natural language).

## Input/Output Examples

### Example 1: Opener Callback (+3)
```
Turn 0: Topic "dad-jokes" introduced → _topics = [("dad-jokes", 0)]
Turn 1: ...
Turn 2: ...
Turn 3: Player picks option with CallbackTurnNumber = 0
  → distance = 3 - 0 = 3
  → callbackTurnNumber == 0, so opener bonus = +3
  → If roll succeeds with SuccessScale delta +1, total interestDelta = +1 + 3 = +4 (before momentum)
```

### Example 2: Mid-conversation Callback (+2)
```
Turn 1: Topic "gym-routine" introduced → _topics = [..., ("gym-routine", 1)]
Turn 5: Player picks option with CallbackTurnNumber = 1
  → distance = 5 - 1 = 4
  → distance >= 4 → bonus = +2
  → If roll succeeds with SuccessScale delta +2, total interestDelta = +2 + 2 = +4 (before momentum)
```

### Example 3: Recent Callback (+1)
```
Turn 3: Topic "pizza" introduced → _topics = [..., ("pizza", 3)]
Turn 5: Player picks option with CallbackTurnNumber = 3
  → distance = 5 - 3 = 2
  → distance >= 2 but < 4, callbackTurnNumber != 0 → bonus = +1
```

### Example 4: Too Recent (no bonus)
```
Turn 4: Topic "cats" introduced
Turn 5: Player picks option with CallbackTurnNumber = 4
  → distance = 5 - 4 = 1
  → distance < 2 → bonus = 0
```

### Example 5: Failed Roll (no bonus)
```
Turn 5: Player picks option with CallbackTurnNumber = 0
  → Roll fails
  → Callback bonus is NOT applied (only applies on success)
  → interestDelta = FailureScale value only
```

### Example 6: No Callback Option
```
Turn 5: Player picks option with CallbackTurnNumber = null
  → No callback computation performed
  → interestDelta = SuccessScale/FailureScale + momentum as normal
```

## Acceptance Criteria

### AC1: `ConversationTopic` is a `sealed class`, NOT a `record`
- The class uses `public sealed class` declaration.
- Constructor takes `string topicKey` and `int turnIntroduced`.
- Properties are get-only (no setters).
- Throws `ArgumentNullException` if `topicKey` is null.

### AC2: `CallbackOpportunities` passed in `DialogueContext`
- `DialogueContext` has a new property: `IReadOnlyList<ConversationTopic> CallbackOpportunities { get; }`.
- The constructor accepts this parameter and validates it is not null.
- `GameSession.StartTurnAsync` passes the current topic list when constructing `DialogueContext`.

### AC3: `DialogueOption.CallbackTurnNumber` respected in interest delta calculation
- `GameSession.ResolveTurnAsync` checks `chosenOption.CallbackTurnNumber`.
- If non-null and the roll is a success, computes the callback bonus via `CallbackBonus.Compute`.
- The bonus is added to `interestDelta`.

### AC4: Distance-to-bonus mapping: 2→+1, 4+→+2, opener→+3
- `CallbackBonus.Compute(currentTurn: 5, callbackTurnNumber: 3)` → `1` (distance 2)
- `CallbackBonus.Compute(currentTurn: 5, callbackTurnNumber: 1)` → `2` (distance 4)
- `CallbackBonus.Compute(currentTurn: 5, callbackTurnNumber: 0)` → `3` (opener)
- `CallbackBonus.Compute(currentTurn: 5, callbackTurnNumber: 4)` → `0` (distance 1, too recent)
- `CallbackBonus.Compute(currentTurn: 5, callbackTurnNumber: 5)` → `0` (same turn)

### AC5: Tests verify bonus applied at correct distances
- Unit tests for `CallbackBonus.Compute` covering all tiers (0, +1, +2, +3).
- Integration-style tests for `GameSession.ResolveTurnAsync` verifying that the callback bonus is reflected in `TurnResult.InterestDelta`.

### AC6: Build clean
- `dotnet build` succeeds with zero warnings/errors.
- All existing tests pass (`dotnet test`).

## Edge Cases

| Case | Expected Behavior |
|------|-------------------|
| `CallbackTurnNumber` is `null` | No callback computation; bonus = 0 |
| `CallbackTurnNumber` equals `currentTurn` (same turn) | Distance = 0; bonus = 0 |
| `CallbackTurnNumber` is 1 turn ago | Distance = 1; bonus = 0 |
| `CallbackTurnNumber` is negative | Undefined — caller should never produce this. If it occurs, `Compute` returns based on raw distance math (would yield large distance → +2, or +3 if value is 0). No special guard required at prototype maturity. |
| `currentTurn` is 0 (first turn) | No callback can have distance ≥ 2, so bonus = 0 for any `CallbackTurnNumber` |
| `currentTurn` is 1 | Max distance is 1 (from turn 0), so bonus = 0 |
| Multiple callback options in one turn | Each option independently has its own `CallbackTurnNumber`; only the chosen option's bonus applies |
| Empty `_topics` list | `CallbackOpportunities` is an empty list; LLM likely won't generate callback options, but if it does, the engine still computes the bonus from `CallbackTurnNumber` |
| Topic added multiple times | Duplicate `TopicKey` values are allowed; the earliest `TurnIntroduced` should be what the LLM references |
| Roll is a failure | Callback bonus is NOT applied regardless of `CallbackTurnNumber` |
| Nat 1 with callback option | Failure → no callback bonus |
| Nat 20 with callback option | Success → callback bonus IS applied on top of the +4 from SuccessScale |

## Error Conditions

| Condition | Error Type | Message |
|-----------|-----------|---------|
| `ConversationTopic` constructed with null `topicKey` | `ArgumentNullException` | `"topicKey"` |
| `DialogueContext` constructed with null `callbackOpportunities` | `ArgumentNullException` | `"callbackOpportunities"` |
| `AddTopic` called with null topic | `ArgumentNullException` | `"topic"` |

No other new error conditions are introduced. The callback bonus computation is pure arithmetic and cannot fail.

## Dependencies

| Dependency | Type | Status |
|-----------|------|--------|
| **#42 — Risk tier interest bonus** | Hard dependency (per issue comment) | Must be merged first. Callback bonus stacks with risk tier bonus in `ResolveTurnAsync`. |
| `DialogueOption.CallbackTurnNumber` | Existing property | Already present in codebase |
| `GameSession` | Existing class | Modified (new field, new logic in `ResolveTurnAsync` and `StartTurnAsync`) |
| `DialogueContext` | Existing class | Modified (new `CallbackOpportunities` property) |
| `ILlmAdapter.GetDialogueOptionsAsync` | Existing interface method | Signature unchanged; receives enriched `DialogueContext` |
| `SuccessScale` / `FailureScale` | Existing static classes | Unchanged |
| `RollEngine.Resolve` | Existing static method | Unchanged — callback bonus is applied in `GameSession`, not in `RollEngine` |

## Stacking Order in `ResolveTurnAsync`

After this issue and #42 are both implemented, the interest delta computation in `ResolveTurnAsync` should follow this order:

```
1. Base delta     = SuccessScale.GetInterestDelta(roll) or FailureScale.GetInterestDelta(roll)
2. Risk bonus     = (from #42) +1 for Hard, +2 for Bold — success only
3. Callback bonus = CallbackBonus.Compute(currentTurn, callbackTurnNumber) — success only
4. Momentum bonus = GetMomentumBonus(streak) — success only
───────────────────
   Total interestDelta = sum of above → InterestMeter.Apply(total)
```

All three bonus types (risk, callback, momentum) apply only on successful rolls.

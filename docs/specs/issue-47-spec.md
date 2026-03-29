# Spec: Issue #47 — Callback Bonus (§15 Callback Distance Detection)

## Overview

When a player selects a dialogue option that references a topic introduced earlier in the conversation, they receive a **hidden roll bonus** based on how far back that topic was introduced. This implements Rules v3.4 §15. The bonus flows through `RollEngine.Resolve(externalBonus)` so that `RollResult.FinalTotal`, `IsSuccess`, and `MissMargin` all reflect the bonus correctly — it is **not** a post-hoc adjustment to the interest delta. The bonus is invisible to the player (not reflected in displayed UI percentages).

## Key Concepts

- **CallbackOpportunity**: An existing sealed class (`Pinder.Core.Conversation.CallbackOpportunity`) pairing a named topic key (e.g. `"fear-of-commitment"`) with the turn number it was introduced. Already in the codebase.
- **Callback**: A dialogue option that references a previously introduced topic. The LLM flags this by setting `DialogueOption.CallbackTurnNumber` (existing `int?` property) to the turn the topic was introduced.
- **Callback Distance**: `currentTurn - CallbackTurnNumber`. Determines the bonus magnitude.
- **Hidden Roll Bonus**: The callback bonus is added to `externalBonus` in `RollEngine.Resolve()`, affecting `FinalTotal`, `IsSuccess`, and `MissMargin`. It is **not** shown to the player in the UI success percentage calculation.

## Existing Types (No Changes Needed)

### `CallbackOpportunity`

Already exists at `src/Pinder.Core/Conversation/CallbackOpportunity.cs`:

```csharp
public sealed class CallbackOpportunity
{
    public string TopicKey { get; }
    public int TurnIntroduced { get; }
    public CallbackOpportunity(string topicKey, int turnIntroduced);
}
```

> **Note**: The issue body specifies a `ConversationTopic` class. The codebase already has `CallbackOpportunity` with an identical shape. Use the existing type — do **not** create a duplicate.

### `DialogueContext.CallbackOpportunities`

Already exists as `List<CallbackOpportunity>? CallbackOpportunities` on `DialogueContext`. No changes needed.

### `DialogueOption.CallbackTurnNumber`

Already exists as `int? CallbackTurnNumber` on `DialogueOption`. No changes needed.

### `TurnResult.CallbackBonusApplied`

Already exists as `int CallbackBonusApplied` on `TurnResult` (defaults to 0). No changes needed.

## New Type: `CallbackBonus`

### Location
`src/Pinder.Core/Conversation/CallbackBonus.cs`

### Signature
```csharp
namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Computes the hidden callback roll bonus based on turn distance.
    /// Rules v3.4 §15.
    /// </summary>
    public static class CallbackBonus
    {
        /// <summary>
        /// Compute the hidden callback bonus given the current turn number
        /// and the turn the referenced topic was introduced.
        /// Returns 0 if no bonus applies.
        /// </summary>
        /// <param name="currentTurn">The current turn number (0-based).</param>
        /// <param name="callbackTurnNumber">The turn the topic was introduced (0-based).</param>
        /// <returns>0, 1, 2, or 3.</returns>
        public static int Compute(int currentTurn, int callbackTurnNumber);
    }
}
```

### Callback Distance-to-Bonus Mapping

| Condition | Rule | Hidden Roll Bonus |
|-----------|------|-------------------|
| Opener (turn 0) | `callbackTurnNumber == 0` AND `distance >= 2` | **+3** |
| 4+ turns ago | `distance >= 4` | **+2** |
| 2–3 turns ago | `distance >= 2` AND `distance < 4` | **+1** |
| 1 turn ago | `distance == 1` | **+0** (too recent) |
| Same turn | `distance == 0` | **+0** (not a callback) |

### Algorithm

```
Compute(currentTurn, callbackTurnNumber):
    distance = currentTurn - callbackTurnNumber
    if distance < 2 → return 0
    if callbackTurnNumber == 0 → return 3   // opener bonus
    if distance >= 4 → return 2
    return 1                                  // distance 2 or 3
```

### Priority Rule

The opener check (`callbackTurnNumber == 0`) takes priority over the distance-based tiers, but only when `distance >= 2`. If `currentTurn < 2`, a reference to turn 0 has distance < 2 and yields +0.

## Changes to `GameSession`

### New State: Topic List

`GameSession` maintains a `List<CallbackOpportunity>` field (e.g. `_topics`). This list is:
- Populated from LLM-extracted topics or seeded by host/player actions.
- Passed to `DialogueContext` as `CallbackOpportunities` during `StartTurnAsync`.
- Topics are append-only within a session; they are never removed.

### New Public Method: `AddTopic`

```csharp
/// <summary>
/// Register a conversation topic for callback tracking.
/// Called by the host or LLM adapter after each turn.
/// </summary>
/// <param name="topic">The topic to register. Must not be null.</param>
/// <exception cref="ArgumentNullException">If topic is null.</exception>
public void AddTopic(CallbackOpportunity topic);
```

### `StartTurnAsync` Changes

When building the `DialogueContext`, pass `_topics` as `CallbackOpportunities`:

```
var context = new DialogueContext(
    ...,
    callbackOpportunities: _topics
);
```

### `ResolveTurnAsync` Changes

After the player selects an option, compute the callback bonus and include it in `externalBonus`:

```
int callbackBonus = 0;
if (chosenOption.CallbackTurnNumber.HasValue)
{
    callbackBonus = CallbackBonus.Compute(_turnNumber, chosenOption.CallbackTurnNumber.Value);
}

// Sum with other external bonuses (tell bonus, triple combo bonus)
int totalExternalBonus = callbackBonus + tellBonus + tripleComboBonus;

// Pass to RollEngine.Resolve via externalBonus parameter
var rollResult = RollEngine.Resolve(
    stat: chosenOption.Stat,
    attacker: ...,
    defender: ...,
    attackerTraps: ...,
    level: ...,
    trapRegistry: ...,
    dice: ...,
    hasAdvantage: ...,
    hasDisadvantage: ...,
    externalBonus: totalExternalBonus
);
```

The callback bonus is applied **regardless of whether the roll succeeds or fails** — it modifies `FinalTotal` which determines success/failure. The bonus can turn a near-miss into a success.

Record `callbackBonus` in the `TurnResult`:

```
return new TurnResult(
    ...,
    callbackBonusApplied: callbackBonus,
    ...
);
```

### UI Display

The displayed success percentage in options does **NOT** include the callback bonus. It is hidden from the player. The `DialogueOption` does not expose the computed bonus — only the raw `CallbackTurnNumber`.

## Input/Output Examples

### Example 1: Opener Callback (+3)

```
Turn 0: Host calls AddTopic(new CallbackOpportunity("dad-jokes", 0))
Turn 3: LLM returns option with CallbackTurnNumber = 0, player selects it
  → CallbackBonus.Compute(3, 0)
  → distance = 3, callbackTurnNumber == 0 → return 3
  → externalBonus includes +3
  → RollEngine resolves with +3 added to FinalTotal
  → TurnResult.CallbackBonusApplied = 3
```

### Example 2: Mid-conversation Callback (+2)

```
Turn 1: Host calls AddTopic(new CallbackOpportunity("gym-routine", 1))
Turn 5: Player picks option with CallbackTurnNumber = 1
  → CallbackBonus.Compute(5, 1)
  → distance = 4, distance >= 4 → return 2
  → externalBonus includes +2
  → TurnResult.CallbackBonusApplied = 2
```

### Example 3: Recent Callback (+1)

```
Turn 3: Topic "pizza" introduced
Turn 5: Player picks option with CallbackTurnNumber = 3
  → CallbackBonus.Compute(5, 3)
  → distance = 2, distance >= 2, < 4, callbackTurnNumber != 0 → return 1
  → externalBonus includes +1
  → TurnResult.CallbackBonusApplied = 1
```

### Example 4: Too Recent (no bonus)

```
Turn 4: Topic "cats" introduced
Turn 5: Player picks option with CallbackTurnNumber = 4
  → CallbackBonus.Compute(5, 4)
  → distance = 1, distance < 2 → return 0
  → TurnResult.CallbackBonusApplied = 0
```

### Example 5: Near-miss Turned Success by Callback

```
Turn 5: Player picks option with CallbackTurnNumber = 0 (opener)
  → CallbackBonus.Compute(5, 0) = +3
  → RollEngine: d20(8) + statMod(2) + levelBonus(1) = Total 11, DC 14
  → Without callback: 11 < 14 → miss by 3 (Misfire)
  → With callback: FinalTotal = 11 + 3 = 14 ≥ 14 → SUCCESS
  → This is why the bonus MUST flow through externalBonus, not interest delta
```

### Example 6: No Callback Option

```
Turn 5: Player picks option with CallbackTurnNumber = null
  → No callback computation performed
  → externalBonus does not include any callback component
  → TurnResult.CallbackBonusApplied = 0
```

## Acceptance Criteria

### AC1: `ConversationTopic` / `CallbackOpportunity` is a `sealed class`, NOT a `record`

`CallbackOpportunity` already exists in the codebase as a sealed class with get-only properties and `ArgumentNullException` on null `topicKey`. This criterion is already satisfied. If the issue's `ConversationTopic` name is required, create a type alias or rename — but prefer the existing `CallbackOpportunity` to avoid breaking existing code.

### AC2: `CallbackOpportunities` passed in `DialogueContext`

- `DialogueContext` already has `List<CallbackOpportunity>? CallbackOpportunities`.
- `GameSession.StartTurnAsync` must populate this from its internal `_topics` list.
- Verify the list contains all topics registered via `AddTopic` up to the current turn.

### AC3: `DialogueOption.CallbackTurnNumber` respected in roll bonus calculation

- `GameSession.ResolveTurnAsync` checks `chosenOption.CallbackTurnNumber`.
- If non-null, computes the callback bonus via `CallbackBonus.Compute(_turnNumber, callbackTurnNumber)`.
- The bonus is included in the `externalBonus` parameter passed to `RollEngine.Resolve()`.

### AC4: Distance-to-bonus mapping: 2→+1, 4+→+2, opener→+3

- `CallbackBonus.Compute(5, 3)` → `1` (distance 2)
- `CallbackBonus.Compute(5, 1)` → `2` (distance 4)
- `CallbackBonus.Compute(5, 0)` → `3` (opener)
- `CallbackBonus.Compute(5, 4)` → `0` (distance 1)
- `CallbackBonus.Compute(5, 5)` → `0` (distance 0)
- `CallbackBonus.Compute(1, 0)` → `0` (distance 1, opener but too close)

### AC5: Bonus flows through `RollEngine.Resolve(externalBonus)` (NOT post-hoc)

- The callback bonus is part of the `externalBonus` parameter on `RollEngine.Resolve()`.
- `RollResult.FinalTotal` = `Total + ExternalBonus` — this is the value used for `IsSuccess` and `MissMargin`.
- The bonus can convert near-misses into successes.
- The bonus is **not** added to interest delta post-hoc.
- **Depends on Wave 0 (#139/#130)** implementing the `externalBonus` parameter on `RollEngine.Resolve`.

### AC6: Tests verify bonus applied at correct distances

- Unit tests for `CallbackBonus.Compute()` covering all tiers and boundary values.
- Integration tests for `GameSession.ResolveTurnAsync` verifying that callback bonus flows through `externalBonus` and affects `RollResult.FinalTotal`/`IsSuccess`.

### AC7: Build clean

- `dotnet build` succeeds with zero warnings/errors.
- All existing tests pass (`dotnet test`).

## Edge Cases

| Case | Expected Behavior |
|------|-------------------|
| `CallbackTurnNumber` is `null` | No callback computation; bonus = 0 |
| `CallbackTurnNumber` equals `currentTurn` (distance 0) | Bonus = 0 |
| Distance = 1 | Bonus = 0 (too recent) |
| Distance = 2 | Bonus = +1 |
| Distance = 3, non-opener | Bonus = +1 |
| Distance = 4 | Bonus = +2 |
| Distance = 100 | Bonus = +2 (capped at +2 for non-opener) |
| `callbackTurnNumber == 0`, `currentTurn == 0` | Distance 0 → Bonus = 0 |
| `callbackTurnNumber == 0`, `currentTurn == 1` | Distance 1 → Bonus = 0 |
| `callbackTurnNumber == 0`, `currentTurn == 2` | Distance 2, opener → Bonus = +3 |
| Negative `callbackTurnNumber` | Undefined input — no guard needed at prototype maturity |
| Multiple external bonuses (callback + tell + combo) | All summed into single `externalBonus` parameter |
| Empty `_topics` list | Valid; LLM gets empty `CallbackOpportunities`; any callback option still evaluated |
| Duplicate topic keys | Allowed; LLM may reference either occurrence via `CallbackTurnNumber` |

## Error Conditions

| Condition | Error Type | Message |
|-----------|-----------|---------|
| `CallbackOpportunity` constructed with null `topicKey` | `ArgumentNullException` | `"topicKey"` |
| `GameSession.AddTopic` called with null | `ArgumentNullException` | `"topic"` |

No other new error conditions. `CallbackBonus.Compute` is pure arithmetic on two ints — cannot fail.

## Dependencies

| Dependency | Type | Status | Detail |
|-----------|------|--------|--------|
| **#130 / #139 Wave 0** | Hard | Must be merged | `RollEngine.Resolve(externalBonus)` parameter |
| **#42** | Hard (per issue comment) | Must be merged | Callback bonus stacks with risk tier bonus |
| `CallbackOpportunity` | Existing type | ✅ Already in codebase | No changes needed |
| `DialogueOption.CallbackTurnNumber` | Existing property | ✅ Already in codebase | No changes needed |
| `DialogueContext.CallbackOpportunities` | Existing property | ✅ Already in codebase | No changes needed |
| `TurnResult.CallbackBonusApplied` | Existing property | ✅ Already in codebase | No changes needed |
| `RollResult.ExternalBonus` / `FinalTotal` | Existing properties | ✅ Already in codebase | Wave 0 wires them into `IsSuccess` |
| `GameSession` | Existing class | Modified | New `_topics` field, `AddTopic()`, logic in `ResolveTurnAsync`/`StartTurnAsync` |

## Stacking with Other External Bonuses

After all Sprint 7 issues are implemented, the `externalBonus` parameter on `RollEngine.Resolve` is the sum of:

```
externalBonus = callbackBonus     (this issue, #47: 0/+1/+2/+3)
              + tellBonus         (#49: +2 when tell is active)
              + tripleComboBonus  (#46: bonus from triple-stat combo)
```

All three are computed in `GameSession.ResolveTurnAsync` and summed before the roll. The interest delta stacking order (post-roll) is separate:

```
interestDelta = baseScaleDelta          (SuccessScale/FailureScale)
              + riskTierBonus           (#42: +1 Hard, +2 Bold — success only)
              + momentumBonus           (existing: streak-based — success only)
              + comboInterestBonus      (#46: combo interest bonus — success only)
```

The callback bonus does **not** appear in the interest delta — it affects the roll outcome itself.

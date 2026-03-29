# Spec: Issue #47 — Callback Bonus Implementation (§15 Callback Distance Detection)

## Overview

When a player selects a dialogue option that references a topic introduced earlier in the conversation, a **hidden roll bonus** is added to the roll total based on how far back that topic was introduced. This implements Rules v3.4 §15. The bonus flows through `RollEngine.Resolve(externalBonus)` so that `RollResult.FinalTotal`, `IsSuccess`, and `MissMargin` all reflect the bonus correctly — it is **not** a post-hoc adjustment to interest delta.

## Existing Infrastructure

The following types already exist in the codebase and are used as-is:

| Type | Location | Relevant API |
|------|----------|-------------|
| `CallbackOpportunity` | `Pinder.Core.Conversation` | `string TopicKey`, `int TurnIntroduced` — sealed class, already matches `ConversationTopic` from the issue description |
| `DialogueOption.CallbackTurnNumber` | `Pinder.Core.Conversation` | `int?` — set by LLM when option references a prior topic |
| `DialogueContext.CallbackOpportunities` | `Pinder.Core.Conversation` | `List<CallbackOpportunity>?` — already wired into the constructor |
| `TurnResult.CallbackBonusApplied` | `Pinder.Core.Conversation` | `int` — already in the TurnResult constructor (defaults to 0) |
| `RollResult.ExternalBonus` | `Pinder.Core.Rolls` | `int` — mutable property, `AddExternalBonus()` method (DEPRECATED) |

**Note on naming:** The issue describes a `ConversationTopic` class, but the codebase already has `CallbackOpportunity` which is functionally identical (sealed class, `string TopicKey`, `int TurnIntroduced`, null-checking constructor). Use `CallbackOpportunity` — do NOT create a duplicate `ConversationTopic` class.

## Dependencies

| Dependency | Issue | What it provides | Status |
|-----------|-------|-----------------|--------|
| `RollEngine.Resolve(externalBonus)` parameter | #130 (Wave 0) | The `externalBonus` optional int parameter on `RollEngine.Resolve()` that flows into `RollResult` and affects `IsSuccess` | **Must be merged first** |
| Risk tier interest bonus | #42 | `RiskTierBonus.GetInterestBonus()` in `ResolveTurnAsync` | **Must be merged first** |
| `RollResult` constructor with `externalBonus` | #130 (Wave 0) | `IsSuccess` computed from `FinalTotal` = `Total + ExternalBonus` | **Must be merged first** |

## New Type: `CallbackBonus`

### Location
`src/Pinder.Core/Conversation/CallbackBonus.cs`

### Signature
```csharp
namespace Pinder.Core.Conversation
{
    /// <summary>
    /// Pure static utility: computes the hidden callback bonus from turn distance.
    /// </summary>
    public static class CallbackBonus
    {
        /// <summary>
        /// Compute the hidden callback bonus given the current turn number
        /// and the turn the referenced topic was introduced.
        /// Returns 0 if no bonus applies (distance &lt; 2).
        /// </summary>
        /// <param name="currentTurn">The current turn number (0-based).</param>
        /// <param name="callbackTurnNumber">The turn when the topic was introduced (0-based).</param>
        /// <returns>0, 1, 2, or 3.</returns>
        public static int Compute(int currentTurn, int callbackTurnNumber);
    }
}
```

### Distance-to-Bonus Mapping

| Condition | Rule | Bonus |
|-----------|------|-------|
| `callbackTurnNumber == 0` AND `distance >= 2` | Opener reference | **+3** |
| `distance >= 4` | Long-distance callback | **+2** |
| `distance >= 2` (and `< 4`) | Mid-distance callback | **+1** |
| `distance < 2` | Too recent / same turn | **+0** |

Where `distance = currentTurn - callbackTurnNumber`.

### Evaluation Order (priority)

```
1. distance = currentTurn - callbackTurnNumber
2. if distance < 2 → return 0
3. if callbackTurnNumber == 0 → return 3   (opener always wins when distance ≥ 2)
4. if distance >= 4 → return 2
5. return 1                                  (distance 2 or 3, non-opener)
```

The opener check (step 3) takes priority over the 4+ distance check (step 4). A turn-0 topic referenced at distance 4+ still yields +3, not +2.

## Changes to `GameSession`

### New Field: Topic List

```csharp
private readonly List<CallbackOpportunity> _topics = new List<CallbackOpportunity>();
```

Topics are append-only within a session. They are never removed.

### New Public Method: `AddTopic`

```csharp
/// <summary>
/// Register a conversation topic for future callback opportunities.
/// Called by the host or LLM adapter after each turn to seed topics.
/// </summary>
/// <param name="topic">The topic to register. Must not be null.</param>
/// <exception cref="ArgumentNullException">If topic is null.</exception>
public void AddTopic(CallbackOpportunity topic);
```

The engine does not extract topics from natural language — the host or LLM adapter is responsible for calling `AddTopic()`.

### `StartTurnAsync` Changes

When building `DialogueContext`, pass `_topics` as `CallbackOpportunities`:

```csharp
var context = new DialogueContext(
    ...,
    callbackOpportunities: _topics.Count > 0 ? new List<CallbackOpportunity>(_topics) : null,
    ...
);
```

This gives the LLM knowledge of which past topics exist, so it can generate callback options with `CallbackTurnNumber` set.

### `ResolveTurnAsync` Changes

**Before the roll** (between choosing the option and calling `RollEngine.Resolve`):

1. Compute `callbackBonus`:
   ```
   int callbackBonus = 0;
   if (chosenOption.CallbackTurnNumber.HasValue)
   {
       callbackBonus = CallbackBonus.Compute(_turnNumber, chosenOption.CallbackTurnNumber.Value);
   }
   ```

2. Sum all external bonuses (callback + tell + triple combo) into a single `externalBonus` value:
   ```
   int externalBonus = callbackBonus + tellBonus + tripleComboBonus;
   ```

3. Pass `externalBonus` to `RollEngine.Resolve()`:
   ```
   var rollResult = RollEngine.Resolve(
       ...,
       externalBonus: externalBonus
   );
   ```

**Critical:** The callback bonus is computed **before** the roll. It flows through `RollEngine.Resolve(externalBonus)` so that `RollResult.IsSuccess` reflects the bonus. This means the bonus can turn a near-miss into a success.

**After the roll:** Set `callbackBonusApplied` on the `TurnResult`:
```
return new TurnResult(
    ...,
    callbackBonusApplied: callbackBonus,
    ...
);
```

### Hidden Nature

The callback bonus is hidden from the player. The UI success percentage shown before the player picks an option does **not** include the callback bonus. Only after the roll resolves is `TurnResult.CallbackBonusApplied` populated (for logging/debugging).

## Input/Output Examples

### Example 1: Opener Callback (+3)
```
Setup: _topics contains ("dad-jokes", turnIntroduced: 0)
Turn 3: Player picks option with CallbackTurnNumber = 0
  → distance = 3 - 0 = 3
  → distance >= 2 AND callbackTurnNumber == 0 → bonus = +3
  → externalBonus includes +3
  → RollEngine.Resolve(..., externalBonus: 3)
  → If d20+mod+levelBonus = 11, DC = 14: Total = 11, FinalTotal = 14, IsSuccess = true
  → TurnResult.CallbackBonusApplied = 3
```

### Example 2: Long-Distance Callback (+2)
```
Setup: _topics contains ("gym-routine", turnIntroduced: 1)
Turn 5: Player picks option with CallbackTurnNumber = 1
  → distance = 5 - 1 = 4
  → distance >= 4, callbackTurnNumber != 0 → bonus = +2
  → externalBonus includes +2
  → RollEngine.Resolve(..., externalBonus: 2)
  → TurnResult.CallbackBonusApplied = 2
```

### Example 3: Mid-Distance Callback (+1)
```
Setup: _topics contains ("pizza", turnIntroduced: 3)
Turn 5: Player picks option with CallbackTurnNumber = 3
  → distance = 5 - 3 = 2
  → distance >= 2 but < 4, callbackTurnNumber != 0 → bonus = +1
  → TurnResult.CallbackBonusApplied = 1
```

### Example 4: Too Recent — No Bonus
```
Setup: _topics contains ("cats", turnIntroduced: 4)
Turn 5: Player picks option with CallbackTurnNumber = 4
  → distance = 5 - 4 = 1
  → distance < 2 → bonus = 0
  → TurnResult.CallbackBonusApplied = 0
```

### Example 5: No Callback Option
```
Turn 5: Player picks option with CallbackTurnNumber = null
  → No callback computation
  → callbackBonus = 0
  → TurnResult.CallbackBonusApplied = 0
```

### Example 6: Opener at Distance 6 (still +3, not +2)
```
Setup: _topics contains ("opener-joke", turnIntroduced: 0)
Turn 6: Player picks option with CallbackTurnNumber = 0
  → distance = 6 - 0 = 6
  → distance >= 2 AND callbackTurnNumber == 0 → bonus = +3 (opener wins over 4+ rule)
```

### Example 7: Bonus Turns Miss Into Hit
```
Turn 4: Player picks option with CallbackTurnNumber = 0 (opener)
  → callbackBonus = +3
  → d20 roll = 10, statMod = 2, levelBonus = 1 → Total = 13
  → DC = 15
  → Without bonus: 13 < 15 → fail
  → With externalBonus=3: FinalTotal = 16 ≥ 15 → SUCCESS
  → This is the key behavioral difference from a post-hoc interest delta approach
```

## Acceptance Criteria

### AC1: `ConversationTopic` is a `sealed class`, NOT a `record`
**Already satisfied.** `CallbackOpportunity` (the codebase equivalent) is already a `sealed class` with `string TopicKey` and `int TurnIntroduced`. No new class needed.

### AC2: `CallbackOpportunities` passed in `DialogueContext`
**Already partially satisfied.** `DialogueContext` already has `List<CallbackOpportunity>? CallbackOpportunities`. Implementation must:
- Populate `_topics` list in `GameSession`
- Pass `_topics` (or a copy) as `callbackOpportunities` in the `DialogueContext` constructor call within `StartTurnAsync()`

### AC3: `DialogueOption.CallbackTurnNumber` respected in roll bonus calculation
In `ResolveTurnAsync`:
- Check `chosenOption.CallbackTurnNumber.HasValue`
- If true, call `CallbackBonus.Compute(_turnNumber, chosenOption.CallbackTurnNumber.Value)`
- Include result in `externalBonus` passed to `RollEngine.Resolve()`
- Record in `TurnResult.CallbackBonusApplied`

### AC4: Distance-to-bonus mapping: 2→+1, 4+→+2, opener→+3
Verified by these `CallbackBonus.Compute` calls:
- `Compute(5, 3)` → `1` (distance 2)
- `Compute(5, 1)` → `2` (distance 4)
- `Compute(5, 0)` → `3` (opener)
- `Compute(5, 4)` → `0` (distance 1)
- `Compute(5, 5)` → `0` (distance 0)
- `Compute(2, 0)` → `3` (opener at distance 2)
- `Compute(1, 0)` → `0` (opener at distance 1 — too recent)

### AC5: Bonus flows through `RollEngine.Resolve(externalBonus)` (NOT post-hoc)
- The callback bonus (along with tell and triple combo bonuses) is summed into a single `externalBonus` int
- This int is passed as the `externalBonus` parameter to `RollEngine.Resolve()`
- `RollResult.FinalTotal` includes the bonus; `IsSuccess` is determined from `FinalTotal`
- The bonus can turn a near-miss into a success (see Example 7)
- Do NOT use `RollResult.AddExternalBonus()` (deprecated)
- Do NOT add the bonus to `interestDelta` after the roll

### AC6: Tests verify bonus applied at correct distances
Unit tests required:
- `CallbackBonus.Compute` — all tiers (0, +1, +2, +3) including boundary values
- `GameSession.ResolveTurnAsync` — callback bonus flows into roll and affects success determination
- `TurnResult.CallbackBonusApplied` reflects the computed bonus

### AC7: Build clean
- `dotnet build` succeeds with zero errors
- All existing tests pass (`dotnet test`)
- No new warnings introduced

## Edge Cases

| Case | Expected Behavior |
|------|-------------------|
| `CallbackTurnNumber` is `null` | No callback computation; bonus = 0 |
| `CallbackTurnNumber == currentTurn` (distance 0) | Bonus = 0 (same turn, not a callback) |
| Distance = 1 | Bonus = 0 (too recent) |
| Distance = 2 | Bonus = +1 |
| Distance = 3 | Bonus = +1 |
| Distance = 4 | Bonus = +2 |
| Distance = 100 | Bonus = +2 (distance ≥ 4, non-opener) |
| Opener at distance = 1 | Bonus = 0 (distance < 2, opener rule doesn't override) |
| Opener at distance = 2 | Bonus = +3 (opener takes priority) |
| Opener at distance = 100 | Bonus = +3 (opener always wins) |
| `currentTurn = 0` | Max distance = 0 for any `callbackTurnNumber`; bonus = 0 |
| `currentTurn = 1` | Max distance = 1 (from turn 0); bonus = 0 |
| Negative `callbackTurnNumber` | Not guarded — undefined behavior. Callers must not produce this. |
| Multiple callback options offered | Each option independently carries its own `CallbackTurnNumber`; only the chosen option's bonus applies |
| Empty `_topics` list | `CallbackOpportunities` passed as `null`; LLM unlikely to generate callback options but engine handles it gracefully |
| Nat 1 with callback bonus | Nat 1 = auto-fail. `externalBonus` is still passed but `IsSuccess` remains `false` per Nat 1 override |
| Nat 20 with callback option | Nat 20 = auto-success. Callback bonus is computed and recorded in `TurnResult.CallbackBonusApplied` but doesn't change the outcome |
| Callback + tell + triple combo stacking | All three bonuses are summed into a single `externalBonus` int before `RollEngine.Resolve()`. After the roll, `GameSession` calls `ComboTracker.ConsumeTripleBonus()` to expire the triple bonus. |
| Duplicate topics in `_topics` | Allowed. LLM may reference any `TurnIntroduced` value via `CallbackTurnNumber` |

## Error Conditions

| Condition | Error Type | Message |
|-----------|-----------|---------|
| `AddTopic(null)` | `ArgumentNullException` | `"topic"` |
| `CallbackOpportunity` constructed with null `topicKey` | `ArgumentNullException` | `"topicKey"` (already implemented) |

No other new error conditions. `CallbackBonus.Compute` is pure arithmetic on two ints and cannot fail.

## Stacking with Other External Bonuses

Per the architecture doc and contracts, `ResolveTurnAsync` computes a single `externalBonus` from multiple sources:

```
externalBonus = callbackBonus     (this issue, #47)
              + tellBonus         (#49 — +2 if tell active)
              + tripleComboBonus  (#46 — bonus from triple combo)
```

All three are computed before the roll and passed as a single `externalBonus` parameter to `RollEngine.Resolve()`. The interest delta calculation then uses:

```
1. Base delta     = SuccessScale.GetInterestDelta(roll) or FailureScale.GetInterestDelta(roll)
2. Risk bonus     = RiskTierBonus.GetInterestBonus(roll) — success only (#42)
3. Momentum bonus = GetMomentumBonus(streak) — success only
4. Combo interest bonus = from ComboTracker — success only (#46)
───────────────────
   Total interestDelta = sum of above → InterestMeter.Apply(total)
```

The callback bonus is **not** in the interest delta stack — it is in the **roll bonus** stack affecting whether the roll succeeds or fails.

## File Summary

| File | Action | Description |
|------|--------|-------------|
| `src/Pinder.Core/Conversation/CallbackBonus.cs` | **Create** | Static class with `Compute(int, int) → int` |
| `src/Pinder.Core/Conversation/GameSession.cs` | **Modify** | Add `_topics` field, `AddTopic()` method, wire callback into `StartTurnAsync` and `ResolveTurnAsync` |
| `tests/.../CallbackBonusTests.cs` | **Create** | Unit tests for `CallbackBonus.Compute` |
| `tests/.../GameSessionCallbackTests.cs` | **Create** | Integration tests verifying callback flows through roll |

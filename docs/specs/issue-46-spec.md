# Spec: Issue #46 — Combo System (§15 Combo Detection and Interest Bonuses)

## Overview

The combo system rewards players for using specific sequences of stats across consecutive turns. Rules v3.4 §15 defines 8 named combos — each triggered by a pattern of 2–3 consecutive stat plays (and in one case, a preceding failure). When a combo completes on a successful roll, a bonus is added to the interest delta for that turn. One combo ("The Triple") grants a flat +1 bonus to all rolls on the *next* turn instead of an immediate interest bonus. The UI can preview which dialogue options would complete a combo before the player chooses.

## Combo Definitions

| Combo Name | Trigger Sequence | Bonus Type | Bonus Value |
|---|---|---|---|
| The Setup | Wit → Charm (success) | Interest | +1 |
| The Reveal | Charm → Honesty (success) | Interest | +1 |
| The Read | SelfAwareness → Honesty (success) | Interest | +1 |
| The Pivot | Honesty → Chaos (success) | Interest | +1 |
| The Recovery | Any fail → SelfAwareness (success) | Interest | +2 |
| The Escalation | Chaos → Rizz (success) | Interest | +1 |
| The Disarm | Wit → Honesty (success) | Interest | +1 |
| The Triple | 3 different stats in 3 consecutive turns (success on 3rd) | Roll bonus | +1 to all rolls next turn |

**Key rules:**
- All combo interest bonuses are applied **only when the completing stat roll succeeds**.
- The Recovery is unique: the first element is "any failed roll" (any stat), not a specific stat.
- The Triple checks that the last 3 turns used 3 distinct `StatType` values. It does NOT require success on the first two turns — only on the third.
- Interest bonuses from combos stack with SuccessScale delta, momentum bonus, and risk tier bonus (#42).
- The Triple's +1 roll bonus applies to the *next* turn only and then expires.

## New Component: `ComboTracker`

### Namespace
`Pinder.Core.Conversation`

### Responsibility
Tracks the history of recent stat plays and failure states to detect combo completions. Pure data tracker — does not modify interest or rolls.

### Class Signature

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class ComboTracker
    {
        /// <summary>
        /// Record the result of a turn: which stat was played and whether the roll succeeded.
        /// Must be called once per turn, in order.
        /// </summary>
        public void RecordTurn(StatType stat, bool success);

        /// <summary>
        /// Check which combo (if any) would be completed if the given stat is played and succeeds.
        /// Returns the combo name (e.g. "The Setup") or null if no combo would complete.
        /// Does NOT mutate state — this is a preview query.
        /// </summary>
        public string? PeekCombo(StatType stat);

        /// <summary>
        /// After RecordTurn, check if the most recently recorded turn completed a combo.
        /// Returns the combo name or null. Only valid immediately after RecordTurn.
        /// </summary>
        public string? LastComboTriggered { get; }

        /// <summary>
        /// Returns the interest bonus for the last triggered combo (0 if none, or if combo is The Triple).
        /// </summary>
        public int LastComboInterestBonus { get; }

        /// <summary>
        /// True if The Triple was triggered on the previous turn, meaning this turn
        /// gets +1 to all rolls.
        /// </summary>
        public bool TripleBonusActive { get; }

        /// <summary>
        /// Call at the start of each turn to expire the Triple bonus if it was consumed.
        /// </summary>
        public void AdvanceTurn();
    }
}
```

### Internal State

- A fixed-size buffer of the last 3 turns: `(StatType stat, bool success)`.
- A boolean `_lastWasFailure` derived from the most recent entry in the buffer.
- A flag `_tripleBonusNextTurn` set when The Triple triggers, and `_tripleBonusActive` which is the published version after `AdvanceTurn()`.

## Changes to Existing Types

### `TurnResult`

Add a new property:

```csharp
/// <summary>Name of the combo triggered this turn, or null if none.</summary>
public string? ComboTriggered { get; }
```

The constructor gains a new parameter `string? comboTriggered`. This is a breaking change to the constructor signature.

### `DialogueOption`

Already has `string? ComboName` property (added in current codebase). No changes needed to the type itself. `GameSession.StartTurnAsync` must populate this field by calling `ComboTracker.PeekCombo(option.Stat)` for each option returned by the LLM adapter.

### `GameStateSnapshot`

Add a new property:

```csharp
/// <summary>True if The Triple bonus is active for the current turn.</summary>
public bool TripleBonusActive { get; }
```

### `GameSession`

- Owns a `ComboTracker` instance (created in constructor).
- **`StartTurnAsync`**: After receiving `DialogueOption[]` from the LLM adapter, iterate each option and set its `ComboName` by calling `_comboTracker.PeekCombo(option.Stat)`. Also call `_comboTracker.AdvanceTurn()` at the beginning of the turn to expire Triple bonus from the previous turn.
- **`ResolveTurnAsync`**: After the roll resolves:
  1. Call `_comboTracker.RecordTurn(chosenOption.Stat, rollResult.IsSuccess)`.
  2. If `rollResult.IsSuccess` and `_comboTracker.LastComboTriggered != null`:
     - Add `_comboTracker.LastComboInterestBonus` to `interestDelta`.
  3. If `_comboTracker.TripleBonusActive` at roll time, the roll should have had +1 applied. This means the Triple bonus must be factored into the `RollEngine.Resolve` call (see Roll Bonus section below).
  4. Pass `_comboTracker.LastComboTriggered` (which may be null) into the `TurnResult` constructor.

### Roll Bonus from The Triple

The Triple grants "+1 to ALL rolls next turn." This is a flat modifier added to the roll total. Implementation options:

**Recommended approach**: `GameSession` checks `_comboTracker.TripleBonusActive` before calling `RollEngine.Resolve`. If active, add +1 to the `RollResult.Total` post-hoc by wrapping or adjusting the interest delta. However, since `RollResult` is immutable and `RollEngine` is stateless, the cleanest approach is:

- Pass the Triple bonus as an additional modifier into `RollEngine.Resolve` (requires adding an optional `int bonusModifier = 0` parameter), OR
- Apply it in `GameSession` after the roll: if `_comboTracker.TripleBonusActive`, create a new `RollResult` with the bonus factored in, OR
- Add the +1 as a post-roll adjustment to the interest delta equivalent (treating +1 to the roll as approximately +1 interest). **This is NOT faithful to the rules** — a roll bonus affects whether you succeed, not just the margin.

The implementer should choose between option 1 (cleanest, small RollEngine change) or option 2 (no RollEngine change, but awkward RollResult reconstruction). The key constraint: the +1 must affect the roll *total* and therefore can change a miss into a hit.

## Input/Output Examples

### Example 1: The Setup (Wit → Charm)

**Turn 1**: Player picks Wit option. Roll succeeds.
- `ComboTracker.RecordTurn(StatType.Wit, true)`
- No combo triggered.

**Turn 2**: Player picks Charm option. Roll succeeds.
- `ComboTracker.PeekCombo(StatType.Charm)` → `"The Setup"` (during StartTurnAsync, for UI preview)
- `ComboTracker.RecordTurn(StatType.Charm, true)`
- `LastComboTriggered` → `"The Setup"`
- `LastComboInterestBonus` → `1`
- `TurnResult.ComboTriggered` → `"The Setup"`
- Interest delta = SuccessScale delta + momentum + risk tier bonus + **1 (combo)**.

### Example 2: The Recovery (Any fail → SA success)

**Turn 1**: Player picks Chaos option. Roll **fails**.
- `ComboTracker.RecordTurn(StatType.Chaos, false)`
- No combo triggered.

**Turn 2**: Player picks SelfAwareness option. Roll succeeds.
- `ComboTracker.PeekCombo(StatType.SelfAwareness)` → `"The Recovery"` (preview)
- `ComboTracker.RecordTurn(StatType.SelfAwareness, true)`
- `LastComboTriggered` → `"The Recovery"`
- `LastComboInterestBonus` → `2`
- Interest delta = SuccessScale delta + momentum + risk tier bonus + **2 (combo)**.

### Example 3: The Recovery — fail requirement NOT met

**Turn 1**: Player picks Charm option. Roll **succeeds**.
- `ComboTracker.RecordTurn(StatType.Charm, true)`

**Turn 2**: Player picks SelfAwareness option. Roll succeeds.
- `ComboTracker.PeekCombo(StatType.SelfAwareness)` → `null` (previous turn was NOT a fail)
- No combo triggered. No combo bonus.

### Example 4: The Triple (3 different stats in 3 turns)

**Turn 1**: Wit, success. **Turn 2**: Charm, success. **Turn 3**: Honesty, success.
- After Turn 3: `LastComboTriggered` → `"The Triple"`
- `LastComboInterestBonus` → `0` (The Triple gives a roll bonus, not interest)
- `_tripleBonusNextTurn` set internally.

**Turn 4 (any stat)**:
- `_comboTracker.AdvanceTurn()` called in `StartTurnAsync`
- `TripleBonusActive` → `true`
- Roll gets +1 modifier.
- After Turn 4 resolves, the bonus expires.

### Example 5: Combo does NOT trigger on failure

**Turn 1**: Wit, success. **Turn 2**: Charm, **fail**.
- Sequence Wit → Charm matches "The Setup", but the completing roll failed.
- `LastComboTriggered` → `null`
- No combo bonus applied.

### Example 6: Overlapping combos

**Turn 1**: Wit, success. **Turn 2**: Honesty, success.
- Wit → Honesty matches "The Disarm" → `LastComboTriggered` → `"The Disarm"`, bonus +1.

**Turn 3**: Chaos, success.
- Honesty → Chaos matches "The Pivot" → `LastComboTriggered` → `"The Pivot"`, bonus +1.
- Note: The completing stat of one combo can be the opening stat of the next.

### Example 7: The Triple with non-success early turns

**Turn 1**: Wit, fail. **Turn 2**: Charm, fail. **Turn 3**: Honesty, success.
- 3 different stats in 3 turns, and the 3rd succeeds.
- `LastComboTriggered` → `"The Triple"` (only the 3rd turn needs to succeed).
- Also: Turn 1 was a fail, but Turn 2 is Charm (not SA), so no Recovery combo either.

### Example 8: Multiple combos same turn

**Turn 1**: Wit, success. **Turn 2**: Honesty, success.
- Wit → Honesty = "The Disarm" (+1).
- If turns 0, 1, 2 also form a Triple (3 different stats), both could trigger.
- **Rule**: If multiple combos complete on the same turn, all bonuses stack. The `LastComboTriggered` should report the highest-value combo name (or a comma-separated list). Implementation decision: report the most valuable single combo for UI, but apply all bonuses.

**Recommendation for prototype**: Apply only the single best combo per turn (highest bonus). This avoids complexity. If rules later require stacking, it's an additive change.

## Acceptance Criteria

### AC1: All 8 combos detected
`ComboTracker` must recognize all 8 combo sequences from the table above. Each combo's trigger pattern and bonus value must match exactly. The combo name strings must be: "The Setup", "The Reveal", "The Read", "The Pivot", "The Recovery", "The Escalation", "The Disarm", "The Triple".

### AC2: Interest bonus applied on success when combo completes
When a roll succeeds and completes a combo (other than The Triple), the combo's interest bonus is added to the total interest delta in `GameSession.ResolveTurnAsync`. The bonus stacks with SuccessScale, momentum, and risk tier bonus (#42). The combo bonus is NOT applied if the completing roll fails.

### AC3: The Recovery combo triggers on any fail → SA success
The Recovery requires:
1. The immediately preceding turn's roll was a failure (any stat).
2. The current turn uses `StatType.SelfAwareness`.
3. The current turn's roll succeeds.

If all three conditions are met, The Recovery triggers with +2 interest bonus.

### AC4: The Triple grants roll bonus next turn
When The Triple triggers (3 different stats in 3 consecutive turns, success on 3rd):
- No immediate interest bonus.
- The next turn's roll gets +1 to its total (affecting success/failure determination).
- The bonus expires after one turn regardless of outcome.
- `GameStateSnapshot.TripleBonusActive` is `true` during that bonus turn.

### AC5: `DialogueOption.ComboName` populated when option would complete a combo
During `StartTurnAsync`, after receiving dialogue options from the LLM adapter, `GameSession` calls `ComboTracker.PeekCombo(option.Stat)` for each option and sets the `ComboName` property. This lets the UI show a ⭐ icon on combo-completing options. `PeekCombo` must not mutate tracker state.

### AC6: `TurnResult.ComboTriggered` populated on combo activation
`TurnResult` must expose a `string? ComboTriggered` property. It is set to the combo name when a combo activated this turn, or `null` otherwise.

### AC7: Tests cover at least The Setup, The Recovery, The Triple
Unit tests must verify:
- The Setup: Wit → Charm success triggers +1 interest bonus.
- The Recovery: Any fail → SA success triggers +2 interest bonus; SA success without prior fail does NOT trigger.
- The Triple: 3 different stats in 3 turns (success on 3rd) sets roll bonus for next turn; bonus expires after one turn.

### AC8: Build clean
`dotnet build` succeeds with zero warnings in the `Pinder.Core` project. All existing tests continue to pass.

## Edge Cases

1. **First turn**: No prior history. No combo can complete (all require at least 2 turns). `PeekCombo` returns `null` for all stats. `LastComboTriggered` is `null`.

2. **Same stat twice**: e.g., Wit → Wit. No combo matches a repeated stat (none of the 8 combos have repeated stats). No combo triggers.

3. **The Recovery after multiple consecutive fails**: Fail → Fail → SA success. Only the most recent fail matters. The Recovery triggers because the immediately preceding turn was a fail.

4. **The Triple with repeated stats**: Wit → Charm → Wit. Only 2 distinct stats in 3 turns. The Triple does NOT trigger (requires 3 *different* stats).

5. **The Triple overlapping with a 2-stat combo**: Wit → Charm → Honesty. This is both "The Setup" (Wit → Charm, triggered on turn 2) and "The Triple" (3 different stats, triggered on turn 3). They trigger on different turns — no conflict.

6. **Triple bonus expires even on fail**: If the player fails their roll during the Triple bonus turn, the +1 was still applied (just wasn't enough). The bonus expires regardless.

7. **Combo preview (`PeekCombo`) with Triple bonus active**: `PeekCombo` only reports combo completion, not the Triple bonus. The Triple bonus is a separate concern tracked via `TripleBonusActive`.

8. **Game ends mid-combo**: If interest hits 0 or 25 before a combo completes, the combo sequence is irrelevant. No special handling needed — the game just ends.

9. **Combo on the very turn that ends the game**: If a combo triggers and the resulting interest delta causes the game to end, the combo is still reported in `TurnResult.ComboTriggered`. The game-over check happens after interest application.

10. **`DialogueOption.ComboName` when `ComboTracker` has no history**: All options return `null` for `ComboName`. This is correct — no combo can complete on turn 1.

## Error Conditions

1. **`ComboTracker.RecordTurn` called with invalid stat**: `StatType` is an enum — invalid values are a programmer error, not a runtime concern. No validation needed beyond what the type system provides.

2. **`ComboTracker.PeekCombo` called before any `RecordTurn`**: Returns `null`. This is the normal case for turn 1.

3. **`TurnResult` constructor missing `comboTriggered` parameter**: Compile error. Existing callers must be updated. Since `GameSession` is the only caller, this is contained.

4. **`GameSession.ResolveTurnAsync` called without `StartTurnAsync`**: Existing `InvalidOperationException` behavior is unchanged. Combo tracking is only affected inside the normal flow.

## Dependencies

- **Issue #42 (Risk Tier Bonus)**: Combo interest bonuses stack additively with risk tier bonuses. The combo bonus is computed independently and added to `interestDelta` in `GameSession.ResolveTurnAsync` alongside the risk tier bonus. The implementation must account for the risk tier bonus already being added to `interestDelta` before or after the combo bonus — order doesn't matter since both are additive.

- **`Pinder.Core.Stats.StatType`**: Enum used for combo sequence matching. Already exists.

- **`Pinder.Core.Rolls.RollEngine`**: May need modification if The Triple's +1 roll bonus is implemented as a parameter to `Resolve()`. Alternatively, `GameSession` can handle it post-hoc.

- **`Pinder.Core.Rolls.SuccessScale`**: Used to compute base interest delta. Unchanged.

- **`Pinder.Core.Conversation.GameSession`**: Primary integration point. Must be modified to own a `ComboTracker`, call `PeekCombo` during `StartTurnAsync`, call `RecordTurn` and apply combo bonus during `ResolveTurnAsync`.

- **`Pinder.Core.Conversation.TurnResult`**: Constructor change to add `ComboTriggered`.

- **`Pinder.Core.Conversation.GameStateSnapshot`**: Constructor change to add `TripleBonusActive`.

- **No external dependencies**. No NuGet packages. No file I/O. Pure C# logic.

# Spec: Issue #46 — Combo System (§15 Combo Detection and Interest Bonuses)

## Overview

The combo system rewards players for using specific sequences of stats across consecutive Speak turns. Rules v3.4 §15 defines 8 named combos — each triggered by a pattern of 2–3 consecutive stat plays (and in one case, a preceding failure). When a combo completes on a successful roll, a bonus is added to the interest delta for that turn. One combo ("The Triple") grants a flat +1 bonus to all rolls on the *next* turn instead of an immediate interest bonus. The UI can preview which dialogue options would complete a combo before the player chooses.

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
- Interest bonuses from combos stack additively with SuccessScale delta, momentum bonus, and risk tier bonus (issue #42).
- The Triple's +1 roll bonus applies to the *next* turn only and then expires.

**Combo priority**: If multiple combos could complete on the same turn (e.g., a 2-stat combo AND The Triple), apply only the single best combo (highest bonus). Report that combo's name. For prototype, no stacking of multiple combos on the same turn.

## New Component: `ComboTracker`

### Namespace
`Pinder.Core.Conversation`

### File
`src/Pinder.Core/Conversation/ComboTracker.cs`

### Responsibility
Tracks the history of recent stat plays and failure states to detect combo completions. Pure data tracker — does not modify interest or rolls directly.

### Class Signature

```csharp
namespace Pinder.Core.Conversation
{
    public sealed class ComboTracker
    {
        /// <summary>
        /// Record the result of a turn: which stat was played and whether the roll succeeded.
        /// Must be called once per Speak turn, in chronological order.
        /// Detects combo completion internally; results available via LastComboTriggered etc.
        /// </summary>
        public void RecordTurn(StatType stat, bool success);

        /// <summary>
        /// Preview: what combo would complete if the given stat is played and succeeds?
        /// Returns the combo name (e.g. "The Setup") or null if no combo would complete.
        /// Does NOT mutate state — this is a read-only preview query.
        /// </summary>
        public string? PeekCombo(StatType stat);

        /// <summary>
        /// Name of the combo triggered by the most recent RecordTurn call, or null if none.
        /// </summary>
        public string? LastComboTriggered { get; }

        /// <summary>
        /// Interest bonus for the last triggered combo.
        /// 0 if no combo triggered, or if the combo was The Triple (which gives a roll bonus instead).
        /// </summary>
        public int LastComboInterestBonus { get; }

        /// <summary>
        /// True if The Triple was triggered on a previous turn and the bonus has not yet
        /// been consumed. When true, the current turn's roll gets +1.
        /// </summary>
        public bool TripleBonusActive { get; }

        /// <summary>
        /// Call at the start of each turn to advance state.
        /// Expires The Triple bonus if it was active (meaning this turn used it).
        /// </summary>
        public void AdvanceTurn();
    }
}
```

### Internal State (guidance for implementer)

- A buffer of the last 3 turn entries: `(StatType stat, bool success)`. Only the last 3 are needed for any combo detection.
- A boolean tracking whether the previous turn was a failure (derived from buffer, used for The Recovery).
- A flag `_tripleBonusNextTurn` set to `true` when The Triple triggers on `RecordTurn`. On the next `AdvanceTurn()` call, this promotes to `TripleBonusActive = true` and `_tripleBonusNextTurn` resets. On the *following* `AdvanceTurn()`, `TripleBonusActive` resets to `false`.

### Detection Logic

**Two-stat combos** (The Setup, The Reveal, The Read, The Pivot, The Escalation, The Disarm):
- Check if the *previous* turn's stat matches the combo's first stat.
- Check if the *current* turn's stat matches the combo's second stat.
- The current turn's roll must succeed.
- Previous turn's success/failure is irrelevant (except for The Recovery).

**The Recovery**:
- The *previous* turn was a failure (any stat, `success == false`).
- The current turn uses `StatType.SelfAwareness` and succeeds.

**The Triple**:
- The last 3 turns (including current) used 3 distinct `StatType` values.
- The current (3rd) turn succeeds.

**Priority when multiple match**: Specific 2-stat combos > The Triple > The Recovery. Report and apply only the highest-priority combo. Among 2-stat combos of equal bonus value, any match is fine (they can't conflict since each has a unique stat pair).

## Changes to Existing Types

### `TurnResult` (existing — `src/Pinder.Core/Conversation/TurnResult.cs`)

**No type changes needed.** `TurnResult` already has:
- `public string? ComboTriggered { get; }` — set via constructor parameter `comboTriggered`

The constructor already accepts `string? comboTriggered = null` as an optional parameter. `GameSession.ResolveTurnAsync` must pass the actual combo name when a combo fires.

### `DialogueOption` (existing — `src/Pinder.Core/Conversation/DialogueOption.cs`)

**No type changes needed.** `DialogueOption` already has:
- `public string? ComboName { get; }` — set via constructor parameter `comboName`

`GameSession.StartTurnAsync` must construct `DialogueOption` instances with the `comboName` parameter populated from `ComboTracker.PeekCombo(option.Stat)`.

### `GameStateSnapshot` (existing — `src/Pinder.Core/Conversation/GameStateSnapshot.cs`)

**Needs new property:**

```csharp
/// <summary>True if The Triple bonus is active for the current turn (+1 to rolls).</summary>
public bool TripleBonusActive { get; }
```

Add to constructor:

```csharp
public GameStateSnapshot(
    int interest,
    InterestState state,
    int momentumStreak,
    string[] activeTrapNames,
    int turnNumber,
    bool tripleBonusActive = false)  // NEW — default false for backward compat
```

### `RollResult` (existing — `src/Pinder.Core/Rolls/RollResult.cs`)

**No type changes needed.** `RollResult` already has:
- `public int ExternalBonus { get; private set; }`
- `public int FinalTotal => Total + ExternalBonus;`
- `public void AddExternalBonus(int bonus)` — adds to ExternalBonus

**Important note on `IsSuccess`:** The current `IsSuccess` property is computed in the constructor from `Total` (not `FinalTotal`). This means `AddExternalBonus()` does NOT retroactively change `IsSuccess`. Per architecture doc, `AddExternalBonus()` is deprecated and the canonical path should be `externalBonus` as a parameter to `RollEngine.Resolve()`. However, since `RollEngine.Resolve()` does NOT yet have an `externalBonus` parameter (Wave 0 issue #130 not yet implemented), the implementer has two options:

1. **Add `externalBonus` parameter to `RollEngine.Resolve()`** (preferred — this implements part of #130): Add `int externalBonus = 0` as an optional parameter. Include it in the `Total` computation so `IsSuccess` accounts for it.
2. **Use `AddExternalBonus()` as interim solution**: Call `rollResult.AddExternalBonus(1)` for The Triple. Accept that `IsSuccess` won't reflect the bonus (it's already computed). This is less correct but avoids touching `RollEngine`.

**Recommendation**: Option 1. Add the parameter to `Resolve()`. The Triple *must* affect whether the roll succeeds — a +1 bonus that doesn't change `IsSuccess` is incorrect.

### `RollEngine` (existing — `src/Pinder.Core/Rolls/RollEngine.cs`)

If implementing Option 1 above, add `int externalBonus = 0` parameter:

```csharp
public static RollResult Resolve(
    StatType stat,
    StatBlock attacker,
    StatBlock defender,
    TrapState attackerTraps,
    int level,
    ITrapRegistry trapRegistry,
    IDiceRoller dice,
    bool hasAdvantage = false,
    bool hasDisadvantage = false,
    int externalBonus = 0)       // NEW — default 0 preserves backward compat
```

The `externalBonus` is added to `Total` inside the constructor call: `usedDieRoll + statModifier + levelBonus + externalBonus`. This ensures `IsSuccess` correctly accounts for external bonuses.

### `GameSession` (existing — `src/Pinder.Core/Conversation/GameSession.cs`)

Add a `ComboTracker` field:

```csharp
private readonly ComboTracker _comboTracker = new ComboTracker();
```

**In `StartTurnAsync()`:**
1. Call `_comboTracker.AdvanceTurn()` at the beginning of the turn (expires Triple bonus).
2. After receiving `DialogueOption[]` from the LLM adapter, reconstruct each option with `ComboName` populated:
   ```
   For each option: comboName = _comboTracker.PeekCombo(option.Stat)
   ```
   Since `DialogueOption` is immutable, create new instances with the `comboName` parameter set.

**In `ResolveTurnAsync(int optionIndex)`:**
1. Before calling `RollEngine.Resolve()`: check `_comboTracker.TripleBonusActive`. If true, pass `externalBonus: 1` (or accumulate with other external bonuses).
2. After roll resolves: call `_comboTracker.RecordTurn(chosenOption.Stat, rollResult.IsSuccess)`.
3. If `_comboTracker.LastComboTriggered != null`:
   - Add `_comboTracker.LastComboInterestBonus` to the interest delta.
   - Pass `_comboTracker.LastComboTriggered` as the `comboTriggered` parameter when constructing `TurnResult`.

**In `GameStateSnapshot` construction:**
- Pass `_comboTracker.TripleBonusActive` to the snapshot.

## Input/Output Examples

### Example 1: The Setup (Wit → Charm)

**Turn 1**: Player picks Wit option. Roll succeeds (Total 15 vs DC 14).
- `_comboTracker.RecordTurn(StatType.Wit, true)` called after roll.
- `LastComboTriggered` → `null` (first turn, no prior history).

**Turn 2**: `StartTurnAsync()` called.
- `_comboTracker.AdvanceTurn()` — no effect (no Triple pending).
- `_comboTracker.PeekCombo(StatType.Charm)` → `"The Setup"` (Wit was previous, Charm completes).
- `_comboTracker.PeekCombo(StatType.Honesty)` → `"The Disarm"` (Wit → Honesty).
- `_comboTracker.PeekCombo(StatType.Rizz)` → `null`.
- UI shows ⭐ on Charm and Honesty options.

Player picks Charm option. Roll succeeds.
- `_comboTracker.RecordTurn(StatType.Charm, true)`.
- `LastComboTriggered` → `"The Setup"`.
- `LastComboInterestBonus` → `1`.
- `TurnResult.ComboTriggered` → `"The Setup"`.
- Interest delta = SuccessScale delta + momentum + risk tier bonus + **1** (combo).

### Example 2: The Recovery (Any fail → SelfAwareness success)

**Turn 1**: Player picks Chaos option. Roll **fails** (Total 10 vs DC 15).
- `_comboTracker.RecordTurn(StatType.Chaos, false)`.
- `LastComboTriggered` → `null`.

**Turn 2**: `StartTurnAsync()`.
- `_comboTracker.PeekCombo(StatType.SelfAwareness)` → `"The Recovery"`.
- Player picks SelfAwareness option. Roll succeeds.
- `_comboTracker.RecordTurn(StatType.SelfAwareness, true)`.
- `LastComboTriggered` → `"The Recovery"`.
- `LastComboInterestBonus` → `2`.
- Interest delta includes +2 combo bonus.

### Example 3: The Recovery — fail requirement NOT met

**Turn 1**: Player picks Charm. Roll **succeeds**.
- `_comboTracker.RecordTurn(StatType.Charm, true)`.

**Turn 2**: Player picks SelfAwareness. Roll succeeds.
- `_comboTracker.PeekCombo(StatType.SelfAwareness)` → `null` (previous was success, not fail).
- `LastComboTriggered` → `null`. No combo bonus.

### Example 4: The Triple (3 different stats in 3 turns)

**Turn 1**: Wit, success. **Turn 2**: Charm, success (also triggers The Setup). **Turn 3**: Honesty, success.
- After Turn 3's `RecordTurn`: 3 distinct stats (Wit, Charm, Honesty), 3rd succeeds.
- But Charm → Honesty also matches "The Reveal". Priority: specific 2-stat combo > The Triple.
- `LastComboTriggered` → `"The Reveal"`, `LastComboInterestBonus` → `1`.

**Alternative — Turn 3 uses SelfAwareness** (Wit, Charm, SelfAwareness):
- Charm → SelfAwareness doesn't match any 2-stat combo.
- 3 distinct stats, 3rd succeeds → The Triple triggers.
- `LastComboTriggered` → `"The Triple"`, `LastComboInterestBonus` → `0`.
- `_tripleBonusNextTurn` set internally.

**Turn 4**: `AdvanceTurn()` → `TripleBonusActive` = `true`.
- Roll gets `externalBonus: 1`.
- After Turn 4 resolves, next `AdvanceTurn()` clears the bonus.

### Example 5: Combo does NOT trigger on failure

**Turn 1**: Wit, success. **Turn 2**: Charm, **fail**.
- Wit → Charm matches "The Setup" sequence, but the completing roll failed.
- `LastComboTriggered` → `null`. No bonus.

### Example 6: Chained combos across turns

**Turn 1**: Wit, success. **Turn 2**: Honesty, success → "The Disarm" (+1).
**Turn 3**: Chaos, success → "The Pivot" (Honesty → Chaos) (+1).
- The completing stat of one combo can be the opening stat of the next.

### Example 7: The Triple with failed early turns

**Turn 1**: Wit, fail. **Turn 2**: Charm, fail. **Turn 3**: Honesty, success.
- 3 different stats, 3rd succeeds → The Triple triggers.
- `LastComboTriggered` → `"The Triple"`.
- Note: Turn 1 was a fail, Turn 2 is Charm (not SA), so no Recovery combo on Turn 2.

### Example 8: Triple bonus expires regardless of outcome

**Turn 3**: The Triple triggers. **Turn 4**: `TripleBonusActive = true`. Player rolls and **fails** (even with the +1).
- The +1 was applied to the roll but wasn't enough.
- On Turn 5: `AdvanceTurn()` → `TripleBonusActive = false`. Bonus is gone.

## Acceptance Criteria

### AC1: All 8 combos detected
`ComboTracker` must recognize all 8 combo sequences from the definition table. Each combo's trigger pattern and bonus value must match exactly. The combo name strings must be exactly: `"The Setup"`, `"The Reveal"`, `"The Read"`, `"The Pivot"`, `"The Recovery"`, `"The Escalation"`, `"The Disarm"`, `"The Triple"`.

### AC2: Interest bonus applied on success when combo completes
When a roll succeeds and completes a combo (other than The Triple), the combo's interest bonus is added to the total interest delta in `GameSession.ResolveTurnAsync`. The bonus stacks additively with SuccessScale, momentum, and risk tier bonus. The combo bonus is NOT applied if the completing roll fails.

### AC3: The Recovery combo triggers on any fail → SA success
The Recovery requires: (1) the immediately preceding Speak turn was a failure (any stat), (2) the current turn uses `StatType.SelfAwareness`, (3) the current turn's roll succeeds. If all three conditions are met, The Recovery triggers with +2 interest bonus.

### AC4: The Triple grants roll bonus next turn via `externalBonus`
When The Triple triggers (3 different stats in 3 consecutive turns, success on 3rd): no immediate interest bonus; the *next* turn's roll gets +1 via the `externalBonus` parameter on `RollEngine.Resolve()` (NOT as a post-hoc adjustment that fails to affect `IsSuccess`). The bonus expires after one turn. `GameStateSnapshot.TripleBonusActive` is `true` during the bonus turn.

### AC5: `DialogueOption.ComboName` populated when option would complete a combo
During `StartTurnAsync`, `GameSession` calls `ComboTracker.PeekCombo(option.Stat)` for each dialogue option and passes the result as the `comboName` constructor parameter. This enables UI to show a ⭐ icon. `PeekCombo` must not mutate tracker state.

### AC6: `TurnResult.ComboTriggered` populated on combo activation
`TurnResult.ComboTriggered` is set to the combo name when a combo activated this turn, or `null` otherwise. The existing `comboTriggered` constructor parameter is used.

### AC7: Tests cover at least The Setup, The Recovery, The Triple
Unit tests must verify:
- **The Setup**: Wit → Charm success → +1 interest bonus, `LastComboTriggered == "The Setup"`.
- **The Recovery**: Any fail → SA success → +2 interest bonus; SA success without prior fail does NOT trigger.
- **The Triple**: 3 different stats in 3 turns, success on 3rd → `TripleBonusActive` true on next turn; bonus expires after one turn.
- Additional recommended tests: PeekCombo returns correct previews; combo does not trigger on failure; chained combos work.

### AC8: Build clean
`dotnet build` succeeds with zero errors. All existing 254+ tests continue to pass. New code compiles under netstandard2.0 / LangVersion 8.0.

## Edge Cases

1. **First turn**: No prior history. `PeekCombo` returns `null` for all stats. `LastComboTriggered` is `null`. No combo can complete.

2. **Same stat twice**: e.g., Wit → Wit. No combo has a repeated stat in its sequence. No combo triggers.

3. **The Recovery after multiple consecutive fails**: Fail → Fail → SA success. Only the immediately preceding turn matters. The Recovery triggers because the previous turn was a fail.

4. **The Triple with repeated stats**: Wit → Charm → Wit. Only 2 distinct stats. The Triple does NOT trigger.

5. **The Triple overlapping with a 2-stat combo on different turns**: Wit (T1) → Charm (T2, triggers The Setup) → Honesty (T3). On T3, Charm → Honesty = "The Reveal" (priority over The Triple). The Triple would also match but is lower priority.

6. **Triple bonus expires even on fail**: The +1 is applied to the roll regardless. If the player still fails, the bonus is consumed and gone.

7. **`PeekCombo` with Triple bonus active**: `PeekCombo` only reports combo *completion* names. The Triple bonus being active is a separate concern — it's a roll modifier, not a combo preview.

8. **Game ends mid-combo sequence**: No special handling. Combo state is irrelevant after game over.

9. **Combo triggers on the turn that ends the game**: If the combo interest bonus causes interest to hit 0 or 25, the combo is still reported in `TurnResult.ComboTriggered`. End-condition check happens after all delta application.

10. **Read/Recover/Wait turns**: These do NOT call `RecordTurn` on `ComboTracker`. They do NOT contribute to or break combo sequences. The combo buffer only tracks Speak turns.

11. **No turns recorded yet, `AdvanceTurn()` called**: No-op. Safe to call.

## Error Conditions

1. **`RecordTurn` with invalid `StatType` enum value**: `StatType` is a closed enum. Invalid values are a programmer error. No runtime validation beyond the type system.

2. **`PeekCombo` before any `RecordTurn`**: Returns `null`. Normal behavior for turn 1.

3. **`GameSession.ResolveTurnAsync` called without `StartTurnAsync`**: Existing `InvalidOperationException` behavior unchanged. Combo tracking only runs within normal turn flow.

4. **Backward compatibility of `GameStateSnapshot` constructor**: New `tripleBonusActive` parameter defaults to `false`. All existing callers continue to work without modification.

5. **Backward compatibility of `RollEngine.Resolve`**: New `externalBonus` parameter defaults to `0`. All existing callers (including 254 tests) continue to work without modification.

## Dependencies

| Dependency | Type | Status | Notes |
|---|---|---|---|
| `Pinder.Core.Stats.StatType` | Enum | Exists | Used for combo sequence matching |
| `Pinder.Core.Rolls.RollEngine` | Static class | Exists, needs modification | Add `externalBonus` param (optional, default 0) |
| `Pinder.Core.Rolls.RollResult` | Class | Exists | Already has `ExternalBonus`/`FinalTotal`/`AddExternalBonus()`. If `externalBonus` is added to `Resolve()`, the bonus flows through `Total` instead. |
| `Pinder.Core.Conversation.GameSession` | Class | Exists, needs modification | Integrate `ComboTracker` |
| `Pinder.Core.Conversation.TurnResult` | Class | Exists | Already has `ComboTriggered` field — no changes |
| `Pinder.Core.Conversation.DialogueOption` | Class | Exists | Already has `ComboName` field — no changes |
| `Pinder.Core.Conversation.GameStateSnapshot` | Class | Exists, needs modification | Add `TripleBonusActive` property |
| `Pinder.Core.Conversation.InterestMeter` | Class | Exists | Used by GameSession — unchanged |
| Issue #42 (Risk Tier Bonus) | Feature dependency | Implemented | Combo bonus stacks with risk tier bonus |
| Issue #130 (Wave 0 externalBonus) | Feature dependency | NOT yet implemented | This issue should add `externalBonus` to `RollEngine.Resolve()` as part of implementation |
| No external NuGet packages | Constraint | — | Pure C#, netstandard2.0 |

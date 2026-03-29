# Spec: Issue #46 — Combo System (§15 Combo Detection and Interest Bonuses)

## Overview

The combo system rewards players for using specific sequences of stats across consecutive turns. Rules v3.4 §15 defines 8 named combos — each triggered by a pattern of 2–3 consecutive stat plays (and in one case, a preceding failure). When a combo-completing roll succeeds, a bonus is applied: either an interest delta bonus (+1 or +2) or a flat +1 roll bonus on the next turn (The Triple). A new `ComboTracker` class in `Pinder.Core.Conversation` detects combos; `GameSession` owns the tracker and applies effects.

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

- All combo bonuses fire **only when the completing roll succeeds**.
- The Recovery is unique: the first element is "any stat that failed" — not a specific stat type.
- The Triple checks that the last 3 turns used 3 **distinct** `StatType` values. It does NOT require success on turns 1 or 2 — only on the 3rd.
- Interest bonuses from combos stack additively with `SuccessScale` delta, momentum bonus, and risk tier bonus (#42).
- The Triple's +1 roll bonus applies to the **next** turn only and then expires.
- If multiple combos match on the same turn, only the highest-bonus combo fires. If tied, the first matched combo wins. (Prototype simplification — stacking may be added later.)

---

## Function Signatures

### New Class: `ComboTracker`

**File:** `src/Pinder.Core/Conversation/ComboTracker.cs`
**Namespace:** `Pinder.Core.Conversation`

```csharp
public sealed class ComboTracker
{
    /// <summary>
    /// Record the stat used this turn and whether the roll succeeded.
    /// Must be called exactly once per turn, in chronological order.
    /// Internally updates the history buffer and checks for combo completion.
    /// </summary>
    /// <param name="stat">The StatType played this turn.</param>
    /// <param name="succeeded">True if the roll succeeded, false if it failed.</param>
    public void RecordTurn(StatType stat, bool succeeded);

    /// <summary>
    /// After RecordTurn, returns the combo that completed this turn, or null.
    /// Only valid immediately after RecordTurn — returns the result of the
    /// most recent RecordTurn call.
    /// </summary>
    /// <returns>A ComboResult with name and bonus, or null if no combo fired.</returns>
    public ComboResult? CheckCombo();

    /// <summary>
    /// True if The Triple was completed on the previous turn, meaning
    /// the current turn gets +1 to all rolls via externalBonus.
    /// Resets to false after the bonus turn is consumed (one-turn only).
    /// GameSession reads this before calling RollEngine.Resolve.
    /// </summary>
    public bool HasTripleBonus { get; }

    /// <summary>
    /// Preview: returns the combo name that would complete if the given stat
    /// is played and succeeds this turn. Returns null if no combo would complete.
    /// Does NOT mutate internal state — safe to call for each dialogue option.
    /// Used by GameSession.StartTurnAsync to populate DialogueOption.ComboName.
    /// </summary>
    /// <param name="stat">The StatType being previewed.</param>
    /// <returns>Combo name string (e.g. "The Setup") or null.</returns>
    public string? PeekCombo(StatType stat);
}
```

### New Class: `ComboResult`

**File:** `src/Pinder.Core/Conversation/ComboResult.cs`
**Namespace:** `Pinder.Core.Conversation`

```csharp
public sealed class ComboResult
{
    /// <summary>Display name of the combo (e.g. "The Setup", "The Recovery").</summary>
    public string Name { get; }

    /// <summary>
    /// Interest bonus to add to the turn's interest delta.
    /// 0 for The Triple (which gives a roll bonus instead).
    /// </summary>
    public int InterestBonus { get; }

    /// <summary>
    /// True only for The Triple — signals that next turn gets +1 roll bonus.
    /// </summary>
    public bool IsTriple { get; }

    public ComboResult(string name, int interestBonus, bool isTriple);
}
```

### Existing Types — Required Modifications

#### `GameStateSnapshot` — add `TripleBonusActive`

```csharp
// New property:
/// <summary>True if The Triple bonus is active for the current turn (+1 to all rolls).</summary>
public bool TripleBonusActive { get; }

// Updated constructor signature:
public GameStateSnapshot(
    int interest,
    InterestState state,
    int momentumStreak,
    string[] activeTrapNames,
    int turnNumber,
    bool tripleBonusActive = false);  // new optional parameter (backward-compatible)
```

#### `TurnResult` — already has `ComboTriggered`

The `string? ComboTriggered` property and constructor parameter already exist on `TurnResult`. No structural changes needed. `GameSession` must populate it with the combo name from `ComboResult.Name` (or null).

#### `DialogueOption` — already has `ComboName`

The `string? ComboName` property already exists. `GameSession.StartTurnAsync` must populate it by calling `ComboTracker.PeekCombo(option.Stat)` for each option returned by the LLM adapter.

#### `RollEngine.Resolve` — `externalBonus` parameter

Per architecture doc §146 and issue #130 (Wave 0 prerequisite), `RollEngine.Resolve` gains an optional `int externalBonus = 0` parameter. This is the canonical path for The Triple's +1 roll bonus. The parameter adds to `Total` to produce `FinalTotal`, and `IsSuccess` must be computed from `FinalTotal` (not just `Total`).

**If #130 is not yet merged**: The implementer should use `RollResult.AddExternalBonus(tripleBonus)` after calling `Resolve`, then use `FinalTotal` (not `Total`) for success/failure determination. Note that `IsSuccess` on `RollResult` is computed at construction time from `Total`, so this fallback means `IsSuccess` may be wrong when `ExternalBonus` flips a miss to a hit. The implementer must check `FinalTotal >= DC` manually in `GameSession` when `ExternalBonus > 0`.

**Preferred**: Wait for #130 to land so `externalBonus` flows through `Resolve` and `IsSuccess` is computed correctly.

---

## GameSession Integration

### `StartTurnAsync` changes

1. Call `_comboTracker` peek logic **after** receiving `DialogueOption[]` from `ILlmAdapter.GetDialogueOptionsAsync()`.
2. For each option, call `_comboTracker.PeekCombo(option.Stat)` and use the result to construct the `DialogueOption` with the `comboName` parameter.
3. The `HasTripleBonus` flag is checked here to set `GameStateSnapshot.TripleBonusActive`.

### `ResolveTurnAsync` changes

After the roll resolves:

1. Call `_comboTracker.RecordTurn(chosenOption.Stat, rollResult.IsSuccess)`.
2. Call `var combo = _comboTracker.CheckCombo()`.
3. If `combo != null`:
   - Add `combo.InterestBonus` to the interest delta (0 for The Triple).
   - Set `comboTriggered = combo.Name` for `TurnResult`.
   - If `combo.IsTriple`, the tracker internally flags `HasTripleBonus` for next turn.
4. If `_comboTracker.HasTripleBonus` was true at the start of this turn, the +1 was already applied to the roll via `externalBonus`. After the roll, `HasTripleBonus` resets to false (consumed).

### Triple Bonus Lifecycle

```
Turn N:   Player plays 3rd distinct stat, succeeds
          → ComboTracker.CheckCombo() returns ComboResult("The Triple", 0, true)
          → ComboTracker internally sets _pendingTripleBonus = true

Turn N+1: GameSession.StartTurnAsync() reads ComboTracker.HasTripleBonus → true
          → GameSession includes +1 in externalBonus when calling RollEngine.Resolve
          → After resolve, HasTripleBonus is consumed (resets to false)

Turn N+2: HasTripleBonus → false. Normal roll.
```

---

## Input/Output Examples

### Example 1: The Setup (Wit → Charm)

**Turn 1:** Player picks Wit option. Roll succeeds.
- `ComboTracker.RecordTurn(StatType.Wit, true)`
- `ComboTracker.CheckCombo()` → `null`

**Turn 2 — StartTurnAsync:** Options include Charm.
- `ComboTracker.PeekCombo(StatType.Charm)` → `"The Setup"`
- `DialogueOption` for Charm gets `ComboName = "The Setup"`

**Turn 2 — ResolveTurnAsync:** Player picks Charm. Roll succeeds (Total=17, DC=15).
- `ComboTracker.RecordTurn(StatType.Charm, true)`
- `ComboTracker.CheckCombo()` → `ComboResult("The Setup", 1, false)`
- Interest delta = SuccessScale(+1) + momentum(0) + riskTierBonus(0) + **combo(+1)** = +2
- `TurnResult.ComboTriggered` = `"The Setup"`

### Example 2: The Recovery (Any fail → SA success)

**Turn 1:** Player picks Chaos. Roll fails (Total=10, DC=15).
- `ComboTracker.RecordTurn(StatType.Chaos, false)`
- `ComboTracker.CheckCombo()` → `null`

**Turn 2 — StartTurnAsync:** Options include SelfAwareness.
- `ComboTracker.PeekCombo(StatType.SelfAwareness)` → `"The Recovery"`

**Turn 2 — ResolveTurnAsync:** Player picks SelfAwareness. Roll succeeds (Total=16, DC=14).
- `ComboTracker.RecordTurn(StatType.SelfAwareness, true)`
- `ComboTracker.CheckCombo()` → `ComboResult("The Recovery", 2, false)`
- Interest delta includes **+2** from combo.
- `TurnResult.ComboTriggered` = `"The Recovery"`

### Example 3: The Recovery — fail requirement NOT met

**Turn 1:** Player picks Charm. Roll succeeds.
- `ComboTracker.RecordTurn(StatType.Charm, true)`

**Turn 2:** Player picks SelfAwareness. Roll succeeds.
- `ComboTracker.PeekCombo(StatType.SelfAwareness)` → `null` (prev turn was not a fail)
- `ComboTracker.CheckCombo()` → `null`
- No combo bonus.

### Example 4: The Triple (3 different stats in 3 turns)

**Turn 1:** Wit, success. **Turn 2:** Charm, success. **Turn 3:** Honesty, success.
- After Turn 3 RecordTurn: `CheckCombo()` → `ComboResult("The Triple", 0, true)`
- `ComboTriggered` = `"The Triple"`, interest bonus = 0.
- Note: Turn 2 also triggered "The Setup" (Wit → Charm). That was a separate combo on turn 2.

**Turn 4 — StartTurnAsync:**
- `ComboTracker.HasTripleBonus` → `true`
- `GameStateSnapshot.TripleBonusActive` → `true`
- GameSession passes `externalBonus: 1` to `RollEngine.Resolve`

**Turn 4 — ResolveTurnAsync:**
- Roll gets +1 from Triple bonus.
- After resolution, `HasTripleBonus` → `false`.

### Example 5: Combo does NOT trigger on failure

**Turn 1:** Wit, success. **Turn 2:** Charm, **fail** (Total=12, DC=16).
- Sequence Wit → Charm matches "The Setup" pattern, but completing roll failed.
- `ComboTracker.CheckCombo()` → `null`
- No combo bonus. No `ComboTriggered` on `TurnResult`.

### Example 6: Overlapping/chaining combos across turns

**Turn 1:** Wit, success.
**Turn 2:** Honesty, success.
- Wit → Honesty = "The Disarm" → combo fires, +1 interest.

**Turn 3:** Chaos, success.
- Honesty → Chaos = "The Pivot" → combo fires, +1 interest.
- The completing stat of one combo can be the opening stat of the next.

### Example 7: The Triple with non-success early turns

**Turn 1:** Wit, **fail**. **Turn 2:** Charm, **fail**. **Turn 3:** Honesty, success.
- 3 different stats in 3 turns. Only 3rd must succeed.
- `CheckCombo()` → `ComboResult("The Triple", 0, true)`
- Also: Turn 1 was a fail, Turn 2 is Charm (not SA), so no Recovery.

### Example 8: Multiple combos match same turn — highest wins

**Setup:** History = [Wit(success), Charm(success)]. Turn 3: Honesty, success.
- Charm → Honesty = "The Reveal" (+1 interest)
- 3 different stats = "The Triple" (+1 roll bonus next turn)
- Both match. The Triple has `InterestBonus=0` but unique roll bonus. "The Reveal" has `InterestBonus=1`.
- **Rule:** Only the highest-bonus combo fires. Since "The Reveal" has higher interest bonus (+1 vs 0), it wins. The Triple does NOT fire.
- **Note:** If the implementer prefers, "highest value" could consider The Triple's roll bonus as more valuable. The contract says "highest-bonus one fires (or first if tied)." For prototype, treat `InterestBonus` as the tiebreaker — higher interest bonus wins.

---

## Acceptance Criteria

### AC1: All 8 combos detected

`ComboTracker` must recognize all 8 combo sequences from the table. The combo name strings must exactly be: `"The Setup"`, `"The Reveal"`, `"The Read"`, `"The Pivot"`, `"The Recovery"`, `"The Escalation"`, `"The Disarm"`, `"The Triple"`.

### AC2: Interest bonus applied on success when combo completes

When a roll succeeds and completes a combo (other than The Triple), the combo's `InterestBonus` is added to the total interest delta in `GameSession.ResolveTurnAsync`. The bonus stacks additively with `SuccessScale` delta, momentum, and risk tier bonus. The combo bonus is **not** applied if the completing roll fails.

### AC3: The Recovery triggers on any fail → SA success

The Recovery requires all three conditions:
1. The immediately preceding turn's roll was a **failure** (any `StatType`).
2. The current turn uses `StatType.SelfAwareness`.
3. The current turn's roll succeeds.

If met, The Recovery fires with `InterestBonus = 2`.

### AC4: The Triple grants roll bonus next turn via `externalBonus`

When The Triple triggers:
- `ComboResult.InterestBonus` = 0 (no immediate interest bonus).
- `ComboResult.IsTriple` = true.
- `ComboTracker.HasTripleBonus` becomes true for the next turn.
- `GameSession` passes `externalBonus: 1` (or adds +1 via `AddExternalBonus`) on the next turn's roll.
- The bonus must affect whether the roll succeeds — it is applied **before** success/failure determination, not as a post-hoc interest adjustment.
- The bonus expires after one turn regardless of outcome.

### AC5: `DialogueOption.ComboName` populated when option would complete a combo

During `StartTurnAsync`, `GameSession` calls `ComboTracker.PeekCombo(option.Stat)` for each option and constructs `DialogueOption` with the result as `comboName`. `PeekCombo` must not mutate tracker state (idempotent).

### AC6: `TurnResult.ComboTriggered` populated on combo activation

`TurnResult.ComboTriggered` is set to the combo name string when a combo fires, or `null` otherwise. This property already exists on `TurnResult`.

### AC7: Tests cover at least The Setup, The Recovery, The Triple

Unit tests must verify:
- **The Setup:** Wit → Charm success triggers `CheckCombo()` returning `ComboResult("The Setup", 1, false)`.
- **The Recovery:** Any fail → SA success triggers `ComboResult("The Recovery", 2, false)`. SA success without a prior fail does NOT trigger.
- **The Triple:** 3 different stats in 3 turns (success on 3rd) returns `ComboResult("The Triple", 0, true)` and sets `HasTripleBonus = true`. The bonus expires after one turn.

### AC8: Build clean

`dotnet build` succeeds with zero errors in `Pinder.Core`. All existing tests (254+) continue to pass.

---

## Edge Cases

1. **First turn (no history):** No combo can complete (all require ≥2 turns of history). `PeekCombo` returns `null` for all stats. `CheckCombo()` returns `null`.

2. **Same stat twice in a row:** e.g. Wit → Wit. No defined combo has a repeated stat. No combo triggers.

3. **Multiple consecutive failures before Recovery:** Fail → Fail → SA success. Only the most recent turn matters. The immediately preceding turn was a fail, so Recovery triggers.

4. **The Triple with repeated stats:** Wit → Charm → Wit. Only 2 distinct stats in 3 turns. The Triple does NOT trigger (requires 3 different stats).

5. **The Triple overlap with 2-stat combo:** Wit → Charm → Honesty. Turn 2 triggers "The Setup" (Wit→Charm). Turn 3 triggers "The Reveal" (Charm→Honesty) AND potentially "The Triple" (3 distinct stats). Per single-best-combo rule, only one fires on turn 3.

6. **Triple bonus expires on fail:** If the player fails during the Triple bonus turn, the +1 was still applied to the roll (just wasn't enough). `HasTripleBonus` resets regardless.

7. **Triple bonus with Read/Recover/Wait actions (#43):** If the player uses Read, Recover, or Wait instead of Speak on the bonus turn, the Triple bonus is consumed without effect (those actions don't go through `RollEngine.Resolve` in the same way, or use `ResolveFixedDC` which should also accept the bonus). Implementation note: `GameSession` should still consume the bonus even if no standard roll occurs.

8. **Game ends mid-combo sequence:** If interest hits 0 (Unmatched) or 25 (DateSecured) before a combo completes, the partial sequence is irrelevant. No special handling.

9. **Combo on game-ending turn:** If a combo fires and the resulting interest delta ends the game, the combo is still reported in `TurnResult.ComboTriggered`. Game-over check happens after interest application.

10. **`PeekCombo` with no history (turn 1):** Returns `null` for all stats. Correct behavior.

11. **`PeekCombo` for Recovery:** If last turn was a fail, `PeekCombo(StatType.SelfAwareness)` returns `"The Recovery"`. For any other stat, it returns null (Recovery only triggers on SA).

12. **The Recovery does NOT require the failing stat to be specific:** Charm fail → SA success = Recovery. Wit fail → SA success = Recovery. Any stat's fail qualifies.

---

## Error Conditions

1. **`RecordTurn` called with invalid enum value:** `StatType` is a C# enum. Invalid values are a programmer error. No runtime validation required.

2. **`CheckCombo` called without prior `RecordTurn`:** Returns `null`. No error — there is simply no history to match against.

3. **`HasTripleBonus` read without calling `RecordTurn`:** Returns `false` by default. No error.

4. **`PeekCombo` called with stat not in any combo sequence:** Returns `null`. Not an error.

5. **`GameSession.ResolveTurnAsync` called when `_currentOptions` is null:** Existing `InvalidOperationException` behavior is unchanged. Combo logic is only reached inside the normal flow after `StartTurnAsync`.

6. **`GameStateSnapshot` constructor called without `tripleBonusActive`:** Default value `false` preserves backward compatibility with existing callers.

---

## Dependencies

| Dependency | Type | Status |
|---|---|---|
| `Pinder.Core.Stats.StatType` | Enum (6 values) | Exists |
| `Pinder.Core.Rolls.RollEngine` | Static class | Exists. May need `externalBonus` param (#130 / Wave 0) |
| `Pinder.Core.Rolls.RollResult` | Data class | Exists. Has `AddExternalBonus()` + `FinalTotal` (deprecated path but functional) |
| `Pinder.Core.Rolls.SuccessScale` | Static class | Exists. Unchanged. |
| `Pinder.Core.Conversation.GameSession` | Orchestrator | Exists. Must be modified to own `ComboTracker`. |
| `Pinder.Core.Conversation.TurnResult` | Data class | Exists. Already has `ComboTriggered` property. |
| `Pinder.Core.Conversation.DialogueOption` | Data class | Exists. Already has `ComboName` property. |
| `Pinder.Core.Conversation.GameStateSnapshot` | Data class | Exists. Needs `TripleBonusActive` property added. |
| Issue #42 (Risk Tier Bonus) | Feature dependency | Combo interest bonuses stack with risk tier bonuses. Both are additive to interest delta. |
| Issue #130 (Wave 0 — externalBonus on Resolve) | Feature dependency | Required for The Triple's +1 roll bonus to flow through `RollEngine.Resolve` correctly. Fallback: use `AddExternalBonus()` on `RollResult`. |
| **No external packages** | Constraint | Zero NuGet dependencies. .NET Standard 2.0, LangVersion 8.0. |

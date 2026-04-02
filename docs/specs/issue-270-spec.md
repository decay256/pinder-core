# Specification: Issue #270 — Missing 5 Shadow Reduction Events from §7

**Module**: docs/modules/conversation-game-session.md

---

## Overview

Rules §7 defines shadow **reduction** events — moments where a shadow stat decreases by 1 as a reward for specific player behavior. Of the 6 defined reductions, only one (4+ different stats → Fixation −1) is currently implemented. This issue adds the remaining 4 reductions plus wires the already-tracked Honesty-success condition to its reduction trigger.

All reductions use `SessionShadowTracker.ApplyOffset()` (not `ApplyGrowth()`, which throws on negative amounts). Each reduction is −1 to a specific `ShadowStatType`.

---

## Function Signatures

No new public methods or types are introduced. All changes are internal to existing `GameSession` methods. The following existing methods on `SessionShadowTracker` are used:

```csharp
// Pinder.Core.Stats.SessionShadowTracker
public string ApplyOffset(ShadowStatType shadow, int delta, string reason);
// delta: signed integer (negative for reductions)
// Returns: description string, e.g. "Dread -1 (Date secured)"
// Also logs the event internally for DrainGrowthEvents()

public int GetDelta(ShadowStatType shadow);
// Returns the cumulative in-session delta for the given shadow stat (0 if untouched)
```

The following `GameSession` private methods are modified (not new — already exist):

```csharp
// Pinder.Core.Conversation.GameSession (private methods)

private void EvaluateEndOfGameShadowGrowth(GameOutcome outcome);
// Modified: add Dread −1 reduction when outcome == GameOutcome.DateSecured

private void EvaluatePerTurnShadowGrowth(
    DialogueOption chosenOption,
    int optionIndex,
    RollResult rollResult,
    int interestAfter);
// Modified: add Denial −1 reduction when Honesty succeeds at interest ≥ 15

public Task<RecoverResult> RecoverAsync();
// Modified: add Madness −1 reduction on successful recovery

public Task<TurnResult> ResolveTurnAsync(int optionIndex);
// Modified: add Overthinking −1 reduction on success with Overthinking disadvantage active
```

---

## Input/Output Examples

### Reduction 1: Date Secured → Dread −1

**Setup**: Player shadow tracker with Dread delta = 3. Game reaches `DateSecured` outcome (interest hits 25).

**Trigger**: `EvaluateEndOfGameShadowGrowth(GameOutcome.DateSecured)` is called.

**Effect**: `_playerShadows.ApplyOffset(ShadowStatType.Dread, -1, "Date secured")` → Dread delta becomes 2.

**Growth event string**: `"Dread -1 (Date secured)"`

### Reduction 2: Honesty Success at Interest ≥ 15 → Denial −1

**Setup**: Player chooses Honesty option. Roll succeeds. Interest after roll is 16.

**Trigger**: Inside `EvaluatePerTurnShadowGrowth`, after existing trigger 6 (Honesty success tracking).

**Condition**: `chosenOption.Stat == StatType.Honesty && rollResult.IsSuccess && interestAfter >= 15`

**Effect**: `_playerShadows.ApplyOffset(ShadowStatType.Denial, -1, "Honesty success at high interest")` → Denial delta decreases by 1.

**Growth event string**: `"Denial -1 (Honesty success at high interest)"`

### Reduction 3: Successful Recover → Madness −1

**Setup**: Player has an active trap. Calls `RecoverAsync()`. SA vs DC 12 roll succeeds.

**Trigger**: Inside `RecoverAsync()`, on the success branch (where trap is cleared).

**Effect**: `_playerShadows?.ApplyOffset(ShadowStatType.Madness, -1, "Recovered from trope trap")` → Madness delta decreases by 1.

**Growth event string**: `"Madness -1 (Recovered from trope trap)"`

### Reduction 4: Success Despite Overthinking Disadvantage → Overthinking −1

**Setup**: Player's Overthinking shadow is ≥ 12 (Tier 2+), which places `StatType.SelfAwareness` in `_shadowDisadvantagedStats`. Player chooses an SA option. Roll succeeds despite the disadvantage.

**Trigger**: Inside `ResolveTurnAsync()`, after roll resolution.

**Condition**: `rollResult.IsSuccess && _shadowDisadvantagedStats != null && _shadowDisadvantagedStats.Contains(chosenOption.Stat)`

**Note on stat scope**: The condition checks whether the *chosen option's stat* had shadow-based disadvantage, not specifically whether the stat is SA. This is correct because Overthinking specifically imposes disadvantage on SA rolls (via `ShadowThresholdEvaluator`), but the pattern generalizes — any shadow disadvantage overcome by success should reduce the corresponding shadow. However, in the current rules, only Overthinking T2+ causes `_shadowDisadvantagedStats` entries, so the practical effect is always Overthinking −1.

**More precise condition for Overthinking specifically**:
```
chosenOption.Stat == StatType.SelfAwareness
&& _shadowDisadvantagedStats != null
&& _shadowDisadvantagedStats.Contains(StatType.SelfAwareness)
&& rollResult.IsSuccess
```

**Effect**: `_playerShadows.ApplyOffset(ShadowStatType.Overthinking, -1, "Succeeded despite Overthinking disadvantage")` → Overthinking delta decreases by 1.

**Growth event string**: `"Overthinking -1 (Succeeded despite Overthinking disadvantage)"`

### Reduction 5: 4+ Different Stats → Fixation −1

**Already implemented** in `EvaluateEndOfGameShadowGrowth` as trigger 13. No changes needed.

---

## Acceptance Criteria

### AC-1: Dread −1 on DateSecured

In `EvaluateEndOfGameShadowGrowth()`, when `outcome == GameOutcome.DateSecured` and `_playerShadows != null`, call:
```
_playerShadows.ApplyOffset(ShadowStatType.Dread, -1, "Date secured")
```

This is independent of the existing trigger 11 (Denial +1 for date without Honesty). Both may fire on the same `DateSecured` outcome — they affect different shadow stats.

**Verification**: After a game that ends with `DateSecured`, `_playerShadows.GetDelta(ShadowStatType.Dread)` is 1 less than it would be without the reduction. The growth events list (via `DrainGrowthEvents()`) contains an entry matching `"Dread -1 (Date secured)"`.

### AC-2: Denial −1 on Honesty Success at Interest ≥ 15

In `EvaluatePerTurnShadowGrowth()`, after the existing trigger 6 block (`_honestySuccessCount++`), add a check:
- `chosenOption.Stat == StatType.Honesty`
- `rollResult.IsSuccess`  
- `interestAfter >= 15`

If all three are true and `_playerShadows != null`, call:
```
_playerShadows.ApplyOffset(ShadowStatType.Denial, -1, "Honesty success at high interest")
```

**Verification**: When a player succeeds with Honesty and current interest (after the roll's effect) is 15 or higher, `GetDelta(ShadowStatType.Denial)` decreases by 1.

**Note**: This can trigger on the same turn that trigger 6 increments `_honestySuccessCount`. The order does not matter — they are independent effects.

### AC-3: Madness −1 on Successful Recover

In `RecoverAsync()`, on the success branch (where `roll.IsSuccess` is true and the trap is cleared), add:
```
_playerShadows?.ApplyOffset(ShadowStatType.Madness, -1, "Recovered from trope trap")
```

Place this after the trap is cleared and XP is recorded, but before trap timers advance.

**Verification**: After a successful `RecoverAsync()` call, `GetDelta(ShadowStatType.Madness)` is 1 less than before the call. Failed recoveries do NOT trigger this reduction (they still trigger Overthinking +1 as per existing code).

### AC-4: Overthinking −1 on Success with Overthinking Disadvantage

In `ResolveTurnAsync()`, after the roll is resolved and `rollResult` is available, check:
- `rollResult.IsSuccess`
- `_shadowDisadvantagedStats != null`
- `_shadowDisadvantagedStats.Contains(chosenOption.Stat)`
- `_playerShadows != null`

If all conditions are met, call:
```
_playerShadows.ApplyOffset(ShadowStatType.Overthinking, -1, "Succeeded despite Overthinking disadvantage")
```

**Verification**: When a player succeeds on a roll where shadow-based disadvantage was active for the chosen stat, `GetDelta(ShadowStatType.Overthinking)` decreases by 1. When the player fails the roll (disadvantage did its job), no reduction occurs.

### AC-5: Tests for Each Reduction

Each reduction must have at least one positive test (condition met → shadow reduced) and one negative test (condition not met → shadow unchanged):

1. **Dread/DateSecured**: Test game ending with DateSecured → Dread delta decreased. Test game ending with Unmatched → no Dread reduction.
2. **Denial/Honesty**: Test Honesty success at interest 15 → Denial reduced. Test Honesty success at interest 14 → no reduction. Test non-Honesty success at interest 15 → no reduction. Test Honesty failure at interest 15 → no reduction.
3. **Madness/Recover**: Test successful recover → Madness reduced. Test failed recover → no Madness reduction (Overthinking +1 still occurs).
4. **Overthinking/Disadvantage**: Test success with shadow disadvantage active → Overthinking reduced. Test failure with shadow disadvantage → no reduction. Test success without shadow disadvantage → no reduction.

### AC-6: Build Clean

All existing tests (1146+) continue to pass. No compilation warnings. No new public API surface introduced.

---

## Edge Cases

1. **Shadow delta goes negative**: If Dread delta is 0 and DateSecured triggers, delta becomes −1. This is valid — `ApplyOffset` allows negative deltas. The effective shadow value is `baseShadow + delta`, so a negative delta reduces the effective value below the base.

2. **Multiple reductions in one turn**: A single turn could trigger both Denial −1 (Honesty success at ≥15) AND Overthinking −1 (if SA had shadow disadvantage and the chosen stat happens to be... wait, Honesty ≠ SA). In practice, AC-2 and AC-4 cannot co-trigger on the same roll because they require different stats. But AC-2 and other per-turn growth triggers CAN fire on the same turn — e.g., Honesty success at ≥15 triggers Denial −1 while also incrementing `_honestySuccessCount`.

3. **Reduction stacking across turns**: Each reduction fires independently each time its condition is met. A player could get Denial −1 multiple times across turns (once per successful Honesty at ≥15). There is no "once per session" cap unless explicitly stated.

4. **`_playerShadows` is null**: All reduction code must null-check `_playerShadows` before calling `ApplyOffset`. When shadow tracking is not configured (no `GameSessionConfig` with shadow trackers), reductions silently do nothing. `RecoverAsync` already uses the `?.` operator pattern for this.

5. **DateSecured + Dread reduction + Denial growth on same outcome**: If the game ends with DateSecured AND the player never succeeded with Honesty, both trigger 11 (Denial +1) and reduction 1 (Dread −1) fire. These affect different shadow stats and do not conflict.

6. **Overthinking disadvantage on non-SA stat**: In theory, `_shadowDisadvantagedStats` could contain stats other than SA if future shadow threshold rules add them. The current implementation should check the chosen stat against `_shadowDisadvantagedStats` generically, but the `ApplyOffset` always reduces Overthinking specifically (since the issue and rules §7 specify Overthinking −1 for "winning despite Overthinking disadvantage"). If the check should be more generic in the future, that's a separate issue.

7. **RecoverAsync when _playerShadows is null**: The `?.` null-conditional operator ensures no `NullReferenceException`. The reduction simply doesn't fire.

8. **Interest exactly 15**: The condition is `interestAfter >= 15`, so interest of exactly 15 DOES trigger the Denial reduction. This matches the rules text ("at Interest ≥15").

---

## Error Conditions

1. **`ApplyGrowth` called with negative amount**: Would throw `ArgumentOutOfRangeException`. This is why all reductions MUST use `ApplyOffset()`, not `ApplyGrowth()`. This is the single most critical implementation constraint — see Vision Concern #279.

2. **`RecoverAsync` called without active trap**: Throws `InvalidOperationException("Cannot recover: no active trap.")` — this is existing behavior and prevents the Madness reduction from triggering when there's nothing to recover from.

3. **`ResolveTurnAsync` called without `StartTurnAsync`**: Throws `InvalidOperationException("Must call StartTurnAsync before ResolveTurnAsync.")` — existing behavior. The Overthinking reduction code is never reached.

4. **Game already ended**: Both `ResolveTurnAsync` and `RecoverAsync` check `_ended` and throw `GameEndedException`. Shadow reductions cannot fire after the game ends (except via `EvaluateEndOfGameShadowGrowth`, which fires as part of the ending sequence itself).

---

## Dependencies

- **`SessionShadowTracker.ApplyOffset()`** — Must exist and accept negative deltas. Already implemented (verified in source).
- **`SessionShadowTracker.GetDelta()`** — Used in tests to verify reduction occurred. Already implemented.
- **`_shadowDisadvantagedStats` field** — Must be populated by `StartTurnAsync()` before `ResolveTurnAsync()` uses it for the Overthinking check. Already implemented.
- **`ShadowThresholdEvaluator`** — Used by `StartTurnAsync` to determine which stats have shadow disadvantage. Already implemented.
- **`StatBlock.ShadowPairs`** — Maps `StatType.SelfAwareness` → `ShadowStatType.Overthinking`. Already implemented.
- **No external services or libraries required.**
- **No changes to other issues in Sprint 11 are prerequisites.** This issue is self-contained within GameSession, though it touches the same file as 6 other Sprint 11 issues. Sequential implementation is mandatory per Vision Concern #277.

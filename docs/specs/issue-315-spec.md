# Spec: Issue #315 — Vision: #312 must use externalBonus parameter, not deprecated AddExternalBonus()

**Module**: docs/modules/conversation.md

---

## Overview

Issue #312 adds Triple combo bonus to Read/Recover rolls. This vision concern (#315) identifies that the suggested fix in #312 uses the **deprecated** `RollResult.AddExternalBonus()` method instead of the `externalBonus` parameter on `RollEngine.ResolveFixedDC()`. Using the deprecated method causes the Triple bonus to be invisible to failure tier determination, success scale margin, and `MissMargin` — because those values are computed at construction time inside `ResolveFromComponents` and are not retroactively updated by post-construction `AddExternalBonus()` calls.

This spec defines the correct approach: compute the Triple bonus **before** the roll and pass it via the `externalBonus` parameter on `ResolveFixedDC`.

---

## Function Signatures

No new functions are introduced by this issue. The following existing signatures are relevant:

### `RollEngine.ResolveFixedDC` (existing — no changes)

```csharp
// File: src/Pinder.Core/Rolls/RollEngine.cs
public static RollResult ResolveFixedDC(
    StatType stat,
    StatBlock attacker,
    int fixedDc,
    TrapState attackerTraps,
    int level,
    ITrapRegistry trapRegistry,
    IDiceRoller dice,
    bool hasAdvantage     = false,
    bool hasDisadvantage  = false,
    int externalBonus     = 0)   // ← Triple bonus goes HERE
```

### `ComboTracker.ConsumeTripleBonus` (existing — no changes)

```csharp
// File: src/Pinder.Core/Conversation/ComboTracker.cs
public int ConsumeTripleBonus()
// Returns: the bonus value (int) if a triple combo is active, 0 otherwise.
// Side effect: clears the triple bonus so it cannot be consumed again.
```

### `RollResult.AddExternalBonus` (existing — DEPRECATED, must NOT be used)

```csharp
// File: src/Pinder.Core/Rolls/RollResult.cs
[System.Obsolete("Use the externalBonus parameter on RollEngine.Resolve() or ResolveFixedDC() instead.")]
public void AddExternalBonus(int bonus)
// Mutates ExternalBonus field. Updates FinalTotal (computed property) and thus IsSuccess.
// Does NOT update: FailureTier, MissMargin, SuccessScale margin — these are frozen at construction.
```

### `GameSession.ReadAsync` (to be modified by #312)

```csharp
// File: src/Pinder.Core/Conversation/GameSession.cs
public Task<ReadResult> ReadAsync()
```

### `GameSession.RecoverAsync` (to be modified by #312)

```csharp
// File: src/Pinder.Core/Conversation/GameSession.cs
public Task<RecoverResult> RecoverAsync()
```

---

## Input/Output Examples

### Example 1: Triple bonus softens failure tier (correct behavior)

**Setup:**
- Player SA stat modifier: +2
- Level bonus: +0
- DC: 12
- Die roll: 4
- Triple combo bonus: +1

**With `externalBonus` parameter (CORRECT):**
```
total = 4 (roll) + 2 (stat) + 0 (level) = 6
finalTotal = 6 + 1 (externalBonus) = 7
miss = 12 - 7 = 5  (computed inside ResolveFromComponents using finalTotal after #309)
→ FailureTier = Misfire (miss 3–5)
→ Interest delta = -2
```

**With `AddExternalBonus()` (WRONG — deprecated):**
```
total = 4 (roll) + 2 (stat) + 0 (level) = 6
finalTotal at construction = 6 + 0 = 6   (externalBonus was 0)
miss = 12 - 6 = 6                        (computed at construction)
→ FailureTier = TropeTrap (miss 6–9)     (FROZEN — never updated)
→ Interest delta = -3                     (WRONG — too harsh)
// After AddExternalBonus(1):
// FinalTotal becomes 7, but tier is still TropeTrap
```

### Example 2: Triple bonus flips failure to success (both methods work)

**Setup:**
- Player SA stat modifier: +3
- Level bonus: +0
- DC: 12
- Die roll: 8
- Triple combo bonus: +1

```
total = 8 + 3 + 0 = 11
finalTotal = 11 + 1 = 12
12 >= 12 → success
```

Both methods produce `IsSuccess = true` for this case because `IsSuccess` reads `FinalTotal` (a computed property). However, the `externalBonus` parameter approach also correctly computes the success margin (beatDcBy = 0), while `AddExternalBonus()` would produce the same result here only because `FinalTotal` is a computed property.

### Example 3: Triple bonus with zero value (no-op)

**Setup:**
- No active triple combo
- `ConsumeTripleBonus()` returns 0

```
externalBonus: 0
→ Behavior identical to current code (no bonus applied)
→ All existing tests pass unchanged
```

---

## Acceptance Criteria

### AC-1: #312 implementation uses `externalBonus` parameter on `ResolveFixedDC`, not `AddExternalBonus()`

**What to verify:**

In `GameSession.ReadAsync()` (currently at line ~971 of `GameSession.cs`):

1. `ComboTracker.ConsumeTripleBonus()` is called and its **return value is captured** in a local variable (e.g., `int tripleBonus`).
2. The captured value is passed as `externalBonus: tripleBonus` to `RollEngine.ResolveFixedDC()`.
3. `RollResult.AddExternalBonus()` is **not called** anywhere in `ReadAsync()`.

The same pattern must apply in `GameSession.RecoverAsync()` (currently at line ~1083).

**Current code (WRONG — as of pre-#312):**
```csharp
// Line 971: return value discarded
_comboTracker.ConsumeTripleBonus();
// ...
// Line 998-1004: externalBonus not passed
var roll = RollEngine.ResolveFixedDC(
    StatType.SelfAwareness, _player.Stats, 12,
    _traps, _player.Level, _trapRegistry, _dice,
    hasAdvantage, hasDisadvantage);
```

**Required code (CORRECT):**
```csharp
int tripleBonus = _comboTracker.ConsumeTripleBonus();
// ...
var roll = RollEngine.ResolveFixedDC(
    StatType.SelfAwareness, _player.Stats, 12,
    _traps, _player.Level, _trapRegistry, _dice,
    hasAdvantage, hasDisadvantage,
    externalBonus: tripleBonus);
```

### AC-2: Triple bonus affects failure tier severity on Read/Recover rolls

**What to verify:**

When a Read or Recover roll fails with a triple combo active, the `externalBonus` reduces the miss margin used for failure tier determination. Specifically:

- `RollResult.Tier` reflects the reduced miss margin (e.g., miss-by-6 with +1 bonus → miss-by-5 → Misfire, not TropeTrap).
- `RollResult.MissMargin` accounts for the external bonus.

**Note:** This depends on #309 landing first (which changes `int miss = dc - total` to `int miss = dc - finalTotal` in `ResolveFromComponents`). If #309 has not landed, tier determination still uses `total` (without external bonus), and the tier won't change even with the parameter approach. The parameter approach is still correct regardless — it future-proofs the code and is the non-deprecated API.

### AC-3: Test — Read roll TropeTrap with Triple +1 → Misfire

**Test scenario:**

1. Set up a `GameSession` with a mock `IDiceRoller` and `ComboTracker` that has an active triple combo bonus of +1.
2. Configure the dice to roll a value such that `total = DC - 6` (i.e., miss by 6 → TropeTrap tier without bonus).
   - DC = 12, so `total` must be 6.
   - With SA stat mod of +2 and level bonus +0, die roll must be 4.
3. Call `ReadAsync()`.
4. Assert:
   - `ReadResult.Roll.Tier == FailureTier.Misfire` (miss by 5, not 6)
   - `ReadResult.Roll.ExternalBonus == 1`
   - `ReadResult.Roll.FinalTotal == 7`
   - `ReadResult.Roll.IsSuccess == false`
   - `ReadResult.InterestDelta == -2` (Misfire penalty, not TropeTrap -3)

**Without the fix (control test):**
If `AddExternalBonus()` were used instead, the same setup would produce:
- `ReadResult.Roll.Tier == FailureTier.TropeTrap` (miss still computed as 6)
- `ReadResult.InterestDelta == -3`

---

## Edge Cases

### Edge Case 1: Triple bonus of 0

When no triple combo is active, `ConsumeTripleBonus()` returns 0. Passing `externalBonus: 0` to `ResolveFixedDC` is a no-op — all behavior is identical to the current code. No special handling needed.

### Edge Case 2: Triple bonus exactly flips failure to success

If `total = 11` and `DC = 12` with triple bonus +1:
- `finalTotal = 12`, which equals DC → `IsSuccess = true`
- `Tier = FailureTier.None`
- No failure tier to soften — the bonus converted a near-miss into a success

### Edge Case 3: Triple bonus on Nat 1

Nat 1 is always a Legendary failure regardless of modifiers or external bonuses. The `externalBonus` parameter does not change this — `ResolveFromComponents` checks `usedRoll == 1` first, before any total/finalTotal comparison. Triple bonus is still consumed (by design — it was used, just didn't help).

### Edge Case 4: Triple bonus on Nat 20

Nat 20 is always a success regardless. Triple bonus is consumed but has no mechanical effect on the success determination. It may affect `beatDcBy` calculation in `GameSession` if the host cares about the margin.

### Edge Case 5: Triple bonus larger than miss margin

If `total = 10`, `DC = 12`, triple bonus = +5:
- `finalTotal = 15`, success (15 >= 12)
- The bonus converts what would have been a Fumble (miss by 2) into a success with margin +3

### Edge Case 6: Concurrent Read/Recover and Speak triple bonus consumption

`ConsumeTripleBonus()` clears the bonus on first call. If `ReadAsync` is called after `StartTurnAsync` (which also peeks at triple bonus), the bonus is consumed once by whichever action resolves first. This is correct — the triple bonus applies to one roll only.

---

## Error Conditions

### Error 1: `ReadAsync()` called when game has ended

`ReadAsync()` throws `GameEndedException` if `_ended` is true. This behavior is unchanged by this fix. The triple bonus is never consumed in this case (the exception is thrown before `ConsumeTripleBonus()` is called).

### Error 2: `RecoverAsync()` called when game has ended

Same as above — `GameEndedException` thrown before any bonus consumption.

### Error 3: `ComboTracker` is null

`ComboTracker` is initialized in the `GameSession` constructor and is never null. No null check needed.

---

## Dependencies

### Issue Dependencies

| Dependency | Reason | Status |
|---|---|---|
| #309 (FinalTotal for tier/scale) | For Triple bonus to actually affect failure tier, `ResolveFromComponents` must use `finalTotal` in the `miss` calculation. Without #309, passing `externalBonus` is correct but has no effect on tier. | Must land before #312 for full correctness |
| #312 (Triple combo bonus on Read/Recover) | This is the issue that #315 advises. #315 defines HOW #312 must be implemented. | #312 is the implementation vehicle |

### Component Dependencies

| Component | Usage |
|---|---|
| `RollEngine` (Pinder.Core.Rolls) | `ResolveFixedDC()` — receives `externalBonus` parameter |
| `ComboTracker` (Pinder.Core.Conversation) | `ConsumeTripleBonus()` — returns bonus value |
| `GameSession` (Pinder.Core.Conversation) | `ReadAsync()` and `RecoverAsync()` — wiring site |
| `RollResult` (Pinder.Core.Rolls) | `AddExternalBonus()` — must NOT be used (deprecated) |

### Library Dependencies

None. All components are in Pinder.Core (zero external dependencies, .NET Standard 2.0).

---

## Why `AddExternalBonus()` Is Wrong (Technical Detail)

Inside `RollEngine.ResolveFromComponents()` (line 155–210 of `RollEngine.cs`):

```
int total = usedRoll + statMod + levelBonus;        // line 156
int finalTotal = total + externalBonus;              // line 157
```

The `miss` variable (line 169, pre-#309) is computed as `dc - total`. After #309, it becomes `dc - finalTotal`. Either way, this computation happens **once** at construction time.

`AddExternalBonus()` (on `RollResult`, line 50) only mutates the `ExternalBonus` field:
```csharp
public void AddExternalBonus(int bonus) { ExternalBonus += bonus; }
```

This updates the `FinalTotal` computed property (`Total + ExternalBonus`) and thus `IsSuccess` (which compares `FinalTotal >= DC`). But `FailureTier`, `MissMargin`, and any success-scale margin computed inside `ResolveFromComponents` are **set once and never recomputed**.

The `externalBonus` constructor parameter, by contrast, participates in `finalTotal` computation inside `ResolveFromComponents`, which is used for success/fail branching and (after #309) for `miss` margin calculation. This is the only correct path for bonuses that should affect tier severity.

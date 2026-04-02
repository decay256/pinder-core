# Spec: Issue #271 — Nat 20 Should Grant Advantage on Next Roll (§4)

**Module**: docs/modules/conversation.md

---

## Overview

Per rules §4 ("Previous crit (1 roll)"), a Nat 20 on any roll should grant the player advantage on their very next roll. This is currently not implemented — `GameSession` has no tracking of previous crits. This spec defines a `_pendingCritAdvantage` flag in `GameSession` that is set when any roll produces a Nat 20 and consumed (granting advantage, then clearing) on the next roll, regardless of action type (Speak, Read, or Recover).

---

## Function Signatures

No new public methods are added. The change is internal to `GameSession`. The following existing methods are modified:

### `GameSession` — New Private Field

```csharp
private bool _pendingCritAdvantage;
```

Initialized to `false`. Set to `true` after any roll where `RollResult.IsNatTwenty == true`. Consumed (read then cleared to `false`) at the start of the next roll.

### `GameSession.StartTurnAsync() → Task<TurnStart>`

**Modification:** Before computing advantage for the Speak path, check `_pendingCritAdvantage`. If `true`, set `hasAdvantage = true` and clear the flag.

Current advantage computation (line ~230):
```csharp
bool hasAdvantage = _interest.GrantsAdvantage;
```

New advantage computation:
```csharp
bool hasAdvantage = _interest.GrantsAdvantage;
if (_pendingCritAdvantage)
{
    hasAdvantage = true;
    _pendingCritAdvantage = false;
}
```

The flag is consumed here because `StartTurnAsync` computes the advantage state that is stored in `_currentHasAdvantage` and used by `ResolveTurnAsync`.

### `GameSession.ResolveTurnAsync(int optionIndex) → Task<TurnResult>`

**Modification:** After the roll resolves, check if the result is a Nat 20. If so, set the flag for the next turn.

Location: After `RollEngine.Resolve(...)` returns `rollResult` (approximately line 440+):
```csharp
if (rollResult.IsNatTwenty)
{
    _pendingCritAdvantage = true;
}
```

This must happen regardless of whether the roll was already a success for other reasons.

### `GameSession.ReadAsync() → Task<ReadResult>`

**Modification (per vision concern #280):** Read is a self-contained action that does NOT call `StartTurnAsync`. Therefore, crit advantage must be consumed directly within `ReadAsync`.

Before the `RollEngine.ResolveFixedDC(...)` call (current line ~933):
```csharp
if (_pendingCritAdvantage)
{
    hasAdvantage = true;
    _pendingCritAdvantage = false;
}
```

After the roll resolves, if `roll.IsNatTwenty`, set `_pendingCritAdvantage = true`.

### `GameSession.RecoverAsync() → Task<RecoverResult>`

**Modification (per vision concern #280):** Identical pattern to `ReadAsync`. Consume the flag before the roll, set it after a Nat 20.

Before the `RollEngine.ResolveFixedDC(...)` call (current line ~1022):
```csharp
if (_pendingCritAdvantage)
{
    hasAdvantage = true;
    _pendingCritAdvantage = false;
}
```

After the roll resolves, if `roll.IsNatTwenty`, set `_pendingCritAdvantage = true`.

---

## Input/Output Examples

### Example 1: Speak Nat 20 → Next Speak Has Advantage

**Setup:** Player in a session, interest at 10 (Interested state, no interest-based advantage).

- **Turn 1:** `StartTurnAsync()` → `_pendingCritAdvantage` is `false`, `hasAdvantage = false`. `ResolveTurnAsync(0)` → dice roll is natural 20 → `rollResult.IsNatTwenty == true` → `_pendingCritAdvantage` set to `true`.
- **Turn 2:** `StartTurnAsync()` → `_pendingCritAdvantage` is `true` → `hasAdvantage = true`, flag cleared to `false`. `ResolveTurnAsync(0)` → dice roll is 14 (not Nat 20) → `_pendingCritAdvantage` stays `false`.
- **Turn 3:** `StartTurnAsync()` → `_pendingCritAdvantage` is `false` → `hasAdvantage = false` (no crit bonus).

### Example 2: Speak Nat 20 → Read Has Advantage

**Setup:** Same as above.

- **Turn 1:** `ResolveTurnAsync(0)` → Nat 20 → `_pendingCritAdvantage = true`.
- **Turn 2:** `ReadAsync()` → `_pendingCritAdvantage` is `true` → `hasAdvantage = true`, flag cleared. Roll resolves with advantage (two dice rolled, higher used). Roll result is 15 (not Nat 20) → flag stays `false`.

### Example 3: Read Nat 20 → Next Speak Has Advantage

- **Turn 1:** `ReadAsync()` → `_pendingCritAdvantage` is `false`, no crit advantage. Roll is Nat 20 → `_pendingCritAdvantage = true`.
- **Turn 2:** `StartTurnAsync()` → `_pendingCritAdvantage` is `true` → `hasAdvantage = true`, flag cleared.

### Example 4: Crit Advantage Stacks with Interest-Based Advantage

**Setup:** Interest at 17 (VeryIntoIt → `GrantsAdvantage == true`).

- **Turn N-1:** Nat 20 → `_pendingCritAdvantage = true`.
- **Turn N:** `StartTurnAsync()` → `_interest.GrantsAdvantage` is already `true`, `_pendingCritAdvantage` is also `true` → `hasAdvantage = true` (both sources agree). Flag cleared. **Advantage is boolean — it does not "double stack"** (no extra dice beyond the normal 2-dice-take-higher mechanic).

---

## Acceptance Criteria

### AC1: Nat 20 on turn N → hasAdvantage on turn N+1

When `ResolveTurnAsync` produces a `RollResult` where `IsNatTwenty == true`, the subsequent call to `StartTurnAsync` (or `ReadAsync`/`RecoverAsync`) must resolve the roll with `hasAdvantage: true`.

**Verification:** In a test, configure dice to return 20 on the first Speak roll. On the next `StartTurnAsync`, assert the roll in `ResolveTurnAsync` is called with advantage (two dice rolled, higher used). The `TurnResult` or roll mechanics should reflect advantage.

### AC2: Advantage clears after one roll

After the crit advantage is consumed by one roll (whether Speak, Read, or Recover), subsequent rolls must NOT have crit-based advantage.

**Verification:** Configure dice to return 20 on turn 1, then non-20 values on turns 2 and 3. Assert turn 2 has advantage, turn 3 does not.

### AC3: Stacks correctly with interest-based advantage

When both crit advantage and interest-based advantage (`InterestMeter.GrantsAdvantage`) are active, the player still receives advantage (not double advantage — advantage is a boolean).

**Verification:** Set interest to VeryIntoIt range (16-20) so `GrantsAdvantage == true`. Also have `_pendingCritAdvantage == true`. Assert `hasAdvantage` resolves to `true`. Confirm only one advantage roll (two dice, take higher) — not two separate advantage sources producing additional dice.

### AC4: Tests verify — Nat 20 → next turn advantage; two turns after Nat 20 → no advantage

Explicit test cases:
1. Nat 20 on Speak → next Speak has advantage.
2. Nat 20 on Speak → two turns later, no crit advantage.
3. Nat 20 on Read → next Speak has advantage.
4. Nat 20 on Speak → next Read has advantage.
5. Nat 20 on Recover → next action has advantage.

### AC5: Build clean

All existing tests (1146+) must pass. No new compiler warnings. The `_pendingCritAdvantage` field default of `false` ensures backward compatibility — sessions that never roll Nat 20 are unaffected.

---

## Edge Cases

### E1: Consecutive Nat 20s

If the player rolls Nat 20 on turn N and Nat 20 again on turn N+1 (which already has crit advantage), the flag is set again after the turn N+1 roll. So turn N+2 also has advantage. The flag is a simple boolean — it doesn't accumulate.

### E2: Nat 20 on the final turn (DateSecured / Ghosted)

If a Nat 20 occurs on the turn that ends the game (interest reaches 25 or 0), `_pendingCritAdvantage` is set to `true` but never consumed because no further turns occur. This is harmless — the flag is just orphaned state.

### E3: Nat 20 on Read failure

`ReadAsync` can produce a Nat 20 on the DC 12 SA roll. Even though Read failure causes −1 interest and Overthinking +1, a Nat 20 always succeeds (`IsSuccess` is `true` when `IsNatTwenty` is `true` per `RollResult` constructor: `IsSuccess = IsNatTwenty || ...`). So this edge case is actually a Read success with crit advantage set for next turn.

### E4: Wait action between crit and next roll

`Wait()` does not involve a roll — it applies −1 interest and advances trap timers. If the player rolls Nat 20, then calls `Wait()`, the `_pendingCritAdvantage` flag should persist through `Wait()` and be consumed on the next actual roll (Speak, Read, or Recover). `Wait()` must NOT consume or clear the flag.

### E5: Crit advantage + interest disadvantage

If the player has `_pendingCritAdvantage == true` AND `_interest.GrantsDisadvantage == true` (Bored state), both apply: `hasAdvantage = true` AND `hasDisadvantage = true`. Per `RollEngine.Resolve` semantics, when both advantage and disadvantage are true, they cancel out and the roll is normal (single die). The crit flag is still consumed.

### E6: Read/Recover called without prior StartTurnAsync

Read and Recover are self-contained — they don't require `StartTurnAsync()`. The crit advantage flag must be checked and consumed directly within `ReadAsync()` and `RecoverAsync()`, not rely on `StartTurnAsync()` having run.

---

## Error Conditions

### No new error conditions

This feature adds no new exceptions or failure modes. The `_pendingCritAdvantage` field is a simple boolean with a safe default (`false`). All existing error conditions (`GameEndedException`, `InvalidOperationException` for missing `StartTurnAsync`) are unchanged.

### Potential implementation error: Flag not consumed in Read/Recover

If the implementer only adds consumption logic to `StartTurnAsync` but not to `ReadAsync`/`RecoverAsync`, crit advantage from a Speak Nat 20 would be "stuck" until the next Speak turn. This violates the §4 rule ("next roll" means the very next roll regardless of action type). Vision concern #280 explicitly requires Read/Recover participation.

---

## Dependencies

- **`RollResult.IsNatTwenty`** (existing, `Rolls/RollResult.cs`): Boolean property, `true` when the natural die roll is 20. Already implemented and tested.
- **`RollEngine.Resolve()` / `RollEngine.ResolveFixedDC()`** (existing, `Rolls/RollEngine.cs`): Accept `hasAdvantage` boolean parameter. When `true`, two dice are rolled and the higher value is used. Already implemented.
- **`InterestMeter.GrantsAdvantage`** (existing, `Conversation/InterestMeter.cs`): Returns `true` when interest state is VeryIntoIt or AlmostThere. Crit advantage is additive (OR) with this.
- **No external dependencies.** This is purely internal `GameSession` state management.
- **No cross-issue dependencies within Sprint 11.** This issue can be implemented independently, though it touches the same file as 6 other issues (#268, #269, #260, #270, #272, #273). Sequential implementation is required per vision concern #277.

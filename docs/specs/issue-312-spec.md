# Spec: Issue #312 — Triple combo bonus not applied to Read/Recover rolls

**Module**: docs/modules/conversation.md

---

## Overview

The Triple combo (3 different stats played in 3 consecutive turns) grants a +1 roll bonus on the next turn. Currently, `GameSession.ReadAsync()` and `GameSession.RecoverAsync()` call `_comboTracker.ConsumeTripleBonus()` to consume the bonus, but never pass the bonus value to the `RollEngine.ResolveFixedDC()` call. The bonus is silently discarded. This issue fixes both methods to pass the triple bonus as the `externalBonus` parameter so it actually affects the roll outcome.

---

## Function Signatures

No new public functions are introduced. Two existing private-scope code paths within `GameSession` are modified:

### `GameSession.ReadAsync()`

```csharp
public Task<ReadResult> ReadAsync()
```

**Internal change**: The triple bonus value (0 or 1) must be captured from `_comboTracker.ConsumeTripleBonus()` and passed as `externalBonus` to `RollEngine.ResolveFixedDC()`.

### `GameSession.RecoverAsync()`

```csharp
public Task<RecoverResult> RecoverAsync()
```

**Internal change**: Same pattern — capture triple bonus and pass as `externalBonus`.

### Referenced APIs

#### `ComboTracker.HasTripleBonus` (read-only property)

```csharp
public bool HasTripleBonus { get; }
```

Returns `true` when The Triple combo was completed on a previous turn and the +1 bonus has not yet been consumed.

#### `ComboTracker.ConsumeTripleBonus()` (existing method)

```csharp
public void ConsumeTripleBonus()
```

Sets `_pendingTripleBonus = false`. Returns `void`. Currently called in both `ReadAsync()` and `RecoverAsync()` but the boolean state is not read beforehand.

#### `RollEngine.ResolveFixedDC()` (existing static method)

```csharp
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
    int externalBonus     = 0)
```

The `externalBonus` parameter (default 0) is added to the roll total to produce `FinalTotal`. It affects success/fail determination via `RollResult.IsSuccess` (which compares `FinalTotal >= DC`), failure tier severity, and success scale margin.

---

## Input/Output Examples

### Example 1: Read with Triple bonus active

**Setup**: Player has completed The Triple combo on the previous Speak turn. `_comboTracker.HasTripleBonus == true`. Player calls `ReadAsync()`.

- Dice rolls: 8
- SA modifier: +2
- Level bonus: +1
- Triple bonus: +1 (consumed)
- Total = 8 + 2 + 1 = 11
- FinalTotal = 11 + 1 = 12
- DC = 12
- Result: `IsSuccess = true` (FinalTotal 12 ≥ DC 12)

Without the fix, `externalBonus` would be 0, `FinalTotal` = 11, and the roll would fail (11 < 12).

### Example 2: Read without Triple bonus

**Setup**: No triple bonus active. `_comboTracker.HasTripleBonus == false`.

- Dice rolls: 8, SA +2, Level +1
- Triple bonus: 0
- FinalTotal = 11
- DC = 12
- Result: `IsSuccess = false`

Behavior is identical to current implementation — no regression.

### Example 3: Recover with Triple bonus active

**Setup**: Player has an active trap and Triple bonus. Calls `RecoverAsync()`.

- Dice rolls: 10
- SA modifier: +1
- Level bonus: +0
- Triple bonus: +1 (consumed)
- Total = 10 + 1 + 0 = 11
- FinalTotal = 11 + 1 = 12
- DC = 12
- Result: `IsSuccess = true`, trap cleared

### Example 4: Triple bonus consumed even on failure

**Setup**: Triple bonus active. Dice rolls: 3.

- SA +1, Level +0, Triple +1
- Total = 4, FinalTotal = 5
- DC = 12
- Result: `IsSuccess = false`
- Triple bonus is still consumed (not preserved for retry)

---

## Acceptance Criteria

### AC1: Read roll includes Triple bonus when active

When `_comboTracker.HasTripleBonus` is `true` at the time `ReadAsync()` executes, the +1 bonus must be passed as the `externalBonus` parameter to `RollEngine.ResolveFixedDC()`. The resulting `RollResult.FinalTotal` must equal `Total + 1`. The `RollResult.ExternalBonus` field must be `1`.

### AC2: Recover roll includes Triple bonus when active

When `_comboTracker.HasTripleBonus` is `true` at the time `RecoverAsync()` executes, the +1 bonus must be passed as the `externalBonus` parameter to `RollEngine.ResolveFixedDC()`. The resulting `RollResult.FinalTotal` must equal `Total + 1`. The `RollResult.ExternalBonus` field must be `1`.

### AC3: Triple bonus consumed correctly after Read or Recover

After `ReadAsync()` or `RecoverAsync()` completes (regardless of success or failure), `_comboTracker.HasTripleBonus` must be `false`. The bonus must not carry over to subsequent turns.

### AC4: Tests verify bonus application and absence

Tests must cover:
1. Triple bonus active → `ReadAsync()` → roll has `ExternalBonus == 1`
2. Triple bonus not active → `ReadAsync()` → roll has `ExternalBonus == 0`
3. Triple bonus active → `RecoverAsync()` → roll has `ExternalBonus == 1`
4. Triple bonus not active → `RecoverAsync()` → roll has `ExternalBonus == 0`
5. After Read/Recover with triple active → `HasTripleBonus == false`

### AC5: Build clean

The solution must compile with zero errors and zero warnings. All existing tests (1718+) must continue to pass.

---

## Edge Cases

### Triple bonus + advantage/disadvantage stacking

The triple bonus is independent of advantage/disadvantage. If the player has both advantage (e.g., from VeryIntoIt interest state) and the triple bonus, both apply: advantage affects dice selection (take higher of two d20 rolls), while the +1 external bonus is added to the total afterward. No special interaction logic is needed — `ResolveFixedDC` handles both parameters independently.

### Triple bonus + shadow-based SA disadvantage

If the player has Overthinking at T2+ (which grants SA disadvantage for Read/Recover), the triple bonus still applies. Disadvantage takes the lower of two dice rolls, but the +1 external bonus is still added. These are orthogonal mechanics.

### Triple bonus on Wait action

`Wait()` does not make a roll, so the triple bonus is simply consumed (already handled by existing `_comboTracker.ConsumeTripleBonus()` in the `Wait()` method at line ~1203). This issue does not change `Wait()` behavior.

### No active trap for Recover

If `RecoverAsync()` is called with no active traps, it returns early before reaching the roll. The current code consumes the triple bonus before the no-trap check. After this fix, the bonus check must still occur before the early return (preserving current consumption order), OR must be moved after the no-trap guard so the bonus is not wasted. **The implementer should preserve the current ordering** — consume before the guard — since this matches the existing behavior and the rules do not specify that the bonus should be preserved if Recover is called with no traps (that's a player error).

### Triple bonus value is always +1

The Triple combo always grants exactly +1. This is defined by the rules (§15: "The Triple — 3 different stats in 3 turns — +1 to all rolls next turn"). The implementation should read `HasTripleBonus` to determine the value (1 if true, 0 if false) rather than hardcoding, in case the bonus value changes in the future.

### Interaction with deprecated `AddExternalBonus()`

Per the architecture ADR (#146, #315), the triple bonus must use the `externalBonus` parameter on `ResolveFixedDC()`, NOT the deprecated `AddExternalBonus()` method on `RollResult`. The parameter approach ensures the bonus affects `FinalTotal` computation, which in turn affects failure tier determination and success scale margin (after issue #309 lands). `AddExternalBonus()` does not retroactively update the tier.

---

## Error Conditions

### No new error conditions

This change does not introduce any new failure modes. The `externalBonus` parameter on `ResolveFixedDC()` already exists and defaults to 0. Passing 0 or 1 are both valid values.

### Potential regression: bonus consumed but not applied

If the implementer consumes the bonus (`ConsumeTripleBonus()`) but forgets to pass it to the roll call, the bug remains. Tests in AC4 specifically guard against this by asserting `ExternalBonus == 1` on the resulting `RollResult`.

---

## Dependencies

### Code dependencies

- **`ComboTracker`** (`Pinder.Core.Conversation`) — provides `HasTripleBonus` and `ConsumeTripleBonus()`
- **`RollEngine.ResolveFixedDC()`** (`Pinder.Core.Rolls`) — accepts `externalBonus` parameter (already exists)
- **`RollResult`** (`Pinder.Core.Rolls`) — exposes `ExternalBonus` and `FinalTotal` for test assertions

### Issue dependencies

- **Issue #309** (FinalTotal for tier/scale) — should land first so that the external bonus properly affects failure tier and success margin calculations. However, this issue is independently implementable: the `externalBonus` parameter already flows into `RollResult.FinalTotal` and `IsSuccess` regardless of #309.
- **Issue #315** (vision concern advisory) — mandates using `externalBonus` parameter, not `AddExternalBonus()`. This spec incorporates that guidance.

### No external dependencies

No external services, NuGet packages, or infrastructure changes required. All changes are within `Pinder.Core`.

---

## Implementation Notes (for implementer reference)

### Current code (ReadAsync, ~line 970-995)

```
_comboTracker.ConsumeTripleBonus();    // ← consumes but discards
// ... advantage/disadvantage logic ...
var roll = RollEngine.ResolveFixedDC(
    StatType.SelfAwareness, _player.Stats, 12,
    _traps, _player.Level, _trapRegistry, _dice,
    hasAdvantage, hasDisadvantage);     // ← externalBonus defaults to 0
```

### Required change pattern

```
int tripleBonus = _comboTracker.HasTripleBonus ? 1 : 0;
_comboTracker.ConsumeTripleBonus();
// ... advantage/disadvantage logic ...
var roll = RollEngine.ResolveFixedDC(
    StatType.SelfAwareness, _player.Stats, 12,
    _traps, _player.Level, _trapRegistry, _dice,
    hasAdvantage, hasDisadvantage,
    externalBonus: tripleBonus);
```

Apply the same pattern in `RecoverAsync()` (~line 1082-1107).

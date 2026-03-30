# Spec: Fix Failing Combo Test — FixedDice Queue Exhausted (Issue #209)

## Overview

The test `ComboGameSessionSpecTests.AC4_Integration_TripleBonusAppliedAsExternalBonus` fails with `FixedDice: no more values in queue` because the `FixedDice` queue does not account for all `dice.Roll()` calls made during the test's 4-turn sequence. Specifically, Turn 4 triggers advantage (from `InterestState.VeryIntoIt`), which causes `RollEngine.Resolve` to roll **two** d20s instead of one. The test was written before `TimingProfile.ComputeDelay` was added, or before the interest level was high enough to trigger advantage — either way, the dice queue is short by at least one value.

## Root Cause Analysis

### Dice Consumption Per `ResolveTurnAsync` Call

Each `ResolveTurnAsync` call consumes dice values from the shared `IDiceRoller` in this order:

1. **`RollEngine.Resolve`** — `dice.Roll(20)` × 1 (normal) or × 2 (if advantage or disadvantage)
2. **`TimingProfile.ComputeDelay`** — `dice.Roll(100)` × 1 (always, for variance calculation)

### The Test's Dice Budget

The test provides 8 values: `15, 50, 15, 50, 15, 50, 15, 50` (4 pairs of d20 + d100).

### Why It Exhausts

| Turn | Interest Before | State Before | Advantage? | d20 Rolls | d100 (ComputeDelay) | Values Consumed | Cumulative |
|------|----------------|--------------|------------|-----------|---------------------|-----------------|------------|
| 1    | 10             | Interested   | No         | 1         | 1                   | 2               | 2          |
| 2    | 12             | Interested   | No         | 1         | 1                   | 2               | 4          |
| 3    | 14             | Interested   | No         | 1         | 1                   | 2               | 6          |
| 4    | 18             | VeryIntoIt   | **Yes**    | **2**     | 1                   | **3**           | **9**      |

Total needed: **9**. Total provided: **8**. Queue exhausts on Turn 4's `ComputeDelay` call (the 9th dice request).

### Interest Progression Detail

Each successful turn with d20=15, stat modifier=2, DC=15:
- `Total = 15 + 2 = 17`, beats DC 15 by 2 → `SuccessScale`: +1 interest
- `RiskTier`: need = DC − statMod − levelBonus = 15 − 2 − 0 = 13 → Hard → `RiskTierBonus`: +1
- Momentum: streak 1→0, streak 2→0, streak 3→+2, streak 4→+2

| Turn | Success Delta | Risk Bonus | Momentum | Combo Bonus | Total Delta | Interest After |
|------|--------------|------------|----------|-------------|-------------|----------------|
| 1    | +1           | +1         | 0        | 0           | +2          | 12             |
| 2    | +1           | +1         | 0        | 0           | +2          | 14             |
| 3    | +1           | +1         | +2       | 0 (Triple)  | +4          | 18             |
| 4    | +1           | +1         | +2       | 0           | +4          | 22             |

At interest 18, `InterestMeter.GetState()` returns `VeryIntoIt`, which sets `GrantsAdvantage = true`. `StartTurnAsync` stores this in `_currentHasAdvantage`. `ResolveTurnAsync` passes it to `RollEngine.Resolve`, which rolls **two** d20s (taking the higher).

## File Locations

| File | Path | Role |
|------|------|------|
| Failing test | `tests/Pinder.Core.Tests/ComboSpecTests.cs` | Line ~821: `AC4_Integration_TripleBonusAppliedAsExternalBonus` |
| FixedDice | `tests/Pinder.Core.Tests/GameSessionTests.cs` | Line ~17: `FixedDice` test double (queue-based `IDiceRoller`) |
| TimingProfile | `src/Pinder.Core/Conversation/TimingProfile.cs` | Line ~41: `ComputeDelay(int, IDiceRoller)` — calls `dice.Roll(100)` |
| GameSession | `src/Pinder.Core/Conversation/GameSession.cs` | Line ~532: calls `ComputeDelay`; Line ~398: calls `RollEngine.Resolve` |
| RollEngine | `src/Pinder.Core/Rolls/RollEngine.cs` | Line ~52-53: rolls 1 or 2 d20s based on advantage/disadvantage |
| InterestMeter | `src/Pinder.Core/Conversation/InterestMeter.cs` | `GrantsAdvantage` — true when `VeryIntoIt` or `AlmostThere` |

## Function Signatures (Relevant)

```csharp
// FixedDice (test double)
public sealed class FixedDice : IDiceRoller
{
    public FixedDice(params int[] values);
    public void Enqueue(params int[] values);
    public int Roll(int sides);  // Dequeues next value; throws InvalidOperationException if empty
}

// TimingProfile
public int ComputeDelay(int interestLevel, IDiceRoller dice);
// Calls dice.Roll(100) once per invocation

// RollEngine.Resolve (simplified)
public static RollResult Resolve(
    StatType stat, StatBlock attacker, StatBlock defender,
    TrapState attackerTraps, int level, ITrapRegistry trapRegistry,
    IDiceRoller dice,
    bool hasAdvantage = false, bool hasDisadvantage = false,
    int externalBonus = 0, int dcAdjustment = 0);
// Calls dice.Roll(20) once normally, twice if hasAdvantage or hasDisadvantage

// InterestMeter
public bool GrantsAdvantage { get; }
// Returns true when state is VeryIntoIt (16-20) or AlmostThere (21-24)
```

## Fix Specification

### Recommended Fix: Add Extra Dice Values

Update the `FixedDice` constructor call in `AC4_Integration_TripleBonusAppliedAsExternalBonus` to provide **9** values instead of 8. The extra value covers the second d20 roll on Turn 4 (advantage from `VeryIntoIt`).

**New dice queue:**
```
Turn 1: 15, 50       — d20(15), d100(50)
Turn 2: 15, 50       — d20(15), d100(50)
Turn 3: 15, 50       — d20(15), d100(50)
Turn 4: 15, 15, 50   — d20(15), d20(15) [advantage: take max], d100(50)
```

That is: `new FixedDice(15, 50, 15, 50, 15, 50, 15, 15, 50)` — 9 values.

**Why this works:** The two d20 values for Turn 4 can both be 15 (advantage takes the max, `Math.Max(15, 15) = 15`). This preserves the same `Total = 17` and the test's assertions about `ExternalBonus = 1` remain valid since the roll outcome is unchanged.

**Why alternatives are worse:**
- Lowering dice values to prevent VeryIntoIt would change the test's semantics (it tests Triple combo, not interest states)
- Creating a new test double for TimingProfile is over-engineering for a 1-line fix
- Neither alternative addresses the root cause as directly

### Changes Required

**Only one file is modified:** `tests/Pinder.Core.Tests/ComboSpecTests.cs`

**Only one line changes:** The `FixedDice` constructor call (line ~824-828).

**No production code changes.**

## Input/Output Examples

### Before Fix (Failing)
```
FixedDice queue: [15, 50, 15, 50, 15, 50, 15, 50]
Turn 1 ResolveTurn: dequeue 15 (d20), dequeue 50 (d100) → queue: [15, 50, 15, 50, 15, 50]
Turn 2 ResolveTurn: dequeue 15 (d20), dequeue 50 (d100) → queue: [15, 50, 15, 50]
Turn 3 ResolveTurn: dequeue 15 (d20), dequeue 50 (d100) → queue: [15, 50]
Turn 4 ResolveTurn: dequeue 15 (d20), dequeue 50 (d20 advantage) → queue: []
Turn 4 ComputeDelay: dequeue ??? → THROWS InvalidOperationException
```

### After Fix (Passing)
```
FixedDice queue: [15, 50, 15, 50, 15, 50, 15, 15, 50]
Turn 1 ResolveTurn: dequeue 15 (d20), dequeue 50 (d100) → queue: [15, 50, 15, 50, 15, 15, 50]
Turn 2 ResolveTurn: dequeue 15 (d20), dequeue 50 (d100) → queue: [15, 50, 15, 15, 50]
Turn 3 ResolveTurn: dequeue 15 (d20), dequeue 50 (d100) → queue: [15, 15, 50]
Turn 4 ResolveTurn: dequeue 15 (d20), dequeue 15 (d20 advantage) → queue: [50]
Turn 4 ComputeDelay: dequeue 50 (d100) → queue: [] ✓
```

## Acceptance Criteria

### AC1: `ComboGameSessionSpecTests.AC4_Integration_TripleBonusAppliedAsExternalBonus` Passes

- The test must complete without throwing `InvalidOperationException`
- All existing assertions in the test must continue to pass:
  - Turn 3 triggers "The Triple" combo (`r3.ComboTriggered == "The Triple"`)
  - Turn 3 sets `TripleBonusActive` (`r3.StateAfter.TripleBonusActive == true`)
  - Turn 4 start shows `TripleBonusActive` (`start4.State.TripleBonusActive == true`)
  - Turn 4 roll has `ExternalBonus == 1` (`r4.Roll.ExternalBonus == 1`)
  - Turn 4 consumes the bonus (`r4.StateAfter.TripleBonusActive == false`)

### AC2: All Other Tests Continue to Pass

- The full test suite (`dotnet test`) must exit with code 0
- No existing test behavior is altered
- The fix is limited to the test file — no production code changes required

### AC3: `dotnet test` Exits 0

- The solution must build and all tests (currently 1139+) must pass

## Edge Cases

1. **Future interest changes causing advantage on earlier turns:** If game constants change such that interest reaches VeryIntoIt before Turn 4, additional dice values would be needed for earlier turns. The implementer should add a comment documenting the dice budget per turn.

2. **Disadvantage also rolls twice:** If the test were modified so that interest drops to Bored (1-4), disadvantage would also cause two d20 rolls. The current test doesn't hit this case.

3. **Ghost trigger in Bored state:** If interest ever dropped to Bored during the test, `StartTurnAsync` would call `dice.Roll(4)` for ghost check. The current test stays above Bored throughout.

4. **Trap-based advantage/disadvantage:** Active traps can add disadvantage inside `RollEngine.Resolve`. The test uses `NullTrapRegistry` so this doesn't apply.

## Error Conditions

| Condition | Error | Resolution |
|-----------|-------|------------|
| FixedDice queue exhausted | `InvalidOperationException("FixedDice: no more values in queue.")` | Add sufficient dice values to cover all `Roll()` calls |
| Wrong ExternalBonus value | `Assert.Equal` failure on `r4.Roll.ExternalBonus` | Ensure Triple bonus flag is still active and consumed correctly — dice values should not change the combo detection logic |

## Dependencies

- **No external dependencies.** This is a test-only fix.
- **No production code changes required.**
- **No other issues block or are blocked by this fix.**
- This fix should be applied first in the Sprint 9 implementation order to establish a clean test baseline.

# Finding 3 Resolution: Absence of Dedicated, Focused Unit Tests for RollResolutionStage Custom Mechanics

## Overview
An audit of the `Pinder.Core` codebase highlighted that while `RollResolutionStage.cs` implements crucial state-transition and mathematical logic for the RPG engine, it lacked a dedicated unit test suite. To secure the math engine against regressions, we created a comprehensive unit test suite in `RollResolutionStageTests.cs` targeting all discrete custom rules, edge cases, and modifiers in isolation.

## Key Test Cases Implemented

1. **Self-Awareness Trap Disarming**
   - Verified that selecting a dialogue option using the `SelfAwareness` stat successfully clears any active trap in the session state and returns the correct trap name.
   - Verified that selecting other stats keeps the active trap intact.

2. **Denial Shadow Growth on Skipping Honesty**
   - Verified that when Honesty is available but skipped by choosing a different stat option, Denial shadow growth of `+1` is applied.
   - Verified that choosing Honesty or having Honesty absent from the options list does not trigger Denial growth.
   - Handled null-safety scenarios where the shadow tracker is absent.

3. **Callback & Tell Bonus Calculations**
   - Verified that callback bonuses are computed correctly based on the target and current turn numbers.
   - Verified that matching active tells applies a `+4` tell bonus, and that the tell is consumed after the turn regardless of a match.

4. **Triple Combo Bonus Processing**
   - Verified completion and consumption of The Triple combo bonus, ensuring a `+2` external bonus is applied and the state is properly reset.

5. **Weakness and Global Difficulty Adjustments**
   - Verified that active weakness windows on defending stats apply correct DC reductions and are subsequently cleared.
   - Verified that `globalDcBias` adjusts the final DC symmetrically (positive bias lowers the DC).

6. **Shadow Threshold Disadvantages**
   - Verified that shadow-based disadvantages force disadvantageous roll resolution mechanics (rolling twice, taking the lower).

7. **Overthinking Shadow Reductions**
   - Verified that succeeding despite Overthinking disadvantage correctly reduces the Overthinking shadow level by `1`.
   - Verified that succeeding at high interest (>= 20) lift pressure and reduces Overthinking shadow level by `1`.

8. **End-of-Game & Dread Triggers**
   - Verified that when interest drops to zero, the game ends in an `Unmatched` outcome, triggering a `+1` Dread shadow growth.

## Verification Result
All 15 targeted tests compile and pass successfully on the target framework.
- **Project file:** `Pinder.Core.Tests.csproj`
- **Filter used:** `FullyQualifiedName~RollResolutionStageTests`
- **Execution status:** Passed (15 passed, 0 failed, 0 skipped).

# Contract: Issue #209 — Fix Failing Combo Test

## Component
`tests/Pinder.Core.Tests/ComboSpecTests.cs` — test fix only, no production code changes

## Maturity: Prototype

---

## Problem
`ComboGameSessionSpecTests.AC4_Integration_TripleBonusAppliedAsExternalBonus` fails with:
```
System.InvalidOperationException: FixedDice: no more values in queue.
```

Stack trace shows `TimingProfile.ComputeDelay` calls `dice.Roll()` but the test's `FixedDice` queue doesn't include values for timing rolls.

## Root Cause
`TimingProfile.ComputeDelay` was added after this test was written. Each `ResolveTurnAsync` call now requires additional dice rolls beyond the d20 roll resolution.

## Fix Approach

**Option A (preferred):** Add sufficient dice values to the `FixedDice` queue for timing rolls. Count how many `dice.Roll()` calls happen per `ResolveTurnAsync`:
1. `RollEngine.Resolve()` → 1 roll (d20)
2. `TimingProfile.ComputeDelay()` → N rolls (check TimingProfile implementation for exact count)

Provide enough values for all turns in the test.

**Option B (alternative):** If TimingProfile is not meaningful for this test, modify the test setup to use a character profile with a `TimingProfile` that doesn't call dice (if such configuration exists).

## Acceptance Criteria
- `ComboGameSessionSpecTests.AC4_Integration_TripleBonusAppliedAsExternalBonus` passes
- All other 1118 tests continue to pass
- `dotnet test` exits 0
- No changes to production code

## Dependencies
None

## Consumers
None (test-only fix)

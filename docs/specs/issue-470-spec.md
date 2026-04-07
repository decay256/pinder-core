**Module**: docs/modules/testing.md (create new)

## Overview
This specification addresses a testing vulnerability in `Issue463_RuleResolverWiringTests.cs` where conditional assertions allow tests to vacuously pass if a dice roll does not produce the expected success/failure outcome. By replacing four conditional statements (`if (result.Roll.IsSuccess)` and `if (!result.Roll.IsSuccess)`) with explicit boolean assertions (`Assert.True` and `Assert.False`), the test suite guarantees that roll outcomes occur as expected before asserting the subsequent behavioral changes. This prevents false positives during game rules refactoring.

## Function Signatures
The following test methods in `Pinder.Core.Tests.Issue463_RuleResolverWiringTests` will be modified:
```csharp
public async Task ResolveTurn_OnFailure_CallsResolverForFailureDelta()
public async Task ResolveTurn_OnFailure_UsesResolverValue()
public async Task ResolveTurn_OnNonNat20Success_CallsResolverForXpMultiplier()
public async Task ResolveTurn_HighXpMultiplier_AffectsXpEarned()
```

## Input/Output Examples

**Before (Vacuous Pass Vulnerability):**
```csharp
var result = await session.ResolveTurnAsync(0);

if (!result.Roll.IsSuccess)
{
    Assert.True(resolver.FailureDeltaCalls.Count > 0,
        "Expected GetFailureInterestDelta to be called on a failed roll");
}
```

**After (Explicit Assertion):**
```csharp
var result = await session.ResolveTurnAsync(0);

Assert.False(result.Roll.IsSuccess, "Expected roll to fail to test failure logic");
Assert.True(resolver.FailureDeltaCalls.Count > 0,
    "Expected GetFailureInterestDelta to be called on a failed roll");
```

## Acceptance Criteria

### 1. Replace Conditional Assertions in 4 Tests
In `Issue463_RuleResolverWiringTests.cs`, the four specific tests containing `if (!result.Roll.IsSuccess)` and `if (result.Roll.IsSuccess)` must be updated. The conditional `if` blocks must be completely removed and their contents evaluated unconditionally.

### 2. Explicit Roll Outcome Assertions
Each modified test must explicitly assert the expected roll outcome immediately prior to checking the resolver behavioral changes:
* For tests expecting a failure: `Assert.False(result.Roll.IsSuccess, "...")`
* For tests expecting a success: `Assert.True(result.Roll.IsSuccess, "...")`

### 3. Prevent Vacuous Passing
If resolver wiring is temporarily broken or the fixed dice roll behavior unexpectedly changes (causing an intended failure to succeed or vice-versa), the tests must fail on the outcome assertion or the behavioral assertion, rather than vacuously passing due to an unmet `if` condition.

### 4. Normal Test Passage
All four modified tests must successfully pass under normal test conditions.

## Edge Cases
* **Engine Stat Adjustments**: If the roll logic in `GameSession` is altered such that `FixedDice` inputs of `2` or `15` no longer guarantee failure or success (respectively), the explicit assertions will now correctly fail the tests rather than bypassing the core logic checks.

## Error Conditions
* If a roll succeeds when it was expected to fail, the test will throw an `Xunit.Sdk.FalseException`.
* If a roll fails when it was expected to succeed, the test will throw an `Xunit.Sdk.TrueException`.
* If the resolver fails to correctly calculate or apply deltas/XP, the subsequent domain-specific assertions will throw evaluation exceptions.

## Dependencies
* `Pinder.Core.Tests.Issue463_RuleResolverWiringTests`
* xUnit test framework (`Xunit.Assert`)

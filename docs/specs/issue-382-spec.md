# Specification: Test Coverage for SuccessScale

**Module**: docs/modules/rolls.md

## Overview
This issue adds comprehensive unit test coverage for the `SuccessScale` static class, ensuring that the rules for determining interest deltas on successful rolls are explicitly verified. This creates a more robust testing suite by decoupling the verification of success tiers from `GameSession` integration tests and guaranteeing correct behavior at tier boundaries.

## Function Signatures

This issue introduces a new unit test class. It does not modify any existing production code.

```csharp
namespace Pinder.Core.Tests.Rolls
{
    public class SuccessScaleTests
    {
        [Theory]
        [InlineData(1, true, false, 1)]
        [InlineData(4, true, false, 1)]
        [InlineData(5, true, false, 2)]
        [InlineData(9, true, false, 2)]
        [InlineData(10, true, false, 3)]
        [InlineData(50, true, false, 3)]
        [InlineData(1, true, true, 4)]
        [InlineData(-5, false, false, 0)]
        public void GetInterestDelta_ReturnsExpectedValue(int margin, bool isSuccess, bool isNat20, int expectedDelta);
    }
}
```

*Note: The exact method signature or data provision strategy (`InlineData`, `MemberData`, etc.) is determined by the implementation as long as all boundaries described below are tested.*

## Input/Output Examples

The tests must invoke `SuccessScale.GetInterestDelta(RollResult result)` with `RollResult` instances configured as follows:

| Margin | IsSuccess | IsNat20 | Expected Delta | Reason |
| :--- | :--- | :--- | :--- | :--- |
| 1 | `true` | `false` | `1` | Lower boundary for Tier 1 |
| 4 | `true` | `false` | `1` | Upper boundary for Tier 1 |
| 5 | `true` | `false` | `2` | Lower boundary for Tier 2 |
| 9 | `true` | `false` | `2` | Upper boundary for Tier 2 |
| 10 | `true` | `false` | `3` | Lower boundary for Tier 3 |
| 50 | `true` | `false` | `3` | Large margin for Tier 3 |
| (Any) | `true` | `true` | `4` | Natural 20 |
| (Any) | `false` | `false` | `0` | Failure (margin <= 0) |

## Acceptance Criteria

### 1. Create `SuccessScaleTests.cs`
- The file `tests/Pinder.Core.Tests/Rolls/SuccessScaleTests.cs` (or `tests/Pinder.Core.Tests/SuccessScaleTests.cs`) must be created.
- The tests must use `[Theory]` and provide `[InlineData]` or `MemberData` to verify all specific boundaries.

### 2. Verify all tier boundaries
- margin=1 → +1
- margin=4 → +1
- margin=5 → +2
- margin=9 → +2
- margin=10 → +3
- margin=50 → +3
- Nat 20 → +4 (regardless of the margin)
- margin=0 or below (failure) → 0

### 3. Direct unit tests
- The assertions must directly target `SuccessScale.GetInterestDelta()`.
- The tests must NOT use or instantiate `GameSession` or other external system integration components.

### 4. Build clean, all tests pass
- The overall solution must compile without warnings.
- The new tests and all existing tests in the suite must pass successfully.

## Edge Cases
- **Natural 20 with small margin:** Even if the margin is low (e.g., 1 or even negative) but `IsNat20` is true, the delta returned must be exactly 4.
- **Failures:** `IsSuccess` evaluates to false, which must always result in 0 regardless of other properties.
- **Extreme Margins:** Unusually high margins (e.g., 50 or 100) must consistently return the tier 3 bonus of +3.

## Error Conditions
- Since `SuccessScale.GetInterestDelta()` handles logic based on its parameter properties securely, no explicit error throws are expected based on game values. 
- A null `RollResult` will trigger a standard `NullReferenceException`, which is typical and out of scope for boundary logic testing.

## Dependencies
- **Pinder.Core**: Target of testing.
- **xUnit**: The testing framework utilized to execute `[Theory]` tests.

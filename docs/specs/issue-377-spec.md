# Specification: Test cleanup: delete ShadowGrowthSpecTests.cs after merging 5 unique tests

**Module**: docs/modules/shadow-stats.md

## Overview
This issue involves cleaning up duplicate tests generated during the sprint. `ShadowGrowthSpecTests.cs` largely duplicates `ShadowGrowthEventTests.cs`. Five unique tests from the former will be merged into the latter to preserve coverage. Afterwards, the redundant `ShadowGrowthSpecTests.cs` file will be deleted entirely, reducing the net test count and improving maintainability.

## Function Signatures

No production code changes are required. The task exclusively entails migrating five specific test methods from `Pinder.Core.Tests.ShadowGrowthSpecTests` to `Pinder.Core.Tests.ShadowGrowthEventTests`:

1. `public async Task AC1_Nat1OnHonesty_GrowsDenial()`
2. `public async Task AC1_Nat1OnChaos_GrowsFixation()`
3. `public async Task AC1_FourTropeTraps_MadnessStillOne()`
4. `public void AC2_SessionShadowTracker_DoesNotMutateStatBlock()`
5. `public async Task AC3_EventsDrainedPerTurn()`

## Input/Output Examples

**Example Migration - Test Signature**
```csharp
[Fact]
public async Task AC1_Nat1OnHonesty_GrowsDenial()
{
    // Implementation moves exactly as is from ShadowGrowthSpecTests to ShadowGrowthEventTests
}
```

## Acceptance Criteria

### 1. 5 unique tests merged into `ShadowGrowthEventTests.cs`
- The following exact tests from `ShadowGrowthSpecTests.cs` must be present and correctly formatted in `ShadowGrowthEventTests.cs`:
  - `AC1_Nat1OnHonesty_GrowsDenial`
  - `AC1_Nat1OnChaos_GrowsFixation`
  - `AC1_FourTropeTraps_MadnessStillOne`
  - `AC2_SessionShadowTracker_DoesNotMutateStatBlock`
  - `AC3_EventsDrainedPerTurn`
- The setup and assertions of these 5 tests must remain unchanged, adapting only to the test class setup if needed.

### 2. `ShadowGrowthSpecTests.cs` deleted
- The file `tests/Pinder.Core.Tests/ShadowGrowthSpecTests.cs` must be removed completely from the repository.

### 3. All remaining tests pass
- `dotnet test` must execute and pass all tests successfully.
- No compilation errors must exist in `ShadowGrowthEventTests.cs` after the merge.

### 4. Net test count reduced by ~32
- The total test count of the Pinder.Core.Tests project should be approximately 32 fewer tests than prior to the merge.

## Edge Cases
- **Missing Helper Methods:** `ShadowGrowthSpecTests.cs` relies on private helper methods (like `MakeTracker`, `BuildSession`, or `Dice`) that may not exist in `ShadowGrowthEventTests.cs`. The implementation must ensure the merged tests correctly port the exact dependencies they need or are refactored to use `ShadowGrowthEventTests.cs` existing test fixture setup.

## Error Conditions
- Compilation failure in `ShadowGrowthEventTests.cs` due to unresolved references or missing helpers when pasting the tests.
- CI pipeline failure if `ShadowGrowthSpecTests.cs` is left in the project file but deleted from disk (though `.NET` SDK style projects handle this implicitly, it's worth noting).

## Dependencies
- Source File: `tests/Pinder.Core.Tests/ShadowGrowthSpecTests.cs`
- Target File: `tests/Pinder.Core.Tests/ShadowGrowthEventTests.cs`

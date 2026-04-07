# Specification: Test cleanup: delete 3 trivial InterestMeter tests

**Module**: docs/modules/conversation.md

## Overview
This issue involves cleaning up the test suite by deleting three trivial `InterestMeter` tests from `CharacterSystemTests.cs`. These tests simply assert that constants equal literals or duplicate existing tests in other files, providing no real value and causing brittle failures if constants change.

## Function Signatures
*N/A - This issue only involves deleting test code.*

## Input/Output Examples
*N/A*

## Acceptance Criteria

### 1. Remove 3 trivial tests from `CharacterSystemTests.cs`
The following three test methods must be completely deleted from `tests/Pinder.Core.Tests/CharacterSystemTests.cs`:
- `InterestMeter_MaxIs25()`
- `InterestMeter_StartingValueIs10()`
- `InterestMeter_StartsAt10()`

### 2. All remaining tests pass
The deletion of these tests must not impact the build or the execution of any other tests in the suite. All remaining tests must pass successfully.

### 3. Net count reduced by 3
The total number of tests in the test suite should decrease by exactly 3 after these deletions.

## Edge Cases
- **Constant Usage**: Ensure that no other tests are relying on these specific test methods (which is impossible in xUnit/NUnit anyway, but guarantees no indirect breakages).

## Error Conditions
- If the tests are not correctly removed, the net count of tests will not decrease by 3.
- If extra code is accidentally deleted, the project might fail to compile.

## Dependencies
- `tests/Pinder.Core.Tests/CharacterSystemTests.cs`

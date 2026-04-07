**Module**: docs/modules/conversation.md

## Overview
This issue entails cleaning up redundant test code. Specifically, the test file `tests/Pinder.Core.Tests/ComboSpecTests.cs` (which contains 50 tests and 895 lines, including the `ComboGameSessionSpecTests` class) must be deleted in its entirety. These tests provide zero unique coverage as they completely duplicate the tests already maintained in `ComboTrackerTests.cs` and `ComboGameSessionTests.cs`.

## Function Signatures
None (this is a file deletion task; no public engine APIs or logic are changed).

## Input/Output Examples
None

## Acceptance Criteria

### 1. `ComboSpecTests.cs` deleted
- The file `tests/Pinder.Core.Tests/ComboSpecTests.cs` is permanently removed from the repository.

### 2. All remaining tests still pass
- The entire test suite must compile and pass cleanly when running `dotnet test`.

### 3. Net test count reduced by 50
- After the deletion, the total number of executed tests in the project must be exactly 50 fewer than before the deletion.

## Edge Cases
- **Stale References**: If any other files or tests accidentally referenced nested types or fixtures specific to `ComboSpecTests.cs`, those will cause compilation failures and must be removed.

## Error Conditions
- **Build Failure:** Occurs if other parts of the test suite inadvertently rely on the file being deleted. This must be resolved so that `dotnet build` succeeds.
- **Coverage Regression:** Though unexpected based on the issue description, if coverage metrics drop significantly, it might indicate that some non-duplicated logic was present. (The issue explicitly states there is 100% duplication, so this is unlikely).

## Dependencies
- This task does not depend on any external services, libraries, or other components.

**Module**: docs/modules/llm-adapters.md

## Overview
This specification addresses the cleanup of redundant test coverage for Tell Categories (from Issue #311). The `Issue311_TellCategoriesSpecTests.cs` file contains 14 tests that duplicate the coverage provided by a `Theory` in `Issue311_TellCategoriesTests.cs`. Deleting the duplicate file reduces the net test count by 14 and lowers the test maintenance burden without sacrificing coverage.

## Function Signatures
No production functions or methods are changed. The scope is purely the deletion of a single test file.
- **Target File**: `tests/Pinder.LlmAdapters.Tests/Issue311_TellCategoriesSpecTests.cs` (to be deleted)

## Input/Output Examples
**Input:** Test suite execution before deletion.
**Output:** Test suite passes.

**Input:** Test suite execution after deletion.
**Output:** Test suite passes with the total number of executed tests reduced by exactly 14.

## Acceptance Criteria

### `Issue311_TellCategoriesSpecTests.cs` deleted
The file `tests/Pinder.LlmAdapters.Tests/Issue311_TellCategoriesSpecTests.cs` must be completely removed from the repository. No logic, helpers, or test cases from this file need to be merged into any other file.

### All remaining tests pass
The entire test suite must compile and execute cleanly after the deletion. There should be no compilation errors, missing references, or test failures resulting from this removal.

### Net test count reduced by 14
The test runner output must reflect that the total number of tests executed is exactly 14 less than the count prior to the deletion.

## Edge Cases
- **Shared Helpers:** Ensure no other test classes or files depend on constants, fixtures, or helper methods defined exclusively inside `Issue311_TellCategoriesSpecTests.cs`.
- **Dangling References:** Verify the project file (`Pinder.LlmAdapters.Tests.csproj`) does not hardcode an inclusion of the deleted file (though SDK-style projects typically use auto-globbing).

## Error Conditions
- **Build Failure:** If deleting the file causes a compiler error, another file incorrectly references a symbol inside `Issue311_TellCategoriesSpecTests.cs`. That reference must be removed or properly isolated.
- **Test Count Mismatch:** If the test count drops by a number other than 14, investigate whether the file being deleted contained a different number of tests than expected, or if another file was inadvertently modified or deleted.

## Dependencies
- **Component:** `Pinder.LlmAdapters.Tests`

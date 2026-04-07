**Module**: docs/modules/testing-audit.md (create new)

## Overview
Issue #375 serves as the epic and orchestration issue for a comprehensive test quality audit of the `Pinder.Core` and `Pinder.LlmAdapters` test suites. The objective of this audit is to improve the overall maintainability, execution speed, and reliability of the test suite by removing trivial tests, deduplicating repetitive specification tests, and addressing specific code coverage gaps.

## Function Signatures
*N/A — This is an epic tracking issue focusing entirely on test suite maintenance and refactoring. No changes to the `Pinder.Core` or `Pinder.LlmAdapters` production code APIs are required.*

## Input/Output Examples
- **Trivial Tests**: Tests that merely assert C# compiler behaviors (e.g., property getters without backing logic or empty constructors) will be removed.
- **Duplicate Tests**: Two test files covering the same logic (e.g., `AnthropicClientSpecTests.cs` and `AnthropicClientTests.cs`) will have their unique cases merged and redundant files deleted.
- **Coverage Gaps**: Logic lacking branch coverage (e.g., edge cases in boundary conditions) will have targeted tests added.

## Acceptance Criteria
- [ ] All trivial tests identified in the audit are removed.
- [ ] Duplicate spec tests are merged and removed as per the audit findings.
- [ ] Coverage gaps identified are addressed with new tests.
- [ ] Test quality audit is considered complete and all child issues (376-385) are resolved.

## Edge Cases
- **False Positives in "Trivial" Tests**: High-value tests that assert boundary behaviors or specific business logic must not be removed under the guise of being "trivial".
- **Unique Logic in Duplicates**: When deduplicating test files, any unique setup conditions, assertions, or edge case combinations present in the duplicate must be migrated to the primary test file before deletion.
- **Coverage Stability**: The removal of tests must not cause a regression in the overall branch or line coverage percentages for core gameplay mechanics.

## Error Conditions
- **Incomplete Audit**: If child issues are ignored or improperly closed, technical debt in the form of slow, duplicate, or unmaintained tests will persist.
- **Compilation Failures**: Deleting tests that are part of shared test fixtures or referenced by other test classes could cause test project build failures.
- **Coverage Drops**: Erroneous removal of substantive tests will result in a drop in code coverage, potentially hiding future regressions.

## Dependencies
- **Child Issues**: This epic tracks the completion of issues #376, #377, #378, #379, #380, #381, #382, #383, #384, and #385.
- **Test Framework**: `xUnit` and associated mocking/coverage tools in the .NET toolchain.

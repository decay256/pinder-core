# Specification: Test cleanup - Delete SessionDocumentBuilderSpecTests.cs

**Module**: docs/modules/llm-adapters.md

## Overview
The test file `tests/Pinder.LlmAdapters.Tests/SessionDocumentBuilderSpecTests.cs` duplicates approximately 60% of the test coverage in `SessionDocumentBuilderTests.cs`. To reduce technical debt and maintenance overhead, we need to extract 8 unique interest-boundary tests that verify correct resistance messaging in the opponent's prompt, migrate them to `SessionDocumentBuilderTests.cs` using an xUnit `[Theory]`, and then delete the `SessionDocumentBuilderSpecTests.cs` file.

## Function Signatures

The new test method in `SessionDocumentBuilderTests.cs` should be defined with the following signature:

```csharp
[Theory]
[InlineData(16, "Interested but holding back", null)]
[InlineData(17, "Interested but holding back", null)]
[InlineData(12, "Engaged but not sold", null)]
[InlineData(8, "Skeptical", null)]
[InlineData(4, "Reconsidering", null)]
[InlineData(25, "resistance dissolved", null)]
[InlineData(0, "Unmatched", null)]
[InlineData(1, "Reconsidering", "Unmatched")]
public void BuildOpponentPrompt_InterestBoundaries_ContainsExpectedResistance(int interestAfter, string expectedText, string unexpectedText)
```

## Input/Output Examples
When running `BuildOpponentPrompt_InterestBoundaries_ContainsExpectedResistance`:
- **Input (interestAfter = 16)**: Evaluates `SessionDocumentBuilder.BuildOpponentPrompt` with an `OpponentContext` (e.g. `interestBefore = 10`, `interestAfter = 16`).
- **Expected Output**: The resulting prompt string MUST contain `"Interested but holding back"`.
- **Input (interestAfter = 1)**: Evaluates `SessionDocumentBuilder.BuildOpponentPrompt` with `interestAfter = 1`.
- **Expected Output**: The resulting prompt string MUST contain `"Reconsidering"` and MUST NOT contain `"Unmatched"`.

## Acceptance Criteria
- [ ] The 8 interest-boundary tests (`Interest16_Engaged`, `Interest17_VeryInterested`, `Interest12_Lukewarm`, `Interest8_Cooling`, `Interest4_Disengaged`, `Interest25_ExtremelyInterested`, `Interest0_Unmatching`, `Interest1_Disengaged_NotUnmatching`) are merged into `SessionDocumentBuilderTests.cs` as a single `[Theory]` test method.
- [ ] `SessionDocumentBuilderSpecTests.cs` is completely deleted from the test project.
- [ ] All remaining tests in the test suite pass successfully.
- [ ] The total number of tests in the project is reduced by approximately 63 tests (due to the deletion of duplicates and consolidation into a parameterized theory).

## Edge Cases
- **Interest = 1 (Disengaged Not Unmatching):** Requires assertions that specifically verify the text "Reconsidering" is present and "Unmatched" is ABSENT. An optional third parameter in the theory data cleanly addresses this edge case without affecting the other tests.

## Error Conditions
- If the text generation logic changes in the future, the theory test cases will fail due to string mismatches. Assertions should fail fast and explicitly print which inline data row caused the failure.

## Dependencies
- Component: `SessionDocumentBuilder.BuildOpponentPrompt`
- Test framework: xUnit framework for `[Theory]` and `[InlineData]` execution.

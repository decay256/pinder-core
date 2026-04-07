# Specification: Issue 385 - Test refactor: extract FailureScaleTests and CharacterProfileTests from GameSessionTests.cs

**Module**: docs/modules/testing.md (create new)

## Overview
This specification covers the extraction of two misplaced test classes, `FailureScaleTests` and `CharacterProfileTests`, which are currently embedded inside the `GameSessionTests.cs` file. Moving these test classes into their own dedicated files (`FailureScaleTests.cs` and `CharacterProfileTests.cs`) improves test organization and discoverability without altering any test logic or behavior.

## Function Signatures
As a structural test refactor, no new public function signatures are added to the core engine. The existing test classes must simply be relocated:
- `public class FailureScaleTests`
- `public class CharacterProfileTests`

## Input/Output Examples
**Input**:
The `tests/Pinder.Core.Tests/GameSessionTests.cs` file containing the `FailureScaleTests` and `CharacterProfileTests` classes.

**Output**:
- `tests/Pinder.Core.Tests/FailureScaleTests.cs` created, containing all `FailureScaleTests` test cases.
- `tests/Pinder.Core.Tests/CharacterProfileTests.cs` created, containing all `CharacterProfileTests` test cases.
- `tests/Pinder.Core.Tests/GameSessionTests.cs` stripped of the `FailureScaleTests` and `CharacterProfileTests` class definitions.

## Acceptance Criteria

### 1. `FailureScaleTests.cs` exists as standalone file
The `FailureScaleTests` class must be extracted entirely and placed in `tests/Pinder.Core.Tests/FailureScaleTests.cs`.

### 2. `CharacterProfileTests.cs` exists as standalone file
The `CharacterProfileTests` class must be extracted entirely and placed in `tests/Pinder.Core.Tests/CharacterProfileTests.cs`.

### 3. Both classes removed from `GameSessionTests.cs`
The `GameSessionTests.cs` file must no longer contain the extracted test classes, improving its focus on game session testing.

### 4. All tests pass
All relocated tests must pass and yield the same behavior as before. Test counts must remain identical.

### 5. Build clean
The test project must compile cleanly without namespace resolution errors or missing references.

## Edge Cases
- **Missing Using Directives**: The extracted test files must include all necessary `using` directives (e.g., `using Xunit;`, `using FluentAssertions;`, `using Pinder.Core.Rolls;`, etc.) that were previously provided by `GameSessionTests.cs`.
- **Nested Visibility**: If the classes were previously nested, they will become top-level classes in their respective namespaces.

## Error Conditions
- Compilation errors due to missing namespace imports in the new files.
- Duplicate class definitions if the originals are not cleanly removed from `GameSessionTests.cs`.

## Dependencies
- Existing `xunit` and test assertion frameworks.
- No changes to `Pinder.Core` dependencies.

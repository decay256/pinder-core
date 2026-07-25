> Scope: current #1338/#1339 modified/untracked implementation, data, docs, and test files in `A:\Data\ClaudeCodex\pinder-web\pinder-core` (27 files) and `A:\Data\ClaudeCodex\pinder-web` (1 dependent-repo file), excluding `repo-checker-results` (28 files total).

### Finding 1: Synthesis-field test only echoes constructor arguments through properties
**File**: `tests/Pinder.Core.Tests/Issue1253_SequentialSynthesisTests.cs:295`
**Issue**: `CharacterDefinitionAndProfile_RetainSynthesisFields` constructs `CharacterDefinition` and `CharacterProfile` with local `backstory`, `stakes`, and `diag` values, then only reads those same values back through properties (`Assert.True(...ContainsKey("f1"))`, `Assert.Contains("Stake line", ...)`, and `Assert.Equal("angst", ...["derived_feeling"])`). It does not exercise the loader, writer, synthesis pipeline, or the mapping from a definition into a runtime profile.
**Impact**: The test presents coverage for synthesis-field retention while verifying only boilerplate constructor/property assignment. Regressions in the actual generation, persistence, or definition-to-profile handoff can still pass this test.
**Urgency**: U2 - topic default; this is a boilerplate getter-only test that gives a misleading coverage signal for a cross-stage data contract.
**Fixer-Agent Action Plan**: Replace the direct constructor/property echo checks with one behavior-level round trip through the real boundary the test intends to protect, such as parsing or synthesizing a `CharacterDefinition`, mapping it into a `CharacterProfile`, and asserting the fields survive that handoff. Remove the redundant direct property assertions once the boundary-level regression test exists.

## Counts

U1: 0
U2: 1
U3: 0

# Contract: Issue #38 — QA Review

## Component
All test files in `tests/Pinder.Core.Tests/`

## Maturity
Prototype

---

## Scope

Audit and improve test quality across all test files. This is a QA task, not a feature task.

### What to audit
1. **Coverage gaps**: Missing edge cases, boundary conditions, error paths
2. **Test isolation**: Tests that depend on execution order or shared state
3. **Assertion quality**: Tests that assert too little (just "doesn't throw") or wrong things
4. **Mock correctness**: Mocks that don't match real interface behavior
5. **Name quality**: Test names that don't describe what's being tested

### Specific areas to check (from vision concerns)
- VC-39: SuccessScale has zero test coverage → add tests
- VC-40: GameSession missing DateSecured end condition test → verify exists
- FailureScale coverage
- RollEngine edge cases: DC exactly equal to roll, modifiers at boundary values
- InterestMeter clamping at both ends simultaneously

### New test files for new sprint components
After sprint features are implemented, QA should add:
- `ComboTrackerTests.cs`
- `SessionShadowTrackerTests.cs`
- `XpLedgerTests.cs`
- `PlayerResponseDelayTests.cs`
- `OpponentTimingCalculatorTests.cs`
- `GameClockTests.cs`
- `RiskTierTests.cs`

---

## Behavioural Contract
- All existing tests must continue to pass
- New tests must follow existing naming conventions
- Tests must be deterministic (use FixedDice, not random)
- No test should depend on another test's execution

## Dependencies
- Should run AFTER all feature issues are implemented (Wave 7)

## Consumers
- CI pipeline
- All developers

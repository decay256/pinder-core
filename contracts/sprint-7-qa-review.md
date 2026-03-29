# Contract: Issue #38 — QA Review

## Component
Test quality audit and improvement across all test files

## Dependencies
None — can run in parallel with Wave 0

---

## Scope

Audit existing 254 tests across all test files:
- `RollEngineTests.cs`
- `StatBlockTests.cs`
- `CharacterSystemTests.cs`
- `InterestMeterTests.cs`
- `LlmAdapterTests.cs`
- `GameSessionTests.cs`
- `RiskTierTests.cs`
- `TrapTaintInjectionTests.cs`
- `TurnResultExpansionTests.cs` / `TurnResultExpansionSpecTests.cs`
- `OpponentTimingCalculatorTests.cs`
- `JsonTimingRepositoryTests.cs`
- `OpponentResponseTests.cs`

## Deliverables
1. Contract-to-test coverage gap report (which contract clauses lack tests)
2. At minimum 5 new or improved tests
3. All existing tests still pass
4. New issues filed for complex gaps (label `test-gap`)

## Interface
No code interface — this is a quality/process task. Output is a PR with improved tests and a coverage gap report in the PR description.

## Does NOT modify
- Source code in `src/` (test-only changes)
- Existing test behavior (only additions/improvements)

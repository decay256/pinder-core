# Spec: Issue #38 — QA Review: Audit and Improve Test Quality Across All Test Files

## Overview

This issue is a dedicated QA pass across the entire Pinder.Core test suite (currently 98 tests in 7 files). The goal is to audit existing tests for quality, coverage gaps relative to interface contracts, and naming conventions — then fix straightforward gaps directly and file GitHub issues for complex ones. The output is a measurably improved test suite plus a contract-to-test coverage gap report.

---

## 1. Scope and Inputs

### Test Files Under Audit

| File | Approx. Tests | Covers |
|------|---------------|--------|
| `tests/Pinder.Core.Tests/RollEngineTests.cs` | 10 | d20 resolution, nat1/nat20, fail tiers, advantage/disadvantage, shadow penalty, level bonus |
| `tests/Pinder.Core.Tests/StatBlockTests.cs` | 9 | Defence pairings, base DC, shadow penalty |
| `tests/Pinder.Core.Tests/CharacterSystemTests.cs` | 17 | Item/anatomy loading, character assembly, prompt builder, InterestMeter basics, TimingProfile |
| `tests/Pinder.Core.Tests/InterestMeterTests.cs` | 12 | Interest states, clamping, advantage/disadvantage, boundary transitions |
| `tests/Pinder.Core.Tests/LlmAdapterTests.cs` | 10 | NullLlmAdapter output shapes, context type construction |
| `tests/Pinder.Core.Tests/GameSessionTests.cs` | 11 | Turn sequencing, game outcomes, momentum, ghost trigger, FailureScale, CharacterProfile |
| `tests/Pinder.Core.Tests/RulesConstantsTests.cs` | (inline in StatBlockTests or separate) | Numeric constants from rules-v3.4 |

### Contracts to Audit Against

| Contract File | Key Clauses to Check |
|---------------|---------------------|
| `contracts/issue-26-llm-adapter.md` | `ILlmAdapter` 4 methods; `NullLlmAdapter` behavioural contract (4 options, distinct stats, non-null text, failure prefix, null narrative beat); context type immutability |
| `contracts/issue-27-game-session.md` | `GameSession` constructor, `StartTurnAsync` sequence (end checks, ghost trigger, advantage/disadvantage, pending options), `ResolveTurnAsync` sequence (validation, roll, interest delta, momentum, trap advance, deliver, opponent, history, turn increment); `FailureScale` deltas; `CharacterProfile` construction; `GameEndedException`; alternating call contract |
| `contracts/issue-6-interest-state.md` | `InterestState` enum (6 values), `GetState()` boundaries, `GrantsAdvantage`/`GrantsDisadvantage` logic, exhaustive non-overlapping ranges |
| `contracts/issue-7-rules-constants-tests.md` | Every rules-v3.4 numeric constant has a corresponding assertion |

---

## 2. Audit Procedure (Function-Level)

There are no new public functions introduced by this issue. The work is analytical (audit) and corrective (new/improved tests). The QA engineer must perform the following steps as discrete tasks:

### 2.1 Contract-First Review

**Procedure**: For each contract file listed in Section 1, enumerate every behavioural clause. For each clause, search the test files for a test that exercises it. Produce a coverage matrix.

**Output**: A Markdown table (in the PR description) with columns:
- `Contract` (file name)
- `Clause` (short description)
- `Test(s)` (test method name, or "MISSING")
- `Action` (fixed in this PR / filed as issue #N / already covered)

### 2.2 Happy Path Coverage Audit

The following happy-path scenarios must each have at least one test. If missing, add it.

#### 2.2.1 GameSession: Full 3-Turn Successful Conversation

- **What to verify**: A `GameSession` initialized with `NullLlmAdapter` and a `FixedDice` that produces successes runs 3 turns. After 3 turns: interest has risen from `InterestMeter.StartingValue` (10), `TurnNumber` is 3, history contains 6 entries (3 player messages + 3 opponent messages).
- **Existing coverage**: `GameSessionTests.ThreeTurnSession_HighRolls_SuccessfulTurns` — covers this. Audit should verify it asserts all three conditions (interest change, turn number, history length). Currently it does NOT assert history length — that is a gap.

#### 2.2.2 RollEngine: Success Margin to Interest Delta Mapping

- **What to verify**: Rolls that beat the DC by 1–4, 5–9, and 10+ map to `SuccessScale` deltas of +1, +2, +3 respectively. Nat 20 maps to +4.
- **Existing coverage**: `RollEngineTests` tests nat20 and fail tiers but does NOT directly test `SuccessScale.GetInterestDelta()` output for specific margins. This is a gap.

#### 2.2.3 InterestMeter: Full State Machine Traversal

- **What to verify**: A single `InterestMeter` instance transitions through all 6 states in sequence: `Interested` (start) → `VeryIntoIt` → `AlmostThere` → `DateSecured` (going up) and `Interested` → `Bored` → `Unmatched` (going down).
- **Existing coverage**: `InterestMeterTests` covers individual boundary transitions (4→5, 15→16, 20→21, 24→25) but not a single-instance full traversal. Partial gap — boundary tests are sufficient for prototype maturity, but a full traversal test is recommended.

### 2.3 Error/Edge Path Coverage Audit

#### 2.3.1 RollEngine: All 5 Failure Tiers with Correct Miss Ranges

- **What to verify**: Each failure tier is triggered at the correct miss margin:
  - Nat 1 → `Legendary` (regardless of margin)
  - Miss by 1–2 → `Fumble`
  - Miss by 3–5 → `Misfire`
  - Miss by 6–9 → `TropeTrap`
  - Miss by 10+ → `Catastrophe`
- **Existing coverage**: `RollEngineTests` has `Nat1_IsLegendaryFail`, `MissByOne_IsFumble` (actually miss by 2), `MissByFive_IsMisfire`, `MissBySeven_IsTropeTrap`, `MissByTen_IsCatastrophe`. All 5 tiers are covered. Audit should verify boundary values (e.g., miss by exactly 2 for Fumble upper bound, miss by exactly 3 for Misfire lower bound, miss by exactly 9 for TropeTrap upper bound, miss by exactly 10 for Catastrophe lower bound).

**Gap**: Missing boundary-exact tests. The current test names are misleading (e.g., `MissByOne_IsFumble` actually tests miss-by-2). The QA engineer should add or rename tests to cover exact boundaries.

#### 2.3.2 InterestMeter: Clamping at 0 and 25 with Large Deltas

- **What to verify**: `Apply(-100)` when at 10 clamps to 0. `Apply(+100)` when at 10 clamps to 25.
- **Existing coverage**: `CharacterSystemTests.InterestMeter_ClampsAtMin` (applies -20 from 10) and `InterestMeter_ClampsAtMax` (applies +20 from 10). These are adequate.

#### 2.3.3 GameSession: Ghost Trigger

- **What to verify**: When interest is in `Bored` state (1–4), `StartTurnAsync` calls `dice.Roll(4)`. If result is 1, game ends with `GameOutcome.Ghosted`. If result is 2/3/4, turn proceeds normally.
- **Existing coverage**: `GameSessionTests.GhostTrigger_WhenBored_25PercentChance` covers the trigger case. **Gap**: No test for the non-trigger case (dice returns 2/3/4 and turn proceeds).

#### 2.3.4 GameSession: Game Over on Interest = 0

- **What to verify**: When interest reaches 0, the game ends with `GameOutcome.Unmatched` and subsequent `StartTurnAsync` throws `GameEndedException`.
- **Existing coverage**: `GameSessionTests.EndCondition_InterestHitsZero_ThrowsOnNextStart` covers this.

#### 2.3.5 StatBlock: Shadow Penalty

- **What to verify**: `GetEffective(stat)` returns `baseStat - floor(shadowStat / 3)` using the correct shadow pairing (Charm↔Madness, Rizz↔Horniness, etc.).
- **Existing coverage**: `RollEngineTests.ShadowPenalty_ReducesModifier` tests Charm with Madness=9 → penalty 3. **Gap**: Only one shadow pair is tested. Ideally test at least 2-3 pairs, and test floor behavior (e.g., shadow=4 → penalty 1, shadow=5 → penalty 1, shadow=6 → penalty 2).

### 2.4 Test Quality Audit

#### 2.4.1 Test Naming Convention

**Required pattern**: Descriptive method names that express the scenario and expected outcome. Acceptable patterns:
- `MethodUnderTest_Scenario_ExpectedResult` (e.g., `GetDefenceDC_BaseDCIs13`)
- `Given_When_Then` style

**Violations to flag and fix**:
- `Nat20_IsAlwaysSuccess` — missing the method under test prefix. Should be `Resolve_Nat20_IsAlwaysSuccess` or similar.
- `Nat1_IsLegendaryFail` — same issue.
- `MissByOne_IsFumble` — misleading name (tests miss-by-2, not miss-by-1). Rename to `Resolve_MissByTwo_IsFumble` or fix to actually test miss-by-1.

#### 2.4.2 Assert-Only-No-Throw Anti-Pattern

**Rule**: Every test must assert at least one specific output value, not merely that execution completes without throwing.

**Review each test**: Verify no test body is just a method call with no `Assert.*`. Current tests appear to all have assertions — confirm during audit.

#### 2.4.3 Magic Number Elimination

**Rule**: Where a constant exists in the production code, tests should reference it rather than hardcoding the value.

**Known violations to check**:
- Tests that use literal `10` for starting interest should use `InterestMeter.StartingValue`.
- Tests that use literal `25` for max interest should use `InterestMeter.Max`.
- Tests that use literal `0` for min interest should use `InterestMeter.Min`.
- Tests that use literal `13` for base DC should reference `StatBlock` if it exposes a constant (if not, the literal is acceptable with a comment).

**Note**: Some existing tests in `CharacterSystemTests` already reference `InterestMeter.Max` and `InterestMeter.StartingValue`. Others in `GameSessionTests` use literal `10`. These should be made consistent.

#### 2.4.4 NullLlmAdapter Structural Verification

**Rule**: `NullLlmAdapter` tests must verify structural correctness, not just non-null.

**What to verify**:
- `GetDialogueOptionsAsync`: returns exactly 4 options, each with a distinct `StatType` from {Charm, Honesty, Wit, Chaos}, each with non-null non-empty `IntendedText`.
- `DeliverMessageAsync` success: returns the `IntendedText` verbatim.
- `DeliverMessageAsync` failure: returns string prefixed with `[{FailureTier}] `.
- `GetOpponentResponseAsync`: returns non-null, non-empty string.
- `GetInterestChangeBeatAsync`: returns null.

**Existing coverage**: `LlmAdapterTests` already covers all of these. This criterion is already met.

### 2.5 Mock/Interface Drift Check

#### 2.5.1 NullLlmAdapter vs ILlmAdapter

**What to verify**: `NullLlmAdapter` implements `ILlmAdapter` (compiler enforces this). Method signatures match: same async `Task<T>` return types, same parameter types. This is enforced by the C# compiler — if `NullLlmAdapter : ILlmAdapter`, it compiles only if signatures match.

**Action**: Confirm `NullLlmAdapter` declaration includes `: ILlmAdapter`. Already true if the code compiles.

#### 2.5.2 FixedDice vs IDiceRoller

**What to verify**: `FixedDice` in `GameSessionTests.cs` implements `IDiceRoller`. Confirm `public int Roll(int sides)` signature matches.

**Known issue**: There are TWO `FixedDice` implementations — one in `RollEngineTests.cs` (private class) and one in `GameSessionTests.cs` (public class). Additionally, `CharacterSystemTests.cs` has a third private `FixedDice`. These should be consolidated into a single shared test helper.

---

## 3. Acceptance Criteria (mapped from issue)

### AC-1: Contract-First Review

**Criterion**: Read contracts in `contracts/` (especially `issue-27-game-session.md`, `issue-26-llm-adapter.md`) and verify every contract clause has at least one test. Flag any clause with NO test.

**Specification**: The QA engineer must produce a contract-to-test matrix as described in Section 2.1. Every row with "MISSING" in the Test(s) column constitutes a gap. Gaps that are straightforward to fix (e.g., adding a simple assertion) must be fixed in this PR. Gaps requiring complex business logic must be filed as separate GitHub issues with the `test-gap` label.

### AC-2: Happy Path — GameSession 3-Turn Conversation

**Criterion**: A full 3-turn successful conversation test where interest rises from 10 toward the date-secured range.

**Specification**: `GameSessionTests.ThreeTurnSession_HighRolls_SuccessfulTurns` exists but should additionally assert that `result.StateAfter` contains the expected history length (6 entries after 3 turns, if exposed via snapshot) or that `DeliveredMessage` and `OpponentMessage` are populated each turn. If `GameStateSnapshot` does not expose history length, assert turn number and interest values instead (already done).

### AC-3: Happy Path — RollEngine Success Margins

**Criterion**: Success with exact margin mapping to +1/+2/+3 interest (beat by 1–4, 5–9, 10+).

**Specification**: Add tests (or verify existing) that call `SuccessScale.GetInterestDelta()` with `RollResult` objects where:
- `BeatDcBy` = 1 → returns +1
- `BeatDcBy` = 4 → returns +1
- `BeatDcBy` = 5 → returns +2
- `BeatDcBy` = 9 → returns +2
- `BeatDcBy` = 10 → returns +3
- Nat 20 (`IsNatTwenty = true`) → returns +4

Each assertion must check the exact return value. If `SuccessScale` tests don't exist in a dedicated file, they may be added to `RollEngineTests.cs` or a new `SuccessScaleTests.cs`.

### AC-4: Happy Path — InterestMeter Full State Machine

**Criterion**: Transition through all 6 InterestState values.

**Specification**: Already covered by boundary transition tests in `InterestMeterTests.cs`. A single full-traversal test (up from 10→25, down from 10→0) would improve confidence but is optional at prototype maturity.

### AC-5: Error Path — All 5 Failure Tiers

**Criterion**: All 5 fail tiers triggered with correct miss ranges (Fumble 1–2, Misfire 3–5, TropeTrap 6–9, Catastrophe 10+, Legendary Nat1).

**Specification**: Existing tests cover all 5 tiers. The QA pass should verify boundary correctness:
- Miss by exactly 1 → Fumble
- Miss by exactly 2 → Fumble (upper bound)
- Miss by exactly 3 → Misfire (lower bound)
- Miss by exactly 5 → Misfire (upper bound)
- Miss by exactly 6 → TropeTrap (lower bound)
- Miss by exactly 9 → TropeTrap (upper bound)
- Miss by exactly 10 → Catastrophe (lower bound)

Add any missing boundary tests. Fix misleading test names.

### AC-6: Error Path — InterestMeter Clamping

**Criterion**: Clamping at 0 and 25 with large deltas.

**Specification**: Already covered. Verify assertions reference `InterestMeter.Min` and `InterestMeter.Max` constants rather than magic numbers.

### AC-7: Error Path — Ghost Trigger

**Criterion**: GameSession ghost trigger (interest in Bored state, 25% per turn).

**Specification**: Existing test covers trigger case. Add a test for the non-trigger case: interest in Bored range, `dice.Roll(4)` returns 2, `StartTurnAsync` returns a `TurnStart` (does NOT throw).

### AC-8: Error Path — Game Over on Interest = 0

**Criterion**: Already covered by `EndCondition_InterestHitsZero_ThrowsOnNextStart`.

### AC-9: Error Path — Shadow Penalty

**Criterion**: Shadow penalty correctly reduces effective stat (`floor(shadow/3)`).

**Specification**: Add tests for at least one additional shadow pair beyond Charm↔Madness. Test floor behavior: shadow=1 → penalty 0, shadow=2 → penalty 0, shadow=3 → penalty 1, shadow=5 → penalty 1, shadow=6 → penalty 2.

### AC-10: Test Quality — Naming Convention

**Criterion**: Test method names follow descriptive pattern.

**Specification**: Rename tests with ambiguous or misleading names. At minimum:
- `Nat20_IsAlwaysSuccess` → `Resolve_Nat20_IsAlwaysSuccess`
- `Nat1_IsLegendaryFail` → `Resolve_Nat1_IsLegendaryFailure`
- `MissByOne_IsFumble` → rename to match actual miss margin tested

### AC-11: Test Quality — No Assert-Only-No-Throw

**Criterion**: No tests assert only that a method doesn't throw.

**Specification**: Confirm all tests have at least one `Assert.*` call checking a return value, property, or exception type.

### AC-12: Test Quality — No Magic Numbers

**Criterion**: Constants reference `InterestMeter.Max`, `InterestMeter.StartingValue`, etc.

**Specification**: Replace hardcoded `10` with `InterestMeter.StartingValue`, `25` with `InterestMeter.Max`, `0` with `InterestMeter.Min` in test assertions where applicable. Literal values in test setup (e.g., dice roll values, stat values) are acceptable since they represent test inputs, not system constants.

### AC-13: Test Quality — NullLlmAdapter Structural Verification

**Criterion**: Verify output is structurally correct (4 options, correct stat types, non-null texts).

**Specification**: Already covered by `LlmAdapterTests`. No action needed.

### AC-14: Mock/Interface Drift Check

**Criterion**: `NullLlmAdapter` matches `ILlmAdapter`; `FixedDice` matches `IDiceRoller`.

**Specification**: Compiler enforces interface compliance. The QA engineer should additionally verify there is no parameter name drift (parameter names match between interface and implementation). Consolidate duplicate `FixedDice` implementations into a single shared test helper class.

### AC-15: Report Findings

**Criterion**: Open a GitHub issue for each gap found (label `test-gap`). Fix straightforward gaps in this PR. Leave complex gaps as issues.

**Specification**: The PR description must contain:
1. Contract-to-test coverage matrix
2. List of tests added/renamed/improved
3. List of issues filed for deferred gaps
4. Confirmation that `dotnet test` passes with no regressions

---

## 4. Edge Cases

- **Duplicate FixedDice classes**: Three separate `FixedDice` implementations exist across test files. Consolidation must not break existing tests. The shared helper should live in a common location (e.g., a `TestHelpers` class or file in the test project root).
- **RulesConstantsTests location**: The issue mentions `RulesConstantsTests.cs` as a standalone file, but it may be embedded in `StatBlockTests.cs`. The audit should locate and verify its existence.
- **Test count regression**: The issue states 98 existing tests. After the QA pass, the count must be ≥ 98 (tests may be added or renamed, but none removed unless they are genuinely wrong). Run `dotnet test` and compare counts.
- **CharacterSystemTests file-loading**: Some tests in `CharacterSystemTests.cs` load JSON from the filesystem (`/root/.openclaw/agents-extra/pinder/data/...`). These tests are environment-dependent. The QA engineer should note this as a known limitation but not attempt to fix it in this sprint (it requires a test data embedding strategy).

---

## 5. Error Conditions

| Condition | Expected Behavior |
|-----------|-------------------|
| Contract clause has no corresponding test | Flag as gap in coverage matrix; fix if straightforward, file issue if complex |
| Test name is misleading | Rename the test method; ensure old and new names don't conflict |
| Magic number found in assertion | Replace with named constant from production code |
| `dotnet test` fails after changes | All changes must be reverted or fixed before PR is opened |
| `dotnet test` count drops below 98 | Investigate — no tests should be deleted without replacement |
| Duplicate test helper classes | Consolidate; update all references; verify compilation |

---

## 6. Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| All existing test files compile and pass | Prerequisite | Assumed — run `dotnet test` before starting audit |
| `contracts/issue-26-llm-adapter.md` | Input (audit reference) | Exists on main |
| `contracts/issue-27-game-session.md` | Input (audit reference) | Exists on main |
| `contracts/issue-6-interest-state.md` | Input (audit reference) | Exists on main |
| `contracts/issue-7-rules-constants-tests.md` | Input (audit reference) | Exists on main |
| xUnit test framework | External library | Already in test project |
| .NET SDK (netstandard2.0 target, net8.0 test runner) | Tooling | Already configured |
| `NullLlmAdapter` | Production code | Exists at `src/Pinder.Core/Conversation/NullLlmAdapter.cs` |
| `FixedDice` (test helper) | Test code | Exists in `GameSessionTests.cs` (public), `RollEngineTests.cs` (private), `CharacterSystemTests.cs` (private) |

---

## 7. Definition of Done

- [ ] QA report (contract-to-test coverage matrix) written and posted as PR description
- [ ] At minimum 5 new or improved tests added
- [ ] All new tests pass (`dotnet test`)
- [ ] No test regressions — all pre-existing tests still pass (count ≥ 98)
- [ ] Contract-to-test coverage gap report in PR description
- [ ] Any complex gaps filed as GitHub issues with `test-gap` label

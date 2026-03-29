# Spec: Issue #38 — QA Review: Audit and Improve Test Quality Across All Test Files

> **Contract**: `contracts/sprint-8-qa-review.md`
> **Related issues**: #39 (SuccessScale zero coverage), #40 (DateSecured end condition gap), #62 (meta-vision concern)
> **Maturity**: Prototype

## Overview

This issue is a dedicated QA pass across the entire Pinder.Core test suite (currently 254 tests in 14 files). The goal is to audit existing tests for quality, coverage gaps relative to interface contracts, and naming conventions — then fix straightforward gaps directly and file GitHub issues for complex ones. The output is a measurably improved test suite plus a contract-to-test coverage gap report. This is an implementation issue: the QA engineer must write actual C# test code, not just a report.

---

## 1. Scope and Inputs

### 1.1 Test Files Under Audit

| File | Tests | Covers |
|------|-------|--------|
| `RollEngineTests.cs` | 10 | d20 resolution, nat1/nat20, fail tiers, advantage/disadvantage, shadow penalty, level bonus |
| `StatBlockTests.cs` | 9 | Defence pairings, base DC |
| `CharacterSystemTests.cs` | 17 | Item/anatomy loading, character assembly, prompt builder, InterestMeter basics, TimingProfile |
| `InterestMeterTests.cs` | 12 | Interest states, clamping, advantage/disadvantage, boundary transitions |
| `LlmAdapterTests.cs` | 10 | NullLlmAdapter output shapes, context type construction |
| `GameSessionTests.cs` | 11 | Turn sequencing, game outcomes, momentum, ghost trigger, FailureScale, CharacterProfile |
| `RiskTierTests.cs` | 19 | RiskTier computation, RiskTierBonus, boundary values |
| `TrapTaintInjectionTests.cs` | 19 | JsonTrapRepository parsing, trap registration, error handling |
| `TurnResultExpansionTests.cs` | 12 | TurnResult expanded fields (shadow, combo, XP, tell, weakness) |
| `TurnResultExpansionSpecTests.cs` | 33 | TurnResult spec compliance, DialogueOption expanded fields |
| `OpponentTimingCalculatorTests.cs` | 29 | Opponent reply delay calculation |
| `OpponentResponseTests.cs` | 17 | OpponentResponse type, Tell, WeaknessWindow, CallbackOpportunity |
| `JsonTimingRepositoryTests.cs` | 6 | TimingProfile JSON loading |

**Total**: 254 tests.

### 1.2 Contracts to Audit Against

| Contract File | Key Clauses to Check |
|---------------|---------------------|
| `contracts/issue-26-llm-adapter.md` | `ILlmAdapter` 4 methods; `NullLlmAdapter` behavioural contract (4 options, distinct stats, non-null text, failure prefix, null narrative beat); context type immutability |
| `contracts/issue-27-game-session.md` | `GameSession` constructor, `StartTurnAsync` sequence (end checks, ghost trigger, adv/disadv, pending options), `ResolveTurnAsync` sequence (validation, roll, interest delta, momentum, trap advance, deliver, opponent, history, turn increment); `FailureScale` deltas; `CharacterProfile` construction; `GameEndedException`; alternating call contract |
| `contracts/issue-6-interest-state.md` | `InterestState` enum (6 values), `GetState()` boundaries, `GrantsAdvantage`/`GrantsDisadvantage` logic, exhaustive non-overlapping ranges |
| `contracts/issue-7-rules-constants-tests.md` | Every rules-v3.4 numeric constant has a corresponding assertion |
| `contracts/sprint-7-qa-review.md` | Meta-contract defining QA scope and deliverables |
| `contracts/sprint-8-qa-review.md` | Updated QA contract for Sprint 8 — adds #39/#40 traceability |

### 1.3 Source Files Referenced

| Source File | What It Contains |
|-------------|------------------|
| `src/Pinder.Core/Rolls/RollEngine.cs` | `Resolve()` — d20 resolution with stat mod, level bonus, advantage/disadvantage, trap activation |
| `src/Pinder.Core/Rolls/SuccessScale.cs` | `GetInterestDelta(RollResult)` — maps beat-DC-by margin to +1/+2/+3/+4 |
| `src/Pinder.Core/Rolls/FailureScale.cs` | `GetInterestDelta(RollResult)` — maps failure tier to -1/-2/-3/-4/-5 |
| `src/Pinder.Core/Rolls/RollResult.cs` | Roll outcome data: `IsSuccess`, `Tier`, `Total`, `DieRoll`, `DC`, `IsNatTwenty`, `IsNatOne`, `ComputeRiskTier()` |
| `src/Pinder.Core/Rolls/RiskTierBonus.cs` | `GetInterestBonus(RollResult)` — Hard→+1, Bold→+2 |
| `src/Pinder.Core/Stats/StatBlock.cs` | Immutable stat container, `GetDefenceDC()`, `GetEffective()`, `DefenceTable`, shadow pairs |
| `src/Pinder.Core/Conversation/InterestMeter.cs` | Interest tracking, `GetState()`, `Apply()`, `GrantsAdvantage`, `GrantsDisadvantage`, constants `Max=25`, `Min=0`, `StartingValue=10` |
| `src/Pinder.Core/Conversation/GameSession.cs` | Session orchestrator: `StartTurnAsync()`, `ResolveTurnAsync()`, momentum, ghost trigger |
| `src/Pinder.Core/Conversation/NullLlmAdapter.cs` | Test-only `ILlmAdapter` implementation |
| `src/Pinder.Core/Conversation/TurnResult.cs` | Extended with shadow events, combo, XP, tell, weakness fields |
| `src/Pinder.Core/Conversation/OpponentTimingCalculator.cs` | Static `ComputeDelayMinutes()` |
| `src/Pinder.Core/Interfaces/IRollDataProvider.cs` | `IDiceRoller`, `IFailurePool`, `ITrapRegistry` interfaces |
| `src/Pinder.Core/Interfaces/ILlmAdapter.cs` | LLM adapter interface (4 async methods) |

### 1.4 Out of Scope — Sprint 8 Components

The following components are being added or significantly modified in Sprint 8. Tests for these are owned by their respective issues, **not** by this QA audit:

| Component | Owning Issue | Reason Out of Scope |
|-----------|-------------|---------------------|
| `SessionShadowTracker` | Sprint 8 shadow growth | New component — no existing tests to audit |
| `ShadowThresholdEvaluator` | Sprint 8 shadow thresholds | New component |
| `ComboTracker` | Sprint 8 combo system | New component |
| `XpLedger` | Sprint 8 XP tracking | New component |
| `PlayerResponseDelayEvaluator` | Sprint 8 player response delay | New component |
| `ConversationRegistry` | Sprint 8 conversation registry | New component |
| `GameClock` / `IGameClock` | Sprint 8 game clock | New component |
| `GameSessionConfig` | Sprint 8 wave-0 infrastructure | New component |
| `GameSession.ReadAsync/RecoverAsync/Wait` | Sprint 8 read-recover-wait | New methods — not yet implemented |
| `RollEngine.ResolveFixedDC` | Sprint 8 wave-0 infrastructure | New overload — not yet implemented |

This audit covers only the **existing** 254 tests and their coverage of **existing** production code as of the Sprint 8 baseline.

---

## 2. Audit Procedure

There are no new public production functions introduced by this issue. The work is analytical (audit) and corrective (new/improved tests in `tests/Pinder.Core.Tests/`). The QA engineer must perform the following tasks:

### 2.1 Contract-First Review

**Procedure**: For each contract file in Section 1.2, enumerate every behavioural clause. For each clause, search test files for a test that exercises it.

**Output**: A Markdown table in the PR description with columns:
- `Contract` — file name
- `Clause` — short description of the behavioural clause
- `Test(s)` — test method name(s), or `MISSING`
- `Action` — "already covered" / "fixed in this PR" / "filed as issue #N"

### 2.2 Happy Path Coverage

#### 2.2.1 GameSession: Full 3-Turn Successful Conversation

**What to verify**: A `GameSession` with `NullLlmAdapter` and a `FixedDice` producing high rolls runs 3 turns. After 3 turns: interest has risen above `InterestMeter.StartingValue` (10), `TurnNumber` is 3, `DeliveredMessage` and `OpponentMessage` are populated each turn.

**Existing coverage**: `GameSessionTests.ThreeTurnSession_HighRolls_SuccessfulTurns` — verify it asserts interest change AND turn count. `GameStateSnapshot` exposes `TurnNumber` (int), `Interest` (int), `State` (InterestState), `MomentumStreak` (int), and `ActiveTrapNames` (string[]) — all are public read-only properties available for assertions.

#### 2.2.2 RollEngine: Success Margin to Interest Delta Mapping

**What to verify**: `SuccessScale.GetInterestDelta()` returns the correct delta for each margin range:
- Beat DC by 1–4 → +1
- Beat DC by 5–9 → +2
- Beat DC by 10+ → +3
- Nat 20 → +4

**Gap**: Current `RollEngineTests` tests nat20 and fail tiers but does NOT directly test `SuccessScale.GetInterestDelta()` for specific margins. This requires new tests.

**New tests to add** (at minimum):

| Test Name | Input | Expected Output |
|-----------|-------|-----------------|
| `SuccessScale_BeatBy1_ReturnsPlus1` | `RollResult` where `BeatDcBy=1`, `IsNatTwenty=false` | `+1` |
| `SuccessScale_BeatBy4_ReturnsPlus1` | `RollResult` where `BeatDcBy=4`, `IsNatTwenty=false` | `+1` |
| `SuccessScale_BeatBy5_ReturnsPlus2` | `RollResult` where `BeatDcBy=5`, `IsNatTwenty=false` | `+2` |
| `SuccessScale_BeatBy9_ReturnsPlus2` | `RollResult` where `BeatDcBy=9`, `IsNatTwenty=false` | `+2` |
| `SuccessScale_BeatBy10_ReturnsPlus3` | `RollResult` where `BeatDcBy=10`, `IsNatTwenty=false` | `+3` |
| `SuccessScale_Nat20_ReturnsPlus4` | `RollResult` where `IsNatTwenty=true` | `+4` |

These may be implemented as a `[Theory]` with `[InlineData]`.

#### 2.2.3 InterestMeter: Full State Machine Traversal

**What to verify**: A single `InterestMeter` instance transitions through all 6 `InterestState` values: `Interested` (start at 10) → `VeryIntoIt` → `AlmostThere` → `DateSecured` (going up) and separately `Interested` → `Bored` → `Unmatched` (going down).

**Existing coverage**: `InterestMeterTests` covers individual boundary transitions at 4→5, 15→16, 20→21, 24→25. This is adequate for prototype maturity. A single full-traversal test is recommended but optional.

### 2.3 Error/Edge Path Coverage

#### 2.3.1 RollEngine: All 5 Failure Tiers with Correct Miss Ranges

Per rules §5:
- Nat 1 → `FailureTier.Legendary` (regardless of margin)
- Miss by 1–2 → `FailureTier.Fumble`
- Miss by 3–5 → `FailureTier.Misfire`
- Miss by 6–9 → `FailureTier.TropeTrap`
- Miss by 10+ → `FailureTier.Catastrophe`

**Existing coverage**: Tests exist for all 5 tiers. However, boundary values need verification:

| Boundary | Expected Tier | Existing Test? |
|----------|--------------|----------------|
| Miss by exactly 1 | Fumble | Named `MissByOne_IsFumble` but implementation may test miss-by-2 — **audit** |
| Miss by exactly 2 | Fumble (upper bound) | **Check** |
| Miss by exactly 3 | Misfire (lower bound) | **Check** |
| Miss by exactly 5 | Misfire (upper bound) | `MissByFive_IsMisfire` — verify |
| Miss by exactly 6 | TropeTrap (lower bound) | **Check** |
| Miss by exactly 9 | TropeTrap (upper bound) | `MissBySeven_IsTropeTrap` — off by 2, missing boundary |
| Miss by exactly 10 | Catastrophe (lower bound) | `MissByTen_IsCatastrophe` — verify |

**Action**: Add missing boundary tests. Rename misleading test names.

#### 2.3.2 InterestMeter: Clamping at 0 and 25

**What to verify**: `Apply(-100)` from 10 clamps to 0. `Apply(+100)` from 10 clamps to 25.

**Existing coverage**: `CharacterSystemTests.InterestMeter_ClampsAtMin` (applies -20) and `InterestMeter_ClampsAtMax` (applies +20). Adequate. Verify assertions use `InterestMeter.Min`/`InterestMeter.Max` constants.

#### 2.3.3 GameSession: Ghost Trigger

**What to verify**: When interest is in `Bored` state (1–4), `StartTurnAsync` calls `dice.Roll(4)`. If result is 1, game ends with `GameOutcome.Ghosted`. If result is 2/3/4, turn proceeds.

**Existing coverage**: `GhostTrigger_WhenBored_25PercentChance` covers the trigger case.
**Gap**: No test for the non-trigger case (dice returns 2/3/4, turn proceeds normally). Add one.

#### 2.3.4 GameSession: Game Over on Interest = 0

**Existing coverage**: `EndCondition_InterestHitsZero_ThrowsOnNextStart`. Adequate.

#### 2.3.5 StatBlock: Shadow Penalty

**What to verify**: `GetEffective(stat)` returns `baseStat - floor(shadowStat / 3)`.

**Existing coverage**: `RollEngineTests.ShadowPenalty_ReducesModifier` tests Charm with Madness=9 → penalty 3.
**Gap**: Only one shadow pair tested. Should test additional pairs and floor behavior (shadow=1→0, shadow=2→0, shadow=3→1, shadow=5→1, shadow=6→2).

### 2.4 Test Quality

#### 2.4.1 Test Naming Convention

**Required pattern**: `MethodUnderTest_Scenario_ExpectedResult` or descriptive `Given_When_Then`.

**Known violations to fix**:
- `Nat20_IsAlwaysSuccess` → `Resolve_Nat20_IsAlwaysSuccess`
- `Nat1_IsLegendaryFail` → `Resolve_Nat1_IsLegendaryFailure`
- `MissByOne_IsFumble` → rename to match actual miss margin tested
- `Disadvantage_UsesMinium` → `Resolve_Disadvantage_UsesMinimum` (also fix typo "Minium")

#### 2.4.2 Assert-Only-No-Throw Anti-Pattern

**Rule**: Every test must assert at least one specific output value via `Assert.*`. No test should only verify a method runs without throwing.

**Action**: Audit all 254 tests. Flag any that lack `Assert.*` calls checking return values.

#### 2.4.3 Magic Number Elimination

**Rule**: Where a named constant exists in production code, tests should reference it.

| Magic Number | Replacement Constant |
|-------------|---------------------|
| `10` (starting interest) | `InterestMeter.StartingValue` |
| `25` (max interest) | `InterestMeter.Max` |
| `0` (min interest) | `InterestMeter.Min` |
| `13` (base DC) | Comment referencing §3 if no public constant exists |

**Note**: Literal values used as test *inputs* (dice rolls, stat values) are acceptable — they represent test scenarios, not system constants.

#### 2.4.4 NullLlmAdapter Structural Verification

**What to verify**:
- `GetDialogueOptionsAsync`: returns exactly 4 options, each with a distinct `StatType` from {Charm, Honesty, Wit, Chaos}, each with non-null non-empty `IntendedText`
- `DeliverMessageAsync` success: returns `IntendedText` verbatim
- `DeliverMessageAsync` failure: returns string prefixed with `[{FailureTier}] `
- `GetOpponentResponseAsync`: returns non-null, non-empty string
- `GetInterestChangeBeatAsync`: returns null

**Existing coverage**: `LlmAdapterTests` covers all of the above. Already adequate.

### 2.5 Mock/Interface Drift Check

#### 2.5.1 NullLlmAdapter vs ILlmAdapter

`NullLlmAdapter` implements `ILlmAdapter` — compiler enforces signature compliance. Verify the declaration includes `: ILlmAdapter`. If it compiles, signatures match.

#### 2.5.2 FixedDice vs IDiceRoller

**Known issue**: Multiple `FixedDice` implementations exist across test files:
1. `RollEngineTests.cs` — private class
2. `GameSessionTests.cs` — public class with `Enqueue` method
3. `CharacterSystemTests.cs` — private class

**Action**: Consolidate into a single shared `FixedDice` class in a common location (e.g., `tests/Pinder.Core.Tests/TestHelpers/FixedDice.cs` or a `TestHelpers.cs` file). All must implement `IDiceRoller` with signature `int Roll(int sides)`. Update all references. Verify compilation passes.

---

## 3. Function Signatures

This issue introduces no new production code. All changes are in `tests/Pinder.Core.Tests/`.

### Test Helper (consolidation target)

```csharp
// tests/Pinder.Core.Tests/TestHelpers/FixedDice.cs (or similar)
namespace Pinder.Core.Tests.TestHelpers
{
    public sealed class FixedDice : IDiceRoller
    {
        // Constructor accepting predetermined roll sequence
        public FixedDice(params int[] rolls);

        // Optional: enqueue additional rolls
        public void Enqueue(params int[] values);

        // IDiceRoller implementation — returns next value from sequence
        // Falls back to `sides` (max value) when sequence is exhausted
        public int Roll(int sides);
    }
}
```

### New Test Methods (minimum 5 required)

The QA engineer must add at least 5 new or improved tests. Recommended additions:

1. **`SuccessScale_BeatByMargin_ReturnsCorrectDelta`** — `[Theory]` covering margins 1, 4, 5, 9, 10, and nat20
2. **`Resolve_MissByExactly1_IsFumble`** — boundary test for Fumble lower bound
3. **`Resolve_MissByExactly3_IsMisfire`** — boundary test for Misfire lower bound
4. **`Resolve_MissByExactly9_IsTropeTrap`** — boundary test for TropeTrap upper bound
5. **`GhostTrigger_WhenBored_DiceReturns2_TurnProceeds`** — non-trigger ghost case
6. **`ShadowPenalty_MultipleStatPairs_CorrectFloor`** — additional shadow pair tests
7. **`GetEffective_ShadowFloorBehavior`** — shadow=4→penalty 1, shadow=6→penalty 2

---

## 4. Input/Output Examples

### 4.1 SuccessScale Margin Mapping

| BeatDcBy | IsNatTwenty | Expected Delta |
|----------|-------------|----------------|
| 1 | false | +1 |
| 4 | false | +1 |
| 5 | false | +2 |
| 9 | false | +2 |
| 10 | false | +3 |
| 15 | false | +3 |
| any | true | +4 |

### 4.2 FailureTier Boundary Values

To produce a specific miss margin, set up `StatBlock` and `FixedDice` such that:
`miss_margin = DC - (DieRoll + StatMod + LevelBonus)`

Example: To test miss-by-exactly-3 (Misfire lower bound):
- Attacker Charm=0, Level=1 (LevelBonus=0)
- Defender SA=0 → DC=13
- DieRoll=10 → Total=10, miss by 3
- Expected: `FailureTier.Misfire`

### 4.3 Shadow Penalty Floor Behavior

| Shadow Value | Expected Penalty (floor(shadow/3)) |
|-------------|-----------------------------------|
| 0 | 0 |
| 1 | 0 |
| 2 | 0 |
| 3 | 1 |
| 4 | 1 |
| 5 | 1 |
| 6 | 2 |
| 9 | 3 |

---

## 5. Acceptance Criteria

### AC-1: Contract-First Review

Read contracts in `contracts/` and verify every contract clause has at least one test. The PR description must contain a contract-to-test coverage matrix. Every row marked "MISSING" is a gap. Straightforward gaps (simple assertion additions) must be fixed in this PR. Complex gaps must be filed as GitHub issues with label `test-gap`.

### AC-2: Happy Path — GameSession 3-Turn Conversation

Verify `GameSessionTests.ThreeTurnSession_HighRolls_SuccessfulTurns` asserts: (a) interest rose above starting value, (b) turn number is 3, (c) `DeliveredMessage` and `OpponentMessage` are non-null each turn. Add missing assertions if needed.

### AC-3: Happy Path — RollEngine Success Margins (addresses #39)

Add tests verifying `SuccessScale.GetInterestDelta()` returns +1 for beat-by 1–4, +2 for beat-by 5–9, +3 for beat-by 10+, +4 for nat20. At minimum test all boundary values (1, 4, 5, 9, 10). This directly addresses the coverage gap identified in issue #39 (SuccessScale has zero test coverage).

### AC-4: Happy Path — InterestMeter Full State Machine (addresses #40)

Verify all 6 `InterestState` values are covered by existing boundary tests. Optionally add a full-traversal test. Additionally, verify that a `DateSecured` end condition test exists — when interest reaches 25, `GameSession` should produce `GameOutcome.DateSecured`. This directly addresses the gap identified in issue #40 (missing DateSecured end condition test).

### AC-5: Error Path — All 5 Failure Tiers

Verify all 5 failure tiers are tested. Add boundary-exact tests where missing (miss by exactly 1, 2, 3, 5, 6, 9, 10). Fix misleading test names.

### AC-6: Error Path — InterestMeter Clamping

Verify clamping tests exist and use named constants (`InterestMeter.Min`, `InterestMeter.Max`) rather than magic numbers.

### AC-7: Error Path — Ghost Trigger (Non-Trigger Case)

Add a test: interest in Bored range, `dice.Roll(4)` returns 2 (or 3 or 4), `StartTurnAsync` returns `TurnStart` without throwing.

### AC-8: Error Path — Game Over on Interest = 0

Already covered. Verify test name is descriptive.

### AC-9: Error Path — Shadow Penalty

Add tests for additional shadow pairs beyond Charm↔Madness. Add floor-behavior tests (shadow=1→0, shadow=3→1, shadow=6→2).

### AC-10: Test Naming Convention

Rename tests with ambiguous or misleading names. At minimum fix: `Nat20_IsAlwaysSuccess`, `Nat1_IsLegendaryFail`, `MissByOne_IsFumble` (if it tests a different margin), `Disadvantage_UsesMinium` (typo).

### AC-11: No Assert-Only-No-Throw

Confirm all tests have at least one `Assert.*` call checking a return value, property, or exception type. Flag and fix any violations.

### AC-12: No Magic Numbers

Replace hardcoded `10`/`25`/`0` with `InterestMeter.StartingValue`/`InterestMeter.Max`/`InterestMeter.Min` in test assertions where those constants exist and the value represents the same concept.

### AC-13: NullLlmAdapter Structural Verification

Verify `LlmAdapterTests` covers all structural assertions (4 options, distinct stats, non-null text, failure prefix, null narrative beat). Already adequate — confirm only.

### AC-14: Mock/Interface Drift — FixedDice Consolidation

Consolidate duplicate `FixedDice` implementations into a single shared test helper. Verify it implements `IDiceRoller` with correct signature. Update all test files to use the shared implementation. All tests must still compile and pass.

> **Note**: This consolidation is mechanical but touches multiple test files. If it proves complex or risky (e.g., different `FixedDice` variants have subtly different fallback behavior), it may be deferred to a dedicated cleanup issue. The implementer should verify all three existing implementations have compatible semantics before consolidating.

### AC-15: Report Findings

PR description must contain:
1. Contract-to-test coverage matrix (per AC-1)
2. List of tests added/renamed/improved
3. List of issues filed for deferred gaps (with issue numbers)
4. Confirmation that `dotnet test` passes with zero regressions

---

## 6. Edge Cases

| Edge Case | Handling |
|-----------|---------|
| Duplicate FixedDice classes across 3 files | Consolidate into one shared helper. Must not break any existing test. Run `dotnet test` after consolidation. |
| RulesConstantsTests location ambiguous | May be embedded in `StatBlockTests.cs` or exist as a separate file. Audit must locate it and verify coverage. |
| Test count must not decrease | After QA pass, `dotnet test` must report ≥ 254 passed. Tests may be renamed but not removed unless genuinely incorrect (document any removal with justification). |
| `CharacterSystemTests` depends on filesystem JSON | Tests loading from external paths are environment-dependent. Note as known limitation — do not attempt to fix in this sprint. |
| `RollResult` construction for SuccessScale tests | `RollResult` may have a private constructor or require specific factory patterns. Check actual constructor signature before writing tests. If `RollResult` cannot be easily constructed in tests, document this as a testability gap. |
| Renaming tests may break CI references | If any CI config references test names by string, renaming could break. Check for `.runsettings` or CI filter expressions. |
| `SuccessScale`/`FailureScale` may take `RollResult` not raw values | Tests must construct valid `RollResult` objects, not call with raw ints. Check method signatures. |

---

## 7. Error Conditions

| Condition | Expected Behavior |
|-----------|-------------------|
| Contract clause has no corresponding test | Flag as gap in coverage matrix; fix if straightforward, file issue with `test-gap` label if complex |
| Test name is misleading (e.g., says miss-by-1 but tests miss-by-2) | Rename the test method to accurately describe what it tests |
| Magic number in test assertion for a value that has a named constant | Replace with the named constant reference |
| `dotnet test` fails after changes | Revert or fix before opening PR. Never merge failing tests. |
| Test count drops below 254 | Investigate — no tests should be deleted without a replacement and documented justification |
| Duplicate test helper consolidation breaks compilation | Fix all `using` statements and namespaces. Verify with `dotnet build` before `dotnet test`. |
| `RollResult` not constructible from tests | Document as testability gap issue. May need to add a test-friendly constructor or factory. |

---

## 8. Dependencies

| Dependency | Type | Status |
|------------|------|--------|
| All 254 existing tests compile and pass | Prerequisite | Verified — `dotnet test` returns 254 passed |
| `contracts/issue-26-llm-adapter.md` | Input (audit reference) | Exists on main |
| `contracts/issue-27-game-session.md` | Input (audit reference) | Exists on main |
| `contracts/issue-6-interest-state.md` | Input (audit reference) | Exists on main |
| `contracts/issue-7-rules-constants-tests.md` | Input (audit reference) | Exists on main |
| `contracts/sprint-7-qa-review.md` | Input (meta-contract) | Exists on main |
| xUnit test framework | External library | Already in test project |
| .NET SDK (netstandard2.0 target, net8.0 test runner) | Tooling | Already configured |
| `NullLlmAdapter` | Production code (under audit) | `src/Pinder.Core/Conversation/NullLlmAdapter.cs` |
| `FixedDice` (test helper) | Test code (consolidation target) | Multiple copies in `GameSessionTests.cs`, `RollEngineTests.cs`, `CharacterSystemTests.cs` |
| No production source code changes | Constraint | This issue modifies ONLY files under `tests/` |

---

## 9. Definition of Done

- [ ] QA report (contract-to-test coverage matrix) written and posted as PR description
- [ ] At minimum 5 new or improved tests added
- [ ] All tests pass (`dotnet test`) — zero failures
- [ ] No test regressions — test count ≥ 254
- [ ] Contract-to-test coverage gap report in PR description
- [ ] Any complex gaps filed as GitHub issues with `test-gap` label
- [ ] Test naming violations fixed (at minimum the 4 listed in AC-10)
- [ ] Magic numbers replaced with named constants where applicable
- [ ] FixedDice consolidation completed (or documented as deferred if complex)
- [ ] No production source code modified (`src/` unchanged)

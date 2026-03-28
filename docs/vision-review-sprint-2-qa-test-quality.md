# Vision Review — Sprint 2: QA — Test Quality Review

## Alignment: ✅

This sprint is the right work at the right time. After shipping the core game loop (GameSession + ILlmAdapter) in Sprint 1, auditing test quality is high-leverage housekeeping. The engine is at prototype maturity with 98 tests across 7 files — enough to have confidence in core mechanics, but written by implementation agents who were optimizing for shipping, not coverage completeness. A dedicated QA pass before building the next feature layer (items, prestige, production LLM adapters) prevents compounding test debt.

## Data Flow Traces

### QA Audit (no new runtime data flow)
This sprint does not introduce new features or data paths. It audits existing test coverage against contracts. No data flow concerns.

### Relevant existing flows the QA agent must understand:
1. **Roll → SuccessScale → InterestMeter**: `RollEngine.Resolve()` → `RollResult` → `SuccessScale.GetInterestDelta(result)` → `InterestMeter.Apply(delta)`. **⚠️ SuccessScale has ZERO tests** — this entire positive feedback path is unvalidated.
2. **Roll → FailureScale → InterestMeter**: Same path but negative. ✅ FailureScale has full parametric coverage.
3. **GameSession turn cycle**: `StartTurnAsync()` → `ResolveTurnAsync()` → alternation enforced. ✅ Invalid call order tested. **⚠️ DateSecured end condition (interest=25) not tested** per contract requirement.

## Unstated Requirements

- The QA agent will need to **read both contracts** (`issue-26-llm-adapter.md`, `issue-27-game-session.md`) to perform the contract-first audit. The issue AC says this but doesn't link the files — the agent must find them in `contracts/`.
- If the QA agent renames test methods for consistency (Given/When/Then), **existing test names are referenced in no external tooling** — safe to rename without breaking anything.
- The QA agent is expected to both audit AND fix — scope could balloon. The issue wisely says "leave complex business logic gaps as issues for a future sprint" which provides a natural scope limiter.

## Domain Invariants

- All 98 existing tests must continue to pass (no regressions) — explicitly stated in DoD
- Every contract clause should have at least one test — this is the QA agent's primary deliverable
- Test helpers (`FixedDice`, `NullTrapRegistry`, `NullLlmAdapter`) must exactly match their interfaces — drift here causes silent test invalidity

## Gaps

### Missing (filed as vision concerns)
- **#39 — SuccessScale has zero tests**: The core positive feedback loop (`SuccessScale.GetInterestDelta()`) has no test coverage whatsoever. Its companion `FailureScale` has full parametric tests. This is the most important gap the QA agent should find. The architecture doc's sync table lists `SuccessScale` as a rules-derived constant that needs drift detection — but the referenced `RulesConstantsTests.cs` file doesn't exist.
- **#40 — GameSession missing DateSecured test**: The contract requires a DateSecured end condition test. Unmatched and Ghosted are tested; DateSecured (the win state) is not.

### Not missing (the QA agent should discover these organically)
- InterestMeter clamping tests exist (boundary transitions at 4→5, 15→16, 20→21, 24→25) ✅
- Ghost trigger test exists ✅
- FailureScale parametric tests exist ✅
- Invalid call order tests exist ✅
- NullLlmAdapter structural tests exist (4 options, distinct stats, non-empty text) ✅

### Unnecessary
- Nothing. The sprint is tightly scoped to one issue with clear AC.

### Assumptions to validate
- **"At minimum 5 new or improved tests"** — given the SuccessScale gap alone requires 5+ tests (4 margin brackets + failure case + Nat 20 + boundaries), this minimum is easily achievable.
- The issue references `RulesConstantsTests.cs` "(in stats tests)" — the QA agent should clarify whether rules constant assertions should be extracted into a dedicated file or left distributed across test files.

## Role Assignment Check

| Issue | Current Role | Correct? | Notes |
|-------|-------------|----------|-------|
| #38 | qa-engineer | ✅ | Test audit + quality improvement is exactly qa-engineer scope |

No role corrections needed.

## Recommendations

1. **QA agent should prioritize SuccessScale tests** (#39) — this is the biggest coverage gap and the most impactful fix. Five parametric tests covering all margin brackets plus boundaries.
2. **QA agent should add DateSecured end condition test** (#40) — the win state must be tested per contract.
3. **QA agent should clarify RulesConstantsTests.cs status** — the architecture doc references it as a drift detection mechanism, but it doesn't exist. Either create it or update the architecture doc.
4. **Don't over-scope test renaming** — the AC mentions Given/When/Then naming but existing names like `Nat20_IsAlwaysSuccess` are perfectly readable. Rename only the truly ambiguous ones.

## VERDICT: CLEAN

The sprint is well-scoped, correctly assigned, and the right priority. The two vision concerns filed (#39, #40) are test gaps that the QA agent is already tasked to find — they serve as guidance, not blockers. No architectural concerns. No sequencing issues. Proceed.

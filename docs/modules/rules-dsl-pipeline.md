# Rules DSL Pipeline

## Overview
The Rules DSL pipeline validates that hardcoded C# game constants in Pinder.Core match the authoritative values defined in `rules/extracted/rules-v3-enriched.yaml`. It consists of auto-generated xUnit test stubs covering failure/success deltas, interest states, shadow thresholds, risk bonuses, and XP/level progression.

## Key Components

- **`tests/Pinder.Core.Tests/RulesSpec/RulesSpecTests.cs`** — 54 auto-generated test methods (37 active `[Fact]` + 17 `[Fact(Skip)]`) produced by `rules/tools/generate_tests.py` from the enriched YAML. Covers §5 failure/success scales, §5 risk tiers/bonuses, §6 interest states/meter, §7 shadow thresholds, and §10 XP progression.
- **`tests/Pinder.Core.Tests/RulesSpec/RulesSpecValidationTests.cs`** — Validation tests written from the spec (not implementation). Verifies file-structure conformance (AC1/AC2/AC3), boundary edge cases for all rule sections, clamping behavior, and mutation-catching assertions.
- **`rules/extracted/rules-v3-enriched.yaml`** — Authoritative YAML source of truth for all game rule constants.
- **`rules/tools/generate_tests.py`** — Python script that reads enriched YAML and generates `RulesSpecTests.cs`.

## API / Public Interface

No new production API is introduced. The tests exercise existing Pinder.Core public API:

| API | Namespace | Purpose |
|-----|-----------|---------|
| `FailureScale.GetInterestDelta(RollResult)` | `Pinder.Core.Rolls` | §5 failure interest deltas |
| `SuccessScale.GetInterestDelta(RollResult)` | `Pinder.Core.Rolls` | §5 success interest deltas |
| `RollResult` constructor + `RiskTier` property | `Pinder.Core.Rolls` | §5 risk tier classification |
| `RiskTierBonus.GetInterestBonus(RollResult)` | `Pinder.Core.Rolls` | §5 risk bonus values |
| `InterestMeter(int)` + `Apply(int)` + `GetState()` + `Current` | `Pinder.Core.Conversation` | §6 interest states and clamping |
| `InterestState` enum | `Pinder.Core.Conversation` | §6 state names |
| `ShadowThresholdEvaluator.GetThresholdLevel(int)` | `Pinder.Core.Stats` | §7 shadow tier boundaries |
| `LevelTable.GetLevel(int)` / `LevelTable.GetBonus(int)` | `Pinder.Core.Progression` | §10 XP→level and level bonuses |

## Architecture Notes

- The pipeline follows a **YAML → Python codegen → C# test** flow. The enriched YAML is the single source of truth; `generate_tests.py` produces `RulesSpecTests.cs`. Manual edits to the generated file are discouraged (source attribution comment enforces this).
- The 17 skipped stubs cover LLM/qualitative effects (shadow taint narrative, failure narrative flavor, ghost triggers, prompt taint injection) that cannot be mechanically unit-tested.
- `RulesSpecValidationTests.cs` is a hand-written companion that validates spec acceptance criteria and boundary edge cases independently of the generated stubs.
- Helper methods (`MakeFailure`, `MakeSuccess`, `MakeRisk`) construct `RollResult` instances with correct internal invariants for each test scenario.

## Change Log
| Date | Issue | Summary |
|------|-------|---------|
| 2026-04-04 | #445 | Initial creation — integrated 54 auto-generated test stubs plus validation tests into `Pinder.Core.Tests/RulesSpec/`. PR #494 added `RulesSpecValidationTests.cs` with boundary/edge-case coverage. Note: the PR diff contains only the validation test file; `RulesSpecTests.cs` (the 54 generated stubs) was expected per the spec but not present in the diff. |
